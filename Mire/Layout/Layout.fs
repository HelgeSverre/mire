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
    /// It honors the same explicit sizes `measure` does — a stack child sized
    /// `Cells n` contributes `n` along the stack's axis, a docked `Top (Cells n)`
    /// contributes `n` rows — so a scroll's content surface always covers every
    /// child's measured rect. (`Fraction`/`Fill` have no intrinsic size, so they
    /// contribute their child's own extent.)
    let rec contentExtent (dir: Direction) (node: LayoutNode<'msg>) : int =
        let lengthExtent (len: Length) (child: LayoutNode<'msg>) =
            match len with
            | Cells n -> max 0 n
            | _ -> contentExtent dir child

        match node with
        | Empty -> 0
        | Filled _
        | Scrim _ -> 1
        | Text(_, text, _) ->
            let lines = text.Split('\n')

            match dir with
            | Vertical -> lines.Length
            | Horizontal -> lines |> Array.fold (fun m l -> max m (Grapheme.lineWidth l)) 0
        | Box(_, _, children) ->
            // Box children all share the inner rect (they overlap), so the inner
            // extent is the largest child's; the border adds one cell per edge.
            let inner =
                if List.isEmpty children then
                    0
                else
                    children |> List.map (contentExtent dir) |> List.max

            inner + 2
        | Stack(_, sdir, children) ->
            if sdir = dir then
                children |> List.sumBy (fun c -> lengthExtent c.Length c.Child)
            elif List.isEmpty children then
                0
            else
                children |> List.map (fun c -> contentExtent dir c.Child) |> List.max
        | Dock(_, children) ->
            // Edges along `dir` stack up; everything else shares the remainder.
            let along, across =
                children
                |> List.fold
                    (fun (along, across) (c: DockChild<'msg>) ->
                        match c.Position, dir with
                        | Top len, Vertical
                        | Bottom len, Vertical
                        | Left len, Horizontal
                        | Right len, Horizontal -> along + lengthExtent len c.Child, across
                        | _ -> along, max across (contentExtent dir c.Child))
                    (0, 0)

            along + across
        | Scroll(_, _, child) -> contentExtent dir child
        | Overlay(_, children) ->
            if List.isEmpty children then
                0
            else
                children |> List.map (contentExtent dir) |> List.max
        | Positioned(_, _, w, h, child) ->
            match dir with
            | Horizontal -> lengthExtent w child
            | Vertical -> lengthExtent h child
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

    /// Paint a measured tree onto `surface`, confining each subtree to the
    /// intersection of its ancestors' content areas: a `Box`'s children are
    /// clipped to its interior, a `Stack`/`Dock`/`Overlay`/`Focusable`'s to its
    /// rect, a `Scroll`'s to its viewport. So a child whose allocation overflows
    /// its parent (a `Cells`/`Content` stack child larger than the space left)
    /// is cut at the parent's edge instead of painting over its border or
    /// neighbours. `collectRegions` applies the same clip to hit regions.
    ///
    /// `dx, dy` translate the subtree's measured rects to surface coordinates —
    /// how a `Scroll` paints its content-space child straight into its viewport.
    /// A leaf entirely outside the clip is skipped without touching its text, so a
    /// huge scrolled subtree costs only its visible part to paint.
    let rec private renderIn (surface: Surface) (dx: int) (dy: int) (clip: Rect) (node: LayoutNode<'msg>) : unit =
        let screen (r: Rect) =
            if dx = 0 && dy = 0 then r else r.Offset(dx, dy)

        let within (r: Rect) = clip.Intersect(screen r)
        let visible (r: Rect) = not (within r).IsEmpty

        match node with
        | Empty -> ()
        | Text(rect, text, style) ->
            if visible rect then
                surface.Clip <- clip
                surface.WriteClipped(screen rect, text, style)
        | Filled(rect, style) ->
            if visible rect then
                surface.Clip <- clip
                surface.FillRect(screen rect, Cell.FromChar(' ', style))
        | Scrim(rect, tint, strength) ->
            if visible rect then
                surface.Clip <- clip
                // Resolve terminal-`Default` cells to a light fg / dark bg so they
                // dim too rather than staying full-bright through the scrim.
                surface.Scrim(screen rect, tint, strength, Color.Rgb(200uy, 200uy, 200uy), Color.Rgb(0uy, 0uy, 0uy))
        | Box(rect, style, children) ->
            if visible rect then
                surface.Clip <- clip
                surface.DrawBox(screen rect, style)
                let inner = within (rect.Inflate(-1, -1))

                for child in children do
                    renderIn surface dx dy inner child
        | Dock(rect, children) ->
            let c = within rect

            if not c.IsEmpty then
                for child in children do
                    renderIn surface dx dy c child.Child
        | Stack(rect, _, children) ->
            let c = within rect

            if not c.IsEmpty then
                for child in children do
                    renderIn surface dx dy c child.Child
        | Scroll(viewport, scroll, child) -> renderScroll surface (within viewport) (screen viewport) scroll child
        | Overlay(rect, children) ->
            let c = within rect

            if not c.IsEmpty then
                for child in children do
                    renderIn surface dx dy c child
        | Positioned(rect, _, _, _, child) -> renderIn surface dx dy (within rect) child
        | Focusable(rect, _, child) -> renderIn surface dx dy (within rect) child

    /// Paints a scroll's content-space child directly into the viewport: the
    /// viewport is cleared (a scroll region is opaque), then the child is rendered
    /// translated by the (clamped) offset and clipped to the viewport. No
    /// full-size off-screen surface is allocated, and off-viewport leaves are
    /// skipped. A wide glyph cut by a viewport edge shows as a blank (see
    /// `Surface.WriteClipped`). Offsets are clamped so over-scroll can't reveal
    /// blank gaps past the content.
    and private renderScroll
        (surface: Surface)
        (clip: Rect)
        (viewport: Rect)
        (scroll: ScrollState)
        (child: LayoutNode<'msg>)
        : unit =
        if viewport.IsEmpty || clip.IsEmpty then
            ()
        else
            let size = scrollContentSize viewport child
            let offY = max 0 (min scroll.OffsetY (size.Height - viewport.Height))
            let offX = max 0 (min scroll.OffsetX (size.Width - viewport.Width))
            surface.Clip <- clip
            surface.FillRect(viewport, Cell.Empty)
            renderIn surface (viewport.Left - offX) (viewport.Top - offY) clip child

    /// Paint a measured tree onto `surface` (see `renderIn` for the clipping
    /// contract). The surface's own `Clip` is restored afterwards.
    let render (surface: Surface) (node: LayoutNode<'msg>) : unit =
        let saved = surface.Clip

        try
            renderIn surface 0 0 saved node
        finally
            surface.Clip <- saved

    /// `r` minus `hole`: up to four rects covering exactly the cells of `r` not
    /// in `hole`.
    let private subtractRect (r: Rect) (hole: Rect) : Rect list =
        let h = r.Intersect hole

        if h.IsEmpty then
            [ r ]
        else
            [ Rect.Create(r.Left, r.Top, r.Width, h.Top - r.Top) // above
              Rect.Create(r.Left, h.Bottom + 1, r.Width, r.Bottom - h.Bottom) // below
              Rect.Create(r.Left, h.Top, h.Left - r.Left, h.Height) // left
              Rect.Create(h.Right + 1, h.Top, r.Right - h.Right, h.Height) ] // right
            |> List.filter (fun x -> not x.IsEmpty)

    /// Collect the hit regions of a *measured* tree: every `Focusable`'s rect,
    /// in paint order (later entries paint on top), in **screen space** and
    /// clipped exactly as `render` clips painting — so input routing sees the
    /// same geometry the user does:
    ///   • regions inside a `Scroll` are mapped from its content space through
    ///     the (clamped) scroll offset into the viewport, and clipped to it;
    ///   • a region is clipped to its ancestors' content areas;
    ///   • a later `Filled` or `Scrim` layer (an opaque panel, a modal's backdrop)
    ///     is a hit barrier: it removes the cells it covers from every earlier
    ///     region that isn't its own ancestor, so a click on a modal never falls
    ///     through to a control beneath it.
    /// A partially covered region may appear as several rects with the same id.
    let collectRegions (node: LayoutNode<'msg>) : (RegionId * Rect) list =
        // (id, rect pieces, is-an-open-ancestor) — pieces shrink as barriers land.
        let acc = ResizeArray<RegionId * Rect list>()
        let openAncestors = System.Collections.Generic.HashSet<int>()

        let barrier (rect: Rect) =
            if not rect.IsEmpty then
                for i in 0 .. acc.Count - 1 do
                    if not (openAncestors.Contains i) then
                        let (id, pieces) = acc.[i]
                        acc.[i] <- (id, pieces |> List.collect (fun p -> subtractRect p rect))

        // `dx, dy` translate this subtree's coordinates to screen space; `clip`
        // is in screen space.
        let rec go (dx: int) (dy: int) (clip: Rect) (n: LayoutNode<'msg>) =
            let screen (r: Rect) = r.Offset(dx, dy)
            let within (r: Rect) = clip.Intersect(screen r)

            match n with
            | Empty
            | Text _ -> ()
            | Filled(rect, _)
            | Scrim(rect, _, _) -> barrier (within rect)
            | Box(rect, _, children) ->
                let inner = within (rect.Inflate(-1, -1))
                children |> List.iter (go dx dy inner)
            | Dock(rect, children) -> children |> List.iter (fun c -> go dx dy (within rect) c.Child)
            | Stack(rect, _, children) -> children |> List.iter (fun c -> go dx dy (within rect) c.Child)
            | Scroll(viewport, scroll, child) ->
                let vp = within viewport

                if not vp.IsEmpty then
                    // Same clamped offset `renderScroll` blits with.
                    let size = scrollContentSize viewport child
                    let offY = max 0 (min scroll.OffsetY (size.Height - viewport.Height))
                    let offX = max 0 (min scroll.OffsetX (size.Width - viewport.Width))
                    go (dx + viewport.Left - offX) (dy + viewport.Top - offY) vp child
            | Overlay(rect, children) -> children |> List.iter (go dx dy (within rect))
            | Positioned(rect, _, _, _, child) -> go dx dy (within rect) child
            | Focusable(rect, id, child) ->
                let r = within rect
                let idx = acc.Count
                acc.Add(id, (if r.IsEmpty then [] else [ r ]))
                openAncestors.Add idx |> ignore
                go dx dy r child
                openAncestors.Remove idx |> ignore

        go 0 0 (Rect.Create(-100000, -100000, 200000, 200000)) node

        [ for (id, pieces) in acc do
              for p in pieces -> (id, p) ]

    /// The topmost focusable region containing `point` — last match wins, matching
    /// paint order (z-order) — or `None`.
    let regionAt (regions: (RegionId * Rect) list) (point: Point) : RegionId option =
        regions
        |> List.fold (fun acc (id, rect) -> if rect.Contains point then Some id else acc) None
