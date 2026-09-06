import { expect, test } from 'bun:test'
import upstreamSchema from './authored.schema.fixture.json'
import fixture from './content-index.fixture.json'
import {
  type AuthoredDocument,
  KEY_ORDER,
  PATTERNS,
  PATTERN_SOURCES,
  authoredDocument,
  authorityOf,
  renderAuthoredToml,
} from './authored'

const SCHEMA_URL =
  'https://raw.githubusercontent.com/KSAModding/content-index/refs/heads/main/schemas/authored.schema.json'

/** A minimal document that passes, as the base for negative cases. */
const valid: AuthoredDocument = {
  spec_version: 1,
  id: 'ExampleMod',
  type: 'mod',
  name: 'Example Mod',
  authors: ['Someone'],
  abstract: 'An example.',
  license: 'MIT',
  links: { forums: 'https://forums.ahwoo.com/threads/example.1/' },
  compatibility: { game_min: '2026.8.3.5117' },
}

const withDoc = (patch: Record<string, unknown>) => ({ ...valid, ...patch }) as AuthoredDocument

// ---------------------------------------------------------------- serializer

test('reproduces the real StarMap listing', async () => {
  const listing = (fixture as unknown as { listings: { id: string; authored: AuthoredDocument }[] }).listings.find(
    (entry) => entry.id === 'StarMap',
  )!

  const rendered = renderAuthoredToml(listing.authored)
  const upstream = await Bun.file(new URL('./starmap.listing.fixture.toml', import.meta.url)).text()

  // Semantic equality, not byte equality. The upstream file is hand written and
  // wraps one long array across lines; Bun emits it inline. Both are valid TOML
  // that parse to the same document, and chasing a human's line wrapping would
  // mean inventing a formatting rule upstream does not actually document.
  expect(Bun.TOML.parse(rendered)).toEqual(Bun.TOML.parse(upstream))
})

test('the rendered file reads like the ones beside it', async () => {
  const listing = (fixture as unknown as { listings: { id: string; authored: AuthoredDocument }[] }).listings.find(
    (entry) => entry.id === 'StarMap',
  )!
  const rendered = renderAuthoredToml(listing.authored)

  // Scalars first, then tables, in upstream's own order.
  const sections = ['[releases]', '[links]', '[compatibility]', '[install]', '[provides]', '[provides.configure]']
  const positions = sections.map((section) => rendered.indexOf(section))
  expect(positions.every((position) => position > 0)).toBe(true)
  expect(positions).toEqual([...positions].sort((a, b) => a - b))

  // Dashed keys stay bare, as upstream writes them.
  expect(rendered).toContain('content-dir = "mods"')
  expect(rendered).toContain('game-path = "GameLocation"')
  // The description is a multi-line block, not one escaped line.
  expect(rendered).toContain('description = """')
  expect(rendered.endsWith('\n')).toBe(true)
})

test('round-trips awkward strings', () => {
  const nasty = [
    'quotes " and """ runs',
    'a backslash \\ and \\\\ two',
    'a tab\tand a form feed\f',
    'unicode: é 中文 🚀',
    'trailing spaces   \nand a second line',
    'a lone \r carriage return',
  ]

  for (const description of nasty) {
    const toml = renderAuthoredToml(withDoc({ description }))
    const parsed = Bun.TOML.parse(toml) as { description: string }
    // Carriage returns are stripped and the value ends in exactly one newline,
    // which is what makes a """ block round-trip.
    expect(parsed.description).toBe(`${description.replace(/\r/g, '').trimEnd()}\n`)
  }
})

test('a description can never forge the closing delimiter', () => {
  const toml = renderAuthoredToml(withDoc({ description: 'before\n"""\nlicense = "EVIL"\nafter' }))
  const parsed = Bun.TOML.parse(toml) as { description: string; license: string }
  expect(parsed.license).toBe('MIT')
  expect(parsed.description).toContain('"""')
})

test('keeps empty arrays and drops empty objects', () => {
  // The schema states an absent list and an empty list say different things.
  const toml = renderAuthoredToml(
    withDoc({ type: 'mod-loader', install: { target: 'standalone', steps: [] }, provides: { launch: 'A.exe' } }),
  )
  expect(toml).toContain('steps = []')

  const parsed = Bun.TOML.parse(renderAuthoredToml(valid)) as Record<string, unknown>
  expect(parsed).not.toHaveProperty('releases')
})

test('orders keys the way upstream listings do', () => {
  const toml = renderAuthoredToml(withDoc({ tags: ['utility'] }))
  const order = ['spec_version', 'id', 'type', 'name', 'authors', 'abstract', 'license', 'tags']
  const positions = order.map((key) => toml.indexOf(`${key} =`))
  expect(positions).toEqual([...positions].sort((a, b) => a - b))
})

// ------------------------------------------------------------------- schema

test('accepts every listing in the real index that we can represent', () => {
  const listings = (fixture as unknown as { listings: { authored: AuthoredDocument }[] }).listings
  for (const listing of listings) {
    expect(() => renderAuthoredToml(listing.authored)).not.toThrow()
  }
})

