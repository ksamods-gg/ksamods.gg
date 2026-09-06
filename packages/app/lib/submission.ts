/**
 * The listing submission form's data, and the rules it checks before asking
 * the server.
 *
 * The regexes are duplicated from the upstream schema on purpose. A round trip
 * per keystroke is not viable, and telling somebody their id is invalid only
 * after they press Preview is unacceptable for a field that can never be
 * renamed. The server stays authoritative: if these ever drift, it wins.
 */

export const ID_RE = /^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/;
export const TAG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
export const GAME_RE =
  /^[0-9]{4}\.(?:1[0-2]|[1-9])(?:\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))?$/;
export const SEMVER_RE =
  /^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/;
export const GH_SLUG_RE =
  /^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?\/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$/;
export const FORUMS_RE = /^https:\/\/forums\.ahwoo\.com\/\S*$/;
export const URL_RE = /^https?:\/\/\S+$/;

/** Reserved by Windows, by the game, or by our own routing. `new` is here
 *  because /mods/new would otherwise shadow the listing's own page. */
const RESERVED = /^(?:core|con|prn|aux|nul|com[1-9]|lpt[1-9]|new)(?:\.|$)/i;

export type Anchor = "mods" | "user-data" | "game-root" | "standalone";
export type DepKind =
  "required" | "optional" | "recommends" | "suggests" | "conflict";

/**
 * A form shaped mirror of the document. Every leaf is a string or a string
 * array, never a number and never undefined, because inputs produce strings and
 * a `number | ''` limbo is where the "0 is falsy" bugs live. Coercion happens
 * once, in toDocument.
 */
export type Draft = {
  id: string;
  type: "mod" | "mod-loader";
  name: string;
  authors: string[];
  abstract: string;
  description: string;
  license: string;
  licenseOther: string;
  tags: string[];
  status: "" | "active" | "deprecated";
  superseded_by: string;
  links: {
    forums: string;
    repository: string;
    spacedock: string;
    bugtracker: string;
    discussions: string;
    homepage: string;
  };
  compatibility: { game_min: string; game_max: string; os: string[] };
  releases: {
    github: string;
    spacedock: string;
    authority: "" | "github" | "spacedock";
  };
  loader: { id: string; min: string; max: string };
  dependencies: { id: string; kind: DepKind; min: string; max: string }[];
  install: {
    root: string;
    target: "" | Anchor;
    path: string;
    manages: string;
    steps: string;
    uninstall: string;
  };
  provides: {
    launch: string;
    contentDir: "" | Anchor;
    contentPath: string;
    configure: { file: string; format: "" | "json" | "toml"; gamePath: string };
  };
};

export const emptyDraft = (): Draft => ({
  id: "",
  type: "mod",
  name: "",
  authors: [""],
  abstract: "",
  description: "",
  license: "MIT",
  licenseOther: "",
  tags: [],
  status: "",
  superseded_by: "",
  links: {
    forums: "",
    repository: "",
    spacedock: "",
    bugtracker: "",
    discussions: "",
    homepage: "",
  },
  compatibility: { game_min: "", game_max: "", os: [] },
  releases: { github: "", spacedock: "", authority: "" },
  loader: { id: "", min: "", max: "" },
  dependencies: [],
  install: {
    root: "",
    target: "",
    path: "",
    manages: "",
    steps: "",
    uninstall: "",
  },
  provides: {
    launch: "",
    contentDir: "",
    contentPath: "",
    configure: { file: "", format: "", gamePath: "" },
  },
});

/** A curated list, because upstream resolves identifiers against the real SPDX
 *  list and a typo is a CI rejection. "other" reveals a free text expression. */
export const LICENSES = [
  "MIT",
  "Apache-2.0",
  "GPL-3.0-only",
  "GPL-3.0-or-later",
  "GPL-2.0-only",
  "LGPL-3.0-only",
  "MPL-2.0",
  "BSD-2-Clause",
  "BSD-3-Clause",
  "Unlicense",
  "CC0-1.0",
  "CC-BY-4.0",
  "CC-BY-SA-4.0",
  "Zlib",
];

const lines = (value: string) =>
  value
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean);

const clean = (values: string[]) =>
  values.map((value) => value.trim()).filter(Boolean);

