# Handoff: Meridian marketing & docs landing page

## Overview
A single-page marketing/documentation site for **Meridian** — a domain-neutral
**structural merge & semantic-diff** toolkit for source files (XML, JSON, YAML,
HTML, etc.) whose meaning is richer than line order. The page explains the
problem, the merge model, the schema format, supported provider bundles, nested
content, design constraints, and a quick-start.

It is a long, single-column scrolling page with a sticky top nav, an anchored
section structure, **light + dark theming**, and several syntax-highlighted code
windows. The tone is calm, precise, developer-facing (Apple-grade restraint).

## About the Design Files
The files in this bundle are **design references created in HTML** — a working
prototype showing the intended look, content, and behavior. They are **not
production code to copy directly**. The build uses a small in-house runtime
(`support.js`, the `.dc.html` "Design Component" format, and `<x-import>` tags);
**do not port that runtime**. Instead, **recreate these designs in the target
codebase's environment** (React, Vue, Svelte, Astro, plain HTML/CSS, etc.) using
its established component patterns. If no front-end environment exists yet, pick
the most appropriate one for a marketing/docs site (e.g. Astro or Next.js) and
implement there.

The page is built against an invented design system called **Keel** (cobalt
accent, Hanken Grotesk + Fira Code, hairline-over-shadow, soft radii). All Keel
values you need are inlined in the **Design Tokens** section below — you do **not**
need the Keel bundle. Map them onto the target app's own design system where one
exists; otherwise use the tokens verbatim.

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, copy, interactions,
and both light/dark palettes are specified. Recreate the UI faithfully using the
codebase's existing libraries and patterns.

---

## Global layout & chrome

- **Container:** content is centered in a `max-width: 1200px` column with `32px`
  horizontal padding. Section vertical padding is `96px` top/bottom; the hero is
  `80px` top / `56px` bottom.
- **Section rhythm:** sections are separated by a `1px` top hairline
  (`--border-subtle`). Backgrounds alternate white (`--surface-base`) and faint
  gray (`--surface-subtle`); the merge-model and quick-start sections use the
  gray fill, the rest are base. The footer is **midnight** (`#0A1430`) in both
  themes.
- **Sticky nav** (`position: sticky; top: 0; z-index: 40`): frosted material —
  `background: --surface-overlay` (72% white / dark) + `backdrop-filter:
  saturate(180%) blur(20px)`, `1px` bottom hairline, `64px` tall. Contains:
  - Logo: a 38px line-art "meridian" glyph (circle + vertical ellipse + crosshair
    + center dot, all cobalt `1.3px` strokes) next to the wordmark **Meridian**
    (700, 18px, `-0.01em`).
  - Section links (15px / 500, `--text-secondary`, hover → `--text-primary`):
    **Problem, Merge model, Schema, Formats, Nesting, Constraints, Quick start**.
  - Right group (`margin-left:auto`, flex, gap 14px): **theme toggle** (36px ghost
    icon button, moon in light / sun in dark) and a **GitHub stars** control
    (octocat + star count, a plain link to the repo — see Components).

## Screens / Views
This is one continuous page; "views" below are its stacked sections in order.

### 1. Hero (`#top`)
- **Eyebrow badge** (Keel Badge, `accent` tone, dot): "Structural merge & semantic
  diff · Git-native".
- **H1** (`clamp(40px,5.6vw,66px)`, 800, line-height 1.02, `-0.03em`,
  `max-width:14ch`): "Merge by structure, not by line order."
- **Sub** (20px/1.5, `--text-secondary`, `max-width:40ch`): one-sentence
  positioning. **Body** (16px/1.6, `--text-secondary`, `max-width:62ch`): lists
  the formats.
- **CTA row** (flex, gap 12px): primary pill **"View on GitHub"** (cobalt,
  48px tall, `980px` radius) + outline pill **"See the merge case →"** + an inline
  install chip (`--surface-subtle`, mono): `$ dotnet tool install --global MeridianGit`.
- **Stat grid** (`dl`, 4 columns, `1px` gaps over a hairline grid, `14px` radius):
  State / Interface / Semantics / Boundary — each an uppercase 11px label
  (`--text-tertiary`) over a 15px/600 value.
