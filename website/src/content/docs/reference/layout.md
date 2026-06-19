---
title: Layout nodes
description: The LayoutNode tree, Length sizing, and the Stack/Dock/Box/Scroll/Overlay/Positioned/Focusable nodes.
category: reference
order: 2
---

A `view` returns a `LayoutNode<'msg>` tree. `Layout.measure` assigns each node a `Rect`
within the available area; `Layout.render` paints it onto a `Surface`. You build nodes
with the `Mire.Widgets` helpers, not the raw cases.

## The node tree

```fsharp
type LayoutNode<'msg> =
    | Empty
    | Text       of Rect * string * Style
    | Filled     of Rect * Style                              // a solid rectangle
    | Box        of Rect * Style * LayoutNode<'msg> list      // a border around one child
    | Dock       of Rect * DockChild<'msg> list               // edges + fill
    | Stack      of Rect * Direction * StackChild<'msg> list  // row or column
    | Scroll     of Rect * ScrollState * LayoutNode<'msg>     // a clipped, offset viewport
    | Overlay    of Rect * LayoutNode<'msg> list              // z-stacked layers
    | Positioned of Rect * Placement * Length * Length * LayoutNode<'msg>
    | Focusable  of Rect * RegionId * LayoutNode<'msg>        // tag for mouse hit-testing
```

## Sizing — `Length`

```fsharp
type Length =
    | Cells of int        // an exact column/row count
    | Fraction of float   // a fraction of the available extent (0.0–1.0)
    | Content             // the child's intrinsic size (Layout.contentExtent)
    | Fill                // share the remainder among Fill siblings
```

`Content` queries `Layout.contentExtent`: a `Text` is as tall as its line count and as
wide as its longest line; a `Box` adds 2 for its border; a `Stack` sums or maxes its
children. Prefer `Cells`/`Fraction`/`Fill` for `Scroll`/`Dock`/`Overlay` children.

## Stacks

```fsharp
Stack.vstack [ a; b ]        // vertical, each child Content-sized
Stack.hstack [ a; b ]        // horizontal
Stack.vstackOf [ Stack.sized (Length.Cells 1) header
                 Stack.sized Length.Fill      body
                 Stack.sized (Length.Cells 1) footer ]
Stack.flex                   // a Fill-sized spacer; pushes neighbours apart / centers
```

## Dock

Pins children to edges, in list order, then fills the remainder:

```fsharp
Dock.dock
    [ Dock.top 3 header
      Dock.bottom 1 statusBar
      Dock.left 30 sidebar
      Dock.fill body ]
```

## Box

A single-line border around exactly one child (multiple children overlap — flow them
through a `Stack`):

```fsharp
Box.box Style.border [ Stack.vstack rows ]
Box.panel "title" Style.border [ child ]
```

## Scroll

Clips its child to the viewport and shows it at an offset; the child is laid out at full
content size off-screen, then the visible window is blitted in. The app owns the offset.
Most uses want the `ScrollView` widget, which adds a track/thumb scrollbar plus
`toBottom`/`clampOffset`/`atBottom` offset helpers (listed under "Not pictured" on the
[Widgets](/docs/reference/widgets/) page).

## Overlay and Positioned

`Overlay` z-stacks layers (later paints on top; a `Filled`/`Backdrop` layer occludes).
`Positioned` sizes a child and places it at one of nine `Placement` points:

```fsharp
LayoutNode.Overlay(Rect.Create(0, 0, 0, 0), [ baseTree; modal ])
```

Use the `Modal`/`Toast`/`Tooltip`/`Completion`/`Overlay` widgets rather than building
`Positioned` by hand.

## Focusable

Tags a subtree with a `RegionId` for mouse hit-testing — layout-neutral (the child fills
the rect). See [Mouse and focus](/docs/how-to/mouse-and-focus/).

```fsharp
Focusable.region (RegionId "ok") (Text.text " [ OK ] " theme.selection)
```

## Pure helpers

```fsharp
Layout.measure availableRect node        // assign rects
Layout.render surface measuredNode        // paint to a Surface
Layout.contentExtent Direction.Vertical node   // intrinsic size along an axis
Layout.collectRegions measuredNode        // (RegionId * Rect) list for hit-testing
Layout.regionAt regions point             // topmost region under a point
```

## Extending layout

A new node case goes in `Mire/Layout/Layout.fs`: add it to the DU and handle it in both
`measure` and `render` (and `contentExtent` if it can be `Content`-sized). Keep the
dependency direction one-way — the `<Compile>` order in `Mire/Mire.fsproj` enforces it.
