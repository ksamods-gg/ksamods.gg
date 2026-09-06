import { ORPCError, type RouterClient } from '@orpc/server'
import { z } from 'zod'
import { adminDiscordIds, isAdmin } from './auth'
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

const adminUserSchema = z.object({
  id: z.string(),
  name: z.string(),
  email: z.string(),
  image: z.string().nullable(),
  role: z.string().nullable(),
  banned: z.boolean().nullable(),
  banReason: z.string().nullable(),
  banExpires: z.date().nullable(),
  createdAt: z.date(),
  providers: z.array(z.string()),
  /** Admin through ADMIN_DISCORD_IDS rather than the role column. */
  breakGlass: z.boolean(),
  claims: z.number().int(),
  submissions: z.number().int(),
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

  users: {
    admin: {
      /** Everyone with an account, newest first. A plain contains match is
       *  what a few hundred users needs; add an index when it stops being. */
      list: adminOnly
        .route({
          method: 'GET',
          path: '/admin/users',
          summary: 'List users',
          tags: ['Admin'],
        })
        .input(z.object({ q: z.string().max(200).optional() }).optional())
        .output(z.array(adminUserSchema))
        .handler(async ({ input }) => {
          const q = input?.q?.trim()
          const rows = await db.user.findMany({
            where: q
              ? {
                  OR: [
                    { name: { contains: q, mode: 'insensitive' } },
                    { email: { contains: q, mode: 'insensitive' } },
                  ],
                }
              : undefined,
            select: {
              id: true,
              name: true,
              email: true,
              image: true,
              role: true,
              banned: true,
              banReason: true,
              banExpires: true,
              createdAt: true,
              accounts: { select: { providerId: true, accountId: true } },
              _count: { select: { claims: true, submissions: true } },
            },
            orderBy: { createdAt: 'desc' },
            // ponytail: no pagination. Add a cursor when the list outgrows one
            // screen of scrolling, which is a long way past 200 users.
            take: 200,
          })

          return rows.map(({ accounts, _count, ...user }) => ({
            ...user,
            providers: [...new Set(accounts.map((account) => account.providerId))].sort(),
            // Reported separately from the role column, because a break-glass
            // admin has no role set and showing them as an ordinary user would
            // make the list lie about who can do what.
            breakGlass: accounts.some(
              (account) =>
                account.providerId === 'discord' && adminDiscordIds.includes(account.accountId),
            ),
            claims: _count.claims,
            submissions: _count.submissions,
          }))
        }),

      setRole: adminOnly
        .route({
          method: 'POST',
          path: '/admin/users/{userId}/role',
          summary: 'Grant or remove admin',
          tags: ['Admin'],
        })
        .input(z.object({ userId: z.string().min(1), admin: z.boolean() }))
        .output(z.object({ id: z.string(), role: z.string().nullable() }))
        .handler(async ({ input, context }) => {
          // Demoting yourself locks you out of the page you are standing on,
          // and only a break-glass Discord id could undo it.
          if (input.userId === context.user.id)
            throw new ORPCError('BAD_REQUEST', { message: 'You cannot change your own role' })

          return db.user.update({
            where: { id: input.userId },
            data: { role: input.admin ? 'admin' : 'user' },
            select: { id: true, role: true },
          })
        }),

      setBan: adminOnly
        .route({
          method: 'POST',
          path: '/admin/users/{userId}/ban',
          summary: 'Suspend or restore an account',
          tags: ['Admin'],
        })
        .input(
          z.object({
            userId: z.string().min(1),
            banned: z.boolean(),
            reason: z.string().max(500).optional(),
          }),
        )
        .output(z.object({ id: z.string(), banned: z.boolean() }))
        .handler(async ({ input, context }) => {
          if (input.userId === context.user.id)
            throw new ORPCError('BAD_REQUEST', { message: 'You cannot suspend your own account' })

          const user = await db.user.update({
            where: { id: input.userId },
            data: input.banned
              ? { banned: true, banReason: input.reason?.trim() || null }
              : { banned: false, banReason: null, banExpires: null },
            select: { id: true, banned: true },
          })

          // Without this the suspension only takes effect when their session
          // expires. The authed middleware refuses a banned user mid-request
          // too, so this is the second lock rather than the only one.
          if (input.banned) await db.session.deleteMany({ where: { userId: input.userId } })

          return { id: user.id, banned: user.banned ?? false }
        }),
    },
  },
}

export type Client = RouterClient<typeof router>
