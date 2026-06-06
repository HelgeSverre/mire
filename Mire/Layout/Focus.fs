namespace Mire.Layout

open Mire.Core

/// An ordered set of focusable region ids with at most one current focus.
type FocusRing =
    { Order: RegionId list
      Current: RegionId option }

/// App-side keyboard focus, held as one field in the model. `Base` is the
/// always-present ring (e.g. panes); `Traps` is a stack of modal rings, innermost
/// first — its head, when non-empty, is the *active* ring. All navigation and
/// queries run against the active ring, so a modal trap is automatic: while a
/// trap is pushed the base ids are unreachable, and popping restores the base
/// ring exactly where focus left it.
type Focus =
    { Base: FocusRing
      Traps: FocusRing list }

/// Pure, total focus operations (no `'msg`, no `Cmd`, no I/O) — routed entirely
/// inside an app's `update`. See `docs/superpowers/specs/2026-06-06-focus-manager-design.md`.
module Focus =

    /// No focusables and no trap.
    let empty: Focus =
        { Base = { Order = []; Current = None }
          Traps = [] }

    /// A base ring over `ids` in tab order, focused on the first id (none if empty).
    let ofOrder (ids: RegionId list) : Focus =
        { Base =
            { Order = ids
              Current = List.tryHead ids }
          Traps = [] }

    /// The ring navigation/queries act on: the innermost trap if any, else the base.
    let private active (f: Focus) : FocusRing =
        match f.Traps with
        | r :: _ -> r
        | [] -> f.Base

    /// Replace the active ring with `g` applied to it (base when no trap is open).
    let private mapActive (g: FocusRing -> FocusRing) (f: Focus) : Focus =
        match f.Traps with
        | r :: rest -> { f with Traps = g r :: rest }
        | [] -> { f with Base = g f.Base }

    /// The currently focused id, or `None` when the active ring is empty.
    let current (f: Focus) : RegionId option = (active f).Current

    /// True when `id` is the active ring's current focus — the view-side query.
    let isFocused (id: RegionId) (f: Focus) : bool = current f = Some id

    /// Focus `id` if it's in the active ring; otherwise leave focus unchanged.
    let focus (id: RegionId) : Focus -> Focus =
        mapActive (fun r ->
            if List.contains id r.Order then
                { r with Current = Some id }
            else
                r)

    /// Step the active ring's current by `delta`, wrapping; no-op when <2 ids.
    let private step (delta: int) (r: FocusRing) : FocusRing =
        match r.Order with
        | []
        | [ _ ] -> r
        | ids ->
            let n = List.length ids

            let i =
                match r.Current with
                | Some cur -> List.tryFindIndex ((=) cur) ids |> Option.defaultValue 0
                | None -> 0

            let j = ((i + delta) % n + n) % n

            { r with
                Current = Some(List.item j ids) }

    /// Advance focus within the active ring, wrapping at the end (conventionally Tab).
    let next: Focus -> Focus = mapActive (step 1)

    /// Retreat focus within the active ring, wrapping at the start (conventionally Shift+Tab).
    let prev: Focus -> Focus = mapActive (step -1)

    /// Open a modal trap: push a fresh active ring over `ids` (focused on the
    /// first), leaving the base and any lower traps untouched.
    let pushTrap (ids: RegionId list) (f: Focus) : Focus =
        { f with
            Traps =
                { Order = ids
                  Current = List.tryHead ids }
                :: f.Traps }

    /// Close the innermost modal trap, restoring the ring beneath it exactly where
    /// its focus was left. A no-op at depth 0, so focus can never be lost entirely.
    let popTrap (f: Focus) : Focus =
        match f.Traps with
        | _ :: rest -> { f with Traps = rest }
        | [] -> f

    /// True while at least one modal trap is open.
    let isTrapped (f: Focus) : bool = not (List.isEmpty f.Traps)
