# Mire Roadmap

The plan of record for what gets built and in what order. Synthesized from
[`SPEC.md`](SPEC.md)'s "Minimal viable version" cuts (v0.1–v0.5) and the verified
state of the code.

**Legend:** ✅ done · 🟡 partial / has known gaps · ⬜ not started

> The code is the source of truth for _what exists_; `SPEC.md` is the source of
> truth for _intended direction_. This file is the bridge — keep the checkboxes
> honest. When you finish something, tick it here and add a CHANGELOG entry.

---

## Widget & node reference

The catalog of every renderable thing, with status. Layout nodes are the
primitives in `Mire.Layout`; widgets are convenience builders in `Mire.Widgets`;
agent widgets are the (not-yet-created) `Mire.Agent` layer.

### Layout nodes — `Mire.Layout` (`LayoutNode<'msg>`)

| Node      | Status | Notes                                                                                                                                                                   |
| --------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Empty`   | ✅     | No-op.                                                                                                                                                                  |
| `Text`    | ✅     | Multi-line via `\n`; grapheme-width aware; clipped to rect.                                                                                                             |
| `Filled`  | ✅     | Opaque rectangle (style-filled). Backdrop / highlight / modal backing.                                                                                                  |
| `Box`     | 🟡     | Border + children. Children all share the inner rect (multi-child overlaps — nest a `Stack`).                                                                           |
| `Dock`    | ✅     | `Cells`/`Fraction`/`Content`/`Fill` on `Top`/`Bottom`/`Left`/`Right`/`Fill`.                                                                                            |
| `Stack`   | ✅     | Vertical/horizontal flow; per-child `Cells`/`Fraction`/`Content`/`Fill`.                                                                                                |
| `Scroll`  | 🟡     | Offset + viewport clipping via off-screen blit. No scrollbar / follow-tail / virtualization yet (→ `ScrollView` widget).                                                |
| `Overlay` | 🟡     | Z-orders (list order) and `Filled` occludes; the sibling `Positioned` node now sizes + places a layer (9-point) within the area. Cursor/region anchoring still pending. |

### Base widgets — `Mire.Widgets`

| Widget                            | Status | Notes                                                                                                                                                                                                                            |
| --------------------------------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Text.text` / `title` / `dimText` | ✅     | Styled text nodes.                                                                                                                                                                                                               |
| `Box.box` / `panel`               | ✅     | Bordered container; `panel` adds a title.                                                                                                                                                                                        |
| `StatusBar.statusBar`             | ✅     | Left/center/right item groups.                                                                                                                                                                                                   |
| `Dock.*` helpers                  | ✅     | `top`/`bottom`/`left`/`right`/`fill`.                                                                                                                                                                                            |
| `Stack.*` helpers                 | ✅     | `vstack`/`hstack` (Content) + `*Of` / `sized` for explicit lengths.                                                                                                                                                              |
| `Scroll.vertical` / `scrollState` | ✅     | Thin wrapper over the `Scroll` node.                                                                                                                                                                                             |
| `Backdrop.solid` / `behind`       | ✅     | `solid` = `Filled` wrapper; `behind style child` fills the rect then draws the child on top (full-bleed row/cell highlight).                                                                                                     |
| `Spacer.spacer` / `flexSpacer`    | ✅     | `spacer` = zero-extent placeholder (explicit-length slots); `flexSpacer` / `Stack.flex` = `Fill`-based flex spacer that absorbs a stack's slack.                                                                                 |
| Predefined styles                 | ✅     | `border`/`title`/`text`/`dim`/`success`/`warning`/`danger`/`info`/…                                                                                                                                                              |
| `Separator`                       | ⬜     | Horizontal/vertical rule.                                                                                                                                                                                                        |
| `Badge`                           | ⬜     | Toned pill (`Tone.Success "done"`).                                                                                                                                                                                              |
| `KeyHint`                         | ⬜     | `Ctrl+P` → label chips for status bars.                                                                                                                                                                                          |
| `Scrollbar`                       | ✅     | Track + thumb, built into `ScrollView` (proportional thumb sized/positioned from viewport + content height).                                                                                                                     |
| `ScrollView`                      | 🟡     | `ScrollView.vertical` (viewport + scrollbar) + `clampOffset`/`toBottom`/`atBottom` helpers for follow-tail/jump-to-bottom/paging. No virtualization yet.                                                                         |
| `List` / `ListView`               | 🟡     | `ListView.view` does single-selection + full-width highlight + auto-scroll-to-selection (string labels). No virtualization, multi-select, or built-in key handling yet.                                                          |
| `Table`                           | ⬜     | Virtualized rows, sticky header, column sizing, selection. (`Mire.SpreadsheetDemo` and `Mire.MinesweeperDemo` both hand-roll a grid from nested `Stack`s — motivates this.)                                                      |
| `Input` (single-line)             | 🟡     | `Mire.Core.TextBuffer` (pure insert/delete/cursor ops) + `Widgets.Input.render` (block cursor + scroll-to-cursor) ship; used by `Mire.SpreadsheetDemo`. Still no selection, and `Mire.AgentDemo`'s `PromptInput` not yet ported. |
| `TextArea` (multi-line)           | ⬜     | Multiline editing, shift-enter newline.                                                                                                                                                                                          |
| `Modal`                           | 🟡     | Layout half shipped: `Widgets.Modal.modal` (centered box + opaque backdrop + title + body slot, on `Positioned`). Focus-trapping + actions pending the focus manager.                                                            |
| `Toast`                           | 🟡     | `Toast.stack` (Positioned column of cards) + `Toast.card`; auto-dismiss is app-side via a `Sub` timer. Used by `Mire.AgentDemo`.                                                                                                 |
| `CommandPalette`                  | ⬜     | Global fuzzy command surface.                                                                                                                                                                                                    |
| `Completion`                      | ⬜     | Cursor/anchor-positioned completion list.                                                                                                                                                                                        |
| `SplitView`                       | ⬜     | Resizable split (today: hand-build with `Dock`/`Stack` fractions).                                                                                                                                                               |
| `Tooltip`                         | ⬜     | Anchored hover/inline doc.                                                                                                                                                                                                       |
| `Markdown`                        | ⬜     | Parse + wrap + style; cached by content+width.                                                                                                                                                                                   |
| `ImagePreview`                    | ⬜     | Kitty graphics protocol, with text fallback.                                                                                                                                                                                     |

