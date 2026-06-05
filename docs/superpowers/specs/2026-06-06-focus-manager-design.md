# Focus manager — design

**Date:** 2026-06-06 · **Phase:** v0.2 · **Status:** approved (design-panel synthesis), implementing Slice 1

## Problem

The framework does no focus routing. Input is a flat pipe — `InputParser.readEvent → program.MapInput (model-blind) → msg → update` — and the runtime retains no measured tree (`view` is built, rendered, and discarded each frame). So apps hand-roll focus in their own model: `Mire.FeedDemo` (`Pane`, `OverlayState`), `Mire.AgentDemo` (`FocusRegion`, `ButtonFocus`, an `Overlay` DU, plus an `McpView` Esc-stack). Each demo independently reinvents (a) a normalized-key DU, (b) an overlay field acting as a focus trap, (c) a per-region sub-`update` dispatcher, (d) a manual focus stack, (e) view-side highlighting. The v0.2 roadmap wants a real focus manager; `Widgets.Modal` defers its focus-trap half to it.

## Decision (from a 3-architecture design panel + adversarial judge)

**The model-aware routing decision must live in `update`** — `MapInput` can't see the model and the runtime holds no region table. This is the codebase's existing grain (both demos route in `update`). A runtime-owned router (a `Focusable` LayoutNode + retained rects + message routing) is the eventual answer for **mouse/spatial** focus, but it's large, risk-dense (frame-lag, two focus truths, the measure/render/contentExtent triple-edit), and premature for a keyboard-only v0.2 whose mouse events aren't even decoded. **Deferred to v0.5.**

**Ship a pure `Focus` value type in `Mire.Layout`**, keyed on the existing `Core.RegionId` (reviving the dead `Region.fs` scaffolding), routed entirely in `update`. Zero changes to the runtime loop, `MapInput`, `Cmd`/`Sub`, or the `LayoutNode` DU — so it cannot regress existing behavior and builds in isolation.

### Resolved forks

1. **Key type:** `RegionId` throughout (monomorphic, in Layout, shareable by widgets); demos define a private `module Ids` of named `RegionId` bindings so matches reference values, not raw strings.
2. **Tab handling:** convention — `update` handles `KTab`/`KShiftTab` as `Focus.next`/`prev` before per-region dispatch; an app puts its one Tab-consuming region (e.g. a completion popup) ahead of the global handler. No handled/unhandled propagation protocol.
3. **Modal coupling:** `Widgets.Modal.modal` stays a pure layout function + a doc note pointing at `Focus.pushTrap`/`popTrap`. Add `Modal.focusIds` only once a demo adopts `Widgets.Modal` for its trap.
4. **`Focusable` node / derive tab order from the tree:** **deferred** (costs the DU triple-edit for a sync problem the demos don't have).

## Slice 1 — `Mire/Layout/Focus.fs` (this change)

Pure immutable records + a total, non-throwing module. Depends only on `Core.RegionId`.

```fsharp
type FocusRing = { Order: RegionId list; Current: RegionId option }
type Focus = { Base: FocusRing; Traps: FocusRing list } // Traps innermost-first; head is the active ring

module Focus =
    val empty     : Focus
    val ofOrder   : RegionId list -> Focus          // Base ring, Current = tryHead
    val current   : Focus -> RegionId option        // active ring's Current
    val isFocused : RegionId -> Focus -> bool        // view-side highlight query
    val focus     : RegionId -> Focus -> Focus       // set active.Current iff id ∈ active.Order, else no-op
    val next      : Focus -> Focus                   // Tab: advance active.Current, wrap
    val prev      : Focus -> Focus                   // Shift+Tab: reverse wrap
    val pushTrap  : RegionId list -> Focus -> Focus  // open modal: cons a fresh active ring; Base + lower untouched
    val popTrap   : Focus -> Focus                   // close modal: discard head of Traps (no-op at depth 0)
    val isTrapped : Focus -> bool
```

**Invariants:** `current`/`next`/`prev`/`focus` operate on the _active_ ring (`List.tryHead Traps`, else `Base`), so the trap is automatic — while trapped, base ids are never yielded or reachable. `pushTrap` leaves `Base.Current` untouched, so `popTrap` restores exactly where focus was (the "remember where I was" the demos hand-code). `popTrap` is a no-op at depth 0 — the app can never lose all focus. All functions total: empty `Order` → `Current = None`; wrap via list length.

**App usage (4 call sites):** model field `Focus = Focus.ofOrder [Ids.list; Ids.reader]`; in `update`'s key arm `KTab → Focus.next` / `KShiftTab → Focus.prev`, then `match Focus.current m.Focus with | Some id when id = Ids.reader → updateReader …`; modal open/close pairs `Focus.pushTrap [Ids.accept; Ids.deny]` / `Focus.popTrap` with the overlay field; view highlight `if Focus.isFocused Ids.reader m.Focus then borderFocus else borderStyle`.

**Files:** new `Mire/Layout/Focus.fs`; `Mire/Mire.fsproj` `<Compile>` after `Layout/Layout.fs`; `Mire/Widgets/Widgets.fs` Modal doc note; `Mire.Tests/Tests.fs` Focus test list.

## Slice 2 — proof (next change)

Migrate **AgentDemo** first (the repo's only real trap: `ButtonFocus → pushTrap [accept; deny]`; the `Overlay`-DU sub-update fan-out → `match Focus.current`; `FocusRegion` Tab → 2-id base ring; `McpView` stack → nested push/pop), then **FeedDemo**. View-highlight branches flip to `Focus.isFocused`. Tick ROADMAP; update the `Region.fs` dead-scaffolding note (`RegionId` now load-bearing).

## Deferred (v0.5)

Runtime-retained region table, a `Focusable` LayoutNode + tree collector, `focusAt : Point -> (RegionId*Rect) list -> RegionId option` for mouse hit-testing → `Focus.focus`, and (optionally) moving the authoritative ring into the runtime. Slice 1's value type is a strict subset of that future and composes under it without rework.
