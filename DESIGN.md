---
name: ksamods.gg
description: Mod browser and community for Kitten Space Agency, built on the KSAModding content index and Borea.
colors:
    accent: '#15736d'
    accent-dark: '#2aa9a0'
    accent-wash: '#e8f6f4'
    accent-wash-dark: '#0a2321'
    error: '#f53003'
    error-dark: '#FF4433'
    error-wash: '#fff2f2'
    error-wash-dark: '#1D0002'
    star: '#f5a623'
    ground: '#FDFDFC'
    ground-dark: '#0a0a0a'
    surface: '#ffffff'
    surface-dark: '#161615'
    ink: '#1b1b18'
    ink-dark: '#EDEDEC'
    muted: '#706f6c'
    muted-dark: '#A1A09A'
    fill-subtle: '#f4f3ef'
    fill-subtle-dark: '#232321'
    border: '#19140035'
    border-dark: '#3E3E3A'
    border-hover: '#1915014a'
    border-hover-dark: '#62605b'
    divider: '#19140014'
typography:
    display:
        fontFamily: 'Instrument Sans, ui-sans-serif, system-ui, sans-serif'
        fontSize: 'clamp(2.25rem, 5vw, 3.5rem)'
        fontWeight: 600
        lineHeight: 1.05
        letterSpacing: '-0.025em'
    headline:
        fontFamily: 'Instrument Sans, ui-sans-serif, system-ui, sans-serif'
        fontSize: '1.5rem'
        fontWeight: 600
        lineHeight: 1.33
        letterSpacing: '-0.025em'
    title:
        fontFamily: 'Instrument Sans, ui-sans-serif, system-ui, sans-serif'
        fontSize: '1rem'
        fontWeight: 600
        lineHeight: 1.5
    body:
        fontFamily: 'Instrument Sans, ui-sans-serif, system-ui, sans-serif'
        fontSize: '0.875rem'
        fontWeight: 400
        lineHeight: 1.43
    label:
        fontFamily: 'Instrument Sans, ui-sans-serif, system-ui, sans-serif'
        fontSize: '0.75rem'
        fontWeight: 500
        lineHeight: 1.33
    version:
        fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace'
        fontSize: '0.75rem'
        fontWeight: 400
        fontFeature: 'tnum'
rounded:
    sm: '4px'
    md: '6px'
    lg: '8px'
    full: '9999px'
spacing:
    xs: '4px'
    sm: '8px'
    md: '16px'
    lg: '24px'
    xl: '32px'
    section: '96px'
components:
    button-primary:
        backgroundColor: '{colors.ink}'
        textColor: '{colors.surface}'
        typography: '{typography.label}'
        rounded: '{rounded.md}'
        padding: '10px 20px'
    button-primary-hover:
        backgroundColor: '#000000'
    button-outline:
        backgroundColor: 'transparent'
        textColor: '{colors.ink}'
        rounded: '{rounded.md}'
        padding: '6px 12px'
    card:
        backgroundColor: '{colors.surface}'
        textColor: '{colors.ink}'
        rounded: '{rounded.lg}'
        padding: '16px'
    input:
        backgroundColor: '{colors.surface}'
        textColor: '{colors.ink}'
        typography: '{typography.body}'
        rounded: '{rounded.md}'
        padding: '8px 12px'
    chip:
        backgroundColor: 'transparent'
        textColor: '{colors.ink}'
        typography: '{typography.label}'
        rounded: '{rounded.full}'
        padding: '4px 12px'
    chip-selected:
        backgroundColor: '{colors.ink}'
        textColor: '{colors.surface}'
    version-pill:
        backgroundColor: '{colors.fill-subtle}'
        textColor: '{colors.ink}'
        typography: '{typography.version}'
        rounded: '{rounded.sm}'
        padding: '2px 6px'
---

# Design System: ksamods.gg

## Overview

**Creative North Star: "The Quiet Index"**