/** Only the keys that carry a value, so an empty section never reaches the
 *  document and gets rendered as a bare TOML table. */
function compact<T extends Record<string, unknown>>(
  value: T,
): Partial<T> | undefined {
  const out: Record<string, unknown> = {};
  for (const [key, entry] of Object.entries(value)) {
    if (entry === "" || entry === undefined) continue;
    if (Array.isArray(entry) && entry.length === 0) continue;
    out[key] = entry;
  }
  return Object.keys(out).length > 0 ? (out as Partial<T>) : undefined;
}

export const licenseOf = (draft: Draft) =>
  draft.license === "other" ? draft.licenseOther.trim() : draft.license;

/**
 * The document the server is asked to render.
 *
 * Fields belonging to a hidden section are DROPPED, not merely hidden. Filling
 * in provides and then switching the type back to mod would otherwise ship a
 * stale value that upstream CI rejects.
 */
export function toDocument(draft: Draft): Record<string, unknown> {
  const isMod = draft.type === "mod";

  const document: Record<string, unknown> = {
    spec_version: 1,
    id: draft.id.trim(),
    type: draft.type,
    name: draft.name.trim(),
    authors: clean(draft.authors),
    abstract: draft.abstract.trim(),
    license: licenseOf(draft),
    links: compact({
      forums: draft.links.forums.trim(),
      repository: draft.links.repository.trim(),
      spacedock: draft.links.spacedock.trim(),
      bugtracker: draft.links.bugtracker.trim(),
      discussions: draft.links.discussions.trim(),
      homepage: draft.links.homepage.trim(),
    }),
    compatibility: compact({
      game_min: draft.compatibility.game_min.trim(),
      game_max: draft.compatibility.game_max.trim(),
      os: draft.compatibility.os,
    }),
  };

  if (draft.description.trim()) document.description = draft.description;
  if (draft.tags.length) document.tags = clean(draft.tags);
  if (draft.status) document.status = draft.status;
  // A successor pointer without a deprecation is data no client acts on.
  if (draft.status === "deprecated" && draft.superseded_by.trim()) {
    document.superseded_by = draft.superseded_by.trim();
  }

  const releases = compact({
    github: draft.releases.github.trim(),
    spacedock: draft.releases.spacedock.trim()
      ? Number(draft.releases.spacedock)
      : "",
    authority: draft.releases.authority,
  });
  if (releases) document.releases = releases;

  // Only a mod carries a loader, and only a mod loader carries provides.
  if (isMod) {
    const loader = compact({
      id: draft.loader.id.trim(),
      min: draft.loader.min.trim(),
      max: draft.loader.max.trim(),
    });
    if (loader) document.loader = loader;
  }

  const dependencies = draft.dependencies
    .filter((entry) => entry.id.trim())
    .map((entry) =>
      compact({
        id: entry.id.trim(),
        kind: entry.kind,
        min: entry.min.trim(),
        max: entry.max.trim(),
      }),
    );
  if (dependencies.length) document.dependencies = dependencies;

  const install = compact({
    root: draft.install.root.trim(),
    // The game decides where a mod goes, so the anchor is pinned.
    target: isMod ? "mods" : draft.install.target,
    path: isMod ? "" : draft.install.path.trim(),
    manages: lines(draft.install.manages),
    steps: lines(draft.install.steps),
    uninstall: lines(draft.install.uninstall),
  });
  // A pinned target alone is not an install section worth writing.
  if (install && Object.keys(install).length > (isMod ? 1 : 0))
    document.install = install;

  if (!isMod) {
    const configure = compact({
      file: draft.provides.configure.file.trim(),
      format: draft.provides.configure.format,
      "game-path": draft.provides.configure.gamePath.trim(),
    });
    const provides = compact({
      launch: draft.provides.launch.trim(),
      "content-dir": draft.provides.contentDir,
      "content-path": draft.provides.contentPath.trim(),
      configure: configure?.file && configure.format ? configure : undefined,
    });
    if (provides) document.provides = provides;
  }

  return document;
}

export type Errors = Record<string, string>;

/**
 * Field errors keyed by dotted path, matching the shape the server returns its
 * blockers in, so both sources land under the same input.
 */
