import { expect, test } from 'bun:test'
import { NAMESPACE, claimedModId, modIdFor, resolveLinks } from './mod-links'

const mods = [
  { id: 'mod_afc', listingId: 'AdvancedFlightComputer' },
  { id: 'mod_mt', listingId: 'MeasureTools' },
]

test('a listing claiming its own row is linked', () => {
  const result = resolveLinks(
    [{ listingId: 'AdvancedFlightComputer', claimedModId: 'mod_afc' }],
    mods,
  )
  expect(result.linked.get('AdvancedFlightComputer')).toBe('mod_afc')
  expect(result.hidden.size).toBe(0)
  expect(result.issues.filter((issue) => issue.kind !== 'orphaned')).toEqual([])
})

test('a listing that does not opt in is simply not ours', () => {
  // Absent metadata is the normal case, not a problem to report.
  const result = resolveLinks([{ listingId: 'KSArmory', claimedModId: null }], [])
  expect(result.linked.size).toBe(0)
  expect(result.hidden.size).toBe(0)
  expect(result.issues).toEqual([])
})

test('a listing naming an id we do not have is hidden', () => {
  const result = resolveLinks([{ listingId: 'KSArmory', claimedModId: 'mod_nope' }], mods)
  expect(result.hidden.has('KSArmory')).toBe(true)
  expect(result.issues[0]).toMatchObject({ kind: 'unknown-id', listingId: 'KSArmory' })
})

test('the incumbent keeps its row and the impostor is hidden', () => {
  // This is the rule that matters. Hiding both would let anybody take a mod
  // down by copying its id into a listing of their own, which costs one pull
  // request and nothing else, because upstream never validates our namespace.
  const result = resolveLinks(
    [
      { listingId: 'AdvancedFlightComputer', claimedModId: 'mod_afc' },
      { listingId: 'EvilCopy', claimedModId: 'mod_afc' },
    ],
    mods,
  )

  expect(result.linked.get('AdvancedFlightComputer')).toBe('mod_afc')
  expect(result.hidden.has('AdvancedFlightComputer')).toBe(false)
  expect(result.hidden.has('EvilCopy')).toBe(true)
  expect(result.issues).toContainEqual(
    expect.objectContaining({
      kind: 'wrong-listing',
      listingId: 'EvilCopy',
      boundTo: 'AdvancedFlightComputer',
    }),
  )
})

test('several impostors are all hidden and the incumbent survives', () => {
  const result = resolveLinks(
    [
      { listingId: 'AdvancedFlightComputer', claimedModId: 'mod_afc' },
      { listingId: 'CopyOne', claimedModId: 'mod_afc' },
      { listingId: 'CopyTwo', claimedModId: 'mod_afc' },
    ],
    mods,
  )
  expect(result.linked.get('AdvancedFlightComputer')).toBe('mod_afc')
  expect([...result.hidden].sort()).toEqual(['CopyOne', 'CopyTwo'])
})

test('when the incumbent is absent every claimant is still hidden', () => {
  // Nobody gets the row by default. An unattended row is an admin decision.
  const result = resolveLinks(
    [
      { listingId: 'CopyOne', claimedModId: 'mod_afc' },
      { listingId: 'CopyTwo', claimedModId: 'mod_afc' },
    ],
    mods,
  )
  expect(result.linked.size).toBe(0)
  expect([...result.hidden].sort()).toEqual(['CopyOne', 'CopyTwo'])
})

test('a row whose listing stopped claiming it is reported, not hidden', () => {
  const result = resolveLinks(
    [{ listingId: 'AdvancedFlightComputer', claimedModId: null }],
    [{ id: 'mod_afc', listingId: 'AdvancedFlightComputer' }],
  )
  // Our row is stale. There is nothing upstream to hide over it.
  expect(result.hidden.size).toBe(0)
  expect(result.issues[0]).toMatchObject({ kind: 'orphaned', listingId: 'AdvancedFlightComputer' })
})

// ------------------------------------------------------------ claimedModId

test('reads the id out of our namespace', () => {
  expect(claimedModId({ metadata: { [NAMESPACE]: { id: 'mod_afc' } } })).toBe('mod_afc')
})

test('the namespace is the hyphenated one from the RFC', () => {
  // Namespaces fold case but never punctuation, so the underscore spelling is
  // a different namespace and must not resolve.
  expect(claimedModId({ metadata: { ksamods_gg: { id: 'mod_afc' } } })).toBeNull()
  expect(NAMESPACE).toBe('ksamods-gg')
})

test.each([
  ['no metadata at all', {}],
  ['metadata that is not a table', { metadata: 'nope' }],
  ['another consumer only', { metadata: { borea: { accent: '#ff8800' } } }],
  ['our namespace without an id', { metadata: { [NAMESPACE]: { banner: 'https://x/y.png' } } }],
  ['an id that is not a string', { metadata: { [NAMESPACE]: { id: 42 } } }],
  ['an empty id', { metadata: { [NAMESPACE]: { id: '' } } }],
  ['an absurdly long id', { metadata: { [NAMESPACE]: { id: 'x'.repeat(65) } } }],
])('reads no claim from %s', (_label, authored) => {
  expect(claimedModId(authored)).toBeNull()
})

test('survives junk in the bag', () => {
  // Upstream never validates our namespace, so this is untrusted text.
  for (const junk of [null, undefined, 0, 'string', [], { metadata: [] }]) {
    expect(() => claimedModId(junk)).not.toThrow()
    expect(claimedModId(junk)).toBeNull()
  }
})

test('the mod id is stable and derived from the listing', () => {
  expect(modIdFor('AdvancedFlightComputer')).toBe(modIdFor('AdvancedFlightComputer'))
  expect(modIdFor('AdvancedFlightComputer')).not.toBe(modIdFor('MeasureTools'))
  expect(modIdFor('AdvancedFlightComputer')).toMatch(/^mod_[0-9a-f]{24}$/)
})
