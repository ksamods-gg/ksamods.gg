import { type Repo, gh } from './github'

/**
 * The write half of the GitHub integration: fork, branch, commit one file, open
 * a pull request. Every call runs on the submitting user's own token, because
 * upstream's ownership check reads the pull request author's account
 * (tools/decide.py passes pull.user.login and pull.user.id into verify_change).
 * A bot authored pull request would fail verification for everybody.
 */

/** GitHub's own limits, same trust boundary reasoning as the SLUG regex. */
const SLUG = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})\/[A-Za-z0-9._-]{1,100}$/

export function parseSlug(slug: string): Repo {
  if (!SLUG.test(slug)) throw new Error(`not a repository slug: ${slug}`)
  const [owner, repo] = slug.split('/')
  return { owner: owner!, repo: repo! }
}

export class GitHubError extends Error {
  constructor(
    message: string,
    /** Retryable means the caller must record nothing against the user. */
    readonly retryable: boolean,
    readonly status?: number,
  ) {
    super(message)
  }
}

const fail = (response: { status: number }, what: string): never => {
  // Zero is a network failure, 5xx and 429 are GitHub's problem, and 403 is
  // usually a rate limit. None of them say anything about the user's input.
  const retryable =
    response.status === 0 || response.status === 429 || response.status >= 500 || response.status === 403
  throw new GitHubError(`${what} (GitHub responded ${response.status})`, retryable, response.status)
}

export type Viewer = { login: string; id: number; scopes: string[] }

/**
 * The authenticated user, and the scopes GitHub says the token actually holds.
 *
 * The x-oauth-scopes header is the only ground truth. Our own Account.scope
 * column is merged optimistically at link time and goes stale the moment
 * somebody revokes the authorization on GitHub.
 */
export async function viewer(token: string): Promise<Viewer> {
  const response = await gh<{ login?: string; id?: number }>(token, '/user')
  if (response.status !== 200 || !response.body?.login || typeof response.body.id !== 'number') {
    fail(response, 'could not read your GitHub account')
  }

  const header = response.header('x-oauth-scopes') ?? ''
  const scopes = header
    .split(',')
    .map((scope) => scope.trim())
    .filter(Boolean)

  return { login: response.body.login!, id: response.body.id!, scopes }
}

/** The stems of every listing on a branch, lowercased. Upstream folds ids case
 *  insensitively (check_index.py), so a per-file probe would miss StarMap
 *  against starmap. */
export async function listingIds(token: string, upstream: Repo, ref: string): Promise<Set<string>> {
  const response = await gh<{ name: string; type: string }[]>(
    token,
    `/repos/${upstream.owner}/${upstream.repo}/contents/listings?ref=${encodeURIComponent(ref)}`,
  )
  if (response.status !== 200 || !Array.isArray(response.body)) {
    fail(response, `could not read the listings on ${ref}`)
  }

  return new Set(
    response.body
      .filter((entry) => entry.type === 'file' && entry.name.toLowerCase().endsWith('.toml'))
      .map((entry) => entry.name.slice(0, -'.toml'.length).toLowerCase()),
  )
}

export type RepoFacts = {
  fullName: string
  ownerId: number
  isFork: boolean
  topics: string[]
}

/** Null when the repository is not visible to this token, which is
 *  indistinguishable from deleted and must never be read as a denial. */
export async function repoFacts(token: string, repo: Repo): Promise<RepoFacts | null> {
  const response = await gh<{
    full_name?: string
    fork?: boolean
    owner?: { id?: number }
    topics?: string[]
  }>(token, `/repos/${repo.owner}/${repo.repo}`)

  if (response.status === 404) return null
  if (response.status !== 200 || !response.body?.full_name) {
    fail(response, `could not read ${repo.owner}/${repo.repo}`)
  }

  return {
    fullName: response.body.full_name!,
    ownerId: response.body.owner?.id ?? -1,
    isFork: response.body.fork === true,
    topics: response.body.topics ?? [],
  }
}

/** The `.github/ksa-content-index.toml` marker, parsed. Null when absent or
 *  unparseable, mirroring upstream's own `except TOMLDecodeError` behaviour. */
export async function ownershipMarker(
  token: string,
  repo: Repo,
): Promise<Record<string, unknown> | null> {
  const response = await gh<{ content?: string; encoding?: string }>(
    token,
    `/repos/${repo.owner}/${repo.repo}/contents/.github/ksa-content-index.toml`,
  )
  if (response.status !== 200 || !response.body?.content) return null

  try {
    const text = Buffer.from(response.body.content, 'base64').toString('utf8')
    return Bun.TOML.parse(text) as Record<string, unknown>
  } catch {
    return null
  }
}

/**
 * The user's fork of upstream, creating it when it does not exist yet.
 *
 * Fork creation is asynchronous: GitHub answers 202 before the repository is
 * usable, so this polls. It also renames on collision, which is why the name is
 * read back from the response rather than assumed.
 */
