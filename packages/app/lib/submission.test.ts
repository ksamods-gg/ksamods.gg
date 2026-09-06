import { expect, test } from "bun:test";
import { type Draft, emptyDraft, toDocument, validate } from "./submission";

/** A draft that passes, as the base for negative cases. */
function filled(patch: (draft: Draft) => void = () => {}): Draft {
  const draft = emptyDraft();
  draft.id = "ExampleMod";
  draft.name = "Example Mod";
  draft.authors = ["Someone"];
  draft.abstract = "An example.";
  draft.links.forums = "https://forums.ahwoo.com/threads/example.1/";
  draft.compatibility.game_min = "2026.8.3.5117";
  patch(draft);
  return draft;
}

test("a filled draft has no errors", () => {
  expect(validate(filled())).toEqual({});
});

test.each([
  ["an empty id", (d: Draft) => (d.id = ""), "id"],
  ["a slash in the id", (d: Draft) => (d.id = "owner/repo"), "id"],
  ["a reserved id", (d: Draft) => (d.id = "CON"), "id"],
  ["the routing reserved id", (d: Draft) => (d.id = "new"), "id"],
  ["a traversing id", (d: Draft) => (d.id = "a..b"), "id"],
  ["no authors", (d: Draft) => (d.authors = ["  "]), "authors"],
  ["no abstract", (d: Draft) => (d.abstract = ""), "abstract"],
  [
    "a non forums thread",
    (d: Draft) => (d.links.forums = "https://example.com/x"),
    "links.forums",
  ],
  [
    "a malformed game build",
    (d: Draft) => (d.compatibility.game_min = "2026"),
    "compatibility.game_min",
  ],
  ["an uppercase tag", (d: Draft) => (d.tags = ["Utility"]), "tags.0"],
  [
    "a bad repo slug",
    (d: Draft) => (d.releases.github = "not-a-slug"),
    "releases.github",
  ],
  [
    "a non numeric spacedock id",
    (d: Draft) => (d.releases.spacedock = "abc"),
    "releases.spacedock",
  ],
  [
    "a loader version that is not semver",
    (d: Draft) => {
      d.loader.id = "StarMap";
      d.loader.min = "v1.0";
    },
    "loader.min",
  ],
  [
    "a broken link",
    (d: Draft) => (d.links.homepage = "example.com"),
    "links.homepage",
  ],
])("flags %s", (_label, patch, field) => {
  expect(Object.keys(validate(filled(patch)))).toContain(field);
});

test("two release hosts need an authority", () => {
  const errors = validate(
    filled((d) => {
      d.releases.github = "owner/repo";
      d.releases.spacedock = "4253";
    }),
  );
  expect(errors["releases.authority"]).toBeDefined();
});

test("an id already in the index is refused", () => {
  // Upstream folds ids case insensitively, so this must too.
  expect(validate(filled(), ["examplemod"]).id).toBeDefined();
  expect(validate(filled(), ["SomethingElse"]).id).toBeUndefined();
});

test("a mod loader must say where it installs", () => {
  const errors = validate(
    filled((d) => {
      d.type = "mod-loader";
      d.install.uninstall = "Delete the folder.";
    }),
  );
  expect(errors["install.target"]).toBeDefined();
});

test("a standalone install must name what to launch", () => {
  const errors = validate(
    filled((d) => {
      d.type = "mod-loader";
      d.install.target = "standalone";
    }),
  );
  expect(errors["provides.launch"]).toBeDefined();
});

// ------------------------------------------------------------- toDocument

test("builds the minimal document", () => {
  const document = toDocument(filled());
  expect(document).toMatchObject({
    spec_version: 1,
    id: "ExampleMod",
    type: "mod",
    name: "Example Mod",
    authors: ["Someone"],
    abstract: "An example.",
    license: "MIT",
    links: { forums: "https://forums.ahwoo.com/threads/example.1/" },
    compatibility: { game_min: "2026.8.3.5117" },
  });
  // Nothing empty rides along, or it renders as a bare TOML table.
  expect(document).not.toHaveProperty("releases");
  expect(document).not.toHaveProperty("install");
  expect(document).not.toHaveProperty("provides");
  expect(document).not.toHaveProperty("tags");
});

test("drops fields belonging to a hidden section", () => {
  // Fill in a mod loader, then switch back to a plain mod. The provides values
  // are still in the draft but must not reach the document, or upstream CI
  // rejects a file the user cannot see anything wrong with.
  const draft = filled((d) => {
    d.type = "mod-loader";
    d.provides.launch = "Loader.exe";
    d.provides.contentDir = "mods";
    d.install.target = "standalone";
    d.install.path = "somewhere";
    d.loader.id = "StarMap";
    d.loader.min = "1.0.0";
  });

  const asLoader = toDocument(draft);
  expect(asLoader).toHaveProperty("provides");
  expect(asLoader).not.toHaveProperty("loader");

  draft.type = "mod";
  const asMod = toDocument(draft);
  expect(asMod).not.toHaveProperty("provides");
  expect(asMod).toHaveProperty("loader");
  // Nothing but the pinned anchor is left, and a section saying only what the
  // type already implies is noise.
  expect(asMod).not.toHaveProperty("install");
});

test("a mod cannot ship a stale install path or anchor", () => {
  const document = toDocument(
    filled((d) => {
      // Both of these are the mod loader's to set, not a mod's.
      d.install.target = "standalone";
      d.install.path = "somewhere";
      d.install.uninstall = "Delete the folder.";
    }),
  );
  const install = document.install as Record<string, unknown>;
  expect(install.target).toBe("mods");
  expect(install).not.toHaveProperty("path");
});

test("splits the prose lists on newlines and drops blanks", () => {
  const document = toDocument(
    filled((d) => {
      d.type = "mod-loader";
      d.install.target = "standalone";
      d.provides.launch = "A.exe";
      d.install.uninstall = "Delete the folder.\n\n  Remove the config.  \n";
    }),
  );
  expect((document.install as Record<string, unknown>).uninstall).toEqual([
    "Delete the folder.",
    "Remove the config.",
  ]);
});

test("coerces the spacedock id to a number", () => {
  const document = toDocument(filled((d) => (d.releases.spacedock = "4253")));
  expect((document.releases as Record<string, unknown>).spacedock).toBe(4253);
});

test("a successor only ships alongside a deprecation", () => {
  const draft = filled((d) => (d.superseded_by = "NewMod"));
  expect(toDocument(draft)).not.toHaveProperty("superseded_by");

  draft.status = "deprecated";
  expect(toDocument(draft)).toHaveProperty("superseded_by", "NewMod");
});

test("the other license escape hatch is used when selected", () => {
  const draft = filled((d) => {
    d.license = "other";
    d.licenseOther = "MIT OR Apache-2.0";
  });
  expect(toDocument(draft).license).toBe("MIT OR Apache-2.0");
  expect(validate(draft)).toEqual({});
});
