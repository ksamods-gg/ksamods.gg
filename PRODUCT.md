# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- **Players of Kitten Space Agency (KSA)** who want mods: find one, judge whether it is good and compatible, and install it. On Windows and Linux they install with Borea, the KSA content manager; elsewhere they download the archive. Phones and tablets browse only; installing happens on a desktop.
- **Mod authors** who want their mod listed: they submit and edit listings, which ksamods.gg opens as pull requests on the KSA content index from their own GitHub account.

The landing page serves both equally.

## Product Purpose

ksamods.gg is a mod browser and community for KSA. It shows every mod in the KSA content index with its releases, dependencies, reviews and discussion, gets players to a one-click Borea install, and gives authors a form-based way to publish and maintain listings. Success: players find and install mods, authors list and keep their mods current, and both talk to each other on the mod pages.

## Positioning

ksamods.gg is its own site, built on top of the KSAModding community's work: the content index (`KSAModding/content-index`, published as `content-index-releases`) and Borea (`KSAModding/Borea`). It is not the official KSAModding site and must not present itself as one. Its owner, SafeShows, is a staff member on the KSAModding Discord server. What it adds over the raw index: browsing, reviews, comments and notifications, Borea one-click links, and publishing listings without editing TOML by hand.

## Operating Context

- Mod data comes from the content index snapshot, refreshed every 10 minutes; ksamods.gg stores no mod files and serves no downloads.
- Sign-in is with GitHub or Discord. Publishing and editing listings need GitHub, because they open pull requests; only a mod's owner, by the index's own ownership rules, can edit its listing.
- Borea handles `borea://` links on Windows and Linux only.

## Capabilities and Constraints

- Browse and search mods, with the search and filters kept in the shareable URL; mod pages with an image gallery, description, dependencies, the mods that require it, releases, reviews (1 to 5 stars) and nested comments.
- The player's game version, remembered in the browser, filters the mod list and tells each mod page whether its release works with it.
- Install with Borea (Windows, Linux), direct download as fallback, desktop-only notice on phones and tablets.
- Submit and edit listings as pull requests; My mods; linked GitHub and Discord accounts; self-service account deletion.
- Link previews (Open Graph) and a sitemap, so shared links show the mod and search engines find every mod.
- Live notifications for replies and @mentions over Laravel Reverb.
- Stack: Laravel 13, Inertia 3 with SSR, Vue 3, TypeScript, Tailwind 4; deployed with Coolify at ksamods.gg.

## Brand Commitments

- Name: ksamods.gg.
- A logo exists; the owner will provide it. Until then the site uses the text wordmark.
- Credit KSAModding for the content index and Borea wherever the site describes where its data or installer comes from.

## Evidence on Hand

- Real content: every listing in the content index (names, authors, icons, description screenshots, download counts, releases), and the site's own reviews and comments.
- No testimonials, press, partner logos, user counts or game screenshots of the site's own; none may be invented.

## Product Principles

1. The index is the source of truth; the site never claims data it does not get from it.
2. Installing is one click where Borea runs, and never a dead end where it does not.
3. Authors keep control: listings change only through pull requests they own.
4. Credit the community the site is built on.
