namespace Mire.Layout

open Mire.Core
open Mire.Renderer

type Length =
    | Cells of int
    | Fraction of float
    | Fill
    | Content

type Direction =
    | Horizontal
    | Vertical

type DockPosition =
    | Top of Length
    | Bottom of Length
    | Left of Length
    | Right of Length
    | Fill

/// Where a `Positioned` layer sits within its assigned rect — the 3×3 grid of
/// {Start, Center, End} on each axis (the empty center slot is `Center`).
type Placement =
    | Center
    | TopLeft
    | TopCenter
    | TopRight
    | CenterLeft
    | CenterRight
    | BottomLeft
    | BottomCenter
    | BottomRight

type LayoutNode<'msg> =
    | Empty
    | Text of Rect * string * Style
    /// An opaque rectangle filled with spaces of the given style. Useful as a
    /// panel background, modal backdrop, or selection highlight — and it gives
    /// `Overlay` genuine opacity (it occludes whatever it covers).
    | Filled of Rect * Style
    /// A translucent scrim over its assigned rect: blends whatever is already
    /// painted underneath toward `tint` by `strength` (0 = untouched, 1 = solid
    /// `tint`), preserving glyphs. Unlike `Filled` it fades rather than occludes —
    /// so it must render *after* the content it dims (place it as a later sibling
    /// in an `Overlay`, over the base tree). The modal pattern uses it to fade the
    /// screen behind a still-opaque dialog box.
    | Scrim of Rect * Color * float
    | Box of Rect * Style * LayoutNode<'msg> list
    | Dock of Rect * DockChild<'msg> list
    | Stack of Rect * Direction * StackChild<'msg> list
    | Scroll of Rect * ScrollState * LayoutNode<'msg>
    | Overlay of Rect * LayoutNode<'msg> list
    /// Sizes `child` to (`width`, `height`) and places it at `placement` within
    /// the rect `measure` assigns. Composes with `Overlay` (backdrop + a centered
    /// box) and works standalone (a corner badge). For a top-level `Overlay`
    /// layer the assigned rect is the screen.
    | Positioned of Rect * Placement * Length * Length * LayoutNode<'msg>
    /// Tags its subtree with a `RegionId` so the runtime can hit-test mouse events
    /// against the laid-out rect (`Layout.collectRegions`/`regionAt`). Layout-neutral
    /// — the child fills the assigned rect exactly; it only records the rect + id.
    | Focusable of Rect * RegionId * LayoutNode<'msg>

