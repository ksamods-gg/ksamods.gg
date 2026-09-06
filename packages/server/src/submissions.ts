import { ORPCError } from '@orpc/server'
import { type AuthoredDocument, authorityOf, authoredDocument, renderAuthoredToml } from './authored'
import { auth } from './auth'
import { db } from './db'
import { type Repo, checkRepoPushAccess } from './github'
import {
  GitHubError,
  type Viewer,
  commitFile,
  ensureBranch,
  ensureFork,
  listingIds,
  openPullRequest,
  ownershipMarker,
  parseSlug,
  readPullRequest,
  repoFacts,
  syncFork,
  viewer,
} from './github-write'
import type { InputJsonValue } from './generated/prisma/internal/prismaNamespace'

/**
 * Opening a listing pull request on the user's behalf.
 *
 * The document is rendered server side and committed to a third party
 * repository under the submitting user's own name, so every blocker is
 * evaluated before anything is written, and nothing about a transient GitHub
 * failure is recorded against the user.
 */

const UPSTREAM_SLUG = process.env.CONTENT_INDEX_REPO ?? 'KSAModding/content-index'
const BASE_BRANCH = process.env.CONTENT_INDEX_BASE_BRANCH ?? 'automation-test'
const ENABLED = process.env.SUBMISSIONS_ENABLED !== 'false'

/** Validated at module load, so a misconfigured slug cannot redirect a call. */
const UPSTREAM: Repo = parseSlug(UPSTREAM_SLUG)

const MAX_EVENTS = 20
const LIMITS = { perUserPerHour: 3, perRowAttempts: 5, globalPerHour: 30 }

export type Blocker = { field?: string; message: string; code?: string }
export type OwnershipProof = 'owner-id' | 'topic' | 'marker' | null

export type PreviewResult = {
  /** Null when the document does not validate. */
  toml: string | null
  path: string
  baseRepo: string
  baseBranch: string
  blockers: Blocker[]
  ownership: {
    proof: OwnershipProof
    /** Whether upstream will merge this without a steward looking at it. */
    selfMerges: boolean
    /** The host ownership binds to, for display. */
    authority: string | null
    /** What the user can do to make it self merge, in upstream's own terms. */
    remediation: string[]
  }
}

type TokenResult =
  | { ok: true; token: string; account: { id: string; accountId: string } }
  | { ok: false; blocker: Blocker }

/** The GitHub token for a user, or a blocker explaining what to do about it. */
async function tokenFor(userId: string): Promise<TokenResult> {
  // providerId is the stable key; issuer is a derived string.
  const account = await db.account.findFirst({
    where: { userId, providerId: 'github' },
    select: { id: true, accountId: true },
  })
  if (!account) {
    return {
      ok: false,
      blocker: { message: 'Connect your GitHub account first', code: 'github_not_connected' },
    }
  }

  try {
    const token = await auth.api.getAccessToken({ body: { accountId: account.id, userId } })
    return { ok: true, token: (token as { accessToken: string }).accessToken, account }
  } catch {
    return {
      ok: false,
      blocker: { message: 'Reconnect GitHub and try again', code: 'github_not_connected' },
    }
  }
}

const hasWriteScope = (scopes: string[]) => scopes.includes('public_repo') || scopes.includes('repo')

/**
 * Which proof a repository offers, given everything already fetched. Pure so
 * the ordering can be tested: it mirrors tools/ownership.py verify() exactly,
 * and getting the order wrong predicts a self merge where upstream actually
 * routes the pull request to a steward.
 */
export function chooseProof(
  target: string,
  facts: { fullName: string; ownerId: number; isFork: boolean; topics: string[] } | null,
  marker: Record<string, unknown> | null,
  who: { login: string; id: number },
  listingId: string,
): OwnershipProof {
  if (!facts) return null

  // The listing points at a name that now answers as something else, so it is
  // stale and no proof on the current repository speaks for it.
  if (facts.fullName.toLowerCase() !== target.toLowerCase()) return null

  // Forks inherit files, so upstream stops here rather than reading a topic or
  // a marker that came along with the fork.
  if (facts.isFork) return null

  if (facts.ownerId === who.id) return 'owner-id'
  if (facts.topics.includes(`ksa-index-${who.login.toLowerCase()}`)) return 'topic'

  const claimed = marker?.login ?? marker?.account
  if (typeof claimed === 'string' && claimed.toLowerCase() === who.login.toLowerCase()) {
    const listing = marker?.id ?? marker?.listing
    if (listing === undefined || listing === listingId) return 'marker'
  }

  return null
}

