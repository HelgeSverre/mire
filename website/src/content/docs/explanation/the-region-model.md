---
title: The region model
description: How RegionId ties together keyboard focus and mouse hit-testing — the two halves of "what's active."
category: explanation
order: 3
---

Two questions every interactive UI must answer: *where does a click land?* and *where
does a keystroke go?* In a browser the platform answers both. Mire answers them with one
small idea — a `RegionId` — split across a spatial half and a keyboard half.

## The shared name

A `RegionId` is just a string-tagged identity: `RegionId "prompt"`, `RegionId "accept"`.
It names a piece of your UI without saying where it is or whether it has focus. Both
halves of focus key off the same name, so a click and a Tab can converge on the same
target.

## The spatial half — mouse hit-testing

When you wrap a subtree with `Focusable.region id child`, you are not changing the layout
— the child still fills its assigned rect. You are *labeling* that rectangle. After each
frame is laid out, the runtime walks the measured tree and collects a table:
`(RegionId, Rect)` for every `Focusable` node (`Layout.collectRegions`). This table is
the screen's current map of named, clickable areas.

On a mouse event, `Layout.regionAt` finds the topmost region containing the cursor
(later-painted regions win, matching what's visually on top), and hands it to your
`withMouseRegion` handler. You react to an *id*, not a coordinate — so when the layout
changes, the hit-testing follows automatically. No geometry mirrored by hand.

One deliberate gap: regions nested inside a `Scroll` are excluded. Their measured rects
live in the scroll's off-screen content space, not screen space, so comparing them to a
screen-space click would be wrong. Scrolled content uses the wheel; fixed chrome uses
regions.

## The keyboard half — the focus ring

`Mire.Layout.Focus` is the other half: a pure, ordered ring of `RegionId`s with at most
one current focus, plus a stack of modal *traps*. It is entirely MVU — one field in your
model, moved by `Focus.next`/`prev`/`focus` in `update`, queried by `Focus.isFocused` in
`view`. No I/O, no global state.

The trap stack is what makes modals behave: `pushTrap [ok; cancel]` makes Tab cycle only
those two ids while the modal is open; `popTrap` restores the base ring exactly where
focus left off. Because it is a value, focus is trivially testable and survives
serialization.

## Why they're separate

Spatial and keyboard focus answer different questions and change at different times — a
click moves the mouse target instantly; Tab walks an order you defined. Keeping them as
two pure pieces (a region table derived from layout, a ring held in the model) means
neither needs to know about the other's mechanism. They only need to agree on names. Use
the same `RegionId` for a region and its ring entry, and a click that focuses a pane and
a Tab that focuses it are the same outcome by construction.

## Where this is going

The region table today is a light `(RegionId * Rect)` list — enough for clicking panes
and modal buttons. The `Core/Region.fs` record (with z-index, clip, and render-mode
fields) is a forward declaration for a fuller runtime-owned focus and z-ordering model.
The shared-name idea stays the same; the table just gets richer.

## Related

- [Make UI clickable and focusable](/docs/how-to/mouse-and-focus/) — the recipe.
- [The loop](/docs/explanation/the-loop/) — where the region table is built each frame.
