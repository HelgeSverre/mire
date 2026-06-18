---
title: Show overlays and modals
description: Layer a modal, a toast, a completion popup, or a tooltip over your base view with Overlay and the overlay widgets.
category: how-to
order: 3
---

Overlays are layers stacked over your base tree. You compose them with
`LayoutNode.Overlay` (list order = z-order, later paints on top) and the overlay
widgets, which place themselves for you.

## A centered modal

`Modal.modal` draws a centered box with its own dimming backdrop. Layer it over the
base view, gated on your model:

```fsharp
let view m =
    let baseTree = (* your normal layout *)

    match m.Confirm with
    | None -> baseTree
    | Some prompt ->
        LayoutNode.Overlay(
            Rect.Create(0, 0, 0, 0),
            [ baseTree
              Modal.modal Style.Default theme.border theme.title 44 9 "Confirm"
                  (Stack.vstack
                      [ Text.text prompt theme.fg
                        Text.text "" theme.fg
                        Text.text "Enter: confirm   Esc: cancel" theme.fgSubtle ]) ])
```

The modal swallows nothing on its own — your `update` decides what Enter/Esc do while
it's open. To make its buttons clickable, see [Mouse and focus](/docs/how-to/mouse-and-focus/).

## A toast

Auto-dismissing notifications, placed top-right over the base tree. The app owns the
list and expires entries with a `Sub` timer:

```fsharp
// model holds a toast list; a Sub.Every tick decrements each TTL and drops expired ones
let subscriptions _ = [ Sub.Every(TimeSpan.FromMilliseconds 200.0, fun () -> TickToasts) ]
```

## A completion popup

`Completion.view` is a cursor-anchored, bordered, selectable list that clamps itself
on-screen (flips above the anchor when low on space). You supply the anchor point, the
filtered candidates, and the selected index:

```fsharp
Completion.view
    areaW areaH        // the screen size
    anchorX anchorY    // where the caret is
    28                 // popup width
    6                  // max rows
    theme.border theme.selection theme.fg
    selectedIndex
    candidates
```

For a `Ctrl+P`-style command surface, use `CommandPalette.view` (a modal + list + a
ranked fuzzy filter). `CommandPalette.filter query candidates` is the reusable ranker
if you want to drive your own list.

## A tooltip

`Tooltip.view` is an anchored doc popup that flips above the anchor when space is tight.
Wrap your lines to `width - 2` first.

## Placing your own layer

The lower-level primitives, if a widget doesn't fit:

```fsharp
Overlay.centered width height child            // dead center
Overlay.positioned placement width height child // a 9-point Placement
Overlay.atPoint x y width height areaW areaH child // anchored at a point, clamped on-screen
```

See the [layout reference](/docs/reference/layout/#overlay-and-positioned) for the full
list and the [widget catalog](/docs/reference/widgets/#overlays) for every overlay widget.
