namespace Mire.Widgets

open Mire.Core
open Mire.Layout

module Style =
    let border = Style.Default.WithForeground(Color.Rgb(0x4Auy, 0x90uy, 0xD9uy))

    let title =
        Style.Default.WithForeground(Color.Rgb(0xF4uy, 0xD0uy, 0x3Fuy)).WithBold(true)

    let text = Style.Default.WithForeground(Color.White)
    let dim = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x88uy))

    let counter =
        Style.Default.WithForeground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)).WithBold(true)

    let highlight =
        Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy)).WithBold(true)

    let key = Style.Default.WithForeground(Color.Rgb(0x9Cuy, 0x27uy, 0xB0uy))
    let bg = Style.Default.WithBackground(Color.Rgb(0x1Auy, 0x1Auy, 0x2Euy))
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
    let positioned (placement: Placement) (width: Length) (height: Length) (child: LayoutNode<'msg>) : LayoutNode<'msg> =
        LayoutNode.Positioned(Rect.Create(0, 0, 0, 0), placement, width, height, child)

    /// Center a child of explicit cell size within the area.
    let centered (width: int) (height: int) (child: LayoutNode<'msg>) : LayoutNode<'msg> =
        positioned Center (Cells width) (Cells height) child

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

    /// A scrollable list `height` rows tall, auto-scrolled to keep `sel` centred
    /// and in view.
    let view (height: int) (selStyle: Style) (rowStyle: Style) (sel: int) (labels: string list) : LayoutNode<'msg> =
        let n = List.length labels
        let off = max 0 (min (max 0 (n - height)) (sel - height / 2))

        labels
        |> List.mapi (fun i l -> row selStyle rowStyle (i = sel) l)
        |> Stack.vstack
        |> Scroll.vertical off

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
