import { db } from './db'
import {
  type ContentIndex,
  type ContentListing,
  contentIndexSchema,
} from './content-index-schema'

const URL_DEFAULT = 'https://ksamodding.github.io/content-index-releases/v1/index.json'
const FETCH_TIMEOUT_MS = 15_000
const MAX_BYTES = 16 * 1024 * 1024

const url = process.env.CONTENT_INDEX_URL ?? URL_DEFAULT
const intervalMs = Number(process.env.CONTENT_INDEX_INTERVAL_MS) || 15 * 60 * 1000

/**
 * Upstream is untrusted input: validate the shape before it reaches the
 * database. The verbatim document is returned rather than zod's output, because
 * the schema strips keys it does not know about and the stored snapshot should
 * stay byte-faithful to upstream.
 */
export function parseIndex(text: string): ContentIndex {
  const doc: unknown = JSON.parse(text)
  contentIndexSchema.parse(doc)
  return doc as ContentIndex
}

export type SyncResult =
  | { status: 'unchanged'; reason: 'not-modified' | 'same-commit' }
  | { status: 'stored'; generatedCommit: string; listings: number }

/**
 * Fetch the content index and store it if it changed. Conditional on the
 * previous ETag, then on the generated commit, so an unchanged upstream costs
 * one 304 and no write.
 */
export async function syncContentIndex(): Promise<SyncResult> {
  const previous = await db.contentSnapshot.findFirst({
    orderBy: { fetchedAt: 'desc' },
    select: { etag: true, generatedCommit: true },
  })

  const response = await fetch(url, {
    headers: previous?.etag ? { 'If-None-Match': previous.etag } : {},
    signal: AbortSignal.timeout(FETCH_TIMEOUT_MS),
    redirect: 'follow',
  })

  if (response.status === 304) return { status: 'unchanged', reason: 'not-modified' }
  if (!response.ok) throw new Error(`content index responded ${response.status}`)

  const size = Number(response.headers.get('content-length'))
  if (size > MAX_BYTES) throw new Error(`content index too large: ${size} bytes`)

  const text = await response.text()
  if (text.length > MAX_BYTES) throw new Error(`content index too large: ${text.length} bytes`)

  const index = parseIndex(text)
  const generatedCommit = index.sources.generated.commit

  const snapshot = {
    snapshotVersion: index.snapshot_version,
    authoredCommit: index.sources.authored.commit,
    generatedCommit,
    etag: response.headers.get('etag'),
    listingCount: index.listings.length,
    data: index as object,
    fetchedAt: new Date(),
  }

  // Upsert rather than skip on same commit: a republished index keeps the
  // commit but may change content, and this keeps one row per upstream release.
  await db.contentSnapshot.upsert({
    where: { generatedCommit },
    create: snapshot,
    update: snapshot,
  })

  return previous?.generatedCommit === generatedCommit
    ? { status: 'unchanged', reason: 'same-commit' }
    : { status: 'stored', generatedCommit, listings: index.listings.length }
}

// ponytail: a plain in-process interval, fine for one server process. Two
// instances would both poll and race on the upsert; move to a real scheduler
// or a Postgres advisory lock if this ever runs more than once.
export function startContentIndexWorker() {
  const run = () =>
    syncContentIndex()
      .then((result) => console.log(`[content-index] ${JSON.stringify(result)}`))
      .catch((error) => console.error(`[content-index] sync failed:`, error.message))

  run()
  const timer = setInterval(run, intervalMs)
  timer.unref?.()
  return () => clearInterval(timer)
}

export type ContentSnapshot = {
  snapshotVersion: number
  authoredCommit: string
  generatedCommit: string
  listingCount: number
  fetchedAt: Date
  data: ContentIndex
}

/**
 * Latest stored snapshot, with `data` typed. Prisma types a Json column as
 * JsonValue; the cast is sound because every row is written through
 * `parseIndex`, which validates against the same schema.
 */
export async function latestSnapshot(): Promise<ContentSnapshot | null> {
  const row = await db.contentSnapshot.findFirst({
    orderBy: { fetchedAt: 'desc' },
    select: {
      snapshotVersion: true,
      authoredCommit: true,
      generatedCommit: true,
      listingCount: true,
      fetchedAt: true,
      data: true,
    },
  })

  return row && { ...row, data: row.data as unknown as ContentIndex }
}

/**
 * One listing from the newest snapshot, or null when the id is unknown.
 * ponytail: reads the whole snapshot jsonb per call, which is free at four
 * listings. Switch to a jsonb path projection when the index grows.
 */
export async function findListing(id: string): Promise<ContentListing | null> {
  const snapshot = await latestSnapshot()
  return snapshot?.data.listings.find((listing) => listing.id === id) ?? null
}
