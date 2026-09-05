import { type RouterClient } from '@orpc/server'
import { z } from 'zod'
import { claimListing, listMaintainers } from './claims'
import { latestSnapshot } from './content-index'
import { db } from './db'
import { adminOnly, authed, pub } from './orpc'

const listingId = z.object({ listingId: z.string().min(1).max(200) })

export const router = {
  health: pub.handler(() => ({ ok: true })),

  contentIndex: {
    /** The most recently fetched upstream snapshot, or null before the first sync. */
    latest: pub.handler(() => latestSnapshot()),
  },

  listings: {
    /** Verified maintainers, shown publicly on the listing. */
    maintainers: pub
      .input(listingId)
      .handler(({ input }) => listMaintainers(input.listingId)),

    /** Every listing with at least one verified maintainer, for badging the browser. */
    claimed: pub.handler(async () => {
      const rows = await db.listingClaim.findMany({
        where: { status: 'approved' },
        select: { listingId: true },
        distinct: ['listingId'],
      })
      return rows.map((row) => row.listingId)
    }),

    claim: authed
      .input(listingId)
      .handler(({ input, context }) => claimListing(context.user.id, input.listingId)),

    mine: authed.handler(({ context }) =>
      db.listingClaim.findMany({
        where: { userId: context.user.id },
        select: { id: true, listingId: true, status: true, note: true, decidedAt: true },
        orderBy: { updatedAt: 'desc' },
      }),
    ),

    admin: {
      claims: adminOnly.handler(() =>
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