and DockChild<'msg> =
    { Position: DockPosition
      Child: LayoutNode<'msg> }

and StackChild<'msg> =
    { Length: Length
      Child: LayoutNode<'msg> }

module Layout =

    /// Intrinsic extent of a node along `dir` (rows for Vertical, columns for
    /// Horizontal), independent of any assigned rect. Used to size `Content`
    /// children in stacks/docks and to size the backing surface of a `Scroll`.
    /// `Dock`/`Scroll`/`Overlay` have no well-defined intrinsic size, so they
    /// fall back to a rough estimate — prefer `Cells`/`Fraction`/`Fill` for
    /// those inside a stack rather than `Content`.
    let rec contentExtent (dir: Direction) (node: LayoutNode<'msg>) : int =
        match node with
        | Empty -> 0
        | Filled _
        | Scrim _ -> 1
        | Text(_, text, _) ->
            let lines = text.Split('\n')

            match dir with
            | Vertical -> lines.Length
            | Horizontal -> lines |> Array.fold (fun m l -> max m (Grapheme.stringWidth l)) 0
        | Box(_, _, children) ->
            // The border adds one cell on each edge → +2 along the axis.
            let inner =
                match dir with
                | Vertical -> children |> List.sumBy (contentExtent Vertical)
                | Horizontal ->
                    if List.isEmpty children then
                        0
                    else
                        children |> List.map (contentExtent Horizontal) |> List.max

            inner + 2
        | Stack(_, sdir, children) ->
            if sdir = dir then
                children |> List.sumBy (fun c -> contentExtent dir c.Child)
            elif List.isEmpty children then
                0
            else
                children |> List.map (fun c -> contentExtent dir c.Child) |> List.max
        | Dock(_, children) -> children |> List.sumBy (fun c -> contentExtent dir c.Child)
        | Scroll(_, _, child) -> contentExtent dir child
        | Overlay(_, children) ->
            if List.isEmpty children then
                0
            else
                children |> List.map (contentExtent dir) |> List.max
        | Positioned(_, _, _, _, child) -> contentExtent dir child
        | Focusable(_, _, child) -> contentExtent dir child

    /// The size of the virtual content surface a `Scroll` lays its child onto.
    /// At least the viewport size, expanded to the child's intrinsic extent so
    /// off-screen content exists to scroll into view. Shared by `measure` and
    /// `render` so the child is laid out and blitted in the same coordinate space.
    let private scrollContentSize (viewport: Rect) (child: LayoutNode<'msg>) : Size =
        let w = max viewport.Width (contentExtent Horizontal child)
        let h = max viewport.Height (contentExtent Vertical child)
        Size.Create(max 1 w, max 1 h)

    /// Resolve a `Length` to a cell count along an axis of `total` cells, using
    /// `child`'s intrinsic extent for `Content`. Shared by `Dock` and `Positioned`.
    let private resolveLength (len: Length) (total: int) (dir: Direction) (child: LayoutNode<'msg>) : int =
        match len with
        | Cells n -> max 0 (min n total)
        | Fraction f -> max 0 (min total (int (float total * f)))
        | Length.Content -> max 0 (min total (contentExtent dir child))
        | Length.Fill -> max 0 total

    let rec measure (available: Rect) (node: LayoutNode<'msg>) : LayoutNode<'msg> =
        match node with
        | Empty -> Empty
        | Text(_, text, style) -> Text(available, text, style)
        | Filled(_, style) -> Filled(available, style)
        | Scrim(_, tint, strength) -> Scrim(available, tint, strength)
        | Box(_, style, children) ->
            let inner = available.Inflate(-1, -1)
            let laidOut = children |> List.map (measure inner)
            Box(available, style, laidOut)
        | Dock(_, children) ->
            let mutable remaining = available

            let laidOut =
                children
                |> List.map (fun child ->
                    // How many cells this child consumes along its axis.
                    let extentOf (len: Length) (axis: int) (dir: Direction) = resolveLength len axis dir child.Child

                    let childRect =
                        match child.Position with
                        | Top len ->
                            let h = extentOf len remaining.Height Vertical
                            let r = { remaining with Height = h }

                            remaining <-
                                { remaining with
                                    Y = remaining.Y + h
                                    Height = remaining.Height - h }

                            r
                        | Bottom len ->
                            let h = extentOf len remaining.Height Vertical

                            let r =
                                { remaining with
                                    Y = remaining.Bottom - h + 1
                                    Height = h }

                            remaining <-
                                { remaining with
                                    Height = remaining.Height - h }

                            r
                        | Left len ->
                            let w = extentOf len remaining.Width Horizontal
                            let r = { remaining with Width = w }

                            remaining <-
                                { remaining with
                                    X = remaining.X + w
                                    Width = remaining.Width - w }

                            r
                        | Right len ->
                            let w = extentOf len remaining.Width Horizontal

                            let r =
                                { remaining with
                                    X = remaining.Right - w + 1
                                    Width = w }

                            remaining <-
                                { remaining with
                                    Width = remaining.Width - w }

                            r
                        | DockPosition.Fill -> remaining

                    { child with
                        Child = measure childRect child.Child })

            Dock(available, laidOut)
        | Stack(_, dir, children) ->
            let total =
                match dir with
                | Vertical -> available.Height
                | Horizontal -> available.Width
            // Pass 1: fixed sizes (Cells/Fraction/Content); Fill children are sized later.
            let sizes =
                children
                |> List.map (fun c ->
                    match c.Length with
                    | Cells n -> Some(max 0 n)
                    | Fraction f -> Some(max 0 (int (float total * f)))
                    | Length.Content -> Some(max 0 (contentExtent dir c.Child))
                    | Length.Fill -> None)

            let usedFixed =
                sizes
                |> List.sumBy (function
                    | Some n -> n
                    | None -> 0)

            let fillCount =
                sizes
                |> List.sumBy (function
                    | None -> 1
                    | Some _ -> 0)

            let fillRemaining = max 0 (total - usedFixed)
            let fillEach = if fillCount > 0 then fillRemaining / fillCount else 0
            let fillExtra = if fillCount > 0 then fillRemaining % fillCount else 0
            // Pass 2: place children sequentially along the axis.
            let mutable cursor =
                match dir with
                | Vertical -> available.Top
                | Horizontal -> available.Left

            let mutable fillSeen = 0

            let laidOut =
                List.map2
                    (fun (c: StackChild<'msg>) size ->
                        let extent =
                            match size with
                            | Some n -> n
                            | None ->
                                // Distribute the leftover remainder to the first Fill children.
                                let e = fillEach + (if fillSeen < fillExtra then 1 else 0)
                                fillSeen <- fillSeen + 1
                                e

                        let childRect =
                            match dir with
                            | Vertical -> Rect.Create(available.Left, cursor, available.Width, extent)
                            | Horizontal -> Rect.Create(cursor, available.Top, extent, available.Height)

                        cursor <- cursor + extent

                        { c with
                            Child = measure childRect c.Child })
                    children
                    sizes

            Stack(available, dir, laidOut)
        | Scroll(_, scroll, child) ->
            let size = scrollContentSize available child
            let contentRect = Rect.Create(0, 0, size.Width, size.Height)
            Scroll(available, scroll, measure contentRect child)
        | Overlay(_, children) ->
            // Each layer measured against the full area; z-order = list order.
            let laidOut = children |> List.map (measure available)
            Overlay(available, laidOut)
        | Positioned(_, placement, width, height, child) ->
            // Size the child, then place its box within the available rect.
            let w = resolveLength width available.Width Horizontal child
            let h = resolveLength height available.Height Vertical child

            let x =
                match placement with
                | TopLeft
                | CenterLeft
                | BottomLeft -> available.Left
                | Center
                | TopCenter
                | BottomCenter -> available.Left + (available.Width - w) / 2
                | TopRight
                | CenterRight
                | BottomRight -> max available.Left (available.Right - w + 1)

            let y =
                match placement with
                | TopLeft
                | TopCenter
                | TopRight -> available.Top
                | Center
                | CenterLeft
                | CenterRight -> available.Top + (available.Height - h) / 2
                | BottomLeft
                | BottomCenter
                | BottomRight -> max available.Top (available.Bottom - h + 1)

            Positioned(available, placement, width, height, measure (Rect.Create(x, y, w, h)) child)
        | Focusable(_, id, child) ->
            // Layout-neutral: the child fills the assigned rect; record it.
            Focusable(available, id, measure available child)

    let rec render (surface: Surface) (node: LayoutNode<'msg>) : unit =
        match node with
        | Empty -> ()
        | Text(rect, text, style) -> surface.WriteClipped(rect, text, style)
        | Filled(rect, style) ->
            if not rect.IsEmpty then
                surface.FillRect(rect, Cell.FromChar(' ', style))
        | Scrim(rect, tint, strength) ->
            if not rect.IsEmpty then
                // Resolve terminal-`Default` cells to a light fg / dark bg so they
                // dim too rather than staying full-bright through the scrim.
                surface.Scrim(rect, tint, strength, Color.Rgb(200uy, 200uy, 200uy), Color.Rgb(0uy, 0uy, 0uy))
        | Box(rect, style, children) ->
            surface.DrawBox(rect, style)

            for child in children do
                render surface child
        | Dock(_, children) ->
            for child in children do
                render surface child.Child
        | Stack(_, _, children) ->
            for child in children do
                render surface child.Child
        | Scroll(viewport, scroll, child) -> renderScroll surface viewport scroll child
        | Overlay(_, children) ->
            for child in children do
                render surface child
        | Positioned(_, _, _, _, child) -> render surface child
        | Focusable(_, _, child) -> render surface child

    /// Renders a scroll child onto an off-screen content surface, then blits the
    /// window selected by the scroll offset into the viewport. Offsets are clamped
    /// to the valid range so over-scroll cannot reveal blank gaps past the content.
    and private renderScroll
        (surface: Surface)
        (viewport: Rect)
        (scroll: ScrollState)
        (child: LayoutNode<'msg>)
        : unit =
        if viewport.IsEmpty then
            ()
        else
            let size = scrollContentSize viewport child
            let temp = Surface(size)
            render temp child
            let offY = max 0 (min scroll.OffsetY (size.Height - viewport.Height))
            let offX = max 0 (min scroll.OffsetX (size.Width - viewport.Width))

            for vy in 0 .. viewport.Height - 1 do
                let sy = vy + offY

                for vx in 0 .. viewport.Width - 1 do
                    let sx = vx + offX
                    surface.[viewport.Left + vx, viewport.Top + vy] <- temp.[sx, sy]

    /// Collect the `(RegionId, Rect)` of every `Focusable` node in a *measured* tree,
    /// in paint order (later entries paint on top). This is the retained region table
    /// the runtime hit-tests mouse events against. Regions nested inside a `Scroll`
    /// are omitted — their measured rects live in the scroll's virtual content space,
    /// not screen space, so they can't be hit-tested from a screen coordinate.
    let collectRegions (node: LayoutNode<'msg>) : (RegionId * Rect) list =
        let acc = ResizeArray<RegionId * Rect>()

        let rec go n =
            match n with
            | Empty
            | Text _
            | Filled _
            | Scrim _ -> ()
            | Box(_, _, children) -> children |> List.iter go
            | Dock(_, children) -> children |> List.iter (fun c -> go c.Child)
            | Stack(_, _, children) -> children |> List.iter (fun c -> go c.Child)
            | Scroll _ -> () // virtual content space — not hit-testable here
            | Overlay(_, children) -> children |> List.iter go
            | Positioned(_, _, _, _, child) -> go child
            | Focusable(rect, id, child) ->
                acc.Add(id, rect)
                go child

        go node
        List.ofSeq acc

    /// The topmost focusable region containing `point` — last match wins, matching
    /// paint order (z-order) — or `None`.
    let regionAt (regions: (RegionId * Rect) list) (point: Point) : RegionId option =
        regions
        |> List.fold (fun acc (id, rect) -> if rect.Contains point then Some id else acc) None