/**
 * Which of upstream's three proofs applies, mirroring tools/ownership.py. The
 * first that applies wins.
 */
async function predictOwnership(
  token: string,
  document: AuthoredDocument,
  who: Viewer,
): Promise<{ proof: OwnershipProof; authority: string | null; pushAccess: boolean }> {
  const authority = authorityOf(document)
  if (!authority || authority.kind !== 'github') {
    // SpaceDock offers no proof a check can read, so those always wait.
    return { proof: null, authority: authority?.target ?? null, pushAccess: false }
  }

  const repo = parseSlug(authority.target)
  const facts = await repoFacts(token, repo)

  // Push access is not one of upstream's proofs. It is our own superset rule,
  // covering a legitimate org maintainer who has not set the topic yet, and it
  // is evaluated independently of the proof.
  const push = facts ? await checkRepoPushAccess(token, repo) : null
  const pushAccess = push?.kind === 'approved'

  // The marker is only fetched when the cheaper proofs cannot settle it.
  const needsMarker =
    facts !== null &&
    !facts.isFork &&
    facts.fullName.toLowerCase() === authority.target.toLowerCase() &&
    facts.ownerId !== who.id &&
    !facts.topics.includes(`ksa-index-${who.login.toLowerCase()}`)
  const marker = needsMarker ? await ownershipMarker(token, repo) : null

  const proof = chooseProof(authority.target, facts, marker, who, document.id)
  return { proof, authority: authority.target, pushAccess: proof !== null || pushAccess }
}

/** Shared by preview and submit, so what the user was shown is what is enforced. */
async function evaluate(userId: string, input: unknown) {
  const blockers: Blocker[] = []

  if (!ENABLED) {
    throw new ORPCError('SERVICE_UNAVAILABLE', { message: 'Submissions are turned off right now' })
  }

  const parsed = authoredDocument.safeParse(input)
  if (!parsed.success) {
    for (const issue of parsed.error.issues) {
      blockers.push({ field: issue.path.join('.') || undefined, message: issue.message })
    }
    return { blockers, document: null, toml: null }
  }

  const document = parsed.data
  const toml = renderAuthoredToml(document)

  const credentials = await tokenFor(userId)
  if (!credentials.ok) {
    blockers.push(credentials.blocker)
    return { blockers, document, toml }
  }

  const who = await viewer(credentials.token)
  if (!hasWriteScope(who.scopes)) {
    blockers.push({
      message: 'GitHub write access is needed to open the pull request as you',
      code: 'github_scope_required',
    })
  }

  const taken = await listingIds(credentials.token, UPSTREAM, BASE_BRANCH)
  if (taken.has(document.id.toLowerCase())) {
    // Upstream folds ids case insensitively, and this flow only adds files.
    blockers.push({ field: 'id', message: `A listing called ${document.id} already exists` })
  }

  const ownership = await predictOwnership(credentials.token, document, who)

  return { blockers, document, toml, token: credentials.token, who, ownership, account: credentials.account }
}

const remediationFor = (login: string) => [
  `Add the topic ksa-index-${login.toLowerCase()} to the repository`,
  'Or commit .github/ksa-content-index.toml naming the listing id and your username',
]

export async function previewSubmission(userId: string, input: unknown): Promise<PreviewResult> {
  const result = await evaluate(userId, input)
  const login = result.who?.login ?? ''

  const proof = result.ownership?.proof ?? null
  const isGithub = Boolean(result.ownership?.authority) && result.document?.releases?.github

  // Refusing here rather than letting upstream route it to a steward: the
  // steward queue is a shared human resource and we should not be the thing
  // that fills it. A host with no possible proof is a different case.
  if (isGithub && !proof && !result.ownership?.pushAccess && result.blockers.length === 0) {
    result.blockers.push({
      field: 'releases.github',
      message: `We could not confirm you control ${result.ownership?.authority}`,
      code: 'ownership_unproven',
    })
  }

  return {
    toml: result.toml,
    path: result.document ? `listings/${result.document.id}.toml` : '',
    baseRepo: UPSTREAM_SLUG,
    baseBranch: BASE_BRANCH,
    blockers: result.blockers,
    ownership: {
      proof,
      selfMerges: proof !== null,
      authority: result.ownership?.authority ?? null,
      remediation: proof === null && isGithub && login ? remediationFor(login) : [],
    },
  }
}