- **Worked-example card** (`18px` radius, `--surface-subtle`, soft shadow):
  - Header strip (white/card): bold line "A merge driver that reads structure
    before it writes markers." + caption + a **Clean merge** success Badge.
  - Body is a 3-column grid `1fr auto 1fr`, items top-aligned:
    1. **Conflict CodeBlock** (editor chrome, tab `catalog.xml`, lang `XML`,
       forced **dark**): a deliberately large Git conflict over `<Product>` nodes
       with `<<<<<<< ours / ||||||| base / ======= / >>>>>>> theirs` markers. Lines
       are tinted per side — ours `rgba(11,95,255,.14)`, theirs
       `rgba(31,168,85,.15)`, base dimmed `#8C8C98`, markers `#FF8B80` bold.
    2. **Arrow** (`align-self:center`): 46px cobalt circle with a white arrow,
       caption "resolves to".
    3. **Merged-result CodeBlock** (editor chrome, tab `catalog.xml`, lang `XML`,
       **adaptive** theme): the clean merged tree with added lines tinted citron
       `rgba(198,242,60,.22)`; below it a legend of 3 pill chips — "ours · atlas →
       preview" (accent), "theirs · beacon labels" (success), "theirs · +caldera,
       appended" (signal/citron).

### 2. The problem (`#problem`)
Two-column grid `1.05fr .95fr`, gap 56px. Left: cobalt uppercase eyebrow "The
problem", H2 "The conflict is not always where the disagreement is.", two body
paragraphs, and a Keel **Callout** (`info` tone, title "Not a sorter"). Right: a
**conflict CodeBlock** (editor chrome `catalog.xml`/`XML`, dark) showing the same
catalog conflict, with an italic caption beneath: "One conflict block — yet
atlas and beacon were edited by different people, and caldera is brand new."
(the three names highlighted in `--accent`).

### 3. The merge model (`#model`, gray section)
Eyebrow "The merge model", H2 "Small at the center, semantic at the edges.", lead.
Then a **4-step pipeline** — a 4-column grid of outlined cards (Parse / Identify /
Diff / Merge) with a numbered header, a tree/diagram mini-visual using small
circle nodes and tinted chips, tied to the atlas/beacon/caldera example. Below,
a **2×2 grid** of numbered steps (48px midnight number tile + heading + paragraph):
1. Parse base, ours, theirs as files. 2. Extract semantic trees by identity
(`Product[sku=atlas]`, `Label[languagecode=en]`, `ProductRef[sku=caldera]`).
3. Compare each side against base. 4. Merge by identity slots.

### 4. Meridian schema (`#schema`)
Eyebrow "Meridian schema", H2 "The merge above was made possible by this schema.",
lead. Two-column grid `1.05fr .95fr`: left a **schema CodeBlock** (editor chrome,
tab `catalog.meridian.yaml`, lang `YAML`, dark) showing a small YAML schema
(`schemaVersion`, `defaults.globalDiscriminatorFields: [id, languagecode]`,
`files[].discriminators` keyed by `sku`, `orderedChildren`). Right: a vertical
stack of 4 **annotation cards** — each an outlined card with a colored left border
(`3px`), a mono label (700, `--accent`; the last one `--signal-text`), and an
explanatory sentence mapping a schema line to the merge behavior.

