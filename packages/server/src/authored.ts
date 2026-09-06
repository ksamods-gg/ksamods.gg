import { z } from 'zod'

/**
 * The authored listing document: `listings/<id>.toml` in KSAModding/content-index.
 *
 * This is a STRICT mirror of the upstream JSON Schema, unlike `authoredSchema`
 * in content-index-schema.ts, which is a deliberately loose model of the copy
 * embedded in the generated index and carries no patterns at all. What is
 * rendered here is committed to a third party repository under a user's own
 * name, so every constraint upstream enforces is enforced here first.
 */

/**
 * Copied verbatim from KSAModding/content-index schemas/authored.schema.json
 * $defs. Do not tidy these: authored.test.ts compares `.source` against the
 * pinned upstream schema, and a reformatted regex fails that comparison.
 *
 * The trailing `(?![\s\S])` is a true end of input anchor. A bare `$` would
 * also match before a trailing newline, which would let an id carry one into a
 * folder name.
 */
/**
 * The pattern strings, copied verbatim from KSAModding/content-index
 * schemas/authored.schema.json $defs. They are strings rather than regex
 * literals for one reason: a literal cannot contain an unescaped forward
 * slash, and authored.test.ts asserts these are byte identical to upstream.
 */
export const PATTERN_SOURCES = {
  contentId: "^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?(?![\\s\\S])",
  semver: "^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*)?(?:\\+[0-9a-zA-Z-]+(?:\\.[0-9a-zA-Z-]+)*)?(?![\\s\\S])",
  gameVersionBound: "^[0-9]{4}\\.(?:1[0-2]|[1-9])(?:\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*))?(?![\\s\\S])",
  relativePath: "^(?!~)(?![A-Za-z]:)[^/\\\\\\u0000-\\u001f]+(?:/[^/\\\\\\u0000-\\u001f]+)*(?![\\s\\S])",
  url: "^https?://[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?(?::[0-9]{1,5})?(?:[/?#][^\\s\\u0000-\\u001f]*)?(?![\\s\\S])",
  forumsUrl: "^https://forums\\.ahwoo\\.com/[^\\s\\u0000-\\u001f]*(?![\\s\\S])",
  spdxExpression: "^[A-Za-z0-9.:+()-]+(?: (?:AND|OR|WITH) [A-Za-z0-9.:+()-]+)*(?![\\s\\S])",
  tag: "^[a-z0-9]+(?:-[a-z0-9]+)*(?![\\s\\S])",
  utcTimestamp: "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]+)?Z(?![\\s\\S])",
  configKeyPath: "^[^.\\u0000-\\u001f]+(?:\\.[^.\\u0000-\\u001f]+)*(?![\\s\\S])",
  githubRepo: "^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?![\\s\\S])",
} as const

export const PATTERNS = Object.fromEntries(
  Object.entries(PATTERN_SOURCES).map(([name, source]) => [name, new RegExp(source)]),
) as Record<keyof typeof PATTERN_SOURCES, RegExp>

/** Reserved ids, compared up to the first dot. Case-insensitive, because
 *  Windows treats CON.mod as a device too. Core is the game's own. */
export const RESERVED_ID =
  /^(?:[Cc][Oo][Rr][Ee]|[Cc][Oo][Nn]|[Pp][Rr][Nn]|[Aa][Uu][Xx]|[Nn][Uu][Ll]|[Cc][Oo][Mm][1-9]|[Ll][Pp][Tt][1-9])(?:[.]|(?![\s\S]))/

/** A path that leaves its anchor. Containment is a validity rule because a
 *  manager executes this with write access to a game directory. */
export const PATH_ESCAPE = /(?:^|\/)[.]{1,2}(?:\/|(?![\s\S]))/

/** A path through a reserved Windows device name, which swallows every write. */
export const PATH_DEVICE =
  /(?:^|\/)(?:[Cc][Oo][Nn]|[Pp][Rr][Nn]|[Aa][Uu][Xx]|[Nn][Uu][Ll]|[Cc][Oo][Mm][1-9]|[Ll][Pp][Tt][1-9])(?:[.][^/]*)?[ .]*(?:\/|(?![\s\S]))/

