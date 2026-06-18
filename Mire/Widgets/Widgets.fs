namespace Mire.Widgets

open Mire.Core
open Mire.Layout
open Mire.Brand

/// The framework's predefined semantic styles — the Mire brand, by default.
/// Neutrals (from `Mire.Brand.Palette`) carry hierarchy; emerald is the single
/// accent moment; the status colors (success/warning/danger/info) are a
/// functional extension (legibility for results/diffs/toasts), not brand neutrals.
/// Tuned for a dark terminal. Apps can swap the whole set via `AppTheme`.
module Style =
    let private rgb (c: Palette.Color) =
        let (r, g, b) = c.Rgb
        Color.Rgb(r, g, b)

    let border = Style.Default.WithForeground(rgb Palette.Semantic.Dark.borderStrong)

    let title =
        Style.Default.WithForeground(rgb Palette.Semantic.Dark.fg).WithBold(true)

    let text = Style.Default.WithForeground(rgb Palette.Semantic.Dark.fg)
    let dim = Style.Default.WithForeground(rgb Palette.Semantic.Dark.fgMuted)

    // The accent moment (emerald). `counter`/`highlight` are kept for back-compat;
    // both are the accent now (was an ad-hoc green / orange).
    let counter =
        Style.Default.WithForeground(rgb Palette.Semantic.Dark.accent).WithBold(true)

    let highlight =
        Style.Default.WithForeground(rgb Palette.Semantic.Dark.accent).WithBold(true)

    let key = Style.Default.WithForeground(rgb Palette.Semantic.Dark.accent)
    let bg = Style.Default.WithBackground(rgb Palette.Semantic.Dark.bg)

    // Functional status palette (not brand neutrals — these must read as states).
    let success = Style.Default.WithForeground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))
    let warning = Style.Default.WithForeground(Color.Rgb(0xFFuy, 0xA0uy, 0x00uy))
    let danger = Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy))
    let info = Style.Default.WithForeground(Color.Rgb(0x4Auy, 0x90uy, 0xD9uy))

module Text =
    let text (content: string) (style: Style) : LayoutNode<'msg> =
        LayoutNode.Text(Rect.Create(0, 0, 0, 0), content, style)

    let textDefault (content: string) : LayoutNode<'msg> = text content Style.text

    let title (content: string) : LayoutNode<'msg> = text content Style.title

    let dimText (content: string) : LayoutNode<'msg> = text content Style.dim

