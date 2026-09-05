import { z } from 'zod'

/**
 * Shape of https://ksamodding.github.io/content-index-releases/v1/index.json
 *
 * The `authored` half mirrors the upstream JSON Schema at
 * KSAModding/content-index/schemas/authored.schema.json, where required vs optional
 * there is authoritative. The generated half (the release entries and the
 * wrapper) has no published schema, so it is derived from the live document.
 *
 * These objects strip unknown keys rather than rejecting them, so an upstream
 * addition never fails a sync. The verbatim document is what gets stored (see
 * parseIndex), so nothing is lost, and the types describe the known subset.
 */

const contentId = z.string()
const semver = z.string()
const gameVersionBound = z.string()
const relativePath = z.string()
const anchor = z.enum(['mods', 'user-data', 'game-root', 'standalone'])

const versionRange = z.object({
  id: contentId,
  min: semver.optional(),
  max: semver.optional(),
})

const dependency = z.object({
  id: contentId.optional(),
  kind: z.enum(['required', 'optional', 'recommends', 'suggests', 'conflict']),
  min: semver.optional(),
  max: semver.optional(),
  any_of: z.array(versionRange).optional(),
})

const packEntry = z.object({ id: contentId, version: semver })

/** `forums` is the only link the upstream schema requires. */
const links = z.object({
  forums: z.string(),
  repository: z.string().optional(),
  spacedock: z.string().optional(),
  bugtracker: z.string().optional(),
  discussions: z.string().optional(),
  homepage: z.string().optional(),
})

const installSpec = z.object({
  root: relativePath.optional(),
  target: anchor.optional(),
  path: relativePath.optional(),
  manages: z.array(relativePath).optional(),
  steps: z.array(z.string()).optional(),
  uninstall: z.array(z.string()).optional(),
})

const provides = z.object({
  launch: relativePath.optional(),
  'content-dir': anchor.optional(),
  'content-path': relativePath.optional(),
  configure: z
    .looseObject({
      file: relativePath,
      format: z.enum(['json', 'toml']),
      'game-path': z.string().optional(),
    })
    .optional(),
})

const loaderRequirement = z.object({
  id: contentId,
  min: semver,
  max: semver.optional(),
})

export const authoredSchema = z.object({
  spec_version: z.number(),
  id: contentId,
  type: z.enum(['mod', 'mod-loader', 'modpack']),
  name: z.string(),
  authors: z.array(z.string()),
  abstract: z.string(),
  description: z.string().optional(),
  license: z.string(),
  tags: z.array(z.string()).optional(),
  status: z.enum(['active', 'deprecated']).optional(),
  superseded_by: contentId.optional(),
  links,
  compatibility: z.object({
    game_min: gameVersionBound,
    game_max: gameVersionBound.optional(),
    os: z.array(z.enum(['windows', 'linux', 'macos'])).optional(),
  }),
  releases: z
    .looseObject({
      github: z.string().optional(),
      spacedock: z.number().optional(),
      authority: z.enum(['github', 'spacedock']).optional(),
    })
    .optional(),
  loader: loaderRequirement.optional(),
  dependencies: z.array(dependency).optional(),
  install: installSpec.optional(),
  provides: provides.optional(),
  version: semver.optional(),
  released_at: z.string().optional(),
  changelog: z.string().optional(),
  mods: z.array(packEntry).optional(),
  vehicles: z.array(packEntry).optional(),
  saves: z.array(packEntry).optional(),
})

/** Denormalised copy of the authored display fields, carried on each release. */
const releaseListing = z.object({
  name: z.string(),
  authors: z.array(z.string()),
  abstract: z.string(),
  description: z.string().optional(),
  license: z.string(),
  tags: z.array(z.string()).optional(),
  links,
})

export const releaseSchema = z.object({
  spec_version: z.number(),
  id: contentId,
  // Generated-side enumerations are left open: they are not covered by the
  // upstream schema, so a new value must not break ingestion.
  type: z.string(),
  version: z.string(),
  version_scheme: z.string(),
  release_status: z.string(),
  release_date: z.string(),
  game_min: gameVersionBound,
  game_min_revision: z.number(),
  game_max: gameVersionBound.optional(),
  game_max_revision: z.number().optional(),
  download: z.object({
    url: z.string(),
    sha256: z.string(),
    size: z.number(),
    content_type: z.string(),
    mirrors: z.array(z.string()).optional(),
  }),
  install_size: z.number().optional(),
  install: installSpec.extend({ derived: z.boolean().optional() }).optional(),
  loader: loaderRequirement.optional(),
  dependencies: z.array(dependency).optional(),
  changelog: z.string().optional(),
  listing: releaseListing.optional(),
})

export const listingSchema = z.object({
  id: contentId,
  authored: authoredSchema,
  releases: z.array(releaseSchema),
})

const source = z.object({ repository: z.string(), commit: z.string() })

export const contentIndexSchema = z.object({
  snapshot_version: z.number(),
  sources: z.object({ authored: source, generated: source }),
  listings: z.array(listingSchema),
  // No pack has shipped yet, so the entry shape is unknown; kept opaque rather
  // than guessed, and tightened once real data exists.
  packs: z.array(z.unknown()).optional(),
  game_versions: z
    .looseObject({
      spec_version: z.number(),
      source: z.string(),
      versions: z.array(z.string()),
    })
    .optional(),
})

export type ContentIndex = z.infer<typeof contentIndexSchema>
export type ContentListing = z.infer<typeof listingSchema>
export type ContentAuthored = z.infer<typeof authoredSchema>
export type ContentRelease = z.infer<typeof releaseSchema>
export type ContentDependency = z.infer<typeof dependency>
