---
title: Widgets
description: The Mire.Widgets catalog — every base widget, its purpose, and its signature.
category: reference
order: 1
---

`Mire.Widgets` is a library of pure view helpers. Each returns a `LayoutNode<'msg>`,
holds no state, and is styled by the values you pass it. Browse them all live with
`just gallery` (the `samples/Gallery` app). Editing widgets are covered in the
[styling reference](/docs/reference/styling/); the agent widgets in
[the agent layer](/docs/reference/agent-layer/).

## Text and chrome

```fsharp
Text.text "plain" Style.text          // styled text, multi-line on '\n', grapheme-width aware
Text.title "Bold heading"
Text.dimText "secondary"

Box.box Style.border [ child ]        // a border around one child
Box.panel "title" Style.border [ … ]  // a titled box

Separator.horizontal width Style.divider
Separator.vertical   height Style.divider

Badge.badge style "NEW"               // a ` NEW ` pill (pass a bg-bearing style)
KeyHint.hint keyStyle labelStyle "⏎" "submit"
StatusBar.statusBar leftItems centerItems rightItems
```

## Backdrops and highlights

A bare styled `Text` colors only the cells under its glyphs. To fill a whole row, use
`Backdrop`:

```fsharp
Backdrop.solid style                       // a solid rectangle filling its rect
Backdrop.behind style (Text.text "row" fg) // fill, then draw the child on top
```

`Backdrop.behind` is the full-width row-highlight primitive that lists and tables use
for selection.

## Lists and tables (virtualized)

Both build only the visible window of rows.

```fsharp
ListView.view height selStyle rowStyle selectedIndex labels             // single-select
ListView.viewWith height selStyle rowStyle isSelected scrollTo labels   // single OR multi

let columns : Column<MyRow, Msg> list =
    [ Table.textColumn "file"   (Length.Cells 20) theme.fg     (fun r -> r.File)
      Table.textColumn "status" (Length.Cells 12) theme.fgMuted (fun r -> r.Status) ]

Table.view height headerStyle selStyle topRow isSelected columns rows
```

`topRow` is the first visible row. `Column.Render` returns any node, so cells can hold
badges or progress bars.

## Inputs

Render over a `TextBuffer` — see [Edit text](/docs/how-to/edit-text/).

```fsharp
Input.render width textStyle cursorStyle focused buffer
TextArea.render        width height textStyle cursorStyle focused buffer  // scroll, no wrap
TextArea.renderWrapped width height textStyle cursorStyle focused buffer  // soft word-wrap
```

## Scrolling

```fsharp
ScrollView.vertical viewportH contentH offset trackStyle thumbStyle content

ScrollView.toBottom     viewportH contentH          // follow-tail offset
ScrollView.clampOffset  viewportH contentH offset
ScrollView.atBottom     viewportH contentH offset
```

The app holds the offset; `contentH` is the content's total height
(`Layout.contentExtent Direction.Vertical`).

## Overlays

Layers you stack over a base tree with `LayoutNode.Overlay` — recipes in
[Show overlays and modals](/docs/how-to/overlays-and-modals/).

```fsharp
Modal.modal backdropStyle borderStyle titleStyle width height "Title" body
Completion.view areaW areaH anchorX anchorY width maxRows borderStyle selStyle rowStyle selected items
CommandPalette.view width height backdropStyle borderStyle accentStyle selStyle rowStyle "title" query selected items
CommandPalette.filter query candidates       // the reusable fuzzy ranker
Tooltip.view areaW areaH anchorX anchorY width borderStyle textStyle lines
Overlay.centered width height child
Overlay.positioned placement width height child
Overlay.atPoint x y width height areaW areaH child
```

## Split views

```fsharp
SplitView.horizontal (Length.Fraction 0.5) dividerStyle leftPane rightPane
SplitView.vertical   (Length.Cells 10)     dividerStyle topPane  bottomPane
```

## Status, progress, toggles

```fsharp
Spinner.view spinStyle tick
Spinner.labeled spinStyle labelStyle tick "working…"
Spinner.frameOf Spinner.braille tick

ProgressBar.view width fillStyle trackStyle 0.6      // 0.0–1.0

Tabs.strip activeStyle inactiveStyle selected [ "Files"; "Search"; "Git" ]

Toggle.checkbox style isChecked "label"   // [x] / [ ]
Toggle.radio    style selected  "label"   // (•) / ( )
Toggle.switch   onStyle offStyle isOn     //  ON / OFF pill
```

## Images and markdown

```fsharp
ImagePreview.render width height borderStyle captionStyle "logo.png" (Some (640, 480))
Markdown.render theme.markdown width markdownSource
Markdown.wrap   theme.markdown width baseStyle plainText
```

See [Show an image](/docs/how-to/show-images/) for the Kitty graphics overlay.