### 5. Format support (`#formats`, gray section)
Two-column intro: left copy ("Every format is a plugin." + the grouped provider
packages `MeridianGit.Formats.Markup/.Web/.Images/.PowerPlatform/.Binary` and
`MeridianGit.Abstractions`); right a **adapter-contract diagram** (source text →
format adapter `detect/parse/render` → structural tree → merge engine), built from
nested outlined boxes, mono chips, and down/right arrow glyphs. Then heading
"Shipped as provider bundles" and a 3-column grid of **5 bundle cards**
(interactive, lift on hover):
- **Markup** — chips `.xml .json .yaml`
- **Web** — chips `.html .css .js`
- **Power Platform** — chips `.liquid .xap` + a citron **"+ solution schema"** chip
  with a hover **Tooltip** ("A ready-made Meridian schema for Power Platform
  solution XML"); a citron **Pro** badge in the header; and a bottom hairline-
  divided contact link "Contact for access & pricing" → `mailto:adam.coulter@me.com`.
- **Binary** — chips `.png .bin`
- **Bring your own** — flat card with a dashed citron inset border.

### 6. Recursive formats (`#nested`)
Two-column grid `.9fr 1.1fr`. Left: copy + a Keel info Callout. Right: a nested
"format-in-format" visualization — an XML window whose value scalar parses as JSON,
which in turn contains an escaped-JSON `rules` string, rendered as **progressively
nested boxes** (outer `--surface-card`, inner `--surface-subtle`, innermost
`--accent-subtle`), each with a small format tag (XML / JSON) and a `↳` note.

### 7. Design constraints (`#constraints`)
Eyebrow, H2 "Constraints that keep the merge honest.", lead, then a 2×2 grid of
outlined interactive cards. Each card = a 46px `--accent-subtle` rounded tile with
a **Lucide** icon (`--accent` stroke) + heading + paragraph:
- **Identity is local** — Lucide `key-2`
- **Order is opt-in** — Lucide `arrow-down-a-z`
- **Nested content stays nested** — Lucide `network`
- **Consumers own domain semantics** — Lucide `file-code`

### 8. Quick start (`#start`, gray section)
Eyebrow, H2 "Install it and wire it into Git.", lead. Two-column grid `1fr .82fr`:
- Left: three stacked **CodeBlocks** — install (**terminal** chrome,
  `dotnet tool install --global MeridianGit`), a `meridian merge` command
  (terminal), and a `.gitattributes`/driver config (**editor** chrome, `INI`).
- Right: an **aside card** "Minimal schema shape" containing a **CodeBlock**
  (editor, tab `config.meridian.yaml`, `YAML`), inset with `16px 20px 20px`
  padding inside the card.
- Below: a row of 3 ghost doc-link buttons (Read the README / Architecture notes /
  Schema contract).

### 9. Footer
Midnight (`#0A1430`) band, white text at reduced opacity. Logo + tagline (Apache
2.0), two link columns (Project / Docs), and a copyright rule
("Copyright 2026 Adam Coulter · adam.coulter@me.com").

---

## Interactions & Behavior
- **Theme toggle:** the nav button cycles light/dark. It sets `data-theme="light"`
  or `"dark"` on `<html>` and persists to `localStorage["meridian-theme"]`. On load,
  a stored value is applied; with no stored value the page **follows the OS**
  (`prefers-color-scheme`). The toggle icon is a **moon in light mode** (click → dark)
  and a **sun in dark mode** (click → light). Every surface re-themes purely through
  CSS variables — no per-component JS.
- **Cards:** interactive cards lift `translateY(-2px)` and deepen their shadow on
  hover (≈220ms ease-out).
- **Buttons:** primary darkens one step on hover (`--accent → --accent-hover`);
  outline/ghost fill with `--surface-sunken`; all controls `scale(0.97)` on press.
- **Links:** nav/footer links shift color on hover; the "See the merge case →"
  arrow is part of the label.
- **Tooltip** (Power Platform "+ solution schema" chip): appears above on hover/
  focus, dark inverse surface, wraps to ~2 lines (`max-width:215px`).
- **GitHub control:** display-only link (octocat + formatted star count) to the
  repo — intentionally **not** a star action.
- **Motion:** default easing `cubic-bezier(.22,1,.36,1)`, 140–220ms. No infinite
  loops. Respect `prefers-reduced-motion`.

## State Management
Minimal. The only stateful behavior is the **theme**:
- `theme` (`'light' | 'dark' | null`) — `null` = follow OS.
- Source of truth: `<html data-theme>` + `localStorage["meridian-theme"]`.
- Transitions: toggle button click → compute effective theme → set the opposite →
  write attribute + storage.
Everything else is static content + CSS hover/focus states.

## Design Tokens (Keel)

> **The token layer now comes from keel itself.** keel is a dependency, not a copy:
> `@adamcoulteroz/keel` is published to GitHub Packages and pinned in
> `docs/package-lock.json`. `./update-keel.sh` installs the locked version and puts
> the stylesheet at `docs/keel.bundle.css`, which is a **build output** — gitignored,
> never hand-edited, never committed. Pass a version (`./update-keel.sh 0.2.3`) to
> move the pin. It is linked *before* the page's own `<style>` block so page rules
> win where the two overlap.
>
> The tables below describe the palette Meridian was designed at, which keel was
> extracted from and still matches. The page holds exactly one token override, in
> `LIGHT_VARS` in `build.py`: `--radius-lg: 12px`, a placeholder for a change keel
> has agreed to and not yet shipped. Drop it when keel does.
>
> **The page does not invent values.** A consumer expresses intent; keel owns the
> behaviour. If a keel value looks wrong here, that is a bug report against keel,
> not licence for a local override — the question is which vocabulary keel failed
> to offer.
>
> **Never read a keel ramp** (`--blue-400`, `--gray-400`, `--signal-700`, …). A ramp
> is a rung on a scale and keel promises nothing about its value; it moves when the
> scale is retuned, and nothing fails at the moment you write it. Name the semantic
> token instead — `--success`, `--signal-text`, `--accent-border`,
> `--accent-on-midnight` — and pick it by **meaning, not appearance**: `--success`
> because the state is good, not because the green was the right green. This page
> reads zero ramps; keep it that way.
>
> Code blocks colour syntax through the block-scoped `--cb-*`, never `--code-*`,
> which describe a window that is dark in both themes and are unreadable on the
> light chrome.


**Typography**
- Sans / UI + display: **Hanken Grotesk** (400/500/600/700/800).
- Mono / code: **Fira Code** (ligatures on). Inline code, flags, package names,
  commands are always mono.
- Display tightens tracking (`-0.02` to `-0.03em`), body 17px / 1.5.

**Radius:** controls 10px · cards 18px (`--radius-xl`) · large bands 24–32px ·
pills 980px. **Grid:** 4px base. **Section padding:** 96px. **Max width:** 1200px.

**Light palette (semantic → value)**
| Token | Value |
|---|---|
| `--surface-base` | `#FFFFFF` |
| `--surface-subtle` | `#F5F5F7` |
| `--surface-sunken` | `#EBEBEF` |
| `--surface-card` | `#FFFFFF` |
| `--surface-overlay` | `rgba(255,255,255,.72)` |
| `--surface-midnight` | `#0A1430` |
| `--text-primary` | `#1D1D1F` |
| `--text-secondary` | `#6E6E73` |
| `--text-tertiary` | `#86868B` |
| `--border-subtle` | `#DEDEDF` |
| `--border-default` | `#C8C8CE` |
| `--accent` | `#0B5FFF` |
| `--accent-hover` | `#094AD1` |
| `--accent-subtle` | `#ECF2FF` |
| `--success` | `#1FA855` |
| `--success-subtle` | `#E9F9EE` |
| `--signal` (citron) | `#C6F23C` |
| `--signal-text` | `#7FAE00` |
| `--signal-on` (text on citron) | `#0A1430` |
| `--code-bg` | `#0B0B0C` |
| `--code-fg` | `#E6E6EA` |
| code keyword / string / type | `#84A9FF` / `#5FD49A` / `#6FD4E0` |

**Dark palette (overrides; same token names)**
| Token | Value |
|---|---|
| `--surface-base` | `#11172A` |
| `--surface-subtle` | `#0C1120` |
| `--surface-sunken` | `#070B16` |
| `--surface-card` | `#161D33` |
| `--surface-overlay` | `rgba(14,19,34,.72)` |
| `--text-primary` | `#F5F5F7` |
| `--text-secondary` | `#A8A8B3` |
| `--text-tertiary` | `#7E7E8A` |
| `--border-subtle` | `rgba(255,255,255,.09)` |
| `--border-default` | `rgba(255,255,255,.15)` |
| `--accent` | `#3B78FF` |
| `--accent-hover` | `#5E92FF` |
| `--accent-subtle` | `rgba(59,120,255,.18)` |
| `--success` | `#30D158` |
| `--signal-text` | `#C6F23C` |
| `--code-bg` | `#05080F` |

> Surfaces are blue-tinted (not neutral black) in dark; cobalt **lifts** for
> vibrance and **lightens** on hover; subtle state tints become translucent. The
> citron **signal** color is identical in both themes and is only used for small
> pops (the Pro badge, the "+ solution schema" chip, added-line tints) — always
> with midnight/ink text on a citron fill.

**Shadows (light):** xs `0 1px 1px rgba(11,11,12,.04)` … lg
`0 12px 32px rgba(11,11,12,.10), 0 4px 10px rgba(11,11,12,.05)`. Dark shadows are
deeper/blacker. Most separation in dense UI comes from **hairline borders, not
shadow**.

## Components (reusable)
Recreate these as components in the target framework. Two are bundled here as
reference implementations (plain React, self-injecting CSS, fully tokenized):

- **`components/CodeBlock.jsx`** — a code surface with **`editor`** chrome (file
  tabs + language label) or **`terminal`** chrome (traffic lights + filename).
  Props: `chrome`, `theme` (`auto|light|dark`), `filename`/`tabs`, `lang`,
  `lineNumbers`, `code` or children. Token color classes `.k .s .n .t .c`. Used
  for **every** code window on the page. (Note: editor for source files, terminal
  for shell commands.)
- **`components/GitHubStars.jsx`** — octocat + formatted star count as a repo link.
  Props: `repo`, `stars`, `variant` (`minimal|outline|split`), `size`. The page
  uses **`minimal`** in the nav.

Other components used (from the Keel system — substitute the codebase's
equivalents): **Button** (primary/secondary/outline/ghost, `pill`), **Badge**
(tones incl. `accent`, `success`, `signal`), **Card** (`elevated|outlined|flat`,
`interactive`), **Callout** (`info` etc.), **Tooltip**.

## Assets
- **Icons:** [Lucide](https://lucide.dev) (ISC) — stroked, ~1.75px, 24px grid,
  `currentColor`. Named icons in Constraints: `key-2`, `arrow-down-a-z`, `network`,
  `file-code`. Other small UI glyphs (arrows, plus, check, mail, sun/moon, file)
  are inline Lucide-style SVGs. **No emoji, no filled/multicolor glyphs.**
- **Logo:** the "meridian" crosshair-in-ellipse mark is hand-drawn inline SVG
  (cobalt strokes) — recreate or replace with the project's real mark.
- **Fonts:** Hanken Grotesk + Fira Code (Google Fonts in the prototype; swap for
  licensed/self-hosted binaries in production).
- No raster images.

## Files
- `build.py` — generates `docs/index.html` from `Meridian.dc.html`. **`index.html`
  is generated: never hand-edit it**, or the next build silently drops the change.
- `check-keel-usage.py` — first asserts the vendored stylesheet is the version
  `package-lock.json` pins, because it is a gitignored build output and nothing updates it
  for you: a stale copy is invisible to `git status` and every other check here reads it.
  Then fails the build if the page uses a keel class **or custom property** the resolved keel version no longer defines, **or if a token keeps its name
  and changes its value**. That last one is the quiet failure: membership checks pass while
  the page renders differently, which is what `--accent-on-midnight` did.
- `snapshot-keel-tokens.py` / `keel-token-values.json` — the resolved value of every keel
  token the page uses, per theme. Run the snapshot ONLY when a value change is intended,
  in the same commit as the keel bump, so it arrives as a reviewed diff. The resolver was
  validated against the browser: 31 tokens, both schemes, 62 comparisons, zero mismatches. Tokens have caused more of this
  page's breakages than classes have (`--code-*` meaning something other than assumed,
  ramps read where semantic aliases were needed, `--surface-footer` removed outright), and
  an unresolved `var()` drops the declaration silently. keel ships no aliases, so a rename is otherwise silent:
  nothing errors, the page just renders unstyled. Runs in the deploy workflow; run it
  yourself after any keel bump. Prefers keel's `dist/classes.json` when present and falls
  back to parsing the bundle.
- `update-keel.sh` — installs keel at the version locked in `docs/package-lock.json`
  and stages `docs/keel.bundle.css`. Pass a version to move the pin; commit the
  lockfile change, never the stylesheet.
- `Meridian.dc.html` — the full design (all sections, inline styles, theme logic).
  Open it to read exact markup, copy, and per-element styles. **Reference only** —
  the `.dc.html`/`<x-import>`/`support.js` runtime is prototyping scaffolding, not
  for production.
- `components/CodeBlock.jsx`, `components/GitHubStars.jsx` — reference component
  implementations to port.
- `screenshots/` — rendered reference images. `01-light` … `07-light` walk the
  page top-to-bottom in **light** mode (hero example, problem, merge model,
  schema, format bundles, constraints, quick start); `01-dark` … `04-dark` show
  the hero example, schema, bundles, and quick start in **dark** mode.
