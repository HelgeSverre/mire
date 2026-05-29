# Mire Roadmap

The plan of record for what gets built and in what order. Synthesized from
[`SPEC.md`](SPEC.md)'s "Minimal viable version" cuts (v0.1–v0.5) and the verified
state of the code.

**Legend:** ✅ done · 🟡 partial / has known gaps · ⬜ not started

> The code is the source of truth for *what exists*; `SPEC.md` is the source of
> truth for *intended direction*. This file is the bridge — keep the checkboxes
> honest. When you finish something, tick it here and add a CHANGELOG entry.

---

## Widget & node reference

The catalog of every renderable thing, with status. Layout nodes are the
primitives in `Mire.Layout`; widgets are convenience builders in `Mire.Widgets`;
agent widgets are the (not-yet-created) `Mire.Agent` layer.

### Layout nodes — `Mire.Layout` (`LayoutNode<'msg>`)

| Node | Status | Notes |
|---|---|---|
| `Empty` | ✅ | No-op. |
| `Text` | ✅ | Multi-line via `\n`; grapheme-width aware; clipped to rect. |
| `Filled` | ✅ | Opaque rectangle (style-filled). Backdrop / highlight / modal backing. |
| `Box` | 🟡 | Border + children. Children all share the inner rect (multi-child overlaps — nest a `Stack`). |
| `Dock` | ✅ | `Cells`/`Fraction`/`Content`/`Fill` on `Top`/`Bottom`/`Left`/`Right`/`Fill`. |
| `Stack` | ✅ | Vertical/horizontal flow; per-child `Cells`/`Fraction`/`Content`/`Fill`. |
| `Scroll` | 🟡 | Offset + viewport clipping via off-screen blit. No scrollbar / follow-tail / virtualization yet (→ `ScrollView` widget). |
| `Overlay` | 🟡 | Z-orders (list order) and `Filled` occludes, but layers take the full area — no anchoring/centering. |

### Base widgets — `Mire.Widgets`

| Widget | Status | Notes |
|---|---|---|
| `Text.text` / `title` / `dimText` | ✅ | Styled text nodes. |
| `Box.box` / `panel` | ✅ | Bordered container; `panel` adds a title. |
| `StatusBar.statusBar` | ✅ | Left/center/right item groups. |
| `Dock.*` helpers | ✅ | `top`/`bottom`/`left`/`right`/`fill`. |
| `Stack.*` helpers | ✅ | `vstack`/`hstack` (Content) + `*Of` / `sized` for explicit lengths. |
| `Scroll.vertical` / `scrollState` | ✅ | Thin wrapper over the `Scroll` node. |
| `Backdrop.solid` / `behind` | ✅ | `solid` = `Filled` wrapper; `behind style child` fills the rect then draws the child on top (full-bleed row/cell highlight). |
| `Spacer.spacer` | 🟡 | Currently `Empty` → 0 extent in a stack. Needs a `Fill`-based flex spacer. |
| Predefined styles | ✅ | `border`/`title`/`text`/`dim`/`success`/`warning`/`danger`/`info`/… |
| `Separator` | ⬜ | Horizontal/vertical rule. |
| `Badge` | ⬜ | Toned pill (`Tone.Success "done"`). |
| `KeyHint` | ⬜ | `Ctrl+P` → label chips for status bars. |
| `Scrollbar` | ⬜ | Track + thumb; pairs with `ScrollView`. |
| `ScrollView` | ⬜ | Scroll + scrollbar + follow-tail + jump-to-bottom + virtualization. |
| `List` / `ListView` | 🟡 | `ListView.view` does single-selection + full-width highlight + auto-scroll-to-selection (string labels). No virtualization, multi-select, or built-in key handling yet. |
| `Table` | ⬜ | Virtualized rows, sticky header, column sizing, selection. (`Mire.SpreadsheetDemo` hand-rolls a grid from nested `Stack`s — motivates this.) |
| `Input` (single-line) | ⬜ | Text buffer, cursor, selection. (`Mire.AgentDemo`'s `PromptInput` and `Mire.SpreadsheetDemo`'s cell editor both hand-roll append+backspace — motivates a real `TextBuffer`.) |
| `TextArea` (multi-line) | ⬜ | Multiline editing, shift-enter newline. |
| `Modal` | ⬜ | Centered, focus-trapping, with actions. |
| `Toast` | ⬜ | Auto-dismissing notification stack. |
| `CommandPalette` | ⬜ | Global fuzzy command surface. |
| `Completion` | ⬜ | Cursor/anchor-positioned completion list. |
| `SplitView` | ⬜ | Resizable split (today: hand-build with `Dock`/`Stack` fractions). |
| `Tooltip` | ⬜ | Anchored hover/inline doc. |
| `Markdown` | ⬜ | Parse + wrap + style; cached by content+width. |
| `ImagePreview` | ⬜ | Kitty graphics protocol, with text fallback. |

