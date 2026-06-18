# Mire Roadmap

The plan of record for what gets built and in what order. Synthesized from
[`SPEC.md`](SPEC.md)'s "Minimal viable version" cuts (v0.1–v0.5) and the verified
state of the code.

**Legend:** ✅ done · 🟡 partial / has known gaps · ⬜ not started

> The code is the source of truth for _what exists_; `SPEC.md` is the source of
> truth for _intended direction_. This file is the bridge — keep the checkboxes
> honest. When you finish something, tick it here and add a CHANGELOG entry.
> Completed phases (v0.1–v0.3) live in full in
> [`docs/ROADMAP-ARCHIVE.md`](docs/ROADMAP-ARCHIVE.md).

---

## Release plan

Pre-1.0, shipped **incrementally** — the public API still moves between minors,
so consumers pin an exact version. A release goes out by publishing a `v*`
GitHub Release; `publish.yml` then packs + pushes to nuget.org via OIDC. Only
the throwaway `0.0.1`/`0.0.2` are on nuget.org today and **no `v*` tag exists
yet** — so nothing has been deliberately released. The next deliberate release
is **0.4.0**.

> Release numbers ≠ the SPEC "phase" numbers below. The 0.4.0 release bundles the
> completed core (SPEC v0.1–v0.3) **plus** the cross-cutting protocol work that
> landed since; the agent layer (SPEC phase v0.4) ships as the **0.5.0** release.

### 0.4.0 — Core framework · _next, ship soon_

The runtime, layout engine, and full base-widget layer (everything in the tables
below), plus the protocol work done since v0.3: OSC 8 links, OSC 52 clipboard,
Kitty event types, bracketed-paste reassembly, and the brand-default theme.
Honestly labeled "a usable core widget layer"; the agent layer is "coming in
0.5". **Gate to tag:**

