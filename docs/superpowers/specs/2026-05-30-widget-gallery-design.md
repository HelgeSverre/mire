# Mire Widget Gallery — design

## Goal

Replace `Mire.Demo`'s scrollable-list demo with a **widget gallery**: a
sidebar + detail-pane app that showcases every layout node and widget builder
that actually exists in the framework, with a live render and status/notes for
each. The gallery doubles as a manual exercise of the runtime (selection,
focus, scroll, text input) and as a verifiable layout surface via `--dump`.

Scope is **only what exists today** (the ✅/🟡 rows of `ROADMAP.md`'s node and
widget tables). No placeholders for unbuilt (⬜) widgets.

## Non-goals

- No new framework widgets. This is a demo app; it composes existing
  `Mire.Layout` nodes and `Mire.Widgets` builders only.
- No multi-file restructure. The demo stays a single `Mire.Demo/Program.fs`,
  matching the existing demo-project convention.
- No new Expecto tests unless a pure helper worth covering emerges. `--dump`
  is the verification surface, consistent with the repo's practice.

## Layout

```
Dock
├ top 3      panel "Mire — Widget Gallery"
├ bottom 3   status bar — context-sensitive key hints
└ fill       Dock[ left 20 = sidebar  |  fill = detail ]
```

- **Sidebar** (`left 20`): a `ListView` of catalog entries, each labelled
  `<status-glyph> <Name>` (✅/🟡). The selected row gets the full-width
  highlight `ListView` already provides. The sidebar's border is drawn in
  `Style.highlight` when the sidebar is focused and `Style.dim` when not.
- **Detail** (`fill`): a `panel` titled with the selected entry's name. Body is
  the live widget for that entry. A footer line shows `Status · Notes` from the
  catalog. The detail border mirrors the focus convention (highlight when
  focused, border/dim otherwise).

## Components

Three isolated pieces inside `Program.fs`, each with one job:

### `Catalog`

The single source of truth for what the gallery shows.

```fsharp
type Demo =
    | TextDemo | FilledDemo | BoxDemo | StatusBarDemo
    | StackDemo | DockDemo | ScrollDemo | OverlayDemo
    | ListViewDemo | InputDemo | StylesDemo

type Entry =
    { Demo: Demo
      Title: string
      Status: string   // "✅" | "🟡"
      Notes: string }

let entries : Entry list = [ ... ]   // one per Demo, order = sidebar order
```

Both the sidebar labels and the `--dump` walk iterate `Catalog.entries`, so the
two can never drift.

### `Detail.render`

```fsharp
val render : Model -> Demo -> LayoutNode<Msg>
```

Pure. One match arm per `Demo`. Returns the live widget, reading interactive
state from the `Model` where relevant. **This is the single render path shared
by the live `view` and `--dump`** — there is no second rendering of any
showcase.

Per-entry content:

| Demo            | Renders                                                                        | Interactive |
| --------------- | ------------------------------------------------------------------------------ | ----------- |
| `TextDemo`      | A few `Text.text`/`title`/`dimText` lines incl. a multi-line (`\n`) string.    | no          |
| `FilledDemo`    | `Backdrop.solid` swatches in several styles (opaque color blocks).             | no          |
| `BoxDemo`       | `Box.panel` with a title and nested children.                                  | no          |
| `StatusBarDemo` | `StatusBar.statusBar` with left/center/right groups.                           | no          |
| `StackDemo`     | An `hstack` of three cells and a `vstackOf` mixing `Cells`/`Fill`/`Content`.   | no          |
| `DockDemo`      | A small `Dock` with top/fill/bottom (and/or left/right) regions labelled.      | no          |
| `ScrollDemo`    | A `Scroll.vertical` over a tall `vstack`, driven by `Model.ScrollOffset`.      | yes         |
| `OverlayDemo`   | Background rows + `Backdrop.solid` + a "MODAL" box (the dump-D composition).   | no          |
| `ListViewDemo`  | `ListView.view` over sample labels, selection = `Model.ListSel`.               | yes         |
| `InputDemo`     | `Input.render` over `Model.Input` (block cursor, focused when detail-focused). | yes         |
| `StylesDemo`    | The predefined `Style` palette as labelled swatches (success/warning/danger/   | no          |
|                 | info/title/dim/key/counter/highlight).                                         |             |