ksamods.gg reads like a well-kept catalogue: warm off-white paper, near-black ink, white cards drawn with a single hairline, and nothing decorative between the reader and the mods. The only imagery is the mods' own icons (and member avatars); every other surface is type, hairline and a little warm grey. The system is light by default and follows the operating system into a near-black dark mode where every value has a hand-picked twin.

Density is moderate and list-like. Pages are built from cards on a 72rem column, with small type (14px body is the working size) and generous vertical gaps between sections. Hierarchy comes from weight and size inside one family, not from colour. The teal accent is rare and means something: today's node on the release rail, the unread badge, the "Mod loader" marker, pre-release pills, link hover and selection. Red is reserved for errors.

**Key Characteristics:**

- Two grounds, two card surfaces, one ink, one muted grey, each with an explicit dark twin.
- Hairline inset borders instead of drop shadows; lift shadows only on floating layers.
- One typeface (Instrument Sans) at weights 400/500/600, tight tracking on headings.
- Accent used for state and signal, never for fills of large areas.
- Mod icons are the only pictures.

## Colors

A warm, near-neutral paper-and-ink palette with one teal signal colour, a red reserved for errors, and a star gold for ratings.

### Primary

- **Signal Teal** (accent / accent-dark): the sole accent, matching the logo's #2AA9A0 (used as-is in dark mode, deepened in light mode so text stays readable), defined once as `--accent` in app.css and used through the `accent` / `accent-wash` Tailwind colours. Today's node on the release rail, the notification count badge, the "Mod loader" type marker, text-link hover, `accent-color` for native controls, and a 25% tint for text selection.
- **Signal Wash** (accent-wash / accent-wash-dark): pale and deep backgrounds behind Signal Teal text in pre-release pills.
- **Error Red** (error / error-dark, with error-wash): form error text, invalid input borders and error banners. Never decorative.

### Secondary

