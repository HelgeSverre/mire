# Typography

Two faces, two jobs:

- **JetBrains Mono** carries the identity: logo, headings, labels, badges, code,
  CLI output, terminal examples, and token names.
- **IBM Plex Sans** carries long-form web reading: body copy, descriptions,
  lists, and table body cells.

Mire is a terminal framework, so the mono face remains the brand signal. The web
manual gets a reading face so the reference content stays legible.

## The font

**JetBrains Mono** — slightly humanist, friendly, highly legible at small sizes.
Free, OFL.

**IBM Plex Sans** — readable, technical, and quiet enough to support the mono
identity without competing with it. Free, OFL.

### Web import

```html
<!-- self-host preferred; CDN shown for prototyping -->
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link
  href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500;700&display=swap"
  rel="stylesheet"
/>
```

### Self-host (preferred for production)

```sh
npm i @fontsource/ibm-plex-sans @fontsource/jetbrains-mono
```

```js
import "@fontsource/ibm-plex-sans/400.css";
import "@fontsource/ibm-plex-sans/500.css";
import "@fontsource/ibm-plex-sans/600.css";
import "@fontsource/ibm-plex-sans/700.css";
import "@fontsource/jetbrains-mono/400.css";
import "@fontsource/jetbrains-mono/500.css";
import "@fontsource/jetbrains-mono/700.css";
```

### Box-drawing characters

Google Fonts and @fontsource serve unicode-range subsets that **omit the
box-drawing block (U+2500–257F)**. Any page that renders terminal diagrams
(`┌─┐│└┘`) with a subset font silently falls back to a system font with a
different advance width, and the box borders misalign. For those pages,
self-host the full build from the official JetBrains Mono release — see
`brand/fonts/` and the `@font-face` rules in `brand/index.html`.

### CSS stack

```css
--font-body: "IBM Plex Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
--font-mono: "JetBrains Mono", ui-monospace, "SF Mono", "Cascadia Mono", Menlo, monospace;
```

### Terminal (TUI / CLI)

The terminal supplies the font — you do not ship one. Recommend JetBrains Mono
in the docs so screenshots match. Mire's own rendering is glyph-cell based;
typography there is the user's terminal font, carried by spacing and weight.

## Sizing scale

```
xs:   12px / 0.75rem    meta, captions, line numbers
sm:   14px / 0.875rem   dense UI, table cells
base: 16px / 1rem       body default
lg:   18px / 1.125rem   prominent body
xl:   20px / 1.25rem    subheadings
2xl:  24px / 1.5rem     section heads
3xl:  32px / 2rem       page heads
4xl:  40px / 2.5rem     page title — the largest regular size
```

Use a separate display size only for a dedicated landing hero. Product manuals
and docs cap at 40px.

## Weights

Four, total:

- `400` body
- `500` emphasis, labels, and badges
- `600` strong prose emphasis
- `700` headings and wordmark

Never `300`. Light weights read as low-contrast slop.

## Tracking & leading

- Body: `letter-spacing: 0`, `line-height: 1.55`.
- Long-form prose: `line-height: 1.6` only when a page is mostly paragraphs.
- Headings: `letter-spacing: 0`, `line-height: 1.2` to `1.25`. Do not tighten mono text.
- Body width: `max-width: 62ch`. Always.

## Example

```
## Diff-based rendering          ← 2xl / 700 / mono

Mire builds a full Surface every frame, then Diff.compute finds the     ← base / 400 / 1.55
changed runs. You never write escape sequences yourself. (max 62ch)

    let runs = Diff.compute prev next        ← code, same font, --bg-elevated
    runs |> Diff.write writer                   block, hairline --border
```

Headings and code use the mono face. Paragraphs use the reading face. The
contrast makes the manual easier to read while keeping the brand signal intact.
