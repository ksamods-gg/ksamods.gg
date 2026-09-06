import { expect, test } from 'bun:test'
import { chooseProof } from './submissions'

const who = { login: 'SafeShows', id: 49234603 }

const repo = (patch: Partial<{ fullName: string; ownerId: number; isFork: boolean; topics: string[] }> = {}) => ({
  fullName: 'Someone/Thing',
  ownerId: 1,
  isFork: false,
  topics: [],
  ...patch,
})

test('a repository you own proves ownership', () => {
  expect(chooseProof('Someone/Thing', repo({ ownerId: who.id }), null, who, 'Thing')).toBe('owner-id')
})

test('the index topic proves ownership on a repository you do not own', () => {
  const facts = repo({ topics: ['unrelated', 'ksa-index-safeshows'] })
  expect(chooseProof('Someone/Thing', facts, null, who, 'Thing')).toBe('topic')
})

test('the topic is matched lowercased, as upstream writes it', () => {
  // Upstream formats the topic with login.lower(), and GitHub lowercases
  // topics, so a mixed case login must still match.
  expect(chooseProof('Someone/Thing', repo({ topics: ['ksa-index-safeshows'] }), null, who, 'Thing')).toBe(
    'topic',
  )
  expect(chooseProof('Someone/Thing', repo({ topics: ['ksa-index-SafeShows'] }), null, who, 'Thing')).toBeNull()
})

test.each([
  ['naming only a login, which covers every listing', { login: 'safeshows' }],
  ['using the account alias', { account: 'SafeShows' }],
  ['naming this listing explicitly', { login: 'SafeShows', id: 'Thing' }],
  ['using the listing alias', { login: 'SafeShows', listing: 'Thing' }],
])('the marker file proves ownership when %s', (_label, marker) => {
  expect(chooseProof('Someone/Thing', repo(), marker, who, 'Thing')).toBe('marker')
})

test.each([
  ['it names somebody else', { login: 'SomeoneElse' }],
  ['it names a different listing', { login: 'SafeShows', id: 'OtherThing' }],
  ['it names nobody', { id: 'Thing' }],
])('the marker file proves nothing when %s', (_label, marker) => {
  expect(chooseProof('Someone/Thing', repo(), marker, who, 'Thing')).toBeNull()
})

// The two rules that are easy to get wrong, and whose failure mode is telling
// the user their listing self merges when upstream will hand it to a steward.

test('a fork proves nothing, even one you own', () => {
  // Forks inherit files, so upstream returns unverified before it ever reads a
  // topic or a marker.
  const facts = repo({ ownerId: who.id, isFork: true, topics: ['ksa-index-safeshows'] })
  expect(chooseProof('Someone/Thing', facts, { login: 'SafeShows' }, who, 'Thing')).toBeNull()
})

test('a renamed repository proves nothing, because the listing is stale', () => {
  const facts = repo({ fullName: 'Someone/Renamed', ownerId: who.id })
  expect(chooseProof('Someone/Thing', facts, null, who, 'Thing')).toBeNull()
})

test('a repository we cannot see proves nothing', () => {
  // Private and deleted are indistinguishable over the API, so neither may be
  // read as a proof or as a denial.
  expect(chooseProof('Someone/Thing', null, null, who, 'Thing')).toBeNull()
})

test('a repository belonging to somebody else proves nothing', () => {
  expect(chooseProof('Someone/Thing', repo(), null, who, 'Thing')).toBeNull()
})

test('the target is compared case insensitively', () => {
  const facts = repo({ fullName: 'SomeOne/Thing', ownerId: who.id })
  expect(chooseProof('someone/thing', facts, null, who, 'Thing')).toBe('owner-id')
})
