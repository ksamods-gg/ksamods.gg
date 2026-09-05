import { expect, test } from 'bun:test'
import fixture from './content-index.fixture.json'
import { parseIndex } from './content-index'
import { contentIndexSchema } from './content-index-schema'

const valid = {
  snapshot_version: 1,
  sources: {
    authored: { repository: 'a/b', commit: 'a' },
    generated: { repository: 'c/d', commit: 'b' },
  },
  listings: [],
}

test('accepts the real upstream document', () => {
  const index = parseIndex(JSON.stringify(fixture))
  expect(index.listings).toHaveLength(4)
  // Field access here is the point: these are typed, not JsonValue.
  expect(index.listings[0]!.authored.type).toBe('mod')
  expect(index.listings[0]!.releases[0]!.download.sha256).toBeString()
})

test('accepts a minimal index', () => {
  expect(parseIndex(JSON.stringify(valid)).sources.generated.commit).toBe('b')
})

test('keeps unknown upstream fields instead of failing', () => {
  const withExtra = structuredClone(valid) as Record<string, unknown>
  withExtra.brand_new_field = { nested: true }
  expect(parseIndex(JSON.stringify(withExtra))).toHaveProperty('brand_new_field')
})

test.each([
  ['not json', 'oops'],
  ['a bare array', '[]'],
  ['null', 'null'],
  ['no snapshot_version', JSON.stringify({ ...valid, snapshot_version: undefined })],
  ['listings not an array', JSON.stringify({ ...valid, listings: {} })],
  ['no sources', JSON.stringify({ ...valid, sources: undefined })],
  ['commit not a string', JSON.stringify({ ...valid, sources: { authored: { repository: 'a', commit: 1 }, generated: { repository: 'b', commit: 'x' } } })],
  ['listing missing authored', JSON.stringify({ ...valid, listings: [{ id: 'x', releases: [] }] })],
  ['authored with unknown type', JSON.stringify({ ...valid, listings: [{ id: 'x', authored: { spec_version: 1, id: 'x', type: 'spaceship', name: 'n', authors: [], abstract: 'a', license: 'MIT', links: { forums: 'u' }, compatibility: { game_min: '1' } }, releases: [] }] })],
])('rejects %s', (_label, input) => {
  expect(() => parseIndex(input)).toThrow()
})

test('exposes a zod-inferred type for downstream consumers', () => {
  const parsed = contentIndexSchema.parse(fixture)
  const names: string[] = parsed.listings.map((l) => l.authored.name)
  expect(names).toContain('Advanced Flight Computer')
})
