---
title: Theme your app
description: Use the default brand theme, build your own AppTheme, and switch light/dark at runtime.
category: how-to
order: 2
---

Widgets take `Style` values as arguments rather than reading a global, so theming is
just deciding which styles you thread through your `view`. The convenient way is one
`AppTheme` record.

## Use the default theme

`AppTheme.defaultTheme` is the Mire brand — emerald accent, neutral hierarchy, inverse
selection — and needs no setup:

```fsharp
let theme = AppTheme.defaultTheme

let view m =
    Box.box theme.border
        [ Stack.vstack
            [ Text.title " My App "
              Text.text "body text" theme.fg
              Text.text "secondary" theme.fgMuted
              Badge.badge theme.accentStrong "NEW" ] ]
```

The fields you'll use most: `fg`, `fgMuted`, `fgSubtle`, `title`, `border`,
`borderFocus`, `accent`, `accentStrong`, `selection`, `success`/`warning`/`danger`/`info`,
and `markdown`. See the [styling reference](/docs/reference/styling/) for the full record.

## Build your own theme

Copy the default and override fields:

```fsharp
let myTheme =
    { AppTheme.defaultTheme with
        accent = Style.Default.WithForeground(Color.Rgb(0x4Auy, 0x90uy, 0xD9uy))
        selection = Style.Default.WithForeground(Color.Rgb(0x05uy, 0x05uy, 0x05uy)).WithBackground(Color.Rgb(0x4Auy, 0x90uy, 0xD9uy)) }
```

Thread `myTheme` through your view instead of `defaultTheme`. Build it once (e.g. a
module-level `let`) and pass it down.

## Style a single widget

Every widget takes explicit styles, so you can deviate locally without a whole theme:

```fsharp
Text.text "warning" (Style.Default.WithForeground(Color.Red).WithBold(true))
ProgressBar.view 30 theme.success theme.fgSubtle 0.8
```

`Style` is an immutable struct with fluent `With*` helpers (`WithForeground`,
`WithBackground`, `WithBold`, `WithItalic`, `WithUnderline`, `WithDim`,
`WithStrikethrough`, `WithLink`). Each returns a new style.

## React to the terminal's light/dark setting

Opt in to DEC mode 2031 notifications. The runtime enables the mode and queries the
current scheme at startup; changes arrive as `ThemeChanged` through `MapInput`:

```fsharp
type Model = { Theme: AppTheme; (* … *) }

let mapInput e =
    match e with
    | ThemeChanged Dark  -> Some UseDark
    | ThemeChanged Light -> Some UseLight
    | _ -> (* … *)

let update msg m =
    match msg with
    | UseDark  -> { m with Theme = darkTheme }, Cmd.none
    | UseLight -> { m with Theme = lightTheme }, Cmd.none
    | (* … *)

[<EntryPoint>]
let main _ =
    Program.create init update view
    |> Program.withMapInput mapInput
    |> Program.withThemeNotifications true
    |> Runtime.run
    0
```

Then render with `m.Theme` so the swap takes effect on the next frame.

## Theme markdown

`Markdown.render` takes a `MarkdownStyle` (heading, code, link, emphasis styles);
`AppTheme.markdown` carries the branded default. Links render as real OSC 8 hyperlinks,
clickable in capable terminals.

```fsharp
Markdown.render theme.markdown width markdownSource
```
