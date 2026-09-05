import { expect, test } from 'bun:test'
import fixture from './content-index.fixture.json'
import { contentIndexSchema } from './content-index-schema'
import { decide, resolveRepo } from './github'

const index = contentIndexSchema.parse(fixture)
const authoredFor = (id: string) => index.listings.find((l) => l.id === id)!.authored

test('resolves a repo for every listing in the real index', () => {
  for (const listing of index.listings) {
    expect(resolveRepo(listing.authored)).not.toBeNull()
  }
})

test('resolves the org-owned listing', () => {
  expect(resolveRepo(authoredFor('StarMap'))).toEqual({ owner: 'StarMapLoader', repo: 'StarMap' })
})

test('falls back to links.repository when releases.github is absent', () => {
  const authored = structuredClone(authoredFor('MeasureTools'))
  delete authored.releases
  authored.links.repository = 'https://github.com/Maxi/MeasureTools.git'
  expect(resolveRepo(authored)).toEqual({ owner: 'Maxi', repo: 'MeasureTools' })
})

test.each([
  ['a non-GitHub repository link', 'https://gitlab.com/someone/thing'],
  ['a lookalike host', 'https://github.com.evil.example/someone/thing'],
  ['a single path segment', 'https://github.com/someone'],
  ['not a url at all', 'someone/thing'],
])('returns null for %s', (_label, repository) => {
  const authored = structuredClone(authoredFor('MeasureTools'))
  delete authored.releases
  authored.links.repository = repository
  expect(resolveRepo(authored)).toBeNull()
})

test.each([
  ['path traversal', '../../etc/passwd'],
  ['an extra segment', 'owner/repo/extra'],
  ['an empty owner', '/repo'],
])('rejects %s in releases.github', (_label, slug) => {
  const authored = structuredClone(authoredFor('MeasureTools'))
  authored.releases = { github: slug }
  delete authored.links.repository
  expect(resolveRepo(authored)).toBeNull()
})

test('approves on push access', () => {
  const outcome = decide(200, { id: 1, full_name: 'a/b', permissions: { push: true, admin: false } })
  expect(outcome.kind).toBe('approved')
})

test('approves a maintainer who is not an admin', () => {
  // rthom91/yourcontrols in the live probe: push true, admin false. Checking
  // admin instead of push would reject this legitimate maintainer.
  expect(decide(200, { permissions: { admin: false, maintain: false, push: true } }).kind).toBe(
    'approved',
  )
})

test('rejects read-only access', () => {
  expect(decide(200, { permissions: { push: false, pull: true } }).kind).toBe('rejected')
})

test.each([
  ['permissions absent', 200, {}, {}],
  ['not visible', 404, null, {}],
  ['SAML enforcement', 403, null, { 'x-github-sso': 'required' }],
  ['a bare refusal', 403, null, {}],
])('stays pending for %s', (_label, status, body, headers) => {
  const outcome = decide(status as number, body, (n) => (headers as Record<string, string>)[n])
  expect(outcome.kind).toBe('pending')
})

test.each([
  ['a bad token', 401, {}],
  ['rate limiting', 403, { 'x-ratelimit-remaining': '0' }],
  ['too many requests', 429, {}],
  ['a server error', 502, {}],
])('is retryable for %s', (_label, status, headers) => {
  const outcome = decide(status as number, null, (n) => (headers as Record<string, string>)[n])
  expect(outcome.kind).toBe('retry')
})

test('never approves without an explicit push true', () => {
  for (const body of [null, {}, { permissions: {} }, { permissions: { push: 'yes' } }]) {
    expect(decide(200, body).kind).not.toBe('approved')
  }
})