export async function ensureFork(token: string, upstream: Repo, login: string): Promise<Repo> {
  const existing = await repoFacts(token, { owner: login, repo: upstream.repo })
  if (existing?.isFork) return { owner: login, repo: upstream.repo }

  const created = await gh<{ full_name?: string }>(
    token,
    `/repos/${upstream.owner}/${upstream.repo}/forks`,
    { method: 'POST', body: {} },
  )
  if (created.status !== 202 && created.status !== 200) fail(created, 'could not fork the index')

  const slug = created.body?.full_name
  if (!slug) throw new GitHubError('GitHub did not name the fork it created', true)
  const fork = parseSlug(slug)

  for (let attempt = 0; attempt < 15; attempt++) {
    if (await repoFacts(token, fork)) return fork
    await Bun.sleep(2000)
  }
  throw new GitHubError('GitHub is still creating your fork, try again in a minute', true)
}

/** Fast-forwards a stale fork. Best effort: a diverged fork is not an error
 *  here, because the branch is created from a known good SHA either way. */
export async function syncFork(token: string, fork: Repo, branch: string): Promise<void> {
  await gh(token, `/repos/${fork.owner}/${fork.repo}/merge-upstream`, {
    method: 'POST',
    body: { branch },
  })
}

/** The tip of a branch. */
async function refSha(token: string, repo: Repo, branch: string): Promise<string | null> {
  const response = await gh<{ object?: { sha?: string } }>(
    token,
    `/repos/${repo.owner}/${repo.repo}/git/ref/heads/${encodeURIComponent(branch)}`,
  )
  if (response.status !== 200) return null
  return response.body?.object?.sha ?? null
}

/**
 * Creates the working branch on the fork. An existing branch is success, which
 * is what makes a retry safe after a partial failure.
 */
export async function ensureBranch(
  token: string,
  fork: Repo,
  upstream: Repo,
  base: string,
  branch: string,
): Promise<void> {
  // The fork's own ref first, so the normal path does not depend on forks
  // sharing object storage with their parent.
  const sha = (await refSha(token, fork, base)) ?? (await refSha(token, upstream, base))
  if (!sha) throw new GitHubError(`the ${base} branch could not be found`, true)

  const response = await gh<{ message?: string }>(token, `/repos/${fork.owner}/${fork.repo}/git/refs`, {
    method: 'POST',
    body: { ref: `refs/heads/${branch}`, sha },
  })

  if (response.status === 201) return
  if (response.status === 422 && /already exists/i.test(response.body?.message ?? '')) return
  fail(response, 'could not create the branch')
}

/** Commits exactly one file. CONTRIBUTING is explicit that a pull request
 *  carrying more than one document waits for a steward. */
export async function commitFile(
  token: string,
  fork: Repo,
  branch: string,
  path: string,
  contents: string,
  message: string,
): Promise<string> {
  const encoded = Buffer.from(contents, 'utf8').toString('base64')
  const url = `/repos/${fork.owner}/${fork.repo}/contents/${path}`

  const write = (sha?: string) =>
    gh<{ commit?: { sha?: string }; message?: string }>(token, url, {
      method: 'PUT',
      body: { message, content: encoded, branch, ...(sha ? { sha } : {}) },
    })

  let response = await write()

  // The file is already on our branch from an earlier attempt, so this is an
  // update rather than a create. Still one file, still one commit.
  if (response.status === 422 || response.status === 409) {
    const existing = await gh<{ sha?: string }>(
      token,
      `${url}?ref=${encodeURIComponent(branch)}`,
    )
    if (existing.status === 200 && existing.body?.sha) response = await write(existing.body.sha)
  }

  if (response.status !== 200 && response.status !== 201) fail(response, 'could not commit the listing')
  return response.body?.commit?.sha ?? ''
}

export type PullRequest = { number: number; url: string; state: string; merged: boolean }

/**
 * Opens the pull request, or adopts the one that is already open for this
 * branch. That adoption is the idempotency backstop: a retry never opens a
 * second pull request.
 */
export async function openPullRequest(
  token: string,
  upstream: Repo,
  base: string,
  head: string,
  title: string,
  body: string,
): Promise<PullRequest> {
  const path = `/repos/${upstream.owner}/${upstream.repo}/pulls`
  const response = await gh<{ number?: number; html_url?: string; message?: string }>(token, path, {
    method: 'POST',
    body: { title, body, head, base, maintainer_can_modify: true },
  })

  if (response.status === 201 && response.body?.number) {
    return {
      number: response.body.number,
      url: response.body.html_url ?? '',
      state: 'open',
      merged: false,
    }
  }

  if (response.status === 422) {
    const open = await gh<{ number: number; html_url: string }[]>(
      token,
      `${path}?head=${encodeURIComponent(head)}&state=open`,
    )
    const first = Array.isArray(open.body) ? open.body[0] : undefined
    if (first) return { number: first.number, url: first.html_url, state: 'open', merged: false }
  }

  return fail(response, 'could not open the pull request')
}

/** The current state of a pull request, for the account page. */
export async function readPullRequest(
  token: string,
  upstream: Repo,
  number: number,
): Promise<PullRequest | null> {
  const response = await gh<{ number?: number; html_url?: string; state?: string; merged?: boolean }>(
    token,
    `/repos/${upstream.owner}/${upstream.repo}/pulls/${number}`,
  )
  if (response.status !== 200 || !response.body?.number) return null

  return {
    number: response.body.number,
    url: response.body.html_url ?? '',
    state: response.body.state ?? 'open',
    merged: response.body.merged === true,
  }
}