/** Postgres backed, unlike the in-process counter in claims.ts, because this
 *  one writes into somebody else's repository. */
async function enforceRateLimits(userId: string, listingId: string) {
  const since = new Date(Date.now() - 60 * 60 * 1000)

  const [mine, global, existing] = await Promise.all([
    db.listingSubmission.count({ where: { userId, createdAt: { gte: since } } }),
    db.listingSubmission.count({ where: { createdAt: { gte: since } } }),
    db.listingSubmission.findUnique({
      where: { listingId_userId: { listingId, userId } },
      select: { attempts: true },
    }),
  ])

  if (existing && existing.attempts >= LIMITS.perRowAttempts) {
    throw new ORPCError('TOO_MANY_REQUESTS', {
      message: 'Too many attempts for this listing. Ask an admin to look.',
    })
  }
  if (mine >= LIMITS.perUserPerHour) {
    throw new ORPCError('TOO_MANY_REQUESTS', { message: 'Too many submissions, try again later' })
  }
  if (global >= LIMITS.globalPerHour) {
    throw new ORPCError('SERVICE_UNAVAILABLE', { message: 'Too many submissions right now' })
  }
}

async function appendEvent(id: string, event: Record<string, unknown>) {
  const row = await db.listingSubmission.findUnique({ where: { id }, select: { events: true } })
  const log = Array.isArray(row?.events) ? (row.events as unknown[]) : []
  return [...log, { at: new Date().toISOString(), ...event }].slice(-MAX_EVENTS) as InputJsonValue
}

export type SubmissionState = 'submitting' | 'open' | 'merged' | 'closed' | 'failed'

export type SubmitResult = {
  id: string
  state: SubmissionState
  prNumber: number | null
  prUrl: string | null
  ownershipProof: OwnershipProof
}

