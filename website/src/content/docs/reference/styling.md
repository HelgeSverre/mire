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
`Style.title`, `Style.border`, `Style.counter`, `Style.highlight`,
`Style.success`/`warning`/`danger`/`info`, `Style.key`, `Style.bg`) are the Mire brand by
default. (The emerald accent lives on `AppTheme.accent`, not as a loose `Style`.)

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

// edits — each replaces the selection if there is one:
TextBuffer.insert "x" b
TextBuffer.backspace b
TextBuffer.delete b
TextBuffer.deleteWordBack b
TextBuffer.deleteWordForward b

// movement — preserves the selection anchor:
TextBuffer.left b
TextBuffer.right b
TextBuffer.home b
TextBuffer.toEnd b
TextBuffer.wordLeft b
TextBuffer.wordRight b
TextBuffer.up b            // over '\n'-delimited lines
TextBuffer.down b
TextBuffer.lineStart b
TextBuffer.lineEnd b

// selection:
TextBuffer.selectAll b
TextBuffer.selectWord b
TextBuffer.extend move b   // shift+move: anchor, then apply the move
TextBuffer.selection b     // (lo, hi) option
TextBuffer.hasSelection b
TextBuffer.clearSelection b
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