### Agent widgets — `Mire.Agent` (project not yet created)

| Widget | Status | Notes |
|---|---|---|
| `ChatTranscript` | ⬜ | Block-virtualized transcript (user/assistant/tool/diff/error blocks). |
| `PromptBox` | ⬜ | Multiline input, slash commands, @mentions, history, attachments. |
| `ToolCallView` | ⬜ | Name + status + streamed output, collapsible. |
| `ThinkingBlock` | ⬜ | Reasoning placeholder. |
| `DiffView` | ⬜ | Unified/split hunks, accept/reject. |
| `FileTree` | ⬜ | Workspace tree. |
| `TaskTimeline` | ⬜ | Run/step status over time. |
| `ApprovalModal` | ⬜ | Command/risk approval prompt. |

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

### v0.2 — Layout, regions & overlays 🟡 ← *current phase*

- [x] **Layout engine complete** — real `Stack` flow, `Scroll` offset+clipping, `Content`/`Fill` dock lengths, `Filled` opaque node
- [x] Headless `--dump` verification mode
- [ ] **Input decoding** — mouse (SGR 1006), bracketed paste, focus events, Kitty release/repeat event types. *Done:* the Kitty **`CSI u`** modifier form is now decoded (`ESC [ <codepoint> ; <mod> u` → `Char`/named key + `KeyModifiers`, super→`Meta`), so **Ctrl/Alt/Shift/Super chords work** (Ctrl+P, Ctrl+O, …); modified navigation keys (`ESC [ 1 ; <mod> A/B/C/D/H/F`, `ESC [ <n> ; <mod> ~`) decode too — alongside the legacy `Ctrl+letter`, `Shift+Tab`, F-keys, and arrows in **both** normal (`ESC [ A`) and application-cursor (`ESC O A`, DECCKM — what JediTerm sends) modes. *Still pending:* mouse/focus sequences are enabled by `Runtime.run` but unparsed; **bracketed paste is neither enabled nor parsed** (`ANSI.enableBracketedPaste` is never written); release/repeat events aren't requested (only the disambiguate flag `CSI > 1 u` is pushed, not "report event types"), so the parser still only emits `Key … Press`. The `Mouse`/`Paste`/`FocusGained`/`FocusLost` `InputEvent` cases exist but aren't produced.
- [ ] **Runtime: quit-from-update** — a `Cmd.quit` (or `Quit` message convention) so apps can exit cleanly without relying on the hard-coded Ctrl+C intercept
- [ ] **Focus manager** — focusable node IDs, tab order, focus trap; route key/scroll events to the focused region first
- [ ] **Overlay positioning** — anchor (`Screen`/`Region`/`Cursor`) + placement (center, above/below, corners); the missing half of `Overlay`
- [ ] **`Modal`** widget — centered, focus-trapping, opaque backdrop (built on `Filled` + overlay positioning + focus trap)
- [ ] **`Toast`** stack — top-right, auto-dismiss via a `Sub` timer
- [ ] **`ScrollView`** widget — `Scroll` + scrollbar + follow-tail + jump-to-bottom
- [ ] **`Spacer`** — real flex spacer (`Fill`-based) instead of `Empty`
- [ ] Fix `Box` multi-child layout (flow children instead of overlapping at inner rect)

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
> existing layout primitives. It's a *testbed*, not the reusable library; these boxes stay
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
- [ ] Wire synchronized output (`?2026h`) around frame writes
- [ ] Richer mouse (hit-testing → focus/selection)

### Cross-cutting — Performance & rendering ⬜

From `SPEC.md`'s optimization tiers. Do these *when they hurt*, not before.

