import { type RouterClient } from '@orpc/server'
import { z } from 'zod'
import { isAdmin } from './auth'
import { claimListing, listMaintainers } from './claims'
import { latestSnapshot } from './content-index'
import { db } from './db'
import { snapshotOutputSchema } from './openapi-schemas'
import { adminOnly, authed, pub } from './orpc'

const listingId = z.object({ listingId: z.string().min(1).max(200) })

const claimStatus = z.enum(['pending', 'approved', 'rejected', 'revoked'])

const maintainerSchema = z.object({
  id: z.string(),
  name: z.string(),
  image: z.string().nullable(),
  verifiedAt: z.date().nullable(),
})

const claimResultSchema = z.object({
  status: z.enum(['approved', 'rejected', 'pending']),
  repo: z.string().optional(),
  reason: z.string().optional(),
})

const myClaimSchema = z.object({
  id: z.string(),
  listingId: z.string(),
  status: claimStatus,
  note: z.string().nullable(),
  decidedAt: z.date().nullable(),
})

/**
 * Every procedure carries route metadata so the one router can be served both
 * as RPC and as a REST style OpenAPI surface. Without it the generated document
 * would fall back to POST /procedure/path for everything.
 */
export const router = {
  health: pub
    .route({ method: 'GET', path: '/health', summary: 'Liveness check', tags: ['Meta'] })
    .output(z.object({ ok: z.boolean() }))
    .handler(() => ({ ok: true })),

  /** Whether the caller is an admin, by either path. Lets the UI show an admin
   *  link without guessing from the role column, which misses break-glass
   *  admins. Cosmetic only: the adminOnly procedures are the real gate. */
  amIAdmin: authed
    .route({ method: 'GET', path: '/me/admin', summary: 'Is the caller an admin', tags: ['Me'] })
    .output(z.boolean())
    .handler(({ context }) => isAdmin(context.user)),

  contentIndex: {
    /** The most recently fetched upstream snapshot, or null before the first sync. */
    latest: pub
      .route({
        method: 'GET',
        path: '/content-index/latest',
        summary: 'Latest upstream content index snapshot',
        tags: ['Content index'],
      })
      // Typed and documented but not validated: see openapi-schemas.ts.
      .output(snapshotOutputSchema)
      .handler(() => latestSnapshot()),
  },

  listings: {
    /** Verified maintainers, shown publicly on the listing. */
    maintainers: pub
      .route({
        method: 'GET',
        path: '/listings/{listingId}/maintainers',
        summary: 'Verified maintainers of a listing',
        tags: ['Listings'],
      })
      .input(listingId)
      .output(z.array(maintainerSchema))
      .handler(({ input }) => listMaintainers(input.listingId)),

    /** Every listing with at least one verified maintainer, for badging the browser. */
    claimed: pub
      .route({
        method: 'GET',
        path: '/listings/claimed',
        summary: 'Listing ids that have a verified maintainer',
        tags: ['Listings'],
      })
      .output(z.array(z.string()))
      .handler(async () => {
        const rows = await db.listingClaim.findMany({
          where: { status: 'approved' },
          select: { listingId: true },
          distinct: ['listingId'],
        })
        return rows.map((row) => row.listingId)
      }),

    claim: authed
      .route({
        method: 'POST',
        path: '/listings/{listingId}/claim',
        summary: 'Claim a listing, verified through GitHub push access',
        tags: ['Listings'],
      })
      .input(listingId)
      .output(claimResultSchema)
      .handler(({ input, context }) => claimListing(context.user.id, input.listingId)),

    mine: authed
      .route({
        method: 'GET',
        path: '/me/listings',
        summary: 'The claims belonging to the caller',
        tags: ['Me'],
      })
      .output(z.array(myClaimSchema))
      .handler(({ context }) =>
        db.listingClaim.findMany({
          where: { userId: context.user.id },
          select: { id: true, listingId: true, status: true, note: true, decidedAt: true },
          orderBy: { updatedAt: 'desc' },
        }),
      ),

    admin: {
      claims: adminOnly
        .route({
          method: 'GET',
          path: '/admin/claims',
          summary: 'All listing claims',
          tags: ['Admin'],
        })
        .handler(() =>
          db.listingClaim.findMany({
            select: {
              id: true,
              listingId: true,
              status: true,
              note: true,
              evidence: true,
              decidedAt: true,
              decidedById: true,
              user: { select: { id: true, name: true, email: true } },
            },
            orderBy: { updatedAt: 'desc' },
            take: 200,
          }),
        ),

      decide: adminOnly
        .route({
          method: 'POST',
          path: '/admin/claims/{claimId}/decide',
          summary: 'Approve, reject or revoke a claim',
          tags: ['Admin'],
        })
        .input(
          z.object({
            claimId: z.string().min(1),
            status: z.enum(['approved', 'rejected', 'revoked']),
            note: z.string().max(500).optional(),
          }),
        )
        .handler(({ input, context }) =>
          db.listingClaim.update({
            where: { id: input.claimId },
            data: {
              status: input.status,
              note: input.note ?? null,
              decidedAt: new Date(),
              // A non-null decider is what marks this as a human decision.
              decidedById: context.user.id,
            },
            select: { id: true, status: true },
          }),
        ),
    },
  },
}

export type Client = RouterClient<typeof router>
