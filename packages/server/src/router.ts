import { type RouterClient } from '@orpc/server'
import { z } from 'zod'
import { isAdmin } from './auth'
import { claimListing, listMaintainers } from './claims'
import { latestSnapshot, linkState, visibleSnapshot } from './content-index'
import { db } from './db'
import { documentInputSchema, snapshotOutputSchema } from './openapi-schemas'
import { adminOnly, authed, pub } from './orpc'
import {
  mySubmissions,
  previewSubmission,
  refreshSubmission,
  submitListing,
} from './submissions'

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

const blockerSchema = z.object({
  /** A dotted path into the document, so the form can put the error under the
   *  right input. Absent when the problem is not about one field. */
  field: z.string().optional(),
  message: z.string(),
  code: z.string().optional(),
})

const ownershipSchema = z.object({
  proof: z.enum(['owner-id', 'topic', 'marker']).nullable(),
  selfMerges: z.boolean(),
  authority: z.string().nullable(),
  remediation: z.array(z.string()),
})

const previewSchema = z.object({
  toml: z.string().nullable(),
  path: z.string(),
  baseRepo: z.string(),
  baseBranch: z.string(),
  blockers: z.array(blockerSchema),
  ownership: ownershipSchema,
})

const submissionStateSchema = z.enum(['submitting', 'open', 'merged', 'closed', 'failed'])

const mySubmissionSchema = z.object({
  id: z.string(),
  listingId: z.string(),
  state: submissionStateSchema,
  prNumber: z.number().int().nullable(),
  prUrl: z.string().nullable(),
  ownershipProof: z.string().nullable(),
  error: z.string().nullable(),
  createdAt: z.date(),
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
      .handler(() => visibleSnapshot()),
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

    submissions: {
      /** Renders the file and reports every problem, without writing anything. */
      preview: authed
        .route({
          method: 'POST',
          path: '/listings/submissions/preview',
          summary: 'Check a listing document and render its TOML',
          tags: ['Submissions'],
        })
        .input(z.object({ document: documentInputSchema }))
        .output(previewSchema)
        .handler(({ input, context }) => previewSubmission(context.user.id, input.document)),

      /** Opens the pull request as the caller. Their GitHub account is what
       *  upstream's ownership check reads, so it can never be a bot. */
      submit: authed
        .route({
          method: 'POST',
          path: '/listings/submissions',
          summary: 'Open a listing pull request as the caller',
          tags: ['Submissions'],
        })
        .input(
          z.object({
            document: documentInputSchema,
            acknowledgeStewardReview: z.boolean().optional(),
          }),
        )
        .output(
          z.object({
            id: z.string(),
            state: submissionStateSchema,
            prNumber: z.number().int().nullable(),
            prUrl: z.string().nullable(),
            ownershipProof: z.enum(['owner-id', 'topic', 'marker']).nullable(),
          }),
        )
        .handler(({ input, context }) =>
          submitListing(context.user.id, input.document, input.acknowledgeStewardReview ?? false),
        ),

      mine: authed
        .route({
          method: 'GET',
          path: '/me/submissions',
          summary: 'The listing submissions belonging to the caller',
          tags: ['Me'],
        })
        .output(z.array(mySubmissionSchema))
        .handler(({ context }) => mySubmissions(context.user.id)),

      refresh: authed
        .route({
          method: 'POST',
          path: '/me/submissions/{id}/refresh',
          summary: 'Re-read the pull request state from GitHub',
          tags: ['Me'],
        })
        .input(z.object({ id: z.string().min(1) }))
        .output(z.object({ state: z.string() }))
        .handler(({ input, context }) => refreshSubmission(context.user.id, input.id)),
    },

    admin: {
      /** Listings whose metadata claim does not resolve. Recomputed from the
       *  snapshot rather than stored, so it can never go stale. */
      linkIssues: adminOnly
        .route({
          method: 'GET',
          path: '/admin/link-issues',
          summary: 'Listings whose ksamods-gg metadata does not resolve',
          tags: ['Admin'],
        })
        .output(
          z.array(
            z.object({
              kind: z.enum(['unknown-id', 'wrong-listing', 'orphaned']),
              listingId: z.string(),
              claimedModId: z.string().nullable(),
              boundTo: z.string().optional(),
              detail: z.string(),
            }),
          ),
        )
        .handler(async () => {
          const snapshot = await latestSnapshot()
          if (!snapshot) return []
          const { issues } = await linkState(snapshot.data.listings)
          return issues
        }),

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
