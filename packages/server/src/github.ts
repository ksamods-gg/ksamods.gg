import type { ContentAuthored } from './content-index-schema'

const API = 'https://api.github.com'
const REQUEST_TIMEOUT_MS = 10_000

/** GitHub's own limits. Validating these is a trust boundary, not cosmetics:
 *  an unchecked segment containing ".." or "/" would redirect the API call. */
const SLUG = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})\/[A-Za-z0-9._-]{1,100}$/

export type Repo = { owner: string; repo: string }

/**
 * The repository a listing declares. Resolved server side from the snapshot so
 * a claimant can never aim the check at a repository they control.
 */
export function resolveRepo(authored: ContentAuthored): Repo | null {
  const slug = authored.releases?.github
  if (typeof slug === 'string' && SLUG.test(slug)) {
    const [owner, repo] = slug.split('/')
    return { owner: owner!, repo: repo! }
  }

  const url = authored.links?.repository
  if (typeof url !== 'string') return null

  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return null
  }
  if (parsed.hostname !== 'github.com' && parsed.hostname !== 'www.github.com') return null

  const segments = parsed.pathname.split('/').filter(Boolean)
  if (segments.length < 2) return null

  const owner = segments[0]!
  const repo = segments[1]!.replace(/\.git$/, '')
  return SLUG.test(`${owner}/${repo}`) ? { owner, repo } : null
}

export type Outcome =
  /** Push access proven. */
  | { kind: 'approved'; repoId?: number; repoFullName?: string; permissions: unknown }
  /** Reached GitHub, and the account demonstrably lacks push access. */
  | { kind: 'rejected'; reason: string }
  /** Ambiguous. Needs a human, never an automatic approval. */
  | { kind: 'pending'; reason: string }
  /** Transient. Record nothing against the user. */
  | { kind: 'retry'; reason: string }

type Header = (name: string) => string | null | undefined

/**
 * Every ambiguous case fails towards pending, never towards approved. A
 * private repository and a deleted one are indistinguishable over this API,
 * so neither may be treated as a denial.
 */
export function decide(status: number, body: unknown, header: Header = () => null): Outcome {
  if (status === 200) {
    const repo = body as { permissions?: Record<string, boolean>; id?: number; full_name?: string }
    const permissions = repo?.permissions
    if (!permissions) {
      return { kind: 'pending', reason: 'GitHub returned no permissions for this repository' }
    }
    // push covers admin, maintain and write, and already folds in org role and
    // team access. Checking admin would reject legitimate maintainers.
    if (permissions.push === true) {
      return { kind: 'approved', repoId: repo.id, repoFullName: repo.full_name, permissions }
    }
    return { kind: 'rejected', reason: 'no push access to the declared repository' }
  }

  if (status === 404) {
    return { kind: 'pending', reason: 'repository not visible to this account' }
  }
  if (status === 401) {
    return { kind: 'retry', reason: 'GitHub rejected the token, reconnect GitHub' }
  }
  if (status === 403) {
    if (header('x-github-sso')) {
      return { kind: 'pending', reason: 'organization requires SAML authorization for this token' }
    }
    if (header('x-ratelimit-remaining') === '0') {
      return { kind: 'retry', reason: 'GitHub rate limit reached' }
    }
    return { kind: 'pending', reason: 'GitHub refused the request' }
  }
  if (status === 429) {
    return { kind: 'retry', reason: 'GitHub rate limit reached' }
  }
  return { kind: 'retry', reason: `GitHub responded ${status}` }
}

/** Performs the call and maps it. Network and timeout failures are retryable. */
export async function checkRepoPushAccess(
  accessToken: string,
  { owner, repo }: Repo,
): Promise<Outcome> {
  let response: Response
  try {
    response = await fetch(`${API}/repos/${owner}/${repo}`, {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        // GitHub rejects requests without one.
        'User-Agent': 'ksamods.gg',
      },
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      redirect: 'follow',
    })
  } catch {
    return { kind: 'retry', reason: 'could not reach GitHub' }
  }

  const body = await response.json().catch(() => null)
  return decide(response.status, body, (name) => response.headers.get(name))
}