- [x] Tier 1–2: surface diffing + run-based output (baseline, done)
- [ ] Frame coalescing / render throttling for streaming (Tier 12) — important for agent UIs
- [ ] Virtualized tables & transcript blocks (Tier 5–6)
- [ ] Text-wrap + grapheme-width caching (Tier 7, 16)
- [ ] Append-only optimization for logs/transcripts (Tier 23)
- [ ] Dirty-region / partial composition (Tier 3, 20) — only for large/remote surfaces

### Project infrastructure 🟡

- [x] **Framework consolidated** into a single `Mire` project (folders = layers); solution is `Mire` + `Mire.Demo` + `Mire.AgentDemo` + `Mire.Tests`
- [x] **Test project** — `Mire.Tests` (Expecto) covering `Layout.measure`/`render`, `Diff.compute`, `InputParser`, `Grapheme` width (18 tests, all green; `dotnet build Mire.slnx` is warning-clean)
- [ ] Promote `--dump` scenarios into golden-frame snapshot tests (assert full cell grids, not just spot checks)
- [x] `git init` + under version control — 8 commits on `main` (framework, both demos, tests, docs, HTML prototype)
- [ ] CI build + `dotnet test` on .NET 10

---

## Known gaps & tech debt

Things that work "well enough" today but have a sharp edge worth remembering:

- **`Box` children overlap.** All children are measured at the same inner rect; multi-child boxes overdraw. Workaround: nest a `Stack`. (v0.2 fix listed above.)
- **`Spacer` is a no-op in stacks.** It maps to `Empty` (0 extent). A real flex spacer needs `Fill`.
- **No quit from `update`.** The runtime only exits on its hard-coded Ctrl+C intercept; `update` can't request exit. (Old counter demo's `q` silently did nothing.)
- **Input is keys-only.** Keyboard is solid now — including Kitty `CSI u` modifier chords (Ctrl/Alt/Shift/Super) and legacy fallbacks — but mouse and focus sequences are *enabled* by the runtime yet ignored by `InputParser`, and **bracketed paste is not even enabled** (the `ANSI.enableBracketedPaste` sequence is never written). The `Mouse`/`Paste`/`FocusGained`/`FocusLost` `InputEvent` cases are defined but never produced. Key **release/repeat** events aren't requested either (only the disambiguate flag is pushed).
- **`Scroll` has no scrollbar / follow-tail / virtualization** — it's the primitive, not the `ScrollView` widget.
- **`Overlay` can't position layers** — every layer fills the area; modals need anchoring/centering.
- **Resize doesn't force a full repaint.** On size change `Runtime.run` sets `NeedsRender` but keeps the previous `Surface`; `Diff.compute` then only diffs the `min(old, new)` overlap, so *growing* the terminal leaves the newly-exposed rows/columns unpainted until a later full redraw. Fix: reset `PreviousSurface` to `None` when the size changes (forcing the no-previous full-render path).
- **Wide-char rendering is BMP-only and leaves a trailing cell.** `Grapheme.charWidth` works per UTF-16 `char`, so the `0x20000–0x2A6DF` CJK-Ext-B branch in `isWide` is unreachable and astral-plane / emoji-ZWJ clusters aren't handled. `Cell.FromChar` always sets `Width = 1` while `Surface.Write` advances the cursor by the glyph width *without blanking the wide glyph's trailing cell* — so a wide glyph overwriting narrower content can leave an artifact.
- **Dead scaffolding & externs.** `Region`/`RegionId`/`RenderMode` (and the `Focusable`/`ZIndex`/`Clip` fields) are defined in `Core/Region.fs` but wired to nothing — forward declarations for the unbuilt focus manager / overlay positioning / z-ordering. The `tcgetattr`/`tcsetattr`/`ioctl` libc externs in `TerminalMode` are also unused (raw mode uses the `stty` subprocess; size uses `Console.WindowWidth/Height`); the "For now, use Console APIs" comment in `setupRawMode` is stale.
- **Solution file is `Mire.slnx`** (modern XML format), not `Mire.sln`.

---

## How to use this file

1. Pick the next unchecked item in the current phase (or an earlier phase's gap).
2. Implement it; verify with `dotnet build Mire.slnx` and, for layout/render
   changes, `dotnet run --project Mire.Demo -- --dump`.
3. Tick the box here, update the widget reference table, add a CHANGELOG entry.
4. Keep the dependency chain one-directional (see `CLAUDE.md` / README).
