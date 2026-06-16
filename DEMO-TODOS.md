# AgentDemo — demo TODOs

What the `Mire.Demo.Agent` demo can and can't do. The demo deliberately renders
the _shape_ of agentic-TUI features even where the framework doesn't support them, so this
file tracks which parts are real and which are faked/blocked — and, since the framework
has now caught up on most of the original blockers, which framework features the demo
still needs to **adopt**. Each gap links to the [`ROADMAP.md`](ROADMAP.md) item.

> The harness builds agent-domain UI at the _app level_ (it must — `CLAUDE.md`/ROADMAP keep
> agent concepts out of the core libraries). So "done" here means "works in the demo", not
> "exists as a reusable widget". Extraction into `Mire.Agent` is ROADMAP v0.4; when that
> lands, most of this file retires.

**Legend:** ✅ works in the demo · 🟡 partial / approximate · ⬜ blocked by the framework

---

## Works today ✅

- ✅ Interactive agent shell: status header, scrolling transcript, prompt, footer hints
- ✅ Canned responses via the `Dummy` module (see the table in `Dummy.fs` / type `help`)
- ✅ **Real prompt editing** — `PromptInput.fs` is a thin wrapper over `Mire.Core.TextBuffer` + `TextEdit` (typing, Backspace/Delete, word-delete/move chords, cursor moves, paste), rendered by `Widgets.TextArea` with a block cursor
- ✅ Markdown via the framework's `Widgets.Markdown` (headings, emphasis, lists, quotes, fenced code with light highlighting, links, `@mentions`) styled by the demo's brand `MarkdownStyle`
- ✅ Streaming text — one word per `Sub.Every 45ms` tick; `Esc` interrupts
- ✅ Async tool resolution — `tool:run` fires `Cmd.ofAsync`, resolves after a delay
- ✅ Braille spinner for running tools / sidebar tasks via `Sub.Every`
- ✅ Toast stack (top-right) via `Widgets.Toast.stack`, TTL auto-dismiss app-side
- ✅ Command palette (`Ctrl+P`) with live substring filter over `Dummy.commands`
- ✅ Skill explorer (`Ctrl+O`) — two independent `Scroll` panes (list + markdown preview), `Tab` switches pane focus
- ✅ **MCP manager overlay** (`/mcp`) — fake server list with statuses, transports, connect/auth/tools/uninstall actions, and a per-server tool browser (list → actions → tools navigation)
- ✅ Permission / approval / confirm modal with **keyboard-focusable** Accept/Deny buttons (`←/→`/`Tab` move, `Enter` confirm, `Esc` deny)
- ✅ Mode switch on `Shift+Tab` (`normal → auto-accept → plan`)
- ✅ Resize-aware text wrapping (tracks terminal `Size` via `Sub.TerminalResize`)
- ✅ Sidebar toggle (`panel` / `split`), transcript scroll (arrows / PgUp/PgDn / Home/End)
- ✅ On-brand theming — the demo's `Theme.fs` builds its styles from `brand/palette.fs` (it intentionally keeps a richer hand-rolled style set instead of the framework `AppTheme` record)
- ✅ Headless `--dump` mode (renders sample screens as text)

## Approximate 🟡

- 🟡 **Diff** — unified only, colored `+`/`-` (`Blocks.fs`); no split view, no accept/reject. → ROADMAP v0.4 `DiffView`
- 🟡 **Table block** — the demo's own fixed-width table card in `Blocks.fs`, not `Widgets.Table` (no virtualization, sticky header, or sort)
- 🟡 **Transcript scroll** — manual follow-tail (jumps to bottom on append); `ScrollView.toBottom`/`atBottom` would make it a one-liner (see adoption list)
- 🟡 **Stream cancel** — `Esc` clears a flag; not a real async cancellation token
- 🟡 **Command palette filter** — plain substring match; the framework's `CommandPalette.filter` (ranked fuzzy) is unused (see adoption list)

## Framework caught up — demo not migrated 🔄

The original blockers here all shipped (v0.2/v0.3); what remains is demo-side adoption.
Each is small, independent, and stress-tests a widget — ROADMAP "What's next" item 2:

- [x] **`Cmd.quit`** — the `quit`/`exit` command exits cleanly via `Cmd.quit` (Ctrl+C still works via the default quit policy). _Done._
- [x] **`Widgets.Spinner`** — `Blocks.spinner` delegates to `Spinner.frameOf Spinner.braille` instead of a hand-rolled frame table. _Done._
- [x] **OSC 52 clipboard (dogfood)** — the `copy` command puts the last assistant message on the system clipboard via `Cmd.setClipboard`. _Done._
- [x] **OSC 8 links (dogfood)** — transcript markdown links carry real URLs through `Widgets.Markdown` → `Style.Link` → `Diff` OSC 8 (no demo code needed; it falls out of the framework wiring). _Done._
- [ ] **Mouse** — `InputParser` decodes SGR 1006, but the demo's `mapInput` has no `Mouse` case; modal buttons / list rows / MCP actions are still keyboard-only. (Full hit-testing is ROADMAP v0.5; basic click-on-button is doable now)
- [ ] **Focus manager** — the demo still routes keys manually off its own `Overlay` field; migrate to `Mire.Layout.Focus` (`pushTrap`/`popTrap`), the way `Mire.Demo.Feed` does
- [ ] **`Widgets.Modal` / `Overlay.centered`** — the demo centers overlays with its own `centered`/`opaque` Dock-spacer helpers; the framework versions replace them
- [ ] **`ScrollView`** — transcript and skill-explorer panes use bare `Scroll` (`▲▼` hints); `ScrollView.vertical` adds the track/thumb scrollbar and the follow-tail helpers
- [ ] **`Widgets.CommandPalette`** — swap the substring filter for the ranked fuzzy `filter` + the framework palette view
- [ ] **`Widgets.Table`** — the `TableBlock` card in `Blocks.fs` hand-rolls column widths; `Widgets.Table.view` (sticky header, windowed rows) replaces it

## Blocked by the framework ⬜

- ⬜ **Hyperlinks** — markdown links render underlined; `Cell` carries no link, so no OSC 8. → ROADMAP v0.5
- ⬜ **Text selection** — `TextBuffer` has no selection, so neither does the prompt. → ROADMAP `Input`/`TextArea` gaps
- ⬜ **Kitty release/repeat** — keys only ever emit `Press`; key-up/auto-repeat interactions aren't possible. → ROADMAP v0.5
- ⬜ **Large pastes** — a paste split across `read()`s arrives as multiple `Paste` events. → ROADMAP v0.5
- ⬜ **Images** — no Kitty-graphics previews. → ROADMAP v0.5 `ImagePreview`
- ⬜ **Markdown inside a heading** — heading lines aren't inline-parsed (kept whole). Cosmetic; lives in `Widgets.Markdown` now.

---

## Notes for whoever picks this up

- v0.4 extraction sources: transcript blocks/cards live in `Blocks.fs`, the prompt in
  `PromptInput.fs`, the skill explorer in `Skills.fs`, the MCP manager + all overlay
  routing in `Program.fs`. The ROADMAP v0.4 checklist names which widget comes from which.
- When the demo adopts `Focus` + `Widgets.Modal`, the `centered`/`opaque` helpers and the
  manual key routing in `Program.update` collapse into framework calls.
- The HTML/Alpine prototype (`prototype/agent-harness.html`) is the design reference — it
  shows the _intended_ behavior (real buttons, mouse clicks, `<dialog>`, syntax-highlighted
  code blocks, the MCP manager) the TUI is chasing.
