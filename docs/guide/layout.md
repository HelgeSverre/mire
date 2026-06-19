# Layout

A `view` returns a `LayoutNode<'msg>` tree. `Layout.measure` assigns every node a
`Rect` within the available area, and `Layout.render` paints it onto a `Surface`. You
rarely build `LayoutNode` cases by hand — the `Mire.Widgets` helpers (`Stack`, `Dock`,
`Box`, …) construct them for you.

## The node tree

```fsharp
type LayoutNode<'msg> =
    | Empty
    | Text       of Rect * string * Style
    | Filled     of Rect * Style                              // a solid rectangle
    | Scrim      of Rect * Color * float                      // translucent scrim — fades what's beneath
    | Box        of Rect * Style * LayoutNode<'msg> list      // a border around one child
    | Dock        of Rect * DockChild<'msg> list              // edges + fill
    | Stack       of Rect * Direction * StackChild<'msg> list // row or column
    | Scroll      of Rect * ScrollState * LayoutNode<'msg>    // a clipped, offset viewport
    | Overlay     of Rect * LayoutNode<'msg> list             // z-stacked layers
    | Positioned  of Rect * Placement * Length * Length * LayoutNode<'msg>
    | Focusable   of Rect * RegionId * LayoutNode<'msg>       // tag a subtree for mouse hit-testing
```

The `Rect`s are placeholders until `measure` fills them in. Build with the widget
helpers; reach for the raw cases only when extending the framework.

## Sizing — `Length`

Children in a `Stack`, `Dock`, or `Positioned` are sized along an axis by a `Length`:

```fsharp
type Length =
    | Cells of int        // an exact number of columns/rows
    | Fraction of float   // a fraction of the available extent (0.0–1.0)
    | Content             // the child's intrinsic size (see contentExtent)
    | Fill                // share whatever's left, equally among Fill siblings
```

`Content` asks the child how big it wants to be (`Layout.contentExtent`): a `Text`
node is as tall as its line count and as wide as its longest line; a `Box` adds 2 for
its border; a `Stack` sums or maxes its children. Prefer `Cells`/`Fraction`/`Fill` for
`Scroll`/`Dock`/`Overlay` children, which have no well-defined intrinsic size.

## Stacks — rows and columns

`Stack` lays children along one axis. The `Mire.Widgets.Stack` helpers:

```fsharp
open Mire.Widgets

// Every child takes its intrinsic (Content) size:
Stack.vstack [ Text.title "Title"; Text.text "body" Style.text ]   // vertical
Stack.hstack [ a; b; c ]                                            // horizontal

// Explicit per-child lengths:
Stack.vstackOf
    [ Stack.sized (Length.Cells 1) header
      Stack.sized Length.Fill      body       // body absorbs the slack
      Stack.sized (Length.Cells 1) footer ]

// A flexible spacer pushes its neighbours apart (a Fill-sized empty child):
Stack.hstackOf [ Stack.sized Length.Content left; Stack.flex; Stack.sized Length.Content right ]
```

`Stack.flex` is the idiom for "left-aligned thing / right-aligned thing" rows and for
centering (`[ flex; sized Content x; flex ]`). A bare `Spacer.spacer` collapses to
nothing in a `vstack`/`hstack` — use `Stack.flex` (alias of `Spacer.flexSpacer`) to
take up space.

## Dock — edges then fill

`Dock` pins children to edges and lets one fill the remainder — the classic app frame:

```fsharp
Dock.dock
    [ Dock.top 3 header        // 3 rows at the top
      Dock.bottom 1 statusBar  // 1 row at the bottom
      Dock.left 30 sidebar     // 30 columns on the left
      Dock.fill body ]         // everything left over
```

Children are placed in list order, each consuming from the remaining rectangle, so
order matters (top/bottom before left/right gives full-width bars).

## Box and Panel

`Box` draws a single-line border around exactly one child (multiple children would
overlap — flow them through a `Stack`). `Box.panel` adds a title row:

```fsharp
Box.box Style.border [ Stack.vstack rows ]
Box.panel "settings" Style.border [ Text.text "a line" Style.text ]
```

## Scroll and ScrollView

`Scroll` clips its child to the viewport and shows it at an offset. The child is laid
out at its full content size on an off-screen surface, then the visible window is
blitted in. The app owns the offset (it's MVU state).

For most cases use the `ScrollView` widget, which adds a track/thumb scrollbar and
clamping helpers — see [Widgets](widgets.md#scrolling).

## Overlay and Positioned — layering

`Overlay` z-stacks layers in list order (later paints on top); a `Filled`/`Backdrop.solid`
layer occludes what's beneath it, while a `Scrim` (`Backdrop.scrim tint strength`) _fades_
it — blending the cells underneath toward a tint, the way `Modal.modal` dims the screen
behind a still-opaque dialog. `Positioned` sizes a child and places it at one of nine
`Placement` points within the area — the basis for modals, toasts, and popups:

```fsharp
LayoutNode.Overlay(Rect.Create(0, 0, 0, 0),
    [ baseTree                                   // the app
      Modal.modal Style.Default border title 40 10 "Confirm" body ])  // floats centered on top
```

You'll usually reach for the `Overlay`, `Modal`, `Toast`, `Tooltip`, and `Completion`
widgets rather than building `Positioned` by hand — see [Widgets](widgets.md#overlays).

## Focusable — mouse hit-testing

Wrap a subtree in `Focusable.region` to tag it with a `RegionId`. It's layout-neutral
(the child fills the assigned rect); it only records the rect so the runtime can
hit-test mouse clicks against it. See [Input](input.md#mouse-hit-testing).

```fsharp
Focusable.region (RegionId "ok-button") (Text.text " [ OK ] " theme.selection)
```

## Extending layout

A new node case goes in `Mire/Layout/Layout.fs`: add it to the `LayoutNode<'msg>` DU
and handle it in **both** `measure` (assign rects) and `render` (paint), plus
`contentExtent` if it can be `Content`-sized. The widget layer then wraps it in a
friendly constructor. Keep the dependency direction one-way (Core → Renderer → Layout
→ Widgets) — the `<Compile>` order in `Mire/Mire.fsproj` enforces it.