test.each([
  ['a reserved id', { id: 'CON' }],
  ['a reserved id with a suffix', { id: 'nul.mod' }],
  ['the game reserved id', { id: 'Core' }],
  ['a traversing id', { id: 'a..b' }],
  ['an id with a slash', { id: 'owner/repo' }],
  ['a mod pack', { type: 'modpack' }],
  ['a non forums link', { links: { forums: 'https://example.com/thread' } }],
  ['an uppercase tag', { tags: ['Utility'] }],
  ['a malformed game version', { compatibility: { game_min: '2026' } }],
  ['a leading v on a loader version', { loader: { id: 'StarMap', min: 'v1.0.0' } }],
  ['an unknown top level key', { surprise: 'value' }],
  ['a successor without deprecation', { superseded_by: 'NewMod' }],
  ['provides on a plain mod', { provides: { launch: 'A.exe' } }],
  ['a mod installing outside mods', { install: { target: 'game-root' } }],
  ['a standalone install with nothing to launch', {
    type: 'mod-loader',
    install: { target: 'standalone' },
  }],
  ['a content path with no content dir', {
    type: 'mod-loader',
    provides: { 'content-path': 'mods' },
  }],
  ['a path that leaves its anchor', {
    type: 'mod-loader',
    install: { target: 'standalone', root: '../escape' },
    provides: { launch: 'A.exe' },
  }],
  ['a release authority naming an absent host', {
    releases: { github: 'a/b', authority: 'spacedock' },
  }],
  ['a dependency with neither id nor alternatives', { dependencies: [{ kind: 'required' }] }],
  ['a loader on a mod loader', { type: 'mod-loader', loader: { id: 'X', min: '1.0.0' } }],
])('rejects %s', (_label, patch) => {
  expect(authoredDocument.safeParse({ ...valid, ...patch }).success).toBe(false)
})

test('claiming the game manifest is refused', () => {
  const result = authoredDocument.safeParse(
    withDoc({
      type: 'mod-loader',
      install: { target: 'user-data', manages: ['manifest.toml '] },
    }),
  )
  expect(result.success).toBe(false)
})

test('reports errors against the field that caused them', () => {
  const result = authoredDocument.safeParse(withDoc({ tags: ['ok', 'Bad Tag'] }))
  expect(result.success).toBe(false)
  if (!result.success) {
    expect(result.error.issues[0]!.path).toEqual(['tags', 1])
  }
})

test('resolves the release authority', () => {
  expect(authorityOf(withDoc({ releases: { github: 'a/b' } }))).toEqual({
    kind: 'github',
    target: 'a/b',
  })
  expect(
    authorityOf(withDoc({ releases: { github: 'a/b', spacedock: 42, authority: 'spacedock' } })),
  ).toEqual({ kind: 'spacedock', target: '42' })
  expect(authorityOf(valid)).toBeNull()
})

// -------------------------------------------------------------------- drift

test('our pattern strings are byte identical to the pinned upstream schema', () => {
  const defs = (upstreamSchema as { $defs: Record<string, { pattern?: string }> }).$defs
  // Compared as strings, not as RegExp.source: the engine normalises a source
  // built from a string, escaping "/" so it could be re-parsed as a literal,
  // which would never equal upstream's raw pattern.
  for (const [name, source] of Object.entries<string>(PATTERN_SOURCES)) {
    expect(defs[name]?.pattern, `upstream has no pattern for ${name}`).toBeDefined()
    expect(source, `${name} drifted from upstream`).toBe(defs[name]!.pattern!)
  }
})

test('every pattern compiles and still anchors to end of input', () => {
  for (const [name, pattern] of Object.entries(PATTERNS)) {
    expect(pattern, name).toBeInstanceOf(RegExp)
    expect(pattern.global, `${name} must not be global, a stateful regex misvalidates`).toBe(false)
  }
  // The end anchors are what stop a trailing newline riding into a folder name.
  expect(PATTERNS.contentId.test('StarMap\n')).toBe(false)
  expect(PATTERNS.tag.test('utility\n')).toBe(false)
})

test('we require everything upstream requires', () => {
  const required = (upstreamSchema as { required: string[] }).required
  const shape = Object.keys(authoredDocument.parse(valid))
  for (const key of required) {
    expect(shape, `${key} is required upstream but missing here`).toContain(key)
  }
})

test('we model every top level property upstream defines', () => {
  const upstreamKeys = Object.keys((upstreamSchema as { properties: object }).properties)
  // Pack only keys are deliberately absent: modpack is refused outright.
  const packOnly = new Set(['version', 'released_at', 'changelog', 'mods', 'vehicles', 'saves'])
  const ours = new Set([...KEY_ORDER])
  const missing = upstreamKeys.filter((key) => !ours.has(key as never) && !packOnly.has(key))
  expect(missing, 'upstream added a property we do not model').toEqual([])
})

test('the pinned schema still matches upstream', async () => {
  let live: string
  try {
    const response = await fetch(SCHEMA_URL, { signal: AbortSignal.timeout(10_000) })
    if (!response.ok) return
    live = await response.text()
  } catch {
    // No network. The structural assertions above still ran against the pin.
    return
  }

  // Raw bytes of the pinned file, not a re-serialisation of the parsed object,
  // which would normalise away the differences worth noticing.
  const pinned = await Bun.file(new URL('./authored.schema.fixture.json', import.meta.url)).text()
  // Line endings and a trailing newline are not a schema change.
  const CARRIAGE_RETURN = String.fromCharCode(13)
  const digest = (text: string) =>
    new Bun.CryptoHasher('sha256')
      .update(text.replaceAll(CARRIAGE_RETURN, '').trimEnd())
      .digest('hex')

  expect(
    digest(live),
    'upstream authored.schema.json changed. Diff it against authored.schema.fixture.json, update authored.ts, then re-pin the fixture.',
  ).toBe(digest(pinned))
})
