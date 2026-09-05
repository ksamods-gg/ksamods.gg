'use client'

import { useMemo, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { Listing } from './types'

const SORTS = [
  { value: 'updated', label: 'Recently updated' },
  { value: 'name', label: 'Name A to Z' },
  { value: 'releases', label: 'Most releases' },
]

type Sort = 'updated' | 'name' | 'releases'

/** Game builds are dotted, but the trailing segment is a monotonic revision. */
const revisionOf = (version: string) => Number(version.split('.').pop()) || 0

const latestRelease = (listing: Listing) =>
  [...listing.releases].sort((a, b) => b.release_date.localeCompare(a.release_date))[0]

function supportsGameVersion(listing: Listing, version: string) {
  if (version === 'any') return true
  const revision = revisionOf(version)
  return listing.releases.some(
    (release) =>
      release.game_min_revision <= revision &&
      (release.game_max_revision == null || revision <= release.game_max_revision),
  )
}

function unique(values: (string | undefined)[]) {
  return [...new Set(values.filter((value): value is string => Boolean(value)))].sort()
}

function FilterGroup({
  title,
  options,
  selected,
  onToggle,
}: {
  title: string
  options: string[]
  selected: Set<string>
  onToggle: (value: string) => void
}) {
  if (options.length === 0) return null

  return (
    <div className="space-y-3">
      <h3 className="text-sm font-medium">{title}</h3>
      <div className="space-y-2">
        {options.map((option) => (
          <div key={option} className="flex items-center gap-2">
            <Checkbox
              id={`${title}-${option}`}
              checked={selected.has(option)}
              onCheckedChange={() => onToggle(option)}
            />
            <Label htmlFor={`${title}-${option}`} className="text-muted-foreground font-normal">
              {option}
            </Label>
          </div>
        ))}
      </div>
    </div>
  )
}

export function ModBrowser({
  listings,
  gameVersions,
}: {
  listings: Listing[]
  gameVersions: string[]
}) {
  const [query, setQuery] = useState('')
  const [types, setTypes] = useState<Set<string>>(new Set())
  const [tags, setTags] = useState<Set<string>>(new Set())
  const [loaders, setLoaders] = useState<Set<string>>(new Set())
  const [licenses, setLicenses] = useState<Set<string>>(new Set())
  const [gameVersion, setGameVersion] = useState('any')
  const [sort, setSort] = useState<Sort>('updated')

  const facets = useMemo(
    () => ({
      types: unique(listings.map((listing) => listing.authored.type)),
      tags: unique(listings.flatMap((listing) => listing.authored.tags ?? [])),
      loaders: unique(listings.map((listing) => listing.authored.loader?.id)),
      licenses: unique(listings.map((listing) => listing.authored.license)),
    }),
    [listings],
  )

  const results = useMemo(() => {
    const needle = query.trim().toLowerCase()

    const filtered = listings.filter((listing) => {
      const { authored } = listing

      if (needle) {
        const haystack = [
          authored.name,
          authored.abstract,
          listing.id,
          ...authored.authors,
          ...(authored.tags ?? []),
        ]
          .join(' ')
          .toLowerCase()
        if (!haystack.includes(needle)) return false
      }

      if (types.size && !types.has(authored.type)) return false
      if (tags.size && !(authored.tags ?? []).some((tag) => tags.has(tag))) return false
      if (loaders.size && !(authored.loader && loaders.has(authored.loader.id))) return false
      if (licenses.size && !licenses.has(authored.license)) return false
      return supportsGameVersion(listing, gameVersion)
    })

    return filtered.sort((a, b) => {
      if (sort === 'name') return a.authored.name.localeCompare(b.authored.name)
      if (sort === 'releases') return b.releases.length - a.releases.length
      const aDate = latestRelease(a)?.release_date ?? ''
      const bDate = latestRelease(b)?.release_date ?? ''
      return bDate.localeCompare(aDate)
    })
  }, [listings, query, types, tags, loaders, licenses, gameVersion, sort])

  const toggle =
    (setter: React.Dispatch<React.SetStateAction<Set<string>>>) => (value: string) =>
      setter((current) => {
        const next = new Set(current)
        if (!next.delete(value)) next.add(value)
        return next
      })

  const activeFilters =
    types.size + tags.size + loaders.size + licenses.size + (gameVersion === 'any' ? 0 : 1)

  function clearFilters() {
    setTypes(new Set())
    setTags(new Set())
    setLoaders(new Set())
    setLicenses(new Set())
    setGameVersion('any')
    setQuery('')
  }

  return (
    <div className="grid gap-8 md:grid-cols-[240px_1fr]">
      <aside className="space-y-6">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold">Filters</h2>
          {activeFilters > 0 && (
            <Button variant="ghost" size="sm" onClick={clearFilters}>
              Clear
            </Button>
          )}
        </div>

        <div className="space-y-3">
          <h3 className="text-sm font-medium">Game version</h3>
          <Select
            items={[
              { value: 'any', label: 'Any version' },
              ...gameVersions.map((version) => ({ value: version, label: version })),
            ]}
            value={gameVersion}
            onValueChange={(value) => setGameVersion(String(value))}
          >
            <SelectTrigger className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent className="max-h-72">
              <SelectItem value="any">Any version</SelectItem>
              {gameVersions.map((version) => (
                <SelectItem key={version} value={version}>
                  {version}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <FilterGroup
          title="Type"
          options={facets.types}
          selected={types}
          onToggle={toggle(setTypes)}
        />
        <FilterGroup
          title="Category"
          options={facets.tags}
          selected={tags}
          onToggle={toggle(setTags)}
        />
        <FilterGroup
          title="Loader"
          options={facets.loaders}
          selected={loaders}
          onToggle={toggle(setLoaders)}
        />
        <FilterGroup
          title="License"
          options={facets.licenses}
          selected={licenses}
          onToggle={toggle(setLicenses)}
        />
      </aside>

      <section className="space-y-4">
        <Input
          placeholder="Search mods..."
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />

        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-muted-foreground text-sm">
            {results.length} of {listings.length} listings
          </p>
          <div className="flex items-center gap-2">
            <span className="text-muted-foreground text-sm">Sort by</span>
            <Select
              items={SORTS}
              value={sort}
              onValueChange={(value) => setSort(String(value) as Sort)}
            >
              <SelectTrigger size="sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SORTS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        {results.length === 0 ? (
          <div className="border-border text-muted-foreground rounded-lg border border-dashed py-16 text-center text-sm">
            No listings match these filters.
          </div>
        ) : (
          <ul className="space-y-3">
            {results.map((listing) => {
              const release = latestRelease(listing)
              return (
                <li
                  key={listing.id}
                  className="border-border bg-card hover:bg-muted/40 flex gap-4 rounded-lg border p-4 transition-colors"
                >
                  <div
                    aria-hidden
                    className="bg-muted text-muted-foreground flex size-14 shrink-0 items-center justify-center rounded-lg text-lg font-semibold"
                  >
                    {listing.authored.name.slice(0, 2)}
                  </div>

                  <div className="min-w-0 flex-1 space-y-2">
                    <div className="flex flex-wrap items-baseline gap-x-2">
                      <h3 className="font-medium">{listing.authored.name}</h3>
                      <span className="text-muted-foreground text-sm">
                        by {listing.authored.authors.join(', ')}
                      </span>
                    </div>

                    <p className="text-muted-foreground text-sm text-pretty">
                      {listing.authored.abstract}
                    </p>

                    <div className="flex flex-wrap gap-1.5">
                      <Badge variant="secondary">{listing.authored.type}</Badge>
                      {listing.authored.tags?.map((tag) => (
                        <Badge key={tag} variant="outline">
                          {tag}
                        </Badge>
                      ))}
                    </div>
                  </div>

                  <div className="text-muted-foreground hidden shrink-0 flex-col items-end gap-1 text-sm sm:flex">
                    {release && (
                      <span className="text-foreground font-medium">v{release.version}</span>
                    )}
                    {release && <span>Updated {release.release_date.slice(0, 10)}</span>}
                    <span>
                      {listing.releases.length}{' '}
                      {listing.releases.length === 1 ? 'release' : 'releases'}
                    </span>
                    {release && (
                      <a
                        className="underline underline-offset-4"
                        href={release.download.url}
                        rel="noreferrer noopener"
                      >
                        Download
                      </a>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        )}
      </section>
    </div>
  )
}
