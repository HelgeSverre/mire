# Mire — Brand Usage

The brand is `└` + JetBrains Mono + Emerald `#1A8870`. Web documentation may add
IBM Plex Sans for long-form reading, but the identity still comes from the mark,
mono type, spacing, and restraint.

## Files

| File              | What                                | When                                                         |
| ----------------- | ----------------------------------- | ------------------------------------------------------------ |
| `symbol.svg`      | Emerald corner on transparent       | The one place the accent appears (loading, error, hero mark) |
| `symbol-mono.svg` | `currentColor` corner               | Headers, footers, anywhere it coexists with text             |
| `favicon.svg`     | Hand-tuned 16×16                    | Browser tab                                                  |
| `palette.json`    | Canonical color source              | Build tooling — edit this, regenerate the rest               |
| `palette.css`     | CSS custom properties, light + dark | Web app, marketing site                                      |
| `palette.fs`      | F# module (`Mire.Brand.Palette`)    | TUI rendering                                                |
| `typography.md`   | Type roles + sizes                  | Writing web docs, CSS, or terminal output                    |
| `voice.md`        | The five voice rules                | Any copy you write                                           |
| `USAGE.md`        | This file                           | Onboarding                                                   |

## The mark

The symbol is a box-drawing corner `└` — the framing primitive Mire renders with.
It is not a literal "M" and never should be.

**Do**

- Use `symbol-mono.svg` wherever the context sets the color.
- Use `symbol.svg` (emerald) for the single accent moment on a surface.
- Preserve the 2px safe padding. Keep the aspect ratio.

**Don't**

- Frame it in a circle or rounded square. The mark is the mark.
- Recolor outside the palette. Add no shadow, glow, outline, or gradient.
- Resize below 16×16 — use `favicon.svg`.
- Place it on a busy background. It needs `--bg` or `--bg-elevated` behind it.

## One accent rule

In any normal web viewport, TUI screen, or 12-character CLI span, emerald appears
**at most once**. Palette and specimen sections may show multiple emerald values
because color is the subject. If the header symbol is emerald and the primary
button is emerald, pick one. Remove the other.

Hierarchy comes from, in order: size → weight (400/500/600/700) → position →
neutral contrast (`--fg` → `--fg-muted` → `--fg-subtle`). Color is the last
layer, applied once.

## Typography roles

Use **IBM Plex Sans** for web documentation body copy, descriptions, lists, and
table body cells. Use **JetBrains Mono** for logo, headings, navigation, labels,
badges, code, CLI output, token names, and terminal examples.

Terminal surfaces still use the user's terminal font. Recommend JetBrains Mono
in docs and screenshots so the rendered examples match the brand.

## CLI / TUI surface

### Banner (`--help`, first run, or interactive entry — not every command)

Single line:

```
  └ mire   a retained-mode TUI runtime for F#
```

Three line (first-run only):

```
  └
  │   mire
  └─  elmish for the terminal
```

The `└` (and only it) is emerald. Everything else is `--fg`.

### Color discipline in the TUI

| Element                                              | Token                            |
| ---------------------------------------------------- | -------------------------------- |
| Body text                                            | `--fg`                           |
| Comments / muted                                     | `--fg-muted`                     |
| Hints / completions                                  | `--fg-subtle`                    |
| Borders / box-drawing                                | `--border`                       |
| Active / focused line                                | `--bg-elevated` (background)     |
| Selection                                            | inverse video, not a color shift |
| The one accent moment (prompt glyph, cursor counter) | `--accent`                       |

Allowed effects: bold (sparingly), dim, inverse. Banned: blink, italic,
underline-for-emphasis (underline is links only).

### Degrade gracefully

Output zero ANSI when `NO_COLOR` is set, when piped (not a TTY), or `TERM=dumb`.
Use hex/RGB when `COLORTERM=truecolor`, else the `ansi256` field from the
palette. The brand survives all of these — the `└`, the type, and the spacing
carry it.

### Spinner / progress (pick one, commit)

- Spinner: `⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏` in `--fg-muted` (never accent).
- Progress: `█` filled (`--fg`) / `░` empty (`--border`), 20 chars max.
- Prompt glyph: `❯`, accent-colored. Input in `--fg`, completions in `--fg-subtle`.

## Color quick reference

- Brand accent: `--accent` = `#1A8870` (the symbol, the one moment).
- Filled CTA / primary button: `--accent-strong` = `#006750` with **white** text (6.58:1).
- Text on the emerald accent: `--accent-fg` = near-black `#050505` (4.66:1). White fails AA here — don't use it.

## Do-not gallery

Shipping any of these in a Mire surface means the brand has been violated:

- Purple/blue gradient backgrounds; any gradient at all.
- Glassmorphism (`backdrop-blur`) cards.
- Centered hero with three "Fast / Simple / Powerful" cards.
- 2×2 bento grid. "Trusted by" logo strip. Lucide icon grid.
- Sparkles, meteors, animated background lines, cursor-following glow.
- "Powered by AI" badge. Emoji in copy or code.
- Inter (or Roboto / Space Grotesk). Font weight 300.
- "Build the future of X" / "Your all-in-one platform".

## Extending the brand

Need a new asset (OG image, slide template)? Stay inside the constraints:
the `└`, JetBrains Mono, one emerald moment, the do-not list. If a use case
genuinely needs to break out, that is a deliberate brand evolution — not a
one-off exception.
