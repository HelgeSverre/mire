# Overlay positioning — design

**Date:** 2026-06-03 · **Phase:** v0.2 (Layout, regions & overlays) · **Status:** approved, pre-implementation

## Problem

`Overlay of Rect * LayoutNode<'msg> list` measures every layer against the full
area and renders them in z-order (`Filled` occludes). There is no way to _place_
a layer: a centered modal or a top-right toast can't be expressed. Today the
demos hand-roll centering with a private `centered w h size node` helper built
from `Dock` spacers — duplicated in `Mire.FeedDemo` and `Mire.SpreadsheetDemo`.

This is the "missing half of `Overlay`" called out in `ROADMAP.md` (v0.2) and the
prerequisite for `Modal` and `Toast`.

## Decisions

1. **Composable `Positioned` node**, not enriched `Overlay` layers and not a
   pure-`Dock` widget helper. It's additive (existing `Overlay(rect,[…])` keeps
   working), reusable anywhere (corner badge, not just overlays), and extends
   naturally to cursor/region anchors later.
2. **Rect-relative 9-point placement** for this pass. Anchoring to an arbitrary
   point/rect (cursor-relative completion, tooltip-on-a-word) is deferred until
   `Completion`/`Tooltip` (v0.3) need it.
3. **Deliverable:** the node + a `Widgets.Overlay` helper + a `Modal` widget
   (the _layout half_) + tests, dogfooded by rewiring the FeedDemo and
   SpreadsheetDemo modals/`centered` helpers.

## The node — `Mire/Layout/Layout.fs`

```fsharp
type Placement =
    | Center
    | TopLeft    | TopCenter    | TopRight
    | CenterLeft |              | CenterRight
    | BottomLeft | BottomCenter | BottomRight

// new LayoutNode<'msg> case
| Positioned of Rect * Placement * Length * Length * LayoutNode<'msg>
//              available  placement  width   height   child
```

`width`/`height` reuse the existing `Length` (`Cells`/`Fraction`/`Content`/`Fill`).
The size-resolution logic already in `Dock`'s `extentOf` (the `Cells`/`Fraction`/
`Content`/`Fill` → cells mapping) is factored into a small shared helper so
`Dock` and `Positioned` resolve lengths identically.

### `measure`

1. Resolve `w` from `width` against `available.Width`, `h` from `height` against
   `available.Height` — each clamped to `0 .. available extent`.
2. Map `Placement` to alignments `(ax, ay)` each ∈ `{ Start; Center; End }`:

   ```
   x = Start  → available.Left
       Center → available.Left  + (available.Width  - w) / 2
       End    → available.Right - w + 1            (clamped ≥ available.Left)
   y = Start  → available.Top
       Center → available.Top   + (available.Height - h) / 2
       End    → available.Bottom - h + 1           (clamped ≥ available.Top)
   ```

3. `measure childRect child`; return `Positioned(available, placement, width, height, measuredChild)`.

Worked example — available `20×10`, child `Cells 6 × Cells 4`:
`Center → (7,3,6,4)`, `TopRight → (14,0,6,4)`, `BottomCenter → (7,6,6,4)`,
`CenterLeft → (0,3,6,4)`.

### `render` / `contentExtent`

- `render`: `| Positioned(_,_,_,_,child) -> render surface child` — the child
  already carries its computed rect (same shape as `Overlay`/`Stack`).
- `contentExtent dir`: returns `contentExtent dir child`. Positioning has no
  well-defined intrinsic size (same caveat documented for `Dock`/`Overlay`);
  prefer `Cells`/`Fraction`/`Fill` if a `Positioned` is ever nested in a stack.

## Widgets — `Mire/Widgets/Widgets.fs`

New `Overlay` module (helper layer over the node; distinct from the
`LayoutNode.Overlay` case):

```fsharp
module Overlay =
    let positioned placement width height child =
        LayoutNode.Positioned(rect0, placement, width, height, child)

    let centered w h child = positioned Center (Cells w) (Cells h) child
```

New `Modal` module — the **layout half** of the roadmap's Modal:

```fsharp
module Modal =
    /// Opaque backdrop + a centered w×h bordered box: a title row above a body slot.
    /// Returns an Overlay layer-set to drop over a base tree via Overlay/Filled.
    let modal
        (backdropStyle: Style) (borderStyle: Style) (titleStyle: Style)
        (w: int) (h: int) (title: string) (body: LayoutNode<'msg>) : LayoutNode<'msg>
```

Expands to `LayoutNode.Overlay(rect0, [ Backdrop.solid backdropStyle;
Overlay.centered w h (Box.box borderStyle [titled-stack of title row + body]) ])`.
Positional signature matches the house style (`ListView.view`, `Input.render`).

**Not included:** focus-trapping. That needs the focus manager (separate v0.2
item). `Modal` ships centering + opaque backdrop + title + a body/actions slot
the app fills; the ROADMAP `Modal` row is annotated to reflect the partial ship.

## Dogfood

- Replace the private `centered` (Dock-spacer) helper in **FeedDemo** and
  **SpreadsheetDemo** with `Overlay.centered`; the `size` parameter disappears
  (the node uses the rect `measure` assigns).
- Rewire FeedDemo's **add-feed** and **filter** modals through `Modal` /
  `Overlay.centered`.
- **AgentDemo** permission modal: deferred (large file; own follow-up).

## Tests — `Mire.Tests/Tests.fs`

New `Positioned` test list:

- child rect for `Center`, all four corners, all four edge-centers against a
  known available rect with `Cells` sizes (assert exact `(x,y,w,h)`);
- `Content` width/height from a `Text` child resolves to its `contentExtent`;
- oversized child (`Cells` larger than available) clamps without negative origin;
- a render test: a centered `Filled` lands in the middle (a cell inside the
  placed rect carries the fill; a corner cell does not).

## Out of scope (follow-ups)

- Cursor/point and sub-rect anchoring (for `Completion`/`Tooltip`, v0.3).
- Focus trap / actions wiring in `Modal` (arrives with the focus manager).
- Per-placement offsets/margins (handle insets in the child for now).
- AgentDemo permission-modal rewire.

## Touchpoints

`Mire/Layout/Layout.fs` · `Mire/Widgets/Widgets.fs` · `Mire.Tests/Tests.fs` ·
`Mire.FeedDemo/Program.fs` · `Mire.SpreadsheetDemo/Program.fs` ·
`ROADMAP.md` + `CHANGELOG.md`.
