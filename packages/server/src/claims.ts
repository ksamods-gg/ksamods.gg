import { ORPCError } from '@orpc/server'
import type { ListingClaimStatus } from './generated/prisma/enums'
import type { InputJsonValue } from './generated/prisma/internal/prismaNamespace'
import { auth } from './auth'
import { findListing } from './content-index'
import { db } from './db'
import { checkRepoPushAccess, resolveRepo } from './github'

/** Keep the proof log bounded without losing the most recent attempts. */
const MAX_EVIDENCE = 20

/**
 * Each attempt is an outbound authenticated GitHub call driven by user input,
 * so one account must not be able to burn the shared rate limit.
 * ponytail: in-process counter, correct for the single server process this runs
 * as today. Move to a database or Redis counter when it runs more than once.
 */
const RATE_LIMIT = { attempts: 10, windowMs: 60 * 60 * 1000 }
const attempts = new Map<string, number[]>()

function rateLimit(key: string) {
  const now = Date.now()
  const recent = (attempts.get(key) ?? []).filter((at) => now - at < RATE_LIMIT.windowMs)
  if (recent.length >= RATE_LIMIT.attempts) {
    throw new ORPCError('TOO_MANY_REQUESTS', { message: 'Too many attempts, try again later' })
  }
  recent.push(now)
  attempts.set(key, recent)
}

type EvidenceEntry = Record<string, unknown> & { at: string; outcome: string }

async function appendEvidence(
  listingId: string,
  userId: string,
  entry: EvidenceEntry,
  update: { status?: ListingClaimStatus; note?: string | null; decidedAt?: Date | null },
) {
  const existing = await db.listingClaim.findUnique({
    where: { listingId_userId: { listingId, userId } },
    select: { evidence: true },
  })
  const log = Array.isArray(existing?.evidence) ? (existing.evidence as EvidenceEntry[]) : []
  const evidence = [...log, entry].slice(-MAX_EVIDENCE) as InputJsonValue

  return db.listingClaim.upsert({
    where: { listingId_userId: { listingId, userId } },
    create: { listingId, userId, evidence, ...update },
    // decidedById stays null: this path is only ever the automated check.
    update: { evidence, decidedById: null, ...update },
  })
}

/**
 * Verify a claim by confirming the claimant's GitHub account has push access to
 * the repository the listing declares. The repository is resolved server side
 * from the current snapshot, so the claimant never chooses what is checked.
 */
export async function claimListing(userId: string, listingId: string) {
  const listing = await findListing(listingId)
  if (!listing) throw new ORPCError('NOT_FOUND', { message: 'Unknown listing' })

  rateLimit(`${userId}:${listingId}`)

  const repo = resolveRepo(listing.authored)
  if (!repo) {
    await appendEvidence(
      listingId,
      userId,
      { at: new Date().toISOString(), outcome: 'no-repo' },
      { status: 'pending', note: 'This listing declares no GitHub repository' },
    )
    return {
      status: 'pending' as const,
      reason: 'This listing does not declare a GitHub repository, so it cannot be verified automatically.',
    }
  }

  const account = await db.account.findFirst({
    // providerId is the stable key; issuer is a derived string.
    where: { userId, providerId: 'github' },
    select: { id: true, accountId: true },
  })
  if (!account) {
    throw new ORPCError('BAD_REQUEST', { message: 'Connect GitHub first' })
  }

  let accessToken: string
  let scopes: unknown
  try {
    const token = await auth.api.getAccessToken({ body: { accountId: account.id, userId } })
    accessToken = (token as { accessToken: string }).accessToken
    scopes = (token as { scopes?: unknown }).scopes
  } catch {
    throw new ORPCError('BAD_REQUEST', { message: 'Reconnect GitHub and try again' })
  }

  const outcome = await checkRepoPushAccess(accessToken, repo)
  const entry: EvidenceEntry = {
    at: new Date().toISOString(),
    outcome: outcome.kind,
    repo: `${repo.owner}/${repo.repo}`,
    // Immutable GitHub user id, never the mutable login.
    githubUserId: account.accountId,
    scopes,
    ...('permissions' in outcome ? { permissions: outcome.permissions } : {}),
    ...('repoId' in outcome ? { repoId: outcome.repoId } : {}),
    ...('reason' in outcome ? { reason: outcome.reason } : {}),
  }

  // A transient failure must never be recorded against the user.
  if (outcome.kind === 'retry') {
    throw new ORPCError('SERVICE_UNAVAILABLE', { message: outcome.reason })
  }

  const status: ListingClaimStatus =
    outcome.kind === 'approved' ? 'approved' : outcome.kind === 'rejected' ? 'rejected' : 'pending'

  await appendEvidence(listingId, userId, entry, {
    status,
    note: outcome.kind === 'approved' ? null : ('reason' in outcome ? outcome.reason : null),
    decidedAt: new Date(),
  })

  return {
    status,
    repo: `${repo.owner}/${repo.repo}`,
    reason: 'reason' in outcome ? outcome.reason : undefined,
  }
}

/** Verified maintainers of a listing, for public display. */
export async function listMaintainers(listingId: string) {
  const claims = await db.listingClaim.findMany({
    where: { listingId, status: 'approved' },
    select: { decidedAt: true, user: { select: { id: true, name: true, image: true } } },
    orderBy: { decidedAt: 'asc' },
  })
  return claims.map((claim) => ({
    id: claim.user.id,
    name: claim.user.name,
    image: claim.user.image,
    verifiedAt: claim.decidedAt,
  }))
}
