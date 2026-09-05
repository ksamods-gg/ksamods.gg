import { expect, test } from 'bun:test'
import { safeNext } from './next-path'

test.each([
  ['/mods/StarMap/claim'],
  ['/mods/AdvancedFlightComputer'],
  ['/account'],
])('allows %s', (path) => {
  expect(safeNext(path)).toBe(path)
})

test.each([
  ['an absolute url', 'https://evil.example'],
  ['a protocol-relative url', '//evil.example'],
  ['a backslash bypass', '/\evil.example'],
  ['an embedded scheme', '/mods/x?u=https://evil.example'],
  ['a javascript url', 'javascript:alert(1)'],
  ['an unknown area', '/admin'],
  ['empty', ''],
  ['null', null],
])('falls back to / for %s', (_label, path) => {
  expect(safeNext(path as string | null)).toBe('/')
})