export async function submitListing(
  userId: string,
  input: unknown,
  acknowledgeStewardReview = false,
): Promise<SubmitResult> {
  const preview = await previewSubmission(userId, input)

  // A steward-review acknowledgement clears only the unprovable case, never a
  // schema failure, a collision, or a missing scope.
  const remaining = preview.blockers.filter(
    (blocker) => !(blocker.code === 'ownership_unproven' && acknowledgeStewardReview),
  )
  if (remaining.length > 0) {
    throw new ORPCError('BAD_REQUEST', {
      message: remaining[0]!.message,
      data: { blockers: remaining },
    })
  }

  const result = await evaluate(userId, input)
  if (!result.document || !result.toml || !result.token || !result.who) {
    throw new ORPCError('BAD_REQUEST', { message: 'That listing could not be prepared' })
  }
  const { document, toml, token, who } = result

  if (!preview.ownership.proof && !acknowledgeStewardReview) {
    throw new ORPCError('BAD_REQUEST', {
      message: 'This cannot be verified automatically, so it needs your acknowledgement',
      data: { blockers: [{ code: 'ownership_unproven', message: 'Acknowledgement required' }] },
    })
  }

  await enforceRateLimits(userId, document.id)

  // Somebody else already has an open submission for this id. Upstream would
  // reject the second pull request, so do not be the thing that opens it.
  const other = await db.listingSubmission.findFirst({
    where: { listingId: document.id, state: { in: ['submitting', 'open'] }, userId: { not: userId } },
    select: { id: true },
  })
  if (other) {
    throw new ORPCError('CONFLICT', { message: 'Somebody else has an open submission for this id' })
  }

  // Reserve the row before any write, so a double click loses the unique race.
  const row = await db.listingSubmission.upsert({
    where: { listingId_userId: { listingId: document.id, userId } },
    create: {
      listingId: document.id,
      userId,
      state: 'submitting',
      toml,
      document: document as unknown as InputJsonValue,
      githubLogin: who.login,
      githubUserId: String(who.id),
      baseRepo: UPSTREAM_SLUG,
      baseBranch: BASE_BRANCH,
      ownershipProof: preview.ownership.proof,
      acknowledged: acknowledgeStewardReview,
      attempts: 1,
    },
    update: {
      state: 'submitting',
      toml,
      document: document as unknown as InputJsonValue,
      githubLogin: who.login,
      githubUserId: String(who.id),
      ownershipProof: preview.ownership.proof,
      acknowledged: acknowledgeStewardReview,
      attempts: { increment: 1 },
      error: null,
    },
  })

  const branch = `ksamods/${document.id}`

  try {
    const fork = await ensureFork(token, UPSTREAM, who.login)
    await syncFork(token, fork, BASE_BRANCH)
    await ensureBranch(token, fork, UPSTREAM, BASE_BRANCH, branch)

    const commitSha = await commitFile(
      token,
      fork,
      branch,
      `listings/${document.id}.toml`,
      toml,
      `Add listings/${document.id}.toml`,
    )

    const pull = await openPullRequest(
      token,
      UPSTREAM,
      BASE_BRANCH,
      `${fork.owner}:${branch}`,
      `Add ${document.id}`,
      pullRequestBody(document, preview.ownership.proof),
    )

    const updated = await db.listingSubmission.update({
      where: { id: row.id },
      data: {
        state: 'open',
        headRepo: `${fork.owner}/${fork.repo}`,
        headBranch: branch,
        commitSha,
        prNumber: pull.number,
        prUrl: pull.url,
        refreshedAt: new Date(),
        events: await appendEvent(row.id, { outcome: 'opened', pr: pull.number }),
      },
    })

    return {
      id: updated.id,
      state: updated.state,
      prNumber: updated.prNumber,
      prUrl: updated.prUrl,
      ownershipProof: preview.ownership.proof,
    }
  } catch (error) {
    const retryable = error instanceof GitHubError && error.retryable
    const message = error instanceof Error ? error.message : 'the submission failed'

    await db.listingSubmission.update({
      where: { id: row.id },
      data: {
        // A transient GitHub failure is not the user's fault and must stay
        // retryable, same discipline as claims.ts.
        state: 'failed',
        error: message,
        events: await appendEvent(row.id, { outcome: retryable ? 'retryable' : 'failed', message }),
      },
    })

    throw new ORPCError(retryable ? 'SERVICE_UNAVAILABLE' : 'BAD_REQUEST', { message })
  }
}

/**
 * Machine generated, interpolating only the validated id and forums URL. No
 * user prose: the document is already in the diff, and echoing it would be a
 * markdown injection surface into a third party repo under someone else's name.
 */
function pullRequestBody(document: AuthoredDocument, proof: OwnershipProof) {
  const lines = [
    `Adds \`listings/${document.id}.toml\`.`,
    '',
    `Forums thread: ${document.links.forums}`,
    '',
    proof
      ? `Ownership should verify through the ${proof} proof.`
      : 'Ownership could not be established automatically, so this needs a steward.',
    '',
    'Opened from ksamods.gg.',
    `<!-- ksamods.gg:submission:${document.id} -->`,
  ]
  return lines.join('\n')
}

export async function mySubmissions(userId: string) {
  return db.listingSubmission.findMany({
    where: { userId },
    select: {
      id: true,
      listingId: true,
      state: true,
      prNumber: true,
      prUrl: true,
      ownershipProof: true,
      error: true,
      createdAt: true,
    },
    orderBy: { updatedAt: 'desc' },
  })
}

/** Reads the pull request on demand, on the user's own token. */
export async function refreshSubmission(userId: string, id: string) {
  const row = await db.listingSubmission.findFirst({
    where: { id, userId },
    select: { id: true, prNumber: true, state: true },
  })
  if (!row) throw new ORPCError('NOT_FOUND', { message: 'No such submission' })
  if (!row.prNumber) return { state: row.state }

  const credentials = await tokenFor(userId)
  if (!credentials.ok) return { state: row.state }

  const pull = await readPullRequest(credentials.token, UPSTREAM, row.prNumber)
  if (!pull) return { state: row.state }

  const state = pull.merged ? 'merged' : pull.state === 'closed' ? 'closed' : 'open'
  await db.listingSubmission.update({
    where: { id: row.id },
    data: { state, refreshedAt: new Date() },
  })
  return { state }
}