export function validate(draft: Draft, takenIds: string[] = []): Errors {
  const errors: Errors = {};
  const id = draft.id.trim();

  if (!id) errors.id = "Pick an id";
  else if (!ID_RE.test(id))
    errors.id = "Letters, numbers, dot, dash and underscore only";
  else if (RESERVED.test(id)) errors.id = "That name is reserved";
  else if (id.includes(".."))
    errors.id = "An id cannot contain two dots in a row";
  else if (takenIds.some((taken) => taken.toLowerCase() === id.toLowerCase())) {
    errors.id = "A listing with that id already exists";
  }

  if (!draft.name.trim()) errors.name = "Give it a name";
  if (!clean(draft.authors).length) errors.authors = "Name at least one author";
  if (!draft.abstract.trim()) errors.abstract = "Write one or two sentences";

  const license = licenseOf(draft);
  if (!license) errors.licenseOther = "Enter an SPDX expression";

  if (!draft.links.forums.trim())
    errors["links.forums"] = "A forums thread is required";
  else if (!FORUMS_RE.test(draft.links.forums.trim())) {
    errors["links.forums"] = "Must be a forums.ahwoo.com thread URL";
  }

  for (const key of [
    "repository",
    "spacedock",
    "bugtracker",
    "discussions",
    "homepage",
  ] as const) {
    const value = draft.links[key].trim();
    if (value && !URL_RE.test(value))
      errors[`links.${key}`] = "Use a full http or https URL";
  }

  if (!draft.compatibility.game_min.trim()) {
    errors["compatibility.game_min"] =
      "Pick the oldest game build known to work";
  } else if (!GAME_RE.test(draft.compatibility.game_min.trim())) {
    errors["compatibility.game_min"] = "Use a build such as 2026.8.3.5117";
  }

  draft.tags.forEach((tag, index) => {
    if (tag.trim() && !TAG_RE.test(tag.trim())) {
      errors[`tags.${index}`] = "Lowercase, dash separated";
    }
  });

  const github = draft.releases.github.trim();
  if (github && !GH_SLUG_RE.test(github))
    errors["releases.github"] = "Use owner/repo";

  const spacedock = draft.releases.spacedock.trim();
  if (spacedock && !/^[1-9][0-9]*$/.test(spacedock)) {
    errors["releases.spacedock"] = "Use the numeric SpaceDock mod id";
  }
  if (github && spacedock && !draft.releases.authority) {
    errors["releases.authority"] =
      "Say which host defines which releases exist";
  }

  if (draft.type === "mod") {
    const loader = draft.loader;
    if ((loader.min.trim() || loader.max.trim()) && !loader.id.trim()) {
      errors["loader.id"] = "Name the loader";
    }
    if (loader.id.trim() && !loader.min.trim())
      errors["loader.min"] = "Give the oldest version";
    for (const key of ["min", "max"] as const) {
      const value = loader[key].trim();
      if (value && !SEMVER_RE.test(value))
        errors[`loader.${key}`] = "Use a version such as 1.2.3";
    }
  }

  draft.dependencies.forEach((entry, index) => {
    if (!entry.id.trim())
      errors[`dependencies.${index}.id`] = "Name the dependency";
    else if (!ID_RE.test(entry.id.trim()))
      errors[`dependencies.${index}.id`] = "Not a valid id";
    for (const key of ["min", "max"] as const) {
      const value = entry[key].trim();
      if (value && !SEMVER_RE.test(value)) {
        errors[`dependencies.${index}.${key}`] = "Use a version such as 1.2.3";
      }
    }
  });

  if (draft.status === "deprecated" && !draft.superseded_by.trim()) {
    // Not fatal upstream, but a deprecation with no successor helps nobody.
    errors.superseded_by = "Name the listing that replaces this one";
  }

  if (draft.type === "mod-loader") {
    const install = draft.install;
    const stated =
      install.root ||
      install.target ||
      install.manages ||
      install.steps ||
      install.uninstall;
    if (stated && !install.target) {
      errors["install.target"] = "A mod loader must say where it installs";
    }
    if (install.target === "standalone" && !draft.provides.launch.trim()) {
      errors["provides.launch"] =
        "A standalone install must name what to launch";
    }
    if (draft.provides.contentPath.trim() && !draft.provides.contentDir) {
      errors["provides.contentDir"] =
        "Naming a content path needs a content directory";
    }
  }

  return errors;
}
