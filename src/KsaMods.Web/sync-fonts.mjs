// Copies the IBM Plex files we actually use out of node_modules and into wwwroot/fonts.
//
// The woff2 files are committed, so a clone builds and renders correctly without running npm at
// all. This script exists so that "where did these binaries come from and how do I update them"
// has an answer in the repository rather than in somebody's shell history: bump the versions in
// package.json, `npm install`, run `npm run sync:fonts`, commit the result.
//
// Node rather than `cp`, because npm scripts run under cmd.exe on Windows and this project is
// developed there.

import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "wwwroot", "fonts");

// Only the weights referenced by @font-face in Styles/app.css. Adding a weight here without
// adding the matching @font-face just ships bytes nobody downloads.
const files = [
  ["@fontsource-variable/ibm-plex-sans", "ibm-plex-sans-latin-wght-normal.woff2"],
  ["@fontsource/ibm-plex-sans-condensed", "ibm-plex-sans-condensed-latin-600-normal.woff2"],
  ["@fontsource/ibm-plex-sans-condensed", "ibm-plex-sans-condensed-latin-700-normal.woff2"],
  ["@fontsource/ibm-plex-mono", "ibm-plex-mono-latin-400-normal.woff2"],
  ["@fontsource/ibm-plex-mono", "ibm-plex-mono-latin-500-normal.woff2"],
];

mkdirSync(out, { recursive: true });

for (const [pkg, file] of files) {
  copyFileSync(join(here, "node_modules", pkg, "files", file), join(out, file));
  console.log(`fonts: ${file}`);
}

console.log(`fonts: ${files.length} file(s) synced to wwwroot/fonts`);