### Agent widgets — `Mire.Agent` (project not yet created)

| Widget           | Status | Notes                                                                 |
| ---------------- | ------ | --------------------------------------------------------------------- |
| `ChatTranscript` | ⬜     | Block-virtualized transcript (user/assistant/tool/diff/error blocks). |
| `PromptBox`      | ⬜     | Multiline input, slash commands, @mentions, history, attachments.     |
| `ToolCallView`   | ⬜     | Name + status + streamed output, collapsible.                         |
| `ThinkingBlock`  | ⬜     | Reasoning placeholder.                                                |
| `DiffView`       | ⬜     | Unified/split hunks, accept/reject.                                   |
| `FileTree`       | ⬜     | Workspace tree.                                                       |
| `TaskTimeline`   | ⬜     | Run/step status over time.                                            |
| `ApprovalModal`  | ⬜     | Command/risk approval prompt.                                         |

---

## Phases

### v0.1 — Terminal runtime foundation ✅

The whole pipeline runs end-to-end: `model → view → layout → surface → diff → terminal`.

- [x] `Mire.Core` value types (`Point`/`Size`/`Rect`/`Color`/`Style`/`Cell`/`Region`/`Grapheme`/input events)
- [x] Raw terminal mode (`stty` + libc `poll`/`read` P/Invoke), alternate screen, resize handling (resize re-renders but doesn't force a full repaint — see Known gaps)
- [x] Byte-level `InputParser` — printable chars, Ctrl chords, arrows, function keys, Home/End/PgUp/PgDn/Ins/Del
- [x] `Surface` cell grid + draw primitives; run-based `Diff` writer
- [x] Elmish `Runtime.run` (~30 FPS) with `Cmd`/`Sub`, `Program` builders, `OnError`
- [x] `Mire.Widgets` convenience layer + predefined semantic styles
- [x] Grapheme-cluster width handling (wide chars, combining marks) — BMP per-`char` width + combining-mark merge; not true cluster handling (see Known gaps)

### v0.2 — Layout, regions & overlays 🟡 ← _current phase_

- [x] **Layout engine complete** — real `Stack` flow, `Scroll` offset+clipping, `Content`/`Fill` dock lengths, `Filled` opaque node
- [x] Headless `--dump` verification mode
- [x] **Input decoding** — **mouse (SGR 1006), bracketed paste, and focus events now decode**, alongside the Kitty **`CSI u`** modifier chords + legacy fallbacks (`Ctrl+letter`, `Shift+Tab`, F-keys, arrows in both normal `ESC [ A` and application-cursor `ESC O A` modes). New: `ESC [ < b ; x ; y M|m` → `Mouse` (0-based coords, wheel, Shift/Alt/Ctrl); `ESC [ 200 ~ … ESC [ 201 ~` → `Paste` (now enabled in `Runtime.run`); `ESC [ I` / `ESC [ O` → `FocusGained` / `FocusLost`. _Remaining:_ Kitty **release/repeat** event types (tracked under v0.5); large pastes split across reads aren't buffered (each chunk is its own `Paste`).
- [x] **Runtime: quit-from-update** — `Cmd.quit` (a `Quit` command the runtime folds into `Running = false` after the message pump, then exits through the normal teardown) lets `update` exit cleanly without the Ctrl+C intercept
- [x] **Focus manager** — `Mire.Layout.Focus`, a pure `RegionId`-keyed focus ring + modal trap stack (`ofOrder`/`next`/`prev`/`focus`/`pushTrap`/`popTrap`/`isFocused`), routed inside `update`. `Mire.FeedDemo` is migrated to it as the proof: the two panes are a base ring, each overlay is a nested trap ring, routing is `match Focus.current`, markers are `Focus.isFocused`. _Deferred to v0.5:_ the runtime-owned + mouse-hit-testing variant (a `Focusable` node + a retained region table)
- [x] **Overlay positioning** — `Positioned` layout node: 9-point placement (center + 4 corners + 4 edge-centers) within the assigned rect, child sized via `Length`. _Deferred:_ cursor/point and sub-rect anchoring (for `Completion`/`Tooltip`).
- [ ] **`Modal`** widget — _layout half shipped_ (`Widgets.Modal.modal`: centered box + opaque backdrop + title + body slot, on `Positioned`; dogfooded by FeedDemo's add/filter modals); focus-trapping still pending the focus manager
- [x] **`Toast`** stack — `Widgets.Toast.stack` places a column of cards at a `Placement` (top-right) on `Positioned`; `Toast.card` for a default card. Auto-dismiss stays app-side via a `Sub` timer (the app owns the toast list). Dogfooded by `Mire.AgentDemo`.
- [x] **`ScrollView`** widget — `Widgets.ScrollView.vertical` wraps `Scroll` with a track/thumb scrollbar; pure `clampOffset`/`toBottom`/`atBottom` helpers make follow-tail, jump-to-bottom, and paging one-liners in `update`. Dogfooded by `Mire.FeedDemo`'s reader. _Not yet:_ virtualization (the `Scroll` blit still measures the whole child)
- [x] **`Spacer`** — `Spacer.flexSpacer` / `Stack.flex` (a `Fill`-length `StackChild`) absorb a stack's slack to push siblings apart; `Spacer.spacer` stays the zero-extent placeholder for explicit-length slots
- [x] Fix `Box` multi-child layout — kept `Box` a single-child container (one child fills the inner rect) and fixed the `panel`/`statusBar` helpers to flow their children through an explicit `Stack` (vertical / horizontal) instead of overlapping

### v0.3 — Core widgets ⬜

- [ ] `Separator`, `Badge`, `KeyHint` (small, unblock richer status bars/panels)
- [ ] `Input` (single-line) — backed by a `TextBuffer` (cursor/selection/edits)
- [ ] `TextArea` (multi-line) — shift-enter newline, paste handling
- [ ] `List` — selectable, keyboard-navigable, virtualized
- [ ] `Table` — virtualized rows, sticky header, column sizing, selection, custom cell renderers
- [ ] `CommandPalette` — global fuzzy command surface (uses overlay + focus + list)
- [ ] `Completion` — cursor/anchor-anchored list (shares item model with palette)

### v0.4 — Agent widgets ⬜ (`Mire.Agent`)

Optional layer above `Mire.App`; the base framework must not depend on it.

> **Prototyped at the app level:** the `Mire.AgentDemo` demo already builds a chat
> transcript, tool-call / thinking / diff / table cards, a prompt box, a command palette,
> a skill-explorer overlay, toasts, and an approval/permission modal — on top of the
> existing layout primitives. It's a _testbed_, not the reusable library; these boxes stay
> ⬜ until the widgets are extracted into `Mire.Agent`. See `DEMO-TODOS.md` for the gaps.
>
> **Design reference:** `prototype/agent-harness.html` (Alpine.js, brand-faithful) mocks the
> intended agent-shell direction — including an MCP-server manager, slash-command completion,
> and code-block syntax highlighting that the TUI hasn't attempted yet. It's a visual target,
> not running F#.

- [ ] Create `Mire.Agent` project (preserve the one-directional dependency chain)
- [ ] `TranscriptBlock` model + `ChatTranscript` (block virtualization, follow-tail)
- [ ] `PromptBox` (multiline, slash commands, @mentions, history, attachments)
- [ ] `ToolCallView`, `ThinkingBlock`
- [ ] `DiffView` (unified/split, accept/reject hunks)
- [ ] `FileTree`, `TaskTimeline`
- [ ] `ApprovalModal`

### v0.5 — Kitty/Ghostty niceties ⬜

- [ ] Full Kitty keyboard protocol decode — 🟡 `CSI u` **modifier** decoding done (see v0.2); remaining: request + decode release/repeat **event types** (push "report event types", parse the `:event` sub-param) and the private-use functional codepoints
- [ ] OSC 8 hyperlinks — render `Cell.Link` (sequences exist; cells don't carry links yet)
- [ ] Kitty graphics protocol → `ImagePreview` with text fallback
- [ ] Light/dark theme notifications
- [x] Wire synchronized output (`?2026h`) around frame writes — `Diff.renderToTerminal` brackets every frame in begin/end-sync (BSU/ESU); covered by a test
- [ ] Richer mouse (hit-testing → focus/selection)

### Cross-cutting — Performance & rendering ⬜

From `SPEC.md`'s optimization tiers. Do these _when they hurt_, not before.

- [x] Tier 1–2: surface diffing + run-based output (baseline, done)
- [ ] Frame coalescing / render throttling for streaming (Tier 12) — important for agent UIs
- [ ] Virtualized tables & transcript blocks (Tier 5–6)
- [ ] Text-wrap + grapheme-width caching (Tier 7, 16)
- [ ] Append-only optimization for logs/transcripts (Tier 23)
- [ ] Dirty-region / partial composition (Tier 3, 20) — only for large/remote surfaces

### Project infrastructure 🟡

- [x] **Framework consolidated** into a single `Mire` project (folders = layers); solution is `Mire` + `Mire.Demo` + `Mire.AgentDemo` + `Mire.FeedDemo` + `Mire.SpreadsheetDemo` + `Mire.MinesweeperDemo` + `Mire.Tests`
- [x] **Test project** — `Mire.Tests` (Expecto) covering `Layout.measure`/`render`, `Diff.compute` (incl. sync-output bracketing + display-width cursor advance), `InputParser`, `Grapheme` width, `TextBuffer`/`Input`, and the `Mire.MinesweeperDemo` `Board` + `Mire.FeedDemo` `Feed` helpers (62 tests, all green; `dotnet build Mire.slnx` is warning-clean)
- [x] **Dev tooling** — `justfile` (build/test/run/format/lint) + Fantomas tool manifest (`.config/dotnet-tools.json`); `just check` = lint + build + test
- [ ] Promote `--dump` scenarios into golden-frame snapshot tests (assert full cell grids, not just spot checks)
- [x] `git init` + under version control — framework, four demos, tests, docs, prototypes (work continues on the `feat/widget-gallery` branch)
- [ ] CI build + `dotnet test` on .NET 10

---

## Known gaps & tech debt

Things that work "well enough" today but have a sharp edge worth remembering:

- **`Box` is single-child by design.** A `Box` renders one child filling its inner rect; passing multiple children overlaps them — flow is `Stack`'s job, so nest a `Stack` (the `panel`/`statusBar` helpers now do this internally).
- **Input decoding has two remaining gaps.** Keyboard (incl. Kitty `CSI u` chords), mouse (SGR 1006), bracketed paste, and focus events all decode now. Still missing: Kitty **release/repeat** event types aren't requested (only the disambiguate flag `CSI > 1 u` is pushed, not "report event types"), so keys only ever emit `… Press`; and a **large paste split across `read()`s** isn't buffered — each chunk becomes its own `Paste` (the end marker is only matched within a single buffer).
- **`Scroll` has no scrollbar / follow-tail / virtualization** — it's the primitive, not the `ScrollView` widget.
- **Wide-char rendering is BMP-only and leaves a trailing cell.** `Grapheme.charWidth` works per UTF-16 `char`, so the `0x20000–0x2A6DF` CJK-Ext-B branch in `isWide` is unreachable and astral-plane / emoji-ZWJ clusters aren't handled. `Cell.FromChar` always sets `Width = 1` while `Surface.Write` advances the cursor by the glyph width _without blanking the wide glyph's trailing cell_ — so a wide glyph overwriting narrower content can leave an artifact.
- **Dead scaffolding & externs.** `RegionId` is now load-bearing (the `Mire.Layout.Focus` key), but `Region`/`RenderMode` (and the `Focusable`/`ZIndex`/`Clip` fields) in `Core/Region.fs` are still wired to nothing — forward declarations for the unbuilt runtime-owned focus / z-ordering. The `tcgetattr`/`tcsetattr`/`ioctl` libc externs in `TerminalMode` are also unused (raw mode uses the `stty` subprocess; size uses `Console.WindowWidth/Height`); the "For now, use Console APIs" comment in `setupRawMode` is stale.
- **Solution file is `Mire.slnx`** (modern XML format), not `Mire.sln`.

---

## How to use this file

1. Pick the next unchecked item in the current phase (or an earlier phase's gap).
2. Implement it; verify with `dotnet build Mire.slnx` and, for layout/render
   changes, `dotnet run --project Mire.Demo -- --dump`.
3. Tick the box here, update the widget reference table, add a CHANGELOG entry.
4. Keep the dependency chain one-directional (see `CLAUDE.md` / README).
