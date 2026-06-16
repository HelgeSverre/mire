# Roadmap archive — completed phases

Full checklists for the shipped phases, moved out of [`ROADMAP.md`](../ROADMAP.md)
to keep the plan of record short. Everything here is **done and verified**; the
living widget/node status tables stay in `ROADMAP.md`, and `CHANGELOG.md` has the
chronological view.

---

## v0.1 — Terminal runtime foundation ✅

The whole pipeline runs end-to-end: `model → view → layout → surface → diff → terminal`.

- [x] `Mire.Core` value types (`Point`/`Size`/`Rect`/`Color`/`Style`/`Cell`/`Region`/`Grapheme`/input events)
- [x] Raw terminal mode (`stty` + libc `poll`/`read` P/Invoke), alternate screen, resize handling (resize re-renders but doesn't force a full repaint — see Known gaps)
- [x] Byte-level `InputParser` — printable chars, Ctrl chords, arrows, function keys, Home/End/PgUp/PgDn/Ins/Del
- [x] `Surface` cell grid + draw primitives; run-based `Diff` writer
- [x] Elmish `Runtime.run` (~30 FPS) with `Cmd`/`Sub`, `Program` builders, `OnError`
- [x] `Mire.Widgets` convenience layer + predefined semantic styles
- [x] Grapheme-cluster width handling (wide chars, combining marks) — BMP per-`char` width + combining-mark merge; not true cluster handling (see Known gaps)

## v0.2 — Layout, regions & overlays ✅

- [x] **Layout engine complete** — real `Stack` flow, `Scroll` offset+clipping, `Content`/`Fill` dock lengths, `Filled` opaque node
- [x] Headless `--dump` verification mode
- [x] **Input decoding** — **mouse (SGR 1006), bracketed paste, and focus events now decode**, alongside the Kitty **`CSI u`** modifier chords + legacy fallbacks (`Ctrl+letter`, `Shift+Tab`, F-keys, arrows in both normal `ESC [ A` and application-cursor `ESC O A` modes). New: `ESC [ < b ; x ; y M|m` → `Mouse` (0-based coords, wheel, Shift/Alt/Ctrl); `ESC [ 200 ~ … ESC [ 201 ~` → `Paste` (now enabled in `Runtime.run`); `ESC [ I` / `ESC [ O` → `FocusGained` / `FocusLost`. _Remaining:_ Kitty **release/repeat** event types (tracked under v0.5); large pastes split across reads aren't buffered (each chunk is its own `Paste`).
- [x] **Runtime: quit-from-update** — `Cmd.quit` (a `Quit` command the runtime folds into `Running = false` after the message pump, then exits through the normal teardown) lets `update` exit cleanly without the Ctrl+C intercept
- [x] **Focus manager** — `Mire.Layout.Focus`, a pure `RegionId`-keyed focus ring + modal trap stack (`ofOrder`/`next`/`prev`/`focus`/`pushTrap`/`popTrap`/`isFocused`), routed inside `update`. `Mire.Demo.Feed` is migrated to it as the proof: the two panes are a base ring, each overlay is a nested trap ring, routing is `match Focus.current`, markers are `Focus.isFocused`. _Deferred to v0.5:_ the runtime-owned + mouse-hit-testing variant (a `Focusable` node + a retained region table)
- [x] **Overlay positioning** — `Positioned` layout node: 9-point placement (center + 4 corners + 4 edge-centers) within the assigned rect, child sized via `Length`. `Widgets.Overlay.atPoint` (cursor-anchored clamped popups) shipped in v0.3 alongside `Completion` and `Tooltip`. _Deferred to v0.5:_ node-level region anchoring (retained rects for hit-testing).
- [x] **`Modal`** widget — `Widgets.Modal.modal` (centered box + opaque backdrop + title + body slot, on `Positioned`; dogfooded by FeedDemo's add/filter modals). The keyboard focus-trap is the app pairing `Focus.pushTrap`/`popTrap` with it — proven by `Mire.Demo.Feed`. _Deferred nicety:_ a focus-aware `Modal.withFocus` returning the trap ids
- [x] **`Toast`** stack — `Widgets.Toast.stack` places a column of cards at a `Placement` (top-right) on `Positioned`; `Toast.card` for a default card. Auto-dismiss stays app-side via a `Sub` timer (the app owns the toast list). Dogfooded by `Mire.Demo.Agent`.
- [x] **`ScrollView`** widget — `Widgets.ScrollView.vertical` wraps `Scroll` with a track/thumb scrollbar; pure `clampOffset`/`toBottom`/`atBottom` helpers make follow-tail, jump-to-bottom, and paging one-liners in `update`. Dogfooded by `Mire.Demo.Feed`'s reader. _Not yet:_ virtualization (the `Scroll` blit still measures the whole child)
- [x] **`Spacer`** — `Spacer.flexSpacer` / `Stack.flex` (a `Fill`-length `StackChild`) absorb a stack's slack to push siblings apart; `Spacer.spacer` stays the zero-extent placeholder for explicit-length slots
- [x] Fix `Box` multi-child layout — kept `Box` a single-child container (one child fills the inner rect) and fixed the `panel`/`statusBar` helpers to flow their children through an explicit `Stack` (vertical / horizontal) instead of overlapping

## v0.3 — Core widgets ✅

- [x] `Separator` (`horizontal`/`vertical` rule), `Badge` (toned pill), `KeyHint` (key+label chip)
- [x] `Input` (single-line) — `Mire.Core.TextBuffer` + `Widgets.Input.render` (block cursor, scroll-to-cursor) ship; used by `Mire.Demo.Spreadsheet`. _No selection yet._
- [x] `TextArea` (multi-line) — `Widgets.TextArea.render` over a `\n`-aware `TextBuffer` (word/line/up-down ops), plus `Mire.Core.TextEdit`: a reusable, **overridable** key→`EditAction` keymap (`defaultKeymap`/`applyInput`) so apps wire bindings instead of inheriting hardcoded ones — word-delete/move chords and paste included. Quit became app-owned too (`Program.withQuitOn`, default Ctrl+C). Dogfooded by `Mire.Demo.Agent`'s prompt. _No selection/soft-wrap yet._
- [x] `List` — `ListView.view` (single-select) + `viewWith` (predicate → multi-select), both **virtualized** (only the visible window is built) + auto-scroll-to-selection. Key handling stays app-side (MVU).
- [x] `Table` — `Widgets.Table.view`: sticky header, `Length`-width columns, per-row cell renderers (`Column.Render` + `textColumn`), windowed/virtualized rows, predicate selection (single **or** multi). Column resize is app-state.
- [x] `CommandPalette` — `Widgets.CommandPalette`: `matches` + ranked `filter` (best-first fuzzy) + a `view` (centered `Modal` with a `❯ query` line over a selectable `ListView`); app pairs open/close with `Focus.pushTrap`/`popTrap`.
- [x] `Completion` — `Widgets.Completion.view`: a cursor-anchored bordered, selectable list (on `Overlay.atPoint`) that flips above the caret when low on space; the app filters with `CommandPalette.matches`/`filter`.

## Project infrastructure — completed items ✅

- [x] **Framework consolidated** into a single `Mire` project (folders = layers); solution is `Mire` + six `Mire.Demo.*` exes + `Mire.Tests` in `Mire.slnx`
- [x] **Test project** — `Mire.Tests` (Expecto) covering `Layout.measure`/`render`, `Diff.compute` (incl. sync-output bracketing + display-width cursor advance), `InputParser`, `Grapheme` width, `TextBuffer`/`Input`, `Focus`, `ScrollView`, mouse/paste/focus decoding, the widget layer (TextArea/TextEdit, SplitView, Tooltip, Spinner, ProgressBar, Tabs, Toggle, Markdown) + full-grid golden-frame snapshots, and the `Mire.Demo.Feed` `Feed` helpers (all green; `dotnet build Mire.slnx` is warning-clean)
- [x] **Dev tooling** — `justfile` (build/test/run/format/lint) + Fantomas tool manifest (`.config/dotnet-tools.json`); `just check` = lint + build + test
- [x] Promote `--dump` scenarios into golden-frame snapshot tests — the `GoldenFrame` suite asserts full cell grids (a Box + a multi-widget dashboard), determinism, and the row-width invariant; the `Mire.Demo.*` `--dump` modes are the broader source
- [x] Under version control; all work happens on `main`
- [x] **NuGet packaging** — `Mire/Mire.fsproj` carries package metadata (id `Mire`, MIT, bundled README, repo URL, doc XML); `just pack` / `just publish` produce and push the single `net10.0` package. Published to nuget.org as `0.0.1`/`0.0.2`
- [x] **Trusted publishing** — `.github/workflows/publish.yml` runs the tests then packs + pushes to nuget.org via OIDC (no stored API key) on a published `v*` GitHub Release
- [x] CI build + `dotnet test` on .NET 10 (on every push/PR)
- [x] Synchronized output (`?2026h`) around frame writes — `Diff.renderToTerminal` brackets every frame in begin/end-sync (BSU/ESU); covered by a test