- **Star Gold** (star): filled rating stars only; also a 10% wash with 40% ring to highlight a linked comment (`:target`). Empty stars use a faint ink tint (#19140026 / #3E3E3A).

### Neutral

- **Paper** (ground / ground-dark): the page background.
- **Card White / Card Coal** (surface / surface-dark): cards, inputs, menus, toasts.
- **Ink** (ink / ink-dark): all primary text, primary button fill, selected chip fill, active tab underline, focus outline.
- **Pencil Grey** (muted / muted-dark): metadata, ledes, hints, bylines, counts, inactive tabs. Mod abstracts on cards sit between ink and muted (#4a4a46 / #c4c3be).
- **Warm Fill** (fill-subtle / fill-subtle-dark): icon placeholders, version pills, step-number discs, inline code and pre blocks, menu-item and list-row hover (dark row hover uses #1c1c1a).
- **Rule Lines** (border, border-hover, divider): input, outline-button and chip borders at border; their hover at border-hover; section dividers, header rule and list dividers at the fainter divider (dark: #3E3E3A at 60%).

### Named Rules

**The Twin Rule.** Every colour is written as a light/dark pair in the same class string. A value without its dark twin is a bug.

**The Signal Rule.** Signal Teal marks state or attention (today, unread, hover, selection); Error Red marks errors only. It never fills a button, a card or a section background.

## Typography

**Display Font:** Instrument Sans (with ui-sans-serif, system-ui, sans-serif), loaded from Bunny Fonts via the Vite font plugin.
**Body Font:** Instrument Sans.
**Label/Mono Font:** the platform monospace stack, for version strings only.

**Character:** One modern grotesque does everything; contrast comes from weight (400 body, 500 names and labels, 600 headings) and tight negative tracking on headings, giving a calm, tool-like voice.

### Hierarchy

- **Display** (600, 2.25rem rising to 3rem at sm and 3.5rem at lg, line-height 1.05, tracking-tight, balanced wrap): the landing headline only.
- **Headline** (600, 1.5rem, tracking-tight): page titles (mod name, Mods, forms). Landing section headings use 1.25rem rising to 1.5rem.
- **Title** (600 or 500, 1rem): card titles, mod names in lists, panel headings. The newest release in the log steps up to 1.125rem.
- **Body** (400, 0.875rem): the working size for nearly all UI text. Long-form ledes and descriptions use 1rem to 1.125rem with relaxed leading and `max-width: 65ch` (prose) or 36–42rem.
- **Label** (500, 0.75rem): metadata rows, counts, day markers, hints and errors. Sentence case; no uppercase labels anywhere.
- **Version** (mono, 0.75rem, tabular numerals): version strings, always inside a version pill.

### Named Rules

**The Sentence-Case Rule.** Labels and headings are sentence case at normal tracking; there is no uppercase micro-label tier in this system.

**The Tabular Count Rule.** Counts, download numbers, versions and step numbers use tabular numerals.

## Layout

A single centred column (max-width 72rem, 16px side padding, 24px from sm) under a full-width header with a bottom hairline, above a footer with a top hairline that holds the 14px muted Privacy and Terms links. Content is cards in grids: mod shelves are 1, 2 then 3 columns (sm, lg) with a 16px gap. The mod page puts content beside a sidebar that is sticky at lg (top 24px, scrolls internally when taller than the viewport). The landing first viewport splits 5:7 at lg.

Rhythm: 4/8px steps inside components, 16–24px card padding (32px on large explanatory panels), 24px between heading and content, and 80–96px between landing sections. Headers and toolbars wrap rather than overflow on narrow phones; tab bars scroll horizontally with hidden scrollbars. The release log and similar lists stay single-column on phones and move actions under the text.

## Elevation & Depth

The system is flat. Depth is drawn, not cast: resting surfaces carry a 1px inset hairline shadow that acts as a border, and hover darkens that hairline rather than lifting the card. Real drop shadows appear only on layers that float above the page (the install menu and the notification toast).

### Shadow Vocabulary

- **Hairline** (`box-shadow: inset 0 0 0 1px rgba(26,26,0,0.16)`; dark `inset 0 0 0 1px #fffaed2d`): every card and panel at rest.
- **Hairline hover** (`inset 0 0 0 1px rgba(26,26,0,0.4)`; dark `#fffaed5a`): hovered link cards and path tiles.
- **Float** (`0 8px 24px rgba(0,0,0,0.12), inset 0 0 0 1px rgba(26,26,0,0.16)`; dark `0 8px 24px rgba(0,0,0,0.5), inset 0 0 0 1px #fffaed2d`): dropdown menus and toasts.
- **Base rule** (`inset 0 -1px 0 #19140014`): the tab bar's bottom line, which the active tab's 2px ink border paints over.

### Named Rules

**The Drawn-Not-Cast Rule.** Surfaces on the page get a hairline, never a drop shadow. Only something floating over other content casts one.

## Shapes

Softly rounded rectangles throughout: small 4px corners on pills and inline code, 6px on buttons, inputs and menus, 8px on cards and mod icons, full circles for chips, avatars, step discs, rail nodes and the unread badge. The landing page's larger panels use 12px; it is a surface choice, not a scale step. Empty states and the "not yours to edit" notice use a dashed border at border colour. Mod icons are square with 8px corners; avatars are circles.

## Components

### Buttons

Plain, inked and compact.

- **Shape:** gently rounded (6px).
- **Primary:** ink fill, white text (dark: #EDEDEC fill, #0a0a0a text), 500 weight at 14px, 10px × 20px padding (8px × 16px on the landing, 6px × 12px at 12px text for per-row Install). Hover goes to pure black (dark: pure white). Disabled drops to 60% opacity.
- **Outline:** transparent with a 1px border (border colour, border-hover on hover), 6px × 12px; used for Log in / Log out. Row-level Download uses the hairline inset instead of a border.
- **Split button:** the mod page's Install joins a primary button and a menu trigger with the inner corners squared; the menu opens below as a Float layer with full-width 8px × 12px items that take Warm Fill on hover and focus.

### Chips

- **Style:** pill (full radius), 1px border, 12px text, 4px × 12px.
- **State:** `aria-pressed` fills with ink and inverts text. Small static tag pills in the sidebar use the same border at 2px × 10px.

### Cards / Containers

- **Corner Style:** 8px.
- **Background:** Card White / Card Coal.
- **Shadow Strategy:** Hairline at rest, Hairline hover when the whole card is a link (see Elevation).
- **Border:** none; the inset hairline is the border.
- **Internal Padding:** 16px (mod card), 20px (form sections, release log), 24–32px (explanatory panels).

### Inputs / Fields

- **Style:** Card White fill, 1px border-colour stroke, 6px radius, 8px × 12px, 14px text. Labels above at 14px/500 with "(optional)" in muted; hint or error below at 12px.
- **Focus:** the border turns ink (dark: #EDEDEC); no glow. Global `:focus-visible` is a 2px ink outline at 2px offset.
- **Error:** border and message in Error Red; banner errors on Error Wash.

### Navigation

Header: site name at 600 with tight tracking on the left; right side 14px text links that underline on hover, a 20px stroked bell (1.75 stroke) with a Signal Teal count badge, avatar plus name, and an outline Log in / Log out button. Below sm only the site name, the bell and a 20px stroked hamburger remain; the hamburger (a Signal Teal dot when reports are open) opens a full-width panel under the header rule with the links as stacked rows (Warm Fill hover) and a full-width outline Log in / Log out button. Tabs on the mod page: 14px/500 text, muted when inactive, 2px ink underline when active, count in 12px muted, arrow-key navigation.

### Mod Page Extras

Gallery: a horizontally scrolling strip of 144px (176px from sm) image thumbnails on the 1px inset hairline, between the header and the tabs; a thumbnail opens a modal viewer on an 80% black backdrop with round translucent-black close, previous and next buttons and a count pill. Under the install button, a 12px Signal Teal check line reads "Works with your game (version)"; when the release needs a newer game, a 12px ink line says so and links the newest release that fits. Sidebar: "Your game" is a compact select in the Details list, tags are chips that link to the filtered mod list, and a "Required by" card lists dependents as 24px icon plus name rows, folding past six behind a muted "Show all" link.

### Mod Card

Icon (48px) beside name (500) and "by" authors (12px muted), a three-line abstract, then a 12px metadata row: "Mod loader" in Signal Teal, a gold star with average and count, version, downloads, updated date.

### Release Log

A card holding a vertical 1px rail at 40px from the left. Each day opens with a 10px node (Signal Teal for the newest day, ink for earlier) and a muted 12px day label; each release row is a 40px mod icon ringed in the card colour so it sits on the rail, the name, a version pill, a one-line muted summary and a right-aligned Install or Download. Rows enter with a 700ms ease-out rise of 8px, staggered 70ms, disabled under reduced motion.

### Community List

One card divided by divider-colour rules; each row is a 32px round avatar, author in 500 with "on Mod · time" in muted, optional stars, and a two-line excerpt. Rows take Warm Fill on hover.

### Toast

Bottom-right, max 24rem, 8px radius, Float shadow, 16px padding; title in 500, excerpt two lines muted. Disappears after 6 seconds.

## Do's and Don'ts

### Do:

- **Do** write every colour as a light/dark pair (for example ink #1b1b18 with #EDEDEC, card #fff with #161615).
- **Do** draw card edges with the inset hairline (rgba(26,26,0,0.16) / #fffaed2d) and darken it on hover.
- **Do** keep body UI at 14px and metadata at 12px muted, with tabular numerals for counts and versions.
- **Do** use mod icons, with the Warm Fill initial-letter placeholder when a mod has none, as the only imagery.
- **Do** keep Signal Teal to state and signal: today, unread, link hover, selection; keep red for errors.
- **Do** inline stroked SVG for icons (as the bell and star do).

### Don't:

- **Don't** fill buttons, cards or sections with Signal Teal; primary actions are ink.
- **Don't** add drop shadows to resting cards; reserve the Float shadow for menus and toasts.
- **Don't** introduce a second typeface or uppercase tracked labels.
- **Don't** add stock photography, illustration or gradient decoration; the mods' own icons carry the imagery.
- **Don't** use unicode arrows or emoji as icons.