module Box =
    let box (style: Style) (children: LayoutNode<'msg> list) : LayoutNode<'msg> =
        LayoutNode.Box(Rect.Create(0, 0, 0, 0), style, children)

    let boxDefault (children: LayoutNode<'msg> list) : LayoutNode<'msg> = box Style.border children

    // A `Box` renders a single child filling its inner rect; multiple children
    // would overlap (flow is `Stack`'s job). `panel` therefore flows its children
    // through an explicit vertical Stack, built inline because the `Stack` module
    // compiles after `Box` in this file.
    let private vflow (children: LayoutNode<'msg> list) : LayoutNode<'msg> =
        LayoutNode.Stack(
            Rect.Create(0, 0, 0, 0),
            Direction.Vertical,
            children |> List.map (fun c -> { Length = Length.Content; Child = c })
        )

    let panel (title: string) (style: Style) (children: LayoutNode<'msg> list) : LayoutNode<'msg> =
        let titleNode = Text.text ($" {title} ") Style.title
        box style [ vflow (titleNode :: children) ]

    let panelDefault (title: string) (children: LayoutNode<'msg> list) : LayoutNode<'msg> =
        panel title Style.border children

module StatusBar =
    let statusBar
        (leftItems: LayoutNode<'msg> list)
        (centerItems: LayoutNode<'msg> list)
        (rightItems: LayoutNode<'msg> list)
        : LayoutNode<'msg> =
        let allItems =
            leftItems
            @ [ Text.text " " Style.dim ]
            @ centerItems
            @ [ Text.text " " Style.dim ]
            @ rightItems

        // Box renders one child filling its inner rect; flow the items through an
        // explicit horizontal Stack so they sit side-by-side instead of overlapping.
        Box.box
            Style.border
            [ LayoutNode.Stack(
                  Rect.Create(0, 0, 0, 0),
                  Direction.Horizontal,
                  allItems |> List.map (fun c -> { Length = Length.Content; Child = c })
              ) ]

    let statusBarSimple (items: string list) : LayoutNode<'msg> =
        let nodes = items |> List.map (fun s -> Text.textDefault s)
        statusBar nodes [] []

module Spacer =
    /// A zero-extent placeholder. Use it in an *explicit-length* slot —
    /// `Stack.sized (Cells n) Spacer.spacer` or `Dock.fill Spacer.spacer`. On its
    /// own in a `vstack`/`hstack` (which wrap every child in `Content`) it
    /// collapses to nothing and can't push siblings apart — use `flexSpacer`.
    let spacer: LayoutNode<'msg> = LayoutNode.Empty

    /// A flexible spacer that absorbs a stack's leftover space, pushing the
    /// siblings on either side to opposite ends. A `StackChild` (its flex lives in
    /// `Length.Fill`, which only `Stack` distributes), so use it in
    /// `Stack.vstackOf`/`hstackOf`. Two of them split the slack, centering the
    /// child between.
    let flexSpacer: StackChild<'msg> =
        { Length = Length.Fill
          Child = LayoutNode.Empty }

module Dock =
    let dock (children: DockChild<'msg> list) : LayoutNode<'msg> =
        LayoutNode.Dock(Rect.Create(0, 0, 0, 0), children)

    let top (height: int) (child: LayoutNode<'msg>) : DockChild<'msg> =
        { Position = DockPosition.Top(Cells height)
          Child = child }

    let bottom (height: int) (child: LayoutNode<'msg>) : DockChild<'msg> =
        { Position = DockPosition.Bottom(Cells height)
          Child = child }

    let left (width: int) (child: LayoutNode<'msg>) : DockChild<'msg> =
        { Position = DockPosition.Left(Cells width)
          Child = child }

    let right (width: int) (child: LayoutNode<'msg>) : DockChild<'msg> =
        { Position = DockPosition.Right(Cells width)
          Child = child }

    let fill (child: LayoutNode<'msg>) : DockChild<'msg> =
        { Position = DockPosition.Fill
          Child = child }

module Stack =
    /// Pair a child with an explicit length along the stack axis.
    let sized (length: Length) (child: LayoutNode<'msg>) : StackChild<'msg> = { Length = length; Child = child }

    /// A flexible spacer slot that soaks up the stack's leftover space, pushing the
    /// siblings around it to opposite ends. Drop it between explicitly-sized
    /// children of a `vstackOf`/`hstackOf`. (Alias of `Spacer.flexSpacer`.)
    let flex: StackChild<'msg> = Spacer.flexSpacer

    /// Build a stack from explicitly-sized children.
    let stackOf (direction: Direction) (children: StackChild<'msg> list) : LayoutNode<'msg> =
        LayoutNode.Stack(Rect.Create(0, 0, 0, 0), direction, children)

    /// Build a stack where every child takes its intrinsic (Content) extent.
    let stack (direction: Direction) (children: LayoutNode<'msg> list) : LayoutNode<'msg> =
        stackOf direction (children |> List.map (sized Length.Content))

    let vstack (children: LayoutNode<'msg> list) : LayoutNode<'msg> = stack Direction.Vertical children

    let hstack (children: LayoutNode<'msg> list) : LayoutNode<'msg> = stack Direction.Horizontal children

    let vstackOf (children: StackChild<'msg> list) : LayoutNode<'msg> = stackOf Direction.Vertical children

    let hstackOf (children: StackChild<'msg> list) : LayoutNode<'msg> = stackOf Direction.Horizontal children

module Scroll =
    /// A scroll region driven by a full ScrollState (offset + content size).
    let scrollState (state: ScrollState) (child: LayoutNode<'msg>) : LayoutNode<'msg> =
        LayoutNode.Scroll(Rect.Create(0, 0, 0, 0), state, child)

    /// A vertically-scrolling region at the given row offset.
    let vertical (offsetY: int) (child: LayoutNode<'msg>) : LayoutNode<'msg> =
        LayoutNode.Scroll(
            Rect.Create(0, 0, 0, 0),
            { ScrollState.Empty with
                OffsetY = offsetY },
            child
        )

module Backdrop =
    /// An opaque rectangle filling its assigned rect — panel background, modal
    /// backdrop, or highlight. Occludes whatever it covers in an Overlay.
    let solid (style: Style) : LayoutNode<'msg> =
        LayoutNode.Filled(Rect.Create(0, 0, 0, 0), style)

    /// Draw `child` over a full-bleed background of `style` — the row/cell
    /// highlight primitive. A bare styled `Text` only colours the cells under its
    /// glyphs; this fills the whole assigned rect first, then renders the child on
    /// top, so a selection background spans the full width (gaps included).
    let behind (style: Style) (child: LayoutNode<'msg>) : LayoutNode<'msg> =
        LayoutNode.Overlay(Rect.Create(0, 0, 0, 0), [ LayoutNode.Filled(Rect.Create(0, 0, 0, 0), style); child ])

/// Place a sized child within the overlay/screen area, on the `Positioned`
/// layout node. Composes with `Backdrop`/`Filled` layers inside a
/// `LayoutNode.Overlay`.
module Overlay =
    /// Place `child`, sized to (`width`, `height`), at `placement` within the area.
    let positioned
        (placement: Placement)
        (width: Length)
        (height: Length)
        (child: LayoutNode<'msg>)
        : LayoutNode<'msg> =
        LayoutNode.Positioned(Rect.Create(0, 0, 0, 0), placement, width, height, child)

    /// Center a child of explicit cell size within the area.
    let centered (width: int) (height: int) (child: LayoutNode<'msg>) : LayoutNode<'msg> =
        positioned Center (Cells width) (Cells height) child

    /// Place a `width`×`height` child at (`x`, `y`) within an `areaW`×`areaH`
    /// region, clamped so it stays fully on-screen. For cursor/anchor popups
    /// (completion, tooltip) where the app supplies the point and the screen size.
    let atPoint
        (x: int)
        (y: int)
        (width: int)
        (height: int)
        (areaW: int)
        (areaH: int)
        (child: LayoutNode<'msg>)
        : LayoutNode<'msg> =
        let px = max 0 (min x (max 0 (areaW - width)))
        let py = max 0 (min y (max 0 (areaH - height)))

        Dock.dock
            [ Dock.top py Spacer.spacer
              Dock.bottom (max 0 (areaH - py - height)) Spacer.spacer
              Dock.left px Spacer.spacer
              Dock.right (max 0 (areaW - px - width)) Spacer.spacer
              Dock.fill child ]

/// A centered modal: an opaque backdrop behind a bordered `width`×`height` box
/// with a title row above a `body` slot — the *layout half* of the modal pattern.
/// For the keyboard focus-trap, pair the open/close with `Focus.pushTrap`/`popTrap`
/// (see `Mire.Layout.Focus`): push the modal's button ids on open, pop on close.
/// Returns a single node to drop over a base tree:
/// `Overlay(rect0, [ baseTree; Modal.modal … ])`.
module Modal =
    let modal
        (backdropStyle: Style)
        (borderStyle: Style)
        (titleStyle: Style)
        (width: int)
        (height: int)
        (title: string)
        (body: LayoutNode<'msg>)
        : LayoutNode<'msg> =
        let titledBox =
            Box.box
                borderStyle
                [ Stack.vstackOf
                      [ Stack.sized (Cells 1) (Text.text (" " + title) titleStyle)
                        Stack.sized Length.Fill body ] ]

        LayoutNode.Overlay(
            Rect.Create(0, 0, 0, 0),
            [ Backdrop.solid backdropStyle; Overlay.centered width height titledBox ]
        )

/// Auto-dismissing notifications. The layout half: render a column of cards and
/// place it (top-right by default) over a base tree, on `Positioned`. The app
/// owns the toast list and expires entries with a `Sub` timer.
module Toast =
    /// A toast card — a bordered box with a toned title line above a body line.
    let card (border: Style) (titleStyle: Style) (bodyStyle: Style) (title: string) (body: string) : LayoutNode<'msg> =
        Box.box
            border
            [ Stack.vstackOf
                  [ Stack.sized (Length.Cells 1) (Text.text (" " + title) titleStyle)
                    Stack.sized (Length.Cells 1) (Text.text (" " + body) bodyStyle) ] ]

    /// Place a column of toast `cards` at `placement` (`TopRight` for the usual
    /// stack), `width` cells wide and each `cardHeight` tall, separated by a blank
    /// row. Returns an overlay layer to drop over a base tree; empty ⇒ nothing.
    let stack (placement: Placement) (width: int) (cardHeight: int) (cards: LayoutNode<'msg> list) : LayoutNode<'msg> =
        if List.isEmpty cards then
            Spacer.spacer
        else
            let rows =
                cards
                |> List.collect (fun c ->
                    [ Stack.sized (Length.Cells cardHeight) c
                      Stack.sized (Length.Cells 1) Spacer.spacer ])

            let height = List.length cards * (cardHeight + 1)
            Overlay.positioned placement (Length.Cells width) (Length.Cells height) (Stack.vstackOf rows)

/// Single-selection, scrollable list of text rows. The selected row gets a
/// full-width highlight (via `Backdrop.behind`), auto-scrolled to stay visible.
/// Labels are caller-truncated to the available width. (Not yet virtualized.)
module ListView =
    /// One row: full-width selection fill when `selected`, plain text otherwise.
    let row (selStyle: Style) (rowStyle: Style) (selected: bool) (label: string) : LayoutNode<'msg> =
        if selected then
            Backdrop.behind selStyle (Text.text label selStyle)
        else
            Text.text label rowStyle

    /// A virtualized list `height` rows tall: only the visible window is built
    /// (not the whole list), auto-scrolled to keep `scrollTo` in view. `isSelected`
    /// decides each row's highlight, so it supports single- *or* multi-selection —
    /// the app owns the selection state and key handling (MVU).
    let viewWith
        (height: int)
        (selStyle: Style)
        (rowStyle: Style)
        (isSelected: int -> bool)
        (scrollTo: int)
        (labels: string list)
        : LayoutNode<'msg> =
        let n = List.length labels
        let off = max 0 (min (max 0 (n - height)) (scrollTo - height / 2))

        labels
        |> List.indexed
        |> List.skip (min off n)
        |> List.truncate (max 0 height)
        |> List.map (fun (i, l) -> row selStyle rowStyle (isSelected i) l)
        |> Stack.vstack

    /// Single-selection convenience: highlight + auto-scroll to `sel`.
    let view (height: int) (selStyle: Style) (rowStyle: Style) (sel: int) (labels: string list) : LayoutNode<'msg> =
        viewWith height selStyle rowStyle (fun i -> i = sel) sel labels

/// A vertically-scrolling viewport with a track/thumb scrollbar. The app owns the
/// offset (like `Scroll.vertical`); the pure helpers make jump-to-bottom,
/// follow-tail, and paging one-liners in `update`:
///   `clampOffset` keeps an offset in range · `toBottom` is the jump-to-bottom /
///   follow-tail offset · `atBottom` decides whether to keep following the tail.
module ScrollView =
    /// The offset that scrolls to the bottom (also the jump-to-bottom / follow-tail offset).
    let toBottom (viewportH: int) (contentH: int) : int = max 0 (contentH - viewportH)

    /// Clamp an offset to `0 .. toBottom`.
    let clampOffset (viewportH: int) (contentH: int) (offset: int) : int =
        max 0 (min offset (toBottom viewportH contentH))

    /// True when `offset` is scrolled all the way down — keep this true to follow the tail.
    let atBottom (viewportH: int) (contentH: int) (offset: int) : bool = offset >= toBottom viewportH contentH

    /// A 1-cell scrollbar column: a proportional thumb over a track.
    let private scrollbar
        (viewportH: int)
        (contentH: int)
        (offset: int)
        (trackStyle: Style)
        (thumbStyle: Style)
        : LayoutNode<'msg> =
        let thumbH, thumbPos =
            if contentH <= viewportH || viewportH <= 0 then
                0, 0 // content fits: just the track, no thumb
            else
                let th = max 1 (viewportH * viewportH / contentH)
                let span = contentH - viewportH
                let pos = clampOffset viewportH contentH offset * (viewportH - th) / span
                th, pos

        Stack.vstackOf
            [ for y in 0 .. viewportH - 1 ->
                  let isThumb = y >= thumbPos && y < thumbPos + thumbH

                  Stack.sized
                      (Length.Cells 1)
                      (Text.text (if isThumb then "█" else "│") (if isThumb then thumbStyle else trackStyle)) ]

    /// `content` in a `viewportH`-tall viewport scrolled to `offset`, with a 1-cell
    /// track/thumb scrollbar on the right. `contentH` is the content's total height
    /// (the caller knows it, or `Layout.contentExtent Vertical`).
    let vertical
        (viewportH: int)
        (contentH: int)
        (offset: int)
        (trackStyle: Style)
        (thumbStyle: Style)
        (content: LayoutNode<'msg>)
        : LayoutNode<'msg> =
        Stack.hstackOf
            [ Stack.sized Length.Fill (Scroll.vertical (clampOffset viewportH contentH offset) content)
              Stack.sized (Length.Cells 1) (scrollbar viewportH contentH offset trackStyle thumbStyle) ]

/// Single-line text editor view over a `TextBuffer`. Pure edit logic lives in
/// `Mire.Core.TextBuffer`; this only renders. When `focused`, a block cursor is
/// drawn (the cell under the cursor in `cursorStyle`) and the view scrolls
/// horizontally to keep it visible within `width` cells.
module Input =
    let render
        (width: int)
        (textStyle: Style)
        (cursorStyle: Style)
        (focused: bool)
        (buf: TextBuffer)
        : LayoutNode<'msg> =
        let text = buf.Text
        let len = text.Length
        let cur = max 0 (min len buf.Cursor)
        // horizontal scroll window of `width` chars containing the cursor
        let start = if cur < width then 0 else cur - width + 1
        let visLen = min (max 0 (len - start)) width
        let visible = if visLen <= 0 then "" else text.Substring(start, visLen)

        if not focused then
            Text.text visible textStyle
        else
            match TextBuffer.selection buf with
            // A selection renders as a highlighted range (reusing `cursorStyle` —
            // inverse video reads as selected text), clipped to the visible window.
            | Some(lo, hi) when hi > lo && min visible.Length (hi - start) > max 0 (lo - start) ->
                let s0 = max 0 (lo - start)
                let s1 = min visible.Length (hi - start)

                Stack.hstackOf
                    [ Stack.sized Length.Content (Text.text (visible.Substring(0, s0)) textStyle)
                      Stack.sized Length.Content (Text.text (visible.Substring(s0, s1 - s0)) cursorStyle)
                      Stack.sized Length.Content (Text.text (visible.Substring(s1)) textStyle) ]
            | _ ->
                let cw = cur - start // cursor column within the window (0 .. width-1)

                let leftPart =
                    if cw <= 0 then
                        ""
                    else
                        visible.Substring(0, min cw visible.Length)

                let atCursor = if cw < visible.Length then string visible.[cw] else " "

                let rightPart =
                    if cw + 1 <= visible.Length then
                        visible.Substring(min (cw + 1) visible.Length)
                    else
                        ""

                Stack.hstackOf
                    [ Stack.sized Length.Content (Text.text leftPart textStyle)
                      Stack.sized (Length.Cells 1) (Text.text atCursor cursorStyle)
                      Stack.sized Length.Content (Text.text rightPart textStyle) ]

/// One table column: a header label, a width (`Length`), and a per-row cell
/// renderer. `Table.textColumn` builds a plain styled-text column.
type Column<'row, 'msg> =
    { Header: string
      Width: Length
      Render: 'row -> LayoutNode<'msg> }

/// A table with a sticky header over a windowed body. The app owns the `topRow`
/// scroll offset and the `selected` row index (like `ListView`/`ScrollView`);
/// columns map each row to a cell node, so cells can be styled/custom. Rows are
/// caller-windowed by `topRow`/`height`; pair with `ScrollView` math for scrolling.
module Table =
    /// A column that renders `toStr row` as styled text.
    let textColumn (header: string) (width: Length) (style: Style) (toStr: 'row -> string) : Column<'row, 'msg> =
        { Header = header
          Width = width
          Render = fun r -> Text.text (toStr r) style }

    let view
        (height: int)
        (headerStyle: Style)
        (selStyle: Style)
        (topRow: int)
        (isSelected: int -> bool)
        (columns: Column<'row, 'msg> list)
        (rows: 'row list)
        : LayoutNode<'msg> =
        let headerRow =
            Stack.hstackOf [ for c in columns -> Stack.sized c.Width (Text.text c.Header headerStyle) ]

        // only the visible window of rows is built (virtualized by `topRow`/`height`)
        let start = max 0 (min topRow (List.length rows))

        let bodyRows =
            rows
            |> List.indexed
            |> List.skip start
            |> List.truncate (max 0 height)
            |> List.map (fun (i, row) ->
                let cells =
                    Stack.hstackOf [ for c in columns -> Stack.sized c.Width (c.Render row) ]

                if isSelected i then
                    Backdrop.behind selStyle cells
                else
                    cells)

        Stack.vstackOf
            [ Stack.sized (Length.Cells 1) headerRow
              Stack.sized (Length.Cells(max 0 height)) (Stack.vstack bodyRows) ]

/// A centered, fuzzy-filtered command surface (the `Ctrl+P`-style palette), built
/// on `Modal` + `ListView`. The pure `matches`/`filter` are the fuzzy core (also
/// reused by `Completion`); the app holds the query + selection, filters the
/// items, and pairs the open/close with `Focus.pushTrap`/`popTrap`.
module CommandPalette =
    /// Greedy subsequence score: `Some (firstMatchIndex, span)` (span = last −
    /// first matched index) when `query` is a subsequence of `text`, else `None`.
    /// Lower `(firstMatchIndex, span)` ranks better — earlier, tighter matches win.
    let private score (query: string) (text: string) : (int * int) option =
        let q = query.ToLowerInvariant()
        let t = text.ToLowerInvariant()

        if q = "" then
            Some(0, 0)
        else
            let mutable qi = 0
            let mutable first = -1
            let mutable last = -1

            for ti in 0 .. t.Length - 1 do
                if qi < q.Length && t.[ti] = q.[qi] then
                    if first < 0 then
                        first <- ti

                    last <- ti
                    qi <- qi + 1

            if qi = q.Length then Some(first, last - first) else None

    /// Case-insensitive subsequence match: do `query`'s characters appear, in
    /// order, somewhere in `text`? An empty query matches everything.
    let matches (query: string) (text: string) : bool = (score query text).IsSome

    /// Items that fuzzy-`matches` the query, ranked best-first (earlier, tighter
    /// matches lead); ties keep their original order.
    let filter (query: string) (items: string list) : string list =
        items
        |> List.choose (fun it -> score query it |> Option.map (fun s -> s, it))
        |> List.sortBy fst
        |> List.map snd

    /// A centered palette: a title, a `❯ query` line, and the fuzzy-filtered,
    /// selectable `items` list. Pass the already-`filter`ed items.
    let view
        (width: int)
        (height: int)
        (backdropStyle: Style)
        (borderStyle: Style)
        (accentStyle: Style)
        (selStyle: Style)
        (rowStyle: Style)
        (title: string)
        (query: string)
        (selected: int)
        (items: string list)
        : LayoutNode<'msg> =
        let listH = max 1 (height - 4) // border(2) + title(1) + query(1)

        let body =
            Stack.vstackOf
                [ Stack.sized (Length.Cells 1) (Text.text (" ❯ " + query + "▏") accentStyle)
                  Stack.sized Length.Fill (ListView.view listH selStyle rowStyle selected items) ]

        Modal.modal backdropStyle borderStyle accentStyle width height title body

/// A cursor-anchored completion popup — a small bordered, selectable list placed
/// just below an anchor point (clamped on-screen via `Overlay.atPoint`). The app
/// filters the candidates (e.g. with `CommandPalette.filter`), tracks the
/// selection, and supplies the anchor + screen size.
module Completion =
    /// `items` in a `width`-wide bordered list anchored just below (`anchorX`,
    /// `anchorY`) within an `areaW`×`areaH` screen, at most `maxRows` tall.
    let view
        (areaW: int)
        (areaH: int)
        (anchorX: int)
        (anchorY: int)
        (width: int)
        (maxRows: int)
        (borderStyle: Style)
        (selStyle: Style)
        (rowStyle: Style)
        (selected: int)
        (items: string list)
        : LayoutNode<'msg> =
        let rows = max 1 (min (List.length items) (max 1 maxRows))
        let h = rows + 2 // border top + bottom

        let box =
            Box.box borderStyle [ ListView.view rows selStyle rowStyle selected items ]
        // place just below the caret if it fits; otherwise flip above it
        let below = anchorY + 1
        let y = if below + h <= areaH then below else max 0 (anchorY - h)
        Overlay.atPoint anchorX y width h areaW areaH box

/// A horizontal or vertical rule.
module Separator =
    /// A `width`-cell horizontal rule (`─`).
    let horizontal (width: int) (style: Style) : LayoutNode<'msg> =
        Text.text (String.replicate (max 0 width) "─") style

    /// A `height`-cell vertical rule (`│`).
    let vertical (height: int) (style: Style) : LayoutNode<'msg> =
        Stack.vstack [ for _ in 1 .. max 0 height -> Text.text "│" style ]

/// A small toned pill — a padded, styled label. The caller supplies the tone via
/// `style` (e.g. `Style.success`); a background-bearing style fills the chip.
module Badge =
    let badge (style: Style) (label: string) : LayoutNode<'msg> = Text.text (sprintf " %s " label) style

/// A key-hint chip: a styled key glyph followed by a label (e.g. `Ctrl+P palette`)
/// for status bars / footers.
module KeyHint =
    let hint (keyStyle: Style) (labelStyle: Style) (key: string) (label: string) : LayoutNode<'msg> =
        Stack.hstackOf
            [ Stack.sized Length.Content (Text.text key keyStyle)
              Stack.sized Length.Content (Text.text (" " + label) labelStyle) ]

/// Multi-line text editor view over a `TextBuffer` (text with `\n`). Renders a
/// `height`-row window of the lines, vertically scrolled to keep the cursor visible
/// (no wrap — long lines clip, and the view tracks the cursor horizontally), with a
/// block cursor at the cursor's (row,col) when `focused`. Pure render — the app
/// drives edits (e.g. via `Mire.Core.TextEdit`); this only renders.
module TextArea =
    /// Break one logical line into visual segments no wider than `width`,
    /// word-wrapping at spaces when possible and hard-breaking words longer than
    /// the width. Returns `(startIndex, text)` pairs that partition the line
    /// exactly — `text` is `line.Substring(startIndex, text.Length)` and the
    /// concatenation of the texts is the original line, so char offsets map
    /// straight back to the buffer (a trailing break-space stays on its segment).
    /// An empty line yields a single empty segment so it still occupies a row.
    let wrapLine (width: int) (line: string) : (int * string) list =
        let n = line.Length

        if width <= 0 || n = 0 then
            [ (0, line) ]
        else
            let rec go (pos: int) (acc: (int * string) list) =
                if n - pos <= width then
                    List.rev ((pos, line.Substring(pos)) :: acc)
                else
                    let segEnd = pos + width // exclusive upper bound of this segment

                    // last space strictly inside the window — break *after* it so the
                    // word stays whole; none → hard break at the width.
                    let mutable brk = -1

                    for i in pos .. segEnd - 1 do
                        if line.[i] = ' ' then
                            brk <- i

                    let cut = if brk > pos then brk + 1 else segEnd
                    go cut ((pos, line.Substring(pos, cut - pos)) :: acc)

            go 0 []

    let render
        (width: int)
        (height: int)
        (textStyle: Style)
        (cursorStyle: Style)
        (focused: bool)
        (buf: TextBuffer)
        : LayoutNode<'msg> =
        if width <= 0 || height <= 0 then
            Spacer.spacer
        else
            let lines = buf.Text.Split('\n')
            let curRow, curCol = TextBuffer.cursorRowCol buf

            // vertical scroll-to-cursor (the vertical analog of Input's window)
            let rawOff = if curRow < height then 0 else curRow - height + 1
            let offY = ScrollView.clampOffset height lines.Length rawOff
            // horizontal window so the cursor column stays visible (no wrap)
            let hstart = if curCol < width then 0 else curCol - width + 1

            // Global char offset of each line's start, for mapping the (whole-text)
            // selection onto individual lines.
            let mutable acc = 0

            let lineStarts =
                [| for ln in lines ->
                       let s = acc
                       acc <- acc + ln.Length + 1
                       s |]

            let sel = if focused then TextBuffer.selection buf else None

            let renderLine (idx: int) (line: string) : LayoutNode<'msg> =
                let visLen = min (max 0 (line.Length - hstart)) width
                let visible = if visLen <= 0 then "" else line.Substring(hstart, visLen)
                let gStart = lineStarts.[idx]

                // this line's selected sub-range, clipped to the visible window
                let selRange =
                    match sel with
                    | Some(lo, hi) ->
                        let a = max lo gStart
                        let b = min hi (gStart + line.Length)

                        if b > a then
                            let la = max 0 (a - gStart - hstart)
                            let lb = min visible.Length (b - gStart - hstart)
                            if lb > la then Some(la, lb) else None
                        else
                            None
                    | None -> None

                match selRange with
                | Some(la, lb) ->
                    // selection highlight (reuse cursorStyle — inverse video)
                    Stack.hstackOf
                        [ Stack.sized Length.Content (Text.text (visible.Substring(0, la)) textStyle)
                          Stack.sized Length.Content (Text.text (visible.Substring(la, lb - la)) cursorStyle)
                          Stack.sized Length.Content (Text.text (visible.Substring(lb)) textStyle) ]
                | None when focused && idx = curRow && Option.isNone sel ->
                    // block cursor — only when nothing is selected
                    let cw = curCol - hstart

                    let leftPart =
                        if cw <= 0 then
                            ""
                        else
                            visible.Substring(0, min cw visible.Length)

                    let atCursor =
                        if cw >= 0 && cw < visible.Length then
                            string visible.[cw]
                        else
                            " "

                    let rightPart =
                        if cw + 1 <= visible.Length then
                            visible.Substring(min (max 0 (cw + 1)) visible.Length)
                        else
                            ""

                    Stack.hstackOf
                        [ Stack.sized Length.Content (Text.text leftPart textStyle)
                          Stack.sized (Length.Cells 1) (Text.text atCursor cursorStyle)
                          Stack.sized Length.Content (Text.text rightPart textStyle) ]
                | None -> Text.text visible textStyle

            Stack.vstackOf
                [ for y in offY .. min (lines.Length - 1) (offY + height - 1) ->
                      Stack.sized (Length.Cells 1) (renderLine y lines.[y]) ]

    /// Soft-wrapping variant of `render`: instead of horizontally scrolling each
    /// logical line, every line is word-wrapped to `width` (see `wrapLine`) and the
    /// resulting *visual* rows are what scroll vertically and carry the cursor and
    /// selection. Same signature as `render`, so an app picks wrap vs. scroll per
    /// call site. (Char-indexed, like the rest of the editing stack.)
    let renderWrapped
        (width: int)
        (height: int)
        (textStyle: Style)
        (cursorStyle: Style)
        (focused: bool)
        (buf: TextBuffer)
        : LayoutNode<'msg> =
        if width <= 0 || height <= 0 then
            Spacer.spacer
        else
            let lines = buf.Text.Split('\n')

            // Global char offset of each logical line's start.
            let mutable acc = 0

            let lineStarts =
                [| for ln in lines ->
                       let s = acc
                       acc <- acc + ln.Length + 1
                       s |]

            // Flatten to visual rows: (logical line idx, char offset of the segment
            // within that line, segment text, global offset of the line start).
            let visRows =
                [| for idx in 0 .. lines.Length - 1 do
                       let g = lineStarts.[idx]

                       for (s, t) in wrapLine width lines.[idx] -> (idx, s, t, g) |]

            let curG = buf.Cursor

            // The visual row holding the cursor. A cursor sitting exactly on a
            // soft-wrap boundary belongs to the *next* row (its start); a cursor at
            // the end of a logical line's last segment stays put.
            let curVis =
                let mutable found = 0

                for i in 0 .. visRows.Length - 1 do
                    let (lineIdx, start, text, g) = visRows.[i]
                    let lo = g + start
                    let hi = lo + text.Length

                    let endsHere =
                        i = visRows.Length - 1
                        || (let (nextLine, _, _, _) = visRows.[i + 1] in nextLine <> lineIdx)

                    if curG >= lo && (curG < hi || (curG = hi && endsHere)) then
                        found <- i

                found

            let sel = if focused then TextBuffer.selection buf else None

            let rawOff = if curVis < height then 0 else curVis - height + 1
            let offY = ScrollView.clampOffset height visRows.Length rawOff

            let renderRow (i: int) : LayoutNode<'msg> =
                let (_, _, text, g) = visRows.[i]
                let rowLo = g + (let (_, s, _, _) = visRows.[i] in s)
                let rowHi = rowLo + text.Length

                let selRange =
                    match sel with
                    | Some(lo, hi) ->
                        let a = max lo rowLo
                        let b = min hi rowHi
                        if b > a then Some(a - rowLo, b - rowLo) else None
                    | None -> None

                match selRange with
                | Some(la, lb) ->
                    Stack.hstackOf
                        [ Stack.sized Length.Content (Text.text (text.Substring(0, la)) textStyle)
                          Stack.sized Length.Content (Text.text (text.Substring(la, lb - la)) cursorStyle)
                          Stack.sized Length.Content (Text.text (text.Substring(lb)) textStyle) ]
                | None when focused && i = curVis && Option.isNone sel ->
                    let cw = curG - rowLo

                    let leftPart =
                        if cw <= 0 then
                            ""
                        else
                            text.Substring(0, min cw text.Length)

                    let atCursor =
                        if cw >= 0 && cw < text.Length then
                            string text.[cw]
                        else
                            " "

                    let rightPart =
                        if cw + 1 <= text.Length then
                            text.Substring(min (max 0 (cw + 1)) text.Length)
                        else
                            ""

                    Stack.hstackOf
                        [ Stack.sized Length.Content (Text.text leftPart textStyle)
                          Stack.sized (Length.Cells 1) (Text.text atCursor cursorStyle)
                          Stack.sized Length.Content (Text.text rightPart textStyle) ]
                | None -> Text.text text textStyle

            Stack.vstackOf
                [ for i in offY .. min (visRows.Length - 1) (offY + height - 1) ->
                      Stack.sized (Length.Cells 1) (renderRow i) ]

/// A two-pane split with a divider between the panes — the convenience the demos
/// hand-roll today as raw `Stack`/`Dock` fractions. The app owns the split
/// position (the "resize"): pass `Cells n` for an app-driven/draggable position or
/// `Fraction f` for a proportional split; the second pane fills the remainder.
/// Nest two `split`s for >2 panes.
module SplitView =
    /// Split the area along `direction`: `first` sized by `firstSize`, then a 1-cell
    /// `dividerStyle` gutter, then `second` filling the rest. The gutter is a
    /// `Filled` strip that auto-spans the cross axis, so it needs a background-bearing
    /// `dividerStyle` to show (like `Badge`); a `Style.Default` gutter is an invisible
    /// 1-cell gap. (A glyph rule instead would need the cross-axis length, which the
    /// app can build with `Separator` if it wants one.)
    let split
        (direction: Direction)
        (firstSize: Length)
        (dividerStyle: Style)
        (first: LayoutNode<'msg>)
        (second: LayoutNode<'msg>)
        : LayoutNode<'msg> =
        Stack.stackOf
            direction
            [ Stack.sized firstSize first
              Stack.sized (Length.Cells 1) (Backdrop.solid dividerStyle)
              Stack.sized Length.Fill second ]

    /// Left | right split (`first` = left pane).
    let horizontal (firstSize: Length) (dividerStyle: Style) (left: LayoutNode<'msg>) (right: LayoutNode<'msg>) =
        split Direction.Horizontal firstSize dividerStyle left right

    /// Top / bottom split (`first` = top pane).
    let vertical (firstSize: Length) (dividerStyle: Style) (top: LayoutNode<'msg>) (bottom: LayoutNode<'msg>) =
        split Direction.Vertical firstSize dividerStyle top bottom

/// An anchored, bordered doc popup — the inline/hover tooltip. Mirrors
/// `Completion`'s placement: sits just below the anchor point, flips above when it
/// would run off the bottom, and is clamped on-screen horizontally (via
/// `Overlay.atPoint`). Pure layout — the app decides when to show it and supplies
/// the anchor + screen size; `lines` are caller-wrapped to fit (`width - 2`).
module Tooltip =
    let view
        (areaW: int)
        (areaH: int)
        (anchorX: int)
        (anchorY: int)
        (width: int)
        (borderStyle: Style)
        (textStyle: Style)
        (lines: string list)
        : LayoutNode<'msg> =
        let rows = max 1 (List.length lines)
        let h = rows + 2 // border top + bottom

        let body =
            Stack.vstackOf [ for ln in lines -> Stack.sized (Length.Cells 1) (Text.text (" " + ln) textStyle) ]

        let box = Box.box borderStyle [ body ]
        // below the anchor if it fits, else flipped above it
        let below = anchorY + 1
        let y = if below + h <= areaH then below else max 0 (anchorY - h)
        Overlay.atPoint anchorX y width h areaW areaH box

/// An animated spinner. The app owns the `tick` (e.g. a `Sub.Every` counter); this
/// just maps it to a frame glyph. Braille is the conventional default; pass your own
/// `frames` (dots, bars, …) to `frameOf`.
module Spinner =
    /// The conventional 10-step braille spinner.
    let braille = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]

    /// The frame glyph at `tick`, wrapping — safe for negative ticks / empty sets.
    let frameOf (frames: string[]) (tick: int) : string =
        if frames.Length = 0 then
            ""
        else
            frames.[((tick % frames.Length) + frames.Length) % frames.Length]

    /// A styled braille spinner glyph at `tick`.
    let view (style: Style) (tick: int) : LayoutNode<'msg> = Text.text (frameOf braille tick) style

    /// A spinner glyph + a trailing label (e.g. `⠋ building…`).
    let labeled (spinStyle: Style) (labelStyle: Style) (tick: int) (label: string) : LayoutNode<'msg> =
        Stack.hstackOf
            [ Stack.sized (Length.Cells 1) (Text.text (frameOf braille tick) spinStyle)
              Stack.sized Length.Content (Text.text (" " + label) labelStyle) ]

/// A determinate progress bar / gauge. The app owns the `fraction` (0..1); cells
/// fill left-to-right.
module ProgressBar =
    /// A `width`-cell bar for `fraction` (clamped 0..1): filled cells (`█`) in
    /// `fillStyle` over a `░` track in `trackStyle`.
    let view (width: int) (fillStyle: Style) (trackStyle: Style) (fraction: float) : LayoutNode<'msg> =
        let w = max 0 width
        let f = max 0.0 (min 1.0 fraction)
        let filled = max 0 (min w (int (System.Math.Round(f * float w))))

        Stack.hstackOf
            [ Stack.sized (Length.Cells filled) (Text.text (String.replicate filled "█") fillStyle)
              Stack.sized (Length.Cells(w - filled)) (Text.text (String.replicate (w - filled) "░") trackStyle) ]

    /// The gauge form — `view` with the percentage centered over the bar.
    /// `labelStyle` should carry a background so it stays legible over the fill.
    let gauge
        (width: int)
        (fillStyle: Style)
        (trackStyle: Style)
        (labelStyle: Style)
        (fraction: float)
        : LayoutNode<'msg> =
        let f = max 0.0 (min 1.0 fraction)
        let pct = sprintf "%d%%" (int (System.Math.Round(f * 100.0)))

        LayoutNode.Overlay(
            Rect.Create(0, 0, 0, 0),
            [ view width fillStyle trackStyle f
              Overlay.positioned Center (Length.Cells pct.Length) (Length.Cells 1) (Text.text pct labelStyle) ]
        )

/// A horizontal tab strip. The app owns the `selected` index and renders the body;
/// the active tab uses `activeStyle`, the rest `inactiveStyle`.
module Tabs =
    let strip (activeStyle: Style) (inactiveStyle: Style) (selected: int) (labels: string list) : LayoutNode<'msg> =
        Stack.hstackOf
            [ for i, label in List.indexed labels ->
                  Stack.sized
                      Length.Content
                      (Text.text (sprintf " %s " label) (if i = selected then activeStyle else inactiveStyle)) ]

/// Selection-control glyphs — pure render, the app owns the boolean/selection.
module Toggle =
    /// `[x] label` / `[ ] label`.
    let checkbox (style: Style) (isChecked: bool) (label: string) : LayoutNode<'msg> =
        Text.text (sprintf "%s %s" (if isChecked then "[x]" else "[ ]") label) style

    /// `(•) label` / `( ) label`.
    let radio (style: Style) (selected: bool) (label: string) : LayoutNode<'msg> =
        Text.text (sprintf "%s %s" (if selected then "(•)" else "( )") label) style

    /// An on/off pill — ` ON ` in `onStyle` / ` OFF ` in `offStyle` (bg-bearing styles).
    let switch (onStyle: Style) (offStyle: Style) (isOn: bool) : LayoutNode<'msg> =
        if isOn then
            Text.text " ON " onStyle
        else
            Text.text " OFF " offStyle

/// An image placeholder. Terminals split into two worlds: those that speak the
/// Kitty graphics protocol (Ghostty/Kitty — real pixels) and those that don't.
/// `render` draws the **portable text fallback** — a bordered, captioned box sized
/// to a cell footprint — which is what lands in the cell grid on every terminal. On
/// a graphics-capable terminal an app overlays the actual image with
/// `Mire.App.Cmd.kittyImage` positioned at this box's on-screen coordinates; the
/// fallback then sits underneath as the reserved region (and shows through whenever
/// the image can't be drawn). The framework never decodes images — the app supplies
/// either a caption (fallback only) or already-encoded PNG bytes (for the overlay).
module ImagePreview =
    /// A bordered box (`width`×`height` cells) with a centered picture glyph, the
    /// `caption` (e.g. a filename), and the pixel dimensions when known.
    let render
        (width: int)
        (height: int)
        (borderStyle: Style)
        (captionStyle: Style)
        (caption: string)
        (pixelSize: (int * int) option)
        : LayoutNode<'msg> =
        if width <= 0 || height <= 0 then
            Spacer.spacer
        else
            // center a node horizontally across the inner width
            let centered (node: LayoutNode<'msg>) =
                Stack.hstackOf [ Stack.flex; Stack.sized Length.Content node; Stack.flex ]

            let row (node: LayoutNode<'msg>) =
                Stack.sized (Length.Cells 1) (centered node)

            let dimsRows =
                match pixelSize with
                | Some(w, h) -> [ row (Text.text $"{w}×{h}" captionStyle) ]
                | None -> []

            let body =
                Stack.vstackOf (
                    [ Stack.flex
                      row (Text.text "🖼" borderStyle)
                      row (Text.text caption captionStyle) ]
                    @ dimsRows
                    @ [ Stack.flex ]
                )

            Box.box borderStyle [ body ]
