---
title: Color, Style, and AppTheme
description: The styling value types and the swappable theme record, plus the TextBuffer/TextEdit editing surface.
category: reference
order: 4
---

## Color

```fsharp
type Color =
    | Rgb of byte * byte * byte
    | Default              // the terminal's default fg/bg

Color.Red                  // and Green/Blue/Yellow/.../Gray/DarkGray/LightGray
Color.Rgb(0x1Auy, 0x88uy, 0x70uy)
(Color.Rgb(26uy, 136uy, 112uy)).ToHex()   // "#1A8870"
```

Colors are truecolor (24-bit RGB). There is no 16-color palette or fallback.

## Style

An immutable struct with fluent `With*` helpers that each return a new record.

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

`Style.ToAnsi()` produces the SGR sequence. `Link` is not an SGR attribute — the `Diff`
writer brackets a linked run in OSC 8, and because `Link` is part of the record, diff
runs split at link boundaries automatically.

The semantic primitives in `Mire.Widgets.Style` (`Style.text`, `Style.dim`,
`Style.title`, `Style.border`, `Style.accent`, `Style.success`/`warning`/`danger`/`info`,
`Style.key`, `Style.bg`) are the Mire brand by default.

## AppTheme

A record of the styles an app needs, threaded as one value through the view.

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

AppTheme.defaultTheme                     // the Mire brand (emerald accent, neutral hierarchy)
{ AppTheme.defaultTheme with accent = … } // re-skin by overriding fields
```

Functional tones map to styles for toasts/notices/status cells:

```fsharp
type AppTheme.Tone = Success | Warning | Danger | Info | Neutral
AppTheme.toneStyle theme AppTheme.Warning   // → theme.warning
```

See [Theme your app](/docs/how-to/theme-your-app/) for usage and runtime light/dark switching.

## TextBuffer

Text + cursor + selection, char-indexed, pure.

```fsharp
type TextBuffer = { Text: string; Cursor: int; Anchor: int option }

TextBuffer.Empty
TextBuffer.Of "initial"

// edits (replace any selection):
TextBuffer.insert "x" b · backspace b · delete b
TextBuffer.deleteWordBack b · deleteWordForward b
// movement (preserve the anchor):
TextBuffer.left b · right b · home b · toEnd b · wordLeft b · wordRight b · up b · down b · lineStart b · lineEnd b
// selection:
TextBuffer.selectAll b · selectWord b · extend move b · selection b · hasSelection b · clearSelection b
```

## TextEdit

Named edit actions and the overridable keymap.

```fsharp
type EditAction =
    | InsertText of string | Newline
    | DeleteBack | DeleteForward | DeleteWordBack | DeleteWordForward
    | CursorLeft | CursorRight | CursorUp | CursorDown
    | WordLeft | WordRight | LineStart | LineEnd | DocStart | DocEnd
    | SelectAll | Select of EditAction

TextEdit.apply action buffer
TextEdit.applyInput inputEvent buffer                 // via the default keymap
TextEdit.applyInputWith myKeymap inputEvent buffer    // custom keymap, default fallback
TextEdit.defaultKeymap keyEvent                       // KeyEvent -> EditAction option
```

`defaultKeymap`: typing inserts; Backspace/Delete (+ Ctrl/Alt/Cmd = word-delete);
arrows/Home/End move (+ Shift = extend); Ctrl/Cmd+A selects all; paste inserts. Returns
`None` for keys it doesn't own. See [Edit text](/docs/how-to/edit-text/).