/** The game's own manifest, which no descriptor may claim. */
export const GAME_MANIFEST = /^[Mm][Aa][Nn][Ii][Ff][Ee][Ss][Tt][.][Tt][Oo][Mm][Ll][ .]*(?![\s\S])/

const contentId = z
  .string()
  .regex(PATTERNS.contentId, 'Use letters, numbers, dot, dash or underscore, up to 64 characters')
  .refine((v) => !RESERVED_ID.test(v), 'That name is reserved by Windows or by the game')
  // The id reaches both `listings/<id>.toml` and a git ref, so this is a trust
  // boundary in the same class as the SLUG regex in github.ts.
  .refine((v) => !v.includes('..'), 'An id cannot contain two dots in a row')

const semver = z.string().regex(PATTERNS.semver, 'Use a SemVer version such as 1.2.3, with no leading v')
const gameVersion = z
  .string()
  .regex(PATTERNS.gameVersionBound, 'Use a game version such as 2026.8.3.5117, or a month such as 2026.8')
const relativePath = z
  .string()
  .regex(PATTERNS.relativePath, 'Use a relative path separated by forward slashes')
  .refine((v) => !PATH_ESCAPE.test(v), 'A path may not leave its install location')
  .refine((v) => !PATH_DEVICE.test(v), 'That path goes through a reserved Windows device name')

const anchor = z.enum(['mods', 'user-data', 'game-root', 'standalone'])
const url = z.string().regex(PATTERNS.url, 'Use a full http or https URL')

const versionRange = z.strictObject({
  id: contentId,
  min: semver.optional(),
  max: semver.optional(),
})

const dependency = z.strictObject({
  id: contentId.optional(),
  kind: z.enum(['required', 'optional', 'recommends', 'suggests', 'conflict']),
  min: semver.optional(),
  max: semver.optional(),
  any_of: z.array(versionRange).min(1).optional(),
})

/** The forums thread is the only required link. It ties the listing to an Ahwoo
 *  account and is the tiebreaker in id disputes. Other keys are free-form
 *  upstream; the name restriction here is ours, because an exotic key would
 *  need TOML quoting and reaches a file written under the user's name. */
const links = z
  .object({ forums: z.string().regex(PATTERNS.forumsUrl, 'Must be a forums.ahwoo.com thread URL') })
  .catchall(url)
  .refine(
    (value) => Object.keys(value).every((key) => /^[A-Za-z0-9_-]{1,40}$/.test(key)),
    'Link names may use letters, numbers, dash and underscore only',
  )

const installSpec = z.strictObject({
  root: relativePath.optional(),
  target: anchor.optional(),
  path: relativePath.optional(),
  manages: z.array(relativePath).optional(),
  // Prose, never parsed for actions. An absent list and an empty list say
  // different things, which is why empty arrays survive rendering.
  steps: z.array(z.string()).optional(),
  uninstall: z.array(z.string()).optional(),
})

const provides = z.strictObject({
  launch: relativePath.optional(),
  'content-dir': anchor.optional(),
  'content-path': relativePath.optional(),
  configure: z
    .strictObject({
      file: relativePath,
      format: z.enum(['json', 'toml']),
      'game-path': z.string().regex(PATTERNS.configKeyPath).optional(),
    })
    .optional(),
})

