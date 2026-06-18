# Styling & theming

Every cell carries a `Style`. Widgets take styles as explicit arguments rather than
reading a global, which keeps them pure — you thread a theme through your view and pass
its fields in.

## Color

```fsharp
type Color =
    | Rgb of byte * byte * byte
    | Default              // the terminal's default fg/bg

// Named conveniences and conversions:
Color.Red                  // Rgb(239, 83, 80)
Color.Rgb(0x2Duy, 0x44uy, 0x3Cuy)
(Color.Rgb(16uy,185uy,129uy)).ToHex()   // "#10B981"
```

Colors are **truecolor** (24-bit RGB). There is no 16-color palette and no fallback —
Mire targets terminals that do truecolor.

## Style

`Style` is an immutable struct with fluent `With*` helpers that return new records:

```fsharp
type Style =
    { Foreground: Color option
      Background: Color option
      Bold: bool
      Italic: bool
      Underline: UnderlineStyle option   // Single | Double | Curly | Dotted | Dashed
      Dim: bool
      Strikethrough: bool
      Link: string option }              // OSC 8 hyperlink target

let s =
    Style.Default
        .WithForeground(Color.Rgb(0x10uy, 0xB9uy, 0x81uy))
        .WithBold(true)
        .WithUnderline(UnderlineStyle.Curly)

let link = Style.text.WithLink "https://example.com"   // clickable in capable terminals
```

`Style.ToAnsi()` turns a style into its SGR sequence; the `Link` is *not* an SGR
attribute — the `Diff` writer brackets a linked run in OSC 8 open/close instead, and
because `Link` is part of the record, structural equality automatically splits diff runs
at link boundaries. (See [Terminal protocol](terminal-protocol.md).)

## Semantic style primitives

`Mire.Widgets.Style` exposes a small set of named, brand-sourced styles so the default
look needs no per-app theme code:

```
Style.text     Style.dim      Style.title     Style.border
Style.success  Style.warning  Style.danger    Style.info
Style.accent   Style.key      Style.bg
```

These are the Mire brand by default (emerald accent, neutral hierarchy on a dark
terminal). Use them directly for quick apps; reach for `AppTheme` to thread a coherent,
swappable set through a larger app.

## AppTheme — a swappable style set

`AppTheme` is a record of the styles an app needs in one place, so you pass *one* value
around instead of dozens of loose `Style`s:

```fsharp
type AppTheme =
    { fg: Style; fgMuted: Style; fgSubtle: Style; title: Style
      bg: Style; bgElevated: Style
      border: Style; borderFocus: Style; divider: Style
      accent: Style; accentFg: Style; accentStrong: Style
      success: Style; warning: Style; danger: Style; info: Style
      selection: Style; selectionAccent: Style
      key: Style
      markdown: MarkdownStyle }
```

`AppTheme.defaultTheme` is the Mire brand: neutrals carry hierarchy, emerald is the one
accent moment, selection is inverse video, and the status colors are functional
(legible for results, diffs, toasts). Thread it through your view:

```fsharp
let theme = AppTheme.defaultTheme

let view m =
    Box.box theme.border
        [ Stack.vstack
            [ Text.title " My App "
              Text.text "body" theme.fg
              Badge.badge theme.accentStrong "NEW" ] ]
```

To re-skin an app, build your own `AppTheme` (copy `defaultTheme` and override fields
with `{ defaultTheme with accent = … }`) and thread it instead. Combined with
[theme notifications](input.md#theme-notifications), you can switch light/dark at runtime.

### Functional tones

For toasts, notices, and status cells, map a semantic *tone* to a style:

```fsharp
type AppTheme.Tone = Success | Warning | Danger | Info | Neutral
AppTheme.toneStyle theme AppTheme.Warning   // → theme.warning
```

## Markdown styling

`Markdown.render`/`wrap` take a `MarkdownStyle` (heading, code, link, emphasis styles);
`AppTheme.markdown` carries the branded default (`Markdown.defaultStyle`). Markdown
links render as OSC 8 hyperlinks carrying their real URL. See
[Widgets → Markdown](widgets.md#markdown).
