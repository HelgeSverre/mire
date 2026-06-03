# Typography

One font does everything: headings, body, code, CLI output. Mire is a terminal
framework — a mono workhorse is the whole point.

## The font

**JetBrains Mono** — slightly humanist, friendly, highly legible at small sizes.
Free, OFL. No second font.

### Web import

```html
<!-- self-host preferred; CDN shown for prototyping -->
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link
  href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&display=swap"
  rel="stylesheet"
/>
```

### Self-host (preferred for production)

```sh
npm i @fontsource/jetbrains-mono
```

```js
import "@fontsource/jetbrains-mono/400.css";
import "@fontsource/jetbrains-mono/500.css";
import "@fontsource/jetbrains-mono/700.css";
```

### CSS stack

```css
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
4xl:  48px / 3rem       hero — the largest, ever
```

No size above 48px on a landing page.

## Weights

Three, total:

- `400` body
- `500` emphasis and headings
- `700` reserve for the rare strong heading

Never `300`. Light weights read as low-contrast slop.

## Tracking & leading

- Body: `letter-spacing: 0`, `line-height: 1.625`.
- Headings: `letter-spacing: -0.01em`, `line-height: 1.25`. Mono tightens well at size.
- Body width: `max-w-[65ch]`. Always.

## Example

```
## Diff-based rendering          ← 2xl / 500 / tracking-tight

Mire builds a full Surface every frame, then Diff.compute finds the     ← base / 400 / leading-relaxed
changed runs. You never write escape sequences yourself. (max 65ch)

    let runs = Diff.compute prev next        ← code, same font, --bg-elevated
    runs |> Diff.write writer                   block, hairline --border
```

Heading, paragraph, and code are the same typeface at different sizes and
weights. That sameness is the brand.
