---
title: Make UI clickable and focusable
description: Route mouse clicks to UI by id with Focusable regions, and manage keyboard focus with the Focus ring.
category: how-to
order: 4
---

Mire gives you two halves of focus: **spatial** (the mouse, via a retained region table)
and **keyboard** (a tab-order ring). They pair naturally with the same `RegionId`s.

## Click a region by id

Instead of computing hit rectangles by hand, tag the clickable subtrees in your `view`
with `Focusable.region`, then install a handler that receives the topmost region under
the cursor.

1. Tag the regions:

```fsharp
Focusable.region (RegionId "accept") (Text.text " [ Accept ] " theme.selection)
Focusable.region (RegionId "deny")   (Text.text " ‹ Deny › "  theme.fgMuted)
```

2. Install `withMouseRegion`. It gets the hit `RegionId` (or `None`) and the mouse
   event; returning `Some msg` consumes the event (it does _not_ also reach `MapInput`):

```fsharp
let onMouseRegion (region: RegionId option) (me: MouseEvent) : Msg option =
    if me.Pressed && me.Button = MouseButton.Left then
        match region with
        | Some r when r = RegionId "accept" -> Some Accept
        | Some r when r = RegionId "deny"   -> Some Deny
        | _ -> None
    else None

Program.create init update view
|> Program.withMapInput mapInput          // keys, wheel
|> Program.withMouseRegion onMouseRegion  // clicks on tagged regions
|> Runtime.run
```

The default handler returns `None`, so apps that don't use it are unaffected — mouse
events flow to `MapInput` as before. `Focusable.region` is layout-neutral: the child
fills the assigned rect, and the runtime records that rect from the last rendered frame.

<aside class="callout callout--warn"><div class="callout__label">heads up</div><div>
Regions nested inside a <code>Scroll</code> are omitted from the table — their rects live
in the scroll's virtual content space, not screen space. Handle wheel scrolling through the
<code>Mouse</code> event in <code>MapInput</code> instead.
</div></aside>

The agent layer's `ApprovalModal` already tags its buttons (`ApprovalModal.acceptRegion`
/ `denyRegion`), so wiring its clicks is just matching those ids.

## Manage keyboard focus

`Mire.Layout.Focus` is a pure tab-order ring with a modal-trap stack. Hold it as one
field in your model and drive it from `update`:

```fsharp
type Model = { Focus: Focus; (* … *) }

let init () =
    { Focus = Focus.ofOrder [ RegionId "prompt"; RegionId "transcript" ] }, Cmd.none

let update msg m =
    match msg with
    | NextField -> { m with Focus = Focus.next m.Focus }, Cmd.none
    | PrevField -> { m with Focus = Focus.prev m.Focus }, Cmd.none
    | _ -> m, Cmd.none

// in view, branch on focus to draw the focused border etc.
let promptBorder = if Focus.isFocused (RegionId "prompt") m.Focus then theme.borderFocus else theme.border
```

Use the **same `RegionId`s** for the focus ring and the `Focusable` regions, so a click
and a Tab converge on one identity.

### Modal traps

Push a trap ring while a modal is open so Tab can't escape it; pop it to restore the base
ring exactly where focus left off:

```fsharp
| OpenModal  -> { m with Focus = Focus.pushTrap [ RegionId "ok"; RegionId "cancel" ] m.Focus }, Cmd.none
| CloseModal -> { m with Focus = Focus.popTrap m.Focus }, Cmd.none
```

See [the region model](/docs/explanation/the-region-model/) for how the two halves fit
together.