const base = z.strictObject({
  spec_version: z.literal(1),
  id: contentId,
  // modpack is refused: packs live under packs/<id>/<version>.toml, and
  // check_layout.py rejects one under listings/.
  type: z.enum(['mod', 'mod-loader']),
  name: z.string().min(1),
  authors: z.array(z.string().min(1)).min(1, 'Name at least one author'),
  abstract: z.string().min(1, 'Write one or two sentences'),
  description: z.string().optional(),
  license: z.string().regex(PATTERNS.spdxExpression, 'Use an SPDX identifier such as MIT'),
  tags: z.array(z.string().regex(PATTERNS.tag, 'Tags are lowercase, dash separated')).optional(),
  status: z.enum(['active', 'deprecated']).optional(),
  superseded_by: contentId.optional(),
  links,
  compatibility: z.strictObject({
    game_min: gameVersion,
    game_max: gameVersion.optional(),
    os: z.array(z.enum(['windows', 'linux', 'macos'])).min(1).optional(),
  }),
  releases: z
    .strictObject({
      github: z.string().regex(PATTERNS.githubRepo, 'Use owner/repo').optional(),
      spacedock: z.number().int().min(1).optional(),
      authority: z.enum(['github', 'spacedock']).optional(),
    })
    .optional(),
  loader: z.strictObject({ id: contentId, min: semver, max: semver.optional() }).optional(),
  dependencies: z.array(dependency).optional(),
  install: installSpec.optional(),
  provides: provides.optional(),
})

/** The conditional rules from the upstream schema's allOf. Each one is
 *  otherwise a guaranteed CI rejection, so it is worth catching here. */
export const authoredDocument = base.superRefine((doc, ctx) => {
  const fail = (path: (string | number)[], message: string) =>
    ctx.addIssue({ code: 'custom', path, message })

  if (doc.type === 'mod') {
    // The game decides where a mod goes, so target is pinned and path is not
    // the author's to set.
    if (doc.provides) fail(['provides'], 'Only a mod loader carries a provides section')
    if (doc.install?.target && doc.install.target !== 'mods')
      fail(['install', 'target'], 'A mod installs to the mods anchor')
    if (doc.install?.path) fail(['install', 'path'], 'A mod does not choose its own install path')
  }

  if (doc.type === 'mod-loader') {
    if (doc.loader) fail(['loader'], 'A mod loader does not itself need a loader')
    // No convention to fall back on, so a stated install section must say where.
    if (doc.install && !doc.install.target)
      fail(['install', 'target'], 'A mod loader must say which anchor it installs to')
  }

  // A successor pointer without a deprecation is data no client acts on.
  if (doc.superseded_by && doc.status !== 'deprecated')
    fail(['status'], 'Set the status to deprecated when naming a successor')

  // RFC 0035 rule 4: a directory nothing runs from and nothing reads is not an install.
  if (doc.install?.target === 'standalone' && !doc.provides?.launch)
    fail(['provides', 'launch'], 'A standalone install must name the executable to launch')

  // RFC 0035 rule 6: the game regenerates manifest.toml, so no descriptor may claim it.
  if (doc.install?.target === 'user-data' && !doc.install.path) {
    doc.install.manages?.forEach((entry, index) => {
      if (GAME_MANIFEST.test(entry))
        fail(['install', 'manages', index], 'The game owns manifest.toml and rewrites it')
    })
  }

  if (doc.provides?.['content-path'] && !doc.provides['content-dir'])
    fail(['provides', 'content-dir'], 'Naming a content path needs a content directory')

  if (doc.releases) {
    const { github, spacedock, authority } = doc.releases
    if (!github && !spacedock) fail(['releases'], 'Name at least one release host')
    if (github && spacedock && !authority)
      fail(['releases', 'authority'], 'Say which host defines which releases exist')
    if (authority === 'github' && !github)
      fail(['releases', 'authority'], 'The authority names a host that is not set')
    if (authority === 'spacedock' && !spacedock)
      fail(['releases', 'authority'], 'The authority names a host that is not set')
  }

  doc.dependencies?.forEach((entry, index) => {
    if (entry.any_of) {
      if (entry.id) fail(['dependencies', index, 'id'], 'Use either an id or a set of alternatives')
      if (entry.min || entry.max)
        fail(['dependencies', index, 'min'], 'Bounds belong on each alternative, not the entry')
      if (entry.kind !== 'required' && entry.kind !== 'recommends')
        fail(['dependencies', index, 'kind'], 'Alternatives are only required or recommends')
    } else if (!entry.id) {
      fail(['dependencies', index, 'id'], 'Name a dependency id')
    }
  })
})

export type AuthoredDocument = z.infer<typeof authoredDocument>

