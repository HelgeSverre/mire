# Widgets

`Mire.Widgets` is a library of pure view helpers — each returns a `LayoutNode<'msg>`,
holds no state, and is styled by the values you pass it (the app owns the state in its
model). Browse them all live with **`just gallery`** (`samples/Gallery`), which renders
every widget below in its key states.

This guide is a catalog. For the editing widgets see [Text editing](text-editing.md);
for theming the styles you pass in see [Styling & theming](styling-and-theming.md).

## Text and chrome

```fsharp
Text.text "plain" Style.text          // styled text (multi-line on '\n', grapheme-width aware)
Text.title "Bold heading"             // Style.title
Text.dimText "secondary"              // Style.dim

Box.box Style.border [ child ]        // a border around one child
Box.panel "title" Style.border [ … ]  // a titled box

Separator.horizontal width Style.divider   // ─────
Separator.vertical   height Style.divider   // a │ rule

Badge.badge style "NEW"               // a ` NEW ` pill (give it a bg-bearing style)
KeyHint.hint keyStyle labelStyle "⏎" "submit"   // a key + label hint

StatusBar.statusBar leftItems centerItems rightItems   // a bordered status row
```

## Backdrop and highlights

A bare styled `Text` only colors the cells under its glyphs. To fill a whole row (a
selection highlight, a panel background) use `Backdrop`:

```fsharp
Backdrop.solid style                       // a solid rectangle filling its rect
Backdrop.behind style (Text.text "row" fg) // fill the rect, then draw the child on top
```

`Backdrop.behind` is the full-width row-highlight primitive — lists and tables use it
for selection.

## Lists and tables (virtualized)

Both build only the visible window of rows, so a million-row list is cheap.

```fsharp
// Single-select list (auto-scrolls to keep the selection in view). The app owns
// the selected index and key handling.
ListView.view height selStyle rowStyle selectedIndex [ "alpha"; "bravo"; "charlie" ]

// Predicate selection (single OR multi); `scrollTo` is the row to keep visible:
ListView.viewWith height selStyle rowStyle (fun i -> Set.contains i selected) scrollTo labels
```

```fsharp
// A table with a sticky header and Length-width columns.
let columns : Column<MyRow, Msg> list =
    [ Table.textColumn "file"   (Length.Cells 20) theme.fg     (fun r -> r.File)
      Table.textColumn "status" (Length.Cells 12) theme.fgMuted (fun r -> r.Status) ]

Table.view height headerStyle selStyle topRow (fun i -> i = selected) columns rows
```

`topRow` is the scroll offset (first visible row); `Column.Render` can return any node,
not just text, so you can put badges or progress bars in cells.

## Inputs

Single-line `Input` and multi-line `TextArea` render over a `TextBuffer` — see
[Text editing](text-editing.md) for the full story (cursor, selection, soft-wrap, the
editing keymap).

```fsharp
Input.render width textStyle cursorStyle focused buffer
TextArea.render width height textStyle cursorStyle focused buffer          // scroll, no wrap
TextArea.renderWrapped width height textStyle cursorStyle focused buffer   // soft word-wrap
```

## Scrolling

`ScrollView` wraps content in a viewport with a track/thumb scrollbar. The app holds
the offset; the helpers compute the bounds.

```fsharp
ScrollView.vertical viewportH contentH offset trackStyle thumbStyle content

// offset math (the app clamps its own offset against these):
ScrollView.toBottom viewportH contentH          // the follow-tail offset
ScrollView.clampOffset viewportH contentH offset
ScrollView.atBottom viewportH contentH offset   // true → keep following the tail
```

`contentH` is the content's total height (`Layout.contentExtent Direction.Vertical`).
For a chat-style transcript that should follow new output, keep the offset at
`toBottom` while `atBottom` is true.

## Overlays

These return layers you stack over a base tree with `LayoutNode.Overlay`.

```fsharp
// A centered modal with its own dimming backdrop:
Modal.modal backdropStyle borderStyle titleStyle width height "Title" body

// Auto-dismissing notifications, placed top-right over a base tree (the app owns the
// list and expires entries with a Sub timer):
Toast.view …

// A cursor-anchored completion popup (clamped on-screen):
Completion.view areaW areaH anchorX anchorY width maxRows borderStyle selStyle rowStyle selected items

// A Ctrl+P-style fuzzy command surface (Modal + ListView + ranked filter):
CommandPalette.view width height backdropStyle borderStyle accentStyle selStyle rowStyle "title" query selected items
CommandPalette.filter query candidates   // the pure best-first subsequence ranker (reusable)

// An anchored doc popup (flips above the anchor when low on space):
Tooltip.view areaW areaH anchorX anchorY width borderStyle textStyle lines

// Place a sized layer at a 9-point Placement, or anchored at a point:
Overlay.positioned placement width height child
Overlay.centered width height child
Overlay.atPoint x y width height areaW areaH child
```

## Split views

```fsharp
SplitView.horizontal (Length.Fraction 0.5) dividerStyle leftPane rightPane
SplitView.vertical   (Length.Cells 10)     dividerStyle topPane  bottomPane
```

The first pane is sized by the `Length`; a 1-cell divider follows; the second pane
fills the rest. Nest two splits for more than two panes.

## Status, progress, and toggles

```fsharp
Spinner.view spinStyle tick                       // a braille frame from an app-owned tick
Spinner.labeled spinStyle labelStyle tick "working…"
Spinner.frameOf Spinner.braille tick              // the raw frame glyph (bring your own frames)

ProgressBar.view width fillStyle trackStyle 0.6   // 60% filled

Tabs.strip activeStyle inactiveStyle selected [ "Files"; "Search"; "Git" ]

Toggle.checkbox style isChecked "label"           // [x] / [ ]
Toggle.radio    style selected  "label"           // (•) / ( )
Toggle.switch   onStyle offStyle isOn             //  ON  /  OFF  pill
```

## Images

```fsharp
ImagePreview.render width height borderStyle captionStyle "logo.png" (Some (640, 480))
```

`ImagePreview` draws a portable, captioned placeholder box that renders on every
terminal. On Kitty/Ghostty you can overlay the real pixels with `Cmd.kittyImage` — see
[Terminal protocol](terminal-protocol.md#kitty-graphics).

## Markdown

```fsharp
Markdown.render theme.markdown width markdownSource         // headings, lists, code, quotes, links
Markdown.wrap   theme.markdown width baseStyle plainText    // word-wrap plain text to width
```

`MarkdownStyle` (carried by `AppTheme.markdown`) controls heading/code/link styling.
Links carry their real URL as an OSC 8 hyperlink — clickable in capable terminals.

## Theming what you pass in

Every widget takes explicit `Style` values rather than reading a global theme, which
keeps them pure and composable. In practice you thread an `AppTheme` through your view
and pass its fields (`theme.fg`, `theme.selection`, `theme.border`, …). See
[Styling & theming](styling-and-theming.md).