### MVU wiring (`view` / `update` / `mapInput`)

Standard Elmish, matching the existing demo's style (no `Cmd`s needed beyond
`Cmd.none`).

## Model

```fsharp
type Focus = Sidebar | Detail

type Model =
    { Selected: int        // sidebar selection index into Catalog.entries
      Focus: Focus
      ScrollOffset: int    // ScrollDemo
      ListSel: int         // ListViewDemo selection
      Input: TextBuffer }  // InputDemo
```

Transient per-entry state (`ScrollOffset`, `ListSel`, `Input`) persists across
selection changes — switching away and back keeps where you were.

## Messages & update

```fsharp
type Msg =
    | SelectPrev | SelectNext     // sidebar navigation
    | ToggleFocus                 // Tab / Esc between Sidebar and Detail
    | DetailKey of KeyEvent       // routed to the focused detail widget
    | Ignore
```

- `SelectPrev`/`SelectNext`: clamp `Selected` to `[0, entries.Length-1]`.
- `ToggleFocus`: flips `Focus`. Moving to `Detail` is a no-op when the selected
  entry is non-interactive (stays on `Sidebar`).
- `DetailKey ke`: dispatch on the selected `Demo`:
  - `ScrollDemo`: `ArrowUp/Down` and `PageUp/Down` adjust `ScrollOffset`
    (clamped to content height).
  - `ListViewDemo`: `ArrowUp/Down` move `ListSel` (clamped).
  - `InputDemo`: printable char → `TextBuffer.insert`; `Backspace` → delete;
    `ArrowLeft/Right`/`Home`/`End` → cursor moves. (Use the existing
    `Mire.Core.TextBuffer` ops; no new edit logic.)
  - other demos: ignore.

### Input routing (`mapInput`)

- `Focus = Sidebar`: `ArrowUp`→`SelectPrev`, `ArrowDown`→`SelectNext`,
  `Tab`→`ToggleFocus`, else `Ignore`.
- `Focus = Detail`: `Tab`/`Esc`→`ToggleFocus`, every other key →
  `DetailKey ke`.
- `Ctrl+C` quit is handled by the runtime as today.

## Status bar

Context-sensitive hints reflecting current focus + entry:

- Sidebar focused: `↑/↓ select   Tab focus pane   Ctrl+C quit`.
- Detail focused, interactive entry: entry-specific hint (e.g. ScrollDemo:
  `↑/↓ scroll   Tab/Esc back`; InputDemo: `type to edit   ←/→ cursor   Tab/Esc back`).
- Non-interactive entry: `Tab does nothing here   ↑/↓ in sidebar`.

## `--dump`

Rewritten to iterate `Catalog.entries` and call `Detail.render` with a
representative `Model` (a fixed `ScrollOffset`/`ListSel`, a sample `Input`
buffer, `Focus = Detail` so the input cursor shows). Each entry is laid out at a
fixed `Size`, labelled with its title, and printed via the existing
`printSurface` helper. Because dump and the live view share `Detail.render`,
every showcase is verifiable headless and CLAUDE.md's layout-verification path
keeps working.

The existing standalone dump cases (A–D) are removed; their content is
subsumed by the corresponding catalog entries (`DockDemo`, `StackDemo`,
`ScrollDemo`, `OverlayDemo`).

## Testing

The render path is pure layout (`Layout.measure`/`render` onto a `Surface`), so
`--dump` is the verification surface. Build with `dotnet build`; eyeball layout
with `dotnet run --project Mire.Demo -- --dump`. No new Expecto tests unless a
pure helper worth covering is introduced.

## Risks / notes

- `Box` renders all children into the same inner rect (overlapping) — entries
  that need stacked children must nest an explicit `Stack` (per ROADMAP's `Box`
  note). The `BoxDemo`/`StackDemo` content must respect this.
- `Overlay` layers take the full area (no anchoring yet), so `OverlayDemo`
  reuses the known-good full-bleed backdrop composition rather than a centered
  modal.
- Sidebar width is fixed at 20 cells; long entry names are caller-truncated by
  `ListView` to the available width.

```

```