/** The order upstream's own listings use, so a generated file reads like the
 *  ones beside it. Bun.TOML.stringify keeps insertion order. */
export const KEY_ORDER: (keyof AuthoredDocument)[] = [
  'spec_version',
  'id',
  'type',
  'name',
  'authors',
  'abstract',
  'description',
  'license',
  'tags',
  'status',
  'superseded_by',
  'releases',
  'links',
  'compatibility',
  'loader',
  'dependencies',
  'install',
  'provides',
]

/** Drops undefined and empty objects, which Bun would render as a bare table
 *  header. Empty arrays are KEPT: the schema states an absent list and an empty
 *  list say different things. */
function prune(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(prune)
  if (value === null || typeof value !== 'object') return value

  const out: Record<string, unknown> = {}
  for (const [key, entry] of Object.entries(value)) {
    if (entry === undefined) continue
    const cleaned = prune(entry)
    if (cleaned !== null && typeof cleaned === 'object' && !Array.isArray(cleaned)) {
      if (Object.keys(cleaned).length === 0) continue
    }
    out[key] = cleaned
  }
  return out
}

/**
 * TOML eats the newline immediately after the opening delimiter, so a value
 * ending in exactly one newline round-trips through a `"""` block unchanged.
 */
function normaliseDescription(value: string) {
  return `${value.replace(/\r/g, '').trimEnd()}\n`
}

/** A multi-line basic string. Every quote is escaped, so no run of three can
 *  appear and the closing delimiter cannot be forged from content. */
function multilineBasic(value: string) {
  const escaped = value
    .replace(/\\/g, '\\\\')
    .replace(/"/g, '\\"')
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, (character) =>
      `\\u${character.codePointAt(0)!.toString(16).padStart(4, '0')}`,
    )
  return `"""\n${escaped}"""`
}

/**
 * Renders the document as the TOML that gets committed.
 *
 * The output is re-parsed and re-validated before it is returned, inside this
 * function so no caller can skip it. That assertion is what makes the
 * serializer safe to point at somebody else's repository.
 */
export function renderAuthoredToml(input: AuthoredDocument): string {
  const document = authoredDocument.parse(input)

  const ordered: Record<string, unknown> = {}
  for (const key of KEY_ORDER) {
    const value = document[key]
    if (value === undefined) continue
    ordered[key] = key === 'description' ? normaliseDescription(value as string) : prune(value)
  }

  const description = ordered.description as string | undefined
  const multiline = description !== undefined && description.includes('\n')
  const sentinel = `ksamods-description-${crypto.randomUUID()}`
  if (multiline) ordered.description = sentinel

  const rendered = Bun.TOML.stringify(ordered)
  if (typeof rendered !== 'string') throw new Error('TOML serialization produced nothing')
  let toml = rendered

  if (multiline) {
    const literal = `description = ${JSON.stringify(sentinel)}`
    if (!toml.includes(literal)) {
      // Fail closed. Emitting the sentinel would publish a placeholder.
      throw new Error('could not place the description block')
    }
    toml = toml.replace(literal, `description = ${multilineBasic(description!)}`)
    ordered.description = description
  }

  const reparsed = Bun.TOML.parse(toml)
  if (!Bun.deepEquals(reparsed, ordered, true)) {
    throw new Error('rendered TOML did not round-trip')
  }
  authoredDocument.parse(reparsed)

  return toml.endsWith('\n') ? toml : `${toml}\n`
}

/** The release host ownership binds to, mirroring tools/ownership.py. */
export function authorityOf(document: AuthoredDocument): { kind: 'github' | 'spacedock'; target: string } | null {
  const releases = document.releases
  if (!releases) return null

  const hosts: { kind: 'github' | 'spacedock'; target: string }[] = []
  if (releases.github) hosts.push({ kind: 'github', target: releases.github })
  if (releases.spacedock) hosts.push({ kind: 'spacedock', target: String(releases.spacedock) })

  if (hosts.length === 0) return null
  if (hosts.length === 1) return hosts[0]!
  return hosts.find((host) => host.kind === releases.authority) ?? null
}
