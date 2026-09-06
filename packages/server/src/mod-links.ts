/**
 * Linking upstream listings to our own mod rows through the metadata bag.
 *
 * A listing opts in by carrying `[metadata.ksamods-gg]` with an `id` naming the
 * mod row it belongs to. Nothing upstream validates our namespace, so any author
 * who can get a listing merged can put any string there, and the link has to be
 * treated as a claim rather than a fact.
 */

/** RFC 0051 examples use this spelling, and namespaces fold case but never
 *  punctuation, so `ksamods_gg` would be a different namespace entirely. */
export const NAMESPACE = 'ksamods-gg'

export type ListingLink = {
  /** The upstream listing id. */
  listingId: string
  /** What the listing claims it belongs to, or null when it does not opt in. */
  claimedModId: string | null
}

/** A mod row as far as linking is concerned: an id, and the listing it is
 *  bound to. The binding is ours and is what an impostor cannot change. */
export type ModBinding = { id: string; listingId: string }

export type LinkIssue = {
  kind: 'unknown-id' | 'wrong-listing' | 'orphaned'
  listingId: string
  claimedModId: string | null
  /** For a contested claim, the listing the mod row is actually bound to. */
  boundTo?: string
  detail: string
}

export type LinkResult = {
  /** Listing ids that resolve to a mod row cleanly, keyed to that row. */
  linked: Map<string, string>
  /** Listing ids to hide from end users until an admin resolves them. */
  hidden: Set<string>
  issues: LinkIssue[]
}

/**
 * Resolves every listing's claim against our rows.
 *
 * The incumbent always wins. A mod row records the listing it belongs to, and a
 * second listing naming the same row is hidden rather than both of them, because
 * hiding both would let anyone take a competitor down by copying their id into a
 * listing of their own. Nothing about that costs the impostor more than one pull
 * request, so the rule has to be the one that cannot be weaponised.
 */
export function resolveLinks(listings: ListingLink[], mods: ModBinding[]): LinkResult {
  const byId = new Map(mods.map((mod) => [mod.id, mod]))
  const linked = new Map<string, string>()
  const hidden = new Set<string>()
  const issues: LinkIssue[] = []

  for (const listing of listings) {
    if (!listing.claimedModId) continue // not managed by us, and that is fine

    const mod = byId.get(listing.claimedModId)

    if (!mod) {
      hidden.add(listing.listingId)
      issues.push({
        kind: 'unknown-id',
        listingId: listing.listingId,
        claimedModId: listing.claimedModId,
        detail: `names a mod id we do not have`,
      })
      continue
    }

    if (mod.listingId !== listing.listingId) {
      // The row is bound elsewhere, so this listing is claiming somebody else's.
      hidden.add(listing.listingId)
      issues.push({
        kind: 'wrong-listing',
        listingId: listing.listingId,
        claimedModId: listing.claimedModId,
        boundTo: mod.listingId,
        detail: `claims a mod id already bound to ${mod.listingId}`,
      })
      continue
    }

    linked.set(listing.listingId, mod.id)
  }

  // A row whose listing stopped carrying the namespace, or vanished upstream.
  // Not hidden, because there is nothing to hide: it is our row that is stale.
  const claiming = new Set(
    listings.filter((listing) => listing.claimedModId).map((listing) => listing.listingId),
  )
  for (const mod of mods) {
    if (!claiming.has(mod.listingId)) {
      issues.push({
        kind: 'orphaned',
        listingId: mod.listingId,
        claimedModId: mod.id,
        detail: `no longer claimed by its listing`,
      })
    }
  }

  return { linked, hidden, issues }
}

/** The mod id a listing claims, or null. Shaped defensively: this is untrusted
 *  upstream text, and the bag holds whatever its author put there. */
export function claimedModId(authored: unknown): string | null {
  const metadata = (authored as { metadata?: unknown })?.metadata
  if (typeof metadata !== 'object' || metadata === null) return null

  const ours = (metadata as Record<string, unknown>)[NAMESPACE]
  if (typeof ours !== 'object' || ours === null) return null

  const id = (ours as Record<string, unknown>).id
  return typeof id === 'string' && id.length > 0 && id.length <= 64 ? id : null
}

/**
 * The mod row id for a listing.
 *
 * Derived rather than random so a preview can show the exact file that will be
 * committed without writing a row first, and so a retry produces the same id.
 * There is nothing secret about it: the value ends up in a public listing file,
 * and resolveLinks never treats knowing an id as evidence of anything.
 */
export function modIdFor(listingId: string) {
  const digest = new Bun.CryptoHasher('sha256')
    .update(`ksamods.gg:mod:${listingId}`)
    .digest('hex')
  return `mod_${digest.slice(0, 24)}`
}
