import type { orpc } from '@/lib/orpc'

type Snapshot = NonNullable<Awaited<ReturnType<typeof orpc.contentIndex.latest>>>

export type Listing = Snapshot['data']['listings'][number]
export type Release = Listing['releases'][number]

/** Game builds are dotted, but the trailing segment is a monotonic revision. */
export const revisionOf = (version: string) => Number(version.split('.').pop()) || 0

/** Upstream lists newest first, but do not rely on that for a version label. */
export const latestRelease = (listing: Listing): Release | undefined =>
  [...listing.releases].sort((a, b) => b.release_date.localeCompare(a.release_date))[0]

export function supportsGameVersion(listing: Listing, version: string) {
  if (version === 'any') return true
  const revision = revisionOf(version)
  return listing.releases.some(
    (release) =>
      release.game_min_revision <= revision &&
      (release.game_max_revision == null || revision <= release.game_max_revision),
  )
}

/**
 * The GitHub repository a listing declares, mirroring the server's resolveRepo.
 * Display only: verification always resolves this again server side.
 */
export function githubSlug(listing: Listing): string | null {
  const slug = listing.authored.releases?.github
  if (typeof slug === 'string' && /^[\w.-]+\/[\w.-]+$/.test(slug)) return slug

  const url = listing.authored.links?.repository
  if (typeof url !== 'string') return null
  try {
    const parsed = new URL(url)
    if (parsed.hostname !== 'github.com' && parsed.hostname !== 'www.github.com') return null
    const [owner, repo] = parsed.pathname.split('/').filter(Boolean)
    return owner && repo ? `${owner}/${repo.replace(/\.git$/, '')}` : null
  } catch {
    return null
  }
}