- [ ] Fix the wide-glyph trailing-cell artifact (Cross-cutting — Correctness) — don't ship a known glyph-corruption bug
- [ ] Bump `Mire/Mire.fsproj` `<Version>` 0.3.0 → 0.4.0
- [ ] Verify the README "minimal app" compiles **and runs** as written (it's the front-door example)
- [ ] Cut the CHANGELOG `[Unreleased]` block into a dated `[0.4.0]` section
- [ ] Publish a `v0.4.0` GitHub Release → `publish.yml` → nuget.org; then smoke-install the package into a scratch project and run a counter app

### 0.5.0 — Agent layer · _the promoted release (delivers SPEC phase v0.4)_

Extract `Mire.Agent` so the SPEC's headline `agentShell { … }` MVP works out of
the box — the version actually worth announcing. Work = the **v0.4 phase below**
(the 8-step extraction) + the cheap remaining v0.5 niceties (theme
notifications). **Gate to tag:**

- [ ] `Mire.Agent` project shipping `ChatTranscript`/`PromptBox`/`ToolCallView`/`ThinkingBlock`/`ApprovalModal`/`DiffView`/`FileTree`/`TaskTimeline` (the v0.4 phase list)
- [ ] `Mire.Demo.Agent` migrated onto `Mire.Agent` (dogfood; retires most of `DEMO-TODOS.md`)
- [ ] A runnable `agentShell` MVP sample matching SPEC's example
- [ ] CHANGELOG `[0.5.0]` + version bump + `v0.5.0` tag

### 0.6.0 — Polish & reach

- [ ] Widget gallery app (revives the deleted KitchenSink's coverage — every widget × its states)
- [ ] `ImagePreview` (Kitty graphics) + light/dark theme notifications (rest of SPEC phase v0.5)
- [ ] True grapheme clusters — astral-plane / emoji-ZWJ (the second Correctness item)
- [ ] Performance tiers _as they hurt_ — frame coalescing for streaming first (the one an agent UI feels)
- [ ] Runtime-owned / mouse-hit-testing half of focus — retires the demo's hand-mirrored modal hit-test

### 1.0.0 — later (not a near-term goal)

Stay 0.x until 0.4/0.5 bake under real use. 1.0 means an API review/freeze, full
XML-doc coverage, and a deprecation policy — revisit once the agent layer has
shipped and the public API stops moving.

---

## Widget & node reference

The catalog of every renderable thing, with status. Layout nodes are the
primitives in `Mire.Layout`; widgets are convenience builders in `Mire.Widgets`;
agent widgets are the (not-yet-created) `Mire.Agent` layer.

### Layout nodes — `Mire.Layout` (`LayoutNode<'msg>`)

| Node      | Status | Notes                                                                                                                                                                                                                                                                      |
| --------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Empty`   | ✅     | No-op.                                                                                                                                                                                                                                                                     |
| `Text`    | ✅     | Multi-line via `\n`; grapheme-width aware; clipped to rect.                                                                                                                                                                                                                |
| `Filled`  | ✅     | Opaque rectangle (style-filled). Backdrop / highlight / modal backing.                                                                                                                                                                                                     |
| `Box`     | 🟡     | Border + children. Children all share the inner rect (multi-child overlaps — nest a `Stack`).                                                                                                                                                                              |
| `Dock`    | ✅     | `Cells`/`Fraction`/`Content`/`Fill` on `Top`/`Bottom`/`Left`/`Right`/`Fill`.                                                                                                                                                                                               |
| `Stack`   | ✅     | Vertical/horizontal flow; per-child `Cells`/`Fraction`/`Content`/`Fill`.                                                                                                                                                                                                   |
| `Scroll`  | 🟡     | Offset + viewport clipping via off-screen blit. The primitive only — scrollbar/follow-tail live in `ScrollView`; no virtualization (the blit measures the whole child).                                                                                                    |
| `Overlay` | 🟡     | Z-orders (list order) and `Filled` occludes; `Positioned` sizes + places a layer (9-point) within the area, and `Widgets.Overlay.atPoint` clamps a popup at a caller-supplied point (cursor anchoring). Node-level region anchoring (retained rects) still pending → v0.5. |

### Base widgets — `Mire.Widgets`

| Widget                            | Status | Notes                                                                                                                                                                                                                                                                                              |
| --------------------------------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Text.text` / `title` / `dimText` | ✅     | Styled text nodes.                                                                                                                                                                                                                                                                                 |
| `Box.box` / `panel`               | ✅     | Bordered container; `panel` adds a title.                                                                                                                                                                                                                                                          |
| `StatusBar.statusBar`             | ✅     | Left/center/right item groups.                                                                                                                                                                                                                                                                     |
| `Dock.*` helpers                  | ✅     | `top`/`bottom`/`left`/`right`/`fill`.                                                                                                                                                                                                                                                              |
| `Stack.*` helpers                 | ✅     | `vstack`/`hstack` (Content) + `*Of` / `sized` for explicit lengths.                                                                                                                                                                                                                                |
| `Scroll.vertical` / `scrollState` | ✅     | Thin wrapper over the `Scroll` node.                                                                                                                                                                                                                                                               |
| `Backdrop.solid` / `behind`       | ✅     | `solid` = `Filled` wrapper; `behind style child` fills the rect then draws the child on top (full-bleed row/cell highlight).                                                                                                                                                                       |
| `Spacer.spacer` / `flexSpacer`    | ✅     | `spacer` = zero-extent placeholder (explicit-length slots); `flexSpacer` / `Stack.flex` = `Fill`-based flex spacer that absorbs a stack's slack.                                                                                                                                                   |
| Predefined styles                 | ✅     | `border`/`title`/`text`/`dim`/`success`/`warning`/`danger`/`info`/…                                                                                                                                                                                                                                |
| `AppTheme`                        | ✅     | `AppTheme` record (swappable style set: `fg`/`fgMuted`/`border`/`accent`/`selection`/`markdown`/…). `AppTheme.defaultTheme` **is the Mire brand** — emerald accent, neutral hierarchy, inverse-video selection — built from `Mire.Brand.Palette` (compiled into the framework), as are the `Style.*` primitives and `Markdown.defaultStyle`. `AppTheme.toneStyle` maps a functional `Tone`. Demos derive from the same palette.          |
| `Separator`                       | ✅     | `Separator.horizontal width` (`─`) / `vertical height` (`│`).                                                                                                                                                                                                                                      |
| `Badge`                           | ✅     | `Badge.badge style label` — a padded, toned pill (the caller supplies the tone via `style`).                                                                                                                                                                                                       |
| `KeyHint`                         | ✅     | `KeyHint.hint keyStyle labelStyle key label` — a styled key glyph + label chip for status bars.                                                                                                                                                                                                    |
| `Spinner`                         | ✅     | `Spinner.view`/`labeled` + `frameOf` — braille frames mapped from an app-owned tick (wraps; negative/empty safe).                                                                                                                                                                                  |
| `ProgressBar` / `Gauge`           | ✅     | `ProgressBar.view` (`█`/`░` determinate bar, 0..1) + `.gauge` (same bar with the centered percentage overlaid).                                                                                                                                                                                    |
| `Tabs`                            | ✅     | `Tabs.strip` — a horizontal tab strip; the active tab is styled, the app owns the selected index + body.                                                                                                                                                                                           |
| `Toggle`                          | ✅     | `Toggle.checkbox` / `radio` / `switch` — selection-control glyphs (`[x]`/`(•)`/`ON/OFF`); app owns state.                                                                                                                                                                                          |
| `Scrollbar`                       | ✅     | Track + thumb, built into `ScrollView` (proportional thumb sized/positioned from viewport + content height).                                                                                                                                                                                       |
| `ScrollView`                      | 🟡     | `ScrollView.vertical` (viewport + scrollbar) + `clampOffset`/`toBottom`/`atBottom` helpers for follow-tail/jump-to-bottom/paging. No virtualization (that's `ListView`/`Table`'s job today).                                                                                                       |
| `List` / `ListView`               | ✅     | `ListView.view` (single-select) + `viewWith` (predicate → multi-select), both **virtualized** (only the visible window is built) + auto-scroll-to-selection. Key handling is app-side (MVU).                                                                                                       |
| `Table`                           | ✅     | `Table.view` — sticky header, `Length`-width columns, per-row cell renderers (`textColumn`), windowed/virtualized rows, predicate selection (single **or** multi). Column resize is app-state.                                                                                                     |
| `Input` (single-line)             | 🟡     | `Mire.Core.TextBuffer` (pure insert/delete/cursor ops, incl. word + line + up/down moves) + `Widgets.Input.render` (block cursor + scroll-to-cursor); used by `Mire.Demo.Spreadsheet` and `Mire.Demo.Agent`'s prompt. Still no selection.                                                          |
| `TextArea` (multi-line)           | ✅     | `Widgets.TextArea.render` — multi-line block-cursor view (scroll-to-cursor both axes, no-wrap) over the `\n`-aware `TextBuffer`; `Mire.Core.TextEdit` is the reusable, overridable key→action keymap (word-delete/move chords, paste). Dogfooded by `Mire.Demo.Agent`. No selection/soft-wrap yet. |
| `Modal`                           | ✅     | `Widgets.Modal.modal` — centered box + opaque backdrop + title + body slot, on `Positioned`. Keyboard focus-trapping is app-side by design: pair open/close with `Focus.pushTrap`/`popTrap` (proven in `Mire.Demo.Feed`). _Optional nicety:_ a `Modal.withFocus` returning the trap ids.           |
| `Toast`                           | ✅     | `Toast.stack` (Positioned column of cards) + `Toast.card`; auto-dismiss is app-side by design via a `Sub` timer (the app owns the toast list). Used by `Mire.Demo.Agent`.                                                                                                                          |
| `CommandPalette`                  | ✅     | `CommandPalette.view` (centered `Modal` + query line + filtered `ListView`) + `matches` and ranked `filter` (best-first fuzzy). App pairs open/close with `Focus.pushTrap`/`popTrap`.                                                                                                              |
| `Completion`                      | ✅     | `Completion.view` — a cursor-anchored bordered, selectable list on `Overlay.atPoint`; flips above the caret when low on space; app filters with `CommandPalette.matches`/`filter`.                                                                                                                 |
| `SplitView`                       | ✅     | `SplitView.split`/`horizontal`/`vertical` — two panes + a 1-cell `Filled` divider gutter; app owns the split position (`Cells n` resizable / `Fraction f` proportional), second pane fills. Nest for >2 panes.                                                                                     |
| `Tooltip`                         | ✅     | `Tooltip.view` — an anchored bordered doc popup on `Overlay.atPoint`: sits below the anchor, flips above when low on space, clamped on-screen. Caller wraps lines to `width - 2`.                                                                                                                  |
| `Markdown`                        | ✅     | `Widgets.Markdown.render style width src` — line-oriented (NOT CommonMark): ATX headings, `>` quotes, `-`/`*`/ordered bullets, `---` rules, fenced code (light highlighting), inline emphasis/links; styled by a `MarkdownStyle` (optional `@mention`). Extracted from the AgentDemo.              |
| `ImagePreview`                    | ⬜     | Kitty graphics protocol, with text fallback.                                                                                                                                                                                                                                                       |

### Agent widgets — `Mire.Agent` (project not yet created)

| Widget           | Status | Notes                                                                 |
| ---------------- | ------ | --------------------------------------------------------------------- |
| `ChatTranscript` | ✅     | `ChatTranscript.{render,renderBlock}` over a `TranscriptBlock` list, styled by `AppTheme`. _Virtualization/follow-tail still app-side (the app owns the `ScrollView`)._ |
| `PromptBox`      | ✅     | `PromptBox` over `TextBuffer`/`TextEdit` + `render` (block cursor, placeholder). _Slash/@mention completion + history still app-side._ |
| `ToolCallView`   | ✅     | A `TranscriptBlock.ToolCall` (name + cmd + status glyph/spinner + output) rendered by `ChatTranscript`. _Collapsing is app-side._ |
| `ThinkingBlock`  | ✅     | A `TranscriptBlock.Thinking` card rendered by `ChatTranscript`.       |
| `DiffView`       | 🟡     | Unified `TranscriptBlock.DiffBlock` (colored `+`/`-`) via `ChatTranscript`. Split view + accept/reject hunks still pending. |
| `FileTree`       | ✅     | A `TranscriptBlock.FileTree` card via `ChatTranscript` (static paths). |
| `TaskTimeline`   | ✅     | A `TranscriptBlock.TaskTimeline` card via `ChatTranscript`.           |
| `ApprovalModal`  | ✅     | `ApprovalModal.view` (title/intro/command/risk + Accept/Deny) + `buttonHit` (click), styled by `AppTheme`. App owns the accept/deny behavior. |

---

## Phases

### v0.1 — Terminal runtime foundation ✅ · v0.2 — Layout, regions & overlays ✅ · v0.3 — Core widgets ✅

Shipped. The pipeline (`model → view → layout → surface → diff → terminal`), the
layout engine (`Stack`/`Dock`/`Scroll`/`Overlay`/`Positioned`), full input
decoding (Kitty `CSI u`, mouse, paste, focus events), the `Focus` manager,
`Cmd.quit`, and the core widget layer (lists, tables, palette, completion,
text editing, modals, toasts, scrollviews, …) all exist and are tested. Full
checklists: [`docs/ROADMAP-ARCHIVE.md`](docs/ROADMAP-ARCHIVE.md).

### v0.4 — Agent widgets ⬜ (`Mire.Agent`)

Optional layer above `Mire.App`; the base framework must not depend on it.
**The work is extraction, not invention** — `Mire.Demo.Agent` already builds
every component at the app level (transcript, tool-call / thinking / diff /
table cards, prompt, palette, skill explorer, an MCP-manager overlay, toasts,
approval modal). Extract each into `Mire.Agent`, then migrate the demo onto the
extracted widget as the proof. `DEMO-TODOS.md` tracks the demo-side gaps;
`prototype/agent-harness.html` (Alpine.js, brand-faithful) is the visual target.

Recommended order — each step names its extraction source in the demo:

- [x] **1. Create `Mire.Agent` project** — classlib referencing `Mire` only, in `Mire.slnx` (CI builds the solution); one-directional chain preserved
- [x] **2. `TranscriptBlock` model + `ChatTranscript`** — extracted from `Blocks.fs` into `Mire.Agent`, parameterized by `AppTheme` (`Notice` uses `AppTheme.Tone`); `ChatTranscript.{renderBlock,render,statusGlyph,statusStyle}`. The demo renders its transcript through it. _Follow-up:_ fold block virtualization + follow-tail into the widget (the demo still owns the `ScrollView`/offset).
- [x] **3. `PromptBox`** — `PromptInput.fs` moved into `Mire.Agent.PromptBox` (verbatim — it was already framework-only). _Follow-up:_ fold slash/@mention completion + history into the widget (still app-side).
- [x] **4. `ToolCallView` + `ThinkingBlock`** — shipped as `TranscriptBlock.ToolCall`/`Thinking` rendered by `ChatTranscript` (they're transcript blocks here, not standalone widgets).
- [x] **5. `ApprovalModal`** — `ApprovalModal.view` + `buttonHit` (click) in `Mire.Agent`, styled by `AppTheme`; the demo's permission modal renders + click-activates through it (accept/deny behavior stays app-side).
- [🟡] **6. `DiffView`** — unified shipped as `TranscriptBlock.DiffBlock` via `ChatTranscript`; split view + accept/reject hunks still pending.
- [x] **7. `FileTree`, `TaskTimeline`** — shipped as `TranscriptBlock.FileTree`/`TaskTimeline` rendered by `ChatTranscript`.
- [x] **8. `agentShell` MVP sample** — `samples/AgentShell` composes `ChatTranscript` + `PromptBox` + `ApprovalModal` on `AppTheme.defaultTheme` (zero app theme code) — the proof the layer composes. `Mire.Demo.Agent`'s transcript/prompt/approval-modal also route through `Mire.Agent`; its remaining overlays (palette/skill/mcp) are generic UI that may stay app-level.
- [ ] Widget gallery app — a dedicated demo exercising every widget in all its
      states (build after the brand-default theme + Agent refactor land)

### v0.5 — Kitty/Ghostty niceties 🟡

- [x] Full Kitty keyboard protocol decode — `CSI u` **modifier** decoding (see archive) **and event types**: the runtime pushes `CSI > 3 u` (disambiguate + report events) and `InputParser` decodes the `:event` sub-param into `Press`/`Repeat`/`Release`. The runtime drops `Release` unless `Program.withKeyReleases`. _Remaining:_ the private-use functional codepoints.
- [x] Buffer large bracketed pastes split across `read()`s — the runtime carries an unfinished paste (via `InputParser.stepPasteBuffer`, capped at 1 MiB) and reassembles it into one `Paste`
- [x] OSC 8 hyperlinks — `Style.Link` (`WithLink url`); the `Diff` writer brackets a linked run in OSC 8 open/close and `Markdown` link spans carry their URL
- [x] OSC 52 clipboard — `Cmd.setClipboard text`, written to the terminal by the runtime (same hook shape as `Cmd.quit`)
- [ ] Kitty graphics protocol → `ImagePreview` with text fallback
- [ ] Light/dark theme notifications
- [ ] Richer mouse (hit-testing → focus/selection) — the runtime-owned half of focus: `Focusable` node + retained region table (the `Region`/`RenderMode` scaffolding in `Core/Region.fs` is the forward declaration)

### Cross-cutting — Performance & rendering ⬜

From `SPEC.md`'s optimization tiers. Do these _when they hurt_, not before.

- [x] Tier 1–2: surface diffing + run-based output (baseline, done)
- [ ] Frame coalescing / render throttling for streaming (Tier 12) — important for agent UIs
- [ ] Virtualized tables & transcript blocks (Tier 5–6)
- [ ] Text-wrap + grapheme-width caching (Tier 7, 16)
- [ ] Append-only optimization for logs/transcripts (Tier 23)
- [ ] Dirty-region / partial composition (Tier 3, 20) — only for large/remote surfaces

### Cross-cutting — Correctness ⬜

Promoted out of "Known gaps" because they are real renderer bugs, not nice-to-haves:

- [ ] Wide-glyph trailing cell — `Cell.FromChar` always sets `Width = 1`, and `Surface.Write` advances by glyph width without blanking the wide glyph's trailing cell, so a wide glyph overwriting narrower content can leave an artifact
- [ ] True grapheme clusters — `Grapheme.charWidth` works per UTF-16 `char`; astral-plane chars (the CJK-Ext-B branch is unreachable) and emoji-ZWJ clusters aren't handled

### Project infrastructure ✅

Done and verified (details in [`docs/ROADMAP-ARCHIVE.md`](docs/ROADMAP-ARCHIVE.md)):
single-project framework + three demos + `Mire.Tests` (Expecto) in
`Mire.slnx`; `justfile` + Fantomas; golden-frame snapshots; CI build+test on
every push/PR; NuGet packaging with OIDC trusted publishing on `v*` releases.

- [ ] Reconcile package versioning + cut the first deliberate release — nuget.org
      has only `0.0.1`/`0.0.2` and no `v*` tag exists; bump `Mire.fsproj` to
      `0.4.0` and tag `v0.4.0` through `publish.yml` (see the **0.4.0** gate in
      the Release plan above)

---

## Known gaps & tech debt

Things that work "well enough" today but have a sharp edge worth remembering:

- **`Box` is single-child by design.** A `Box` renders one child filling its inner rect; passing multiple children overlaps them — flow is `Stack`'s job, so nest a `Stack` (the `panel`/`statusBar` helpers do this internally).
- **Input decoding is feature-complete for the targeted terminals.** Keyboard (Kitty `CSI u` chords + press/repeat/release event types), mouse (SGR 1006), bracketed paste (reassembled across reads), and focus events all decode. The only remaining nicety is the Kitty private-use functional codepoints (v0.5).
- **One input event per read.** `InputParser.parseBytes` decodes a single event from a buffer, and the runtime calls it once per read. Interactive typing is unaffected (each keystroke is its own read), but a burst of *distinct* keystrokes delivered in one `read()` (e.g. piped/scripted input) only yields the first — non-bracketed bursts aren't split into multiple events. Bracketed paste is handled separately (reassembled, not split). Low priority; surfaces mainly in headless input-feeding tests.
- **Wide-char rendering is BMP-only and leaves a trailing cell.** Tracked as Cross-cutting — Correctness above.
- **Dead scaffolding.** `RegionId` is load-bearing (the `Mire.Layout.Focus` key), but `Region`/`RenderMode` (and the `Focusable`/`ZIndex`/`Clip` fields) in `Core/Region.fs` are wired to nothing — forward declarations for the v0.5 runtime-owned focus / z-ordering. (The unused `tcgetattr`/`tcsetattr`/`ioctl` externs and the stale "For now, use Console APIs" comment in `TerminalMode` were removed.)
- **Solution file is `Mire.slnx`** (modern XML format), not `Mire.sln`.

---

## How to use this file

1. Pick the next unchecked item — "What's next" at the top is the recommended
   order.
2. Implement it; verify with `dotnet build Mire.slnx`, `dotnet run --project
Mire.Tests`, and, for layout/render changes, `dotnet run --project
Mire.Demo.Agent -- --dump`.
3. Tick the box here, update the widget reference table, add a CHANGELOG entry.
4. Keep the dependency chain one-directional (see `CLAUDE.md` / README).
