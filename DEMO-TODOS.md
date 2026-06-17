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
- ✅ Braille spinner for running tools / sidebar tasks — `Widgets.Spinner` glyphs driven by a `Sub.Every` tick
- ✅ Toast stack (top-right) via `Widgets.Toast.stack`, TTL auto-dismiss app-side
- ✅ Command palette (`Ctrl+P`) with ranked fuzzy filter (`Widgets.CommandPalette.filter`) over `Dummy.commands`
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
- 🟡 **Transcript scroll** — renders through `ScrollView.vertical` (scrollbar) now; follow-tail on append is still app-side via `maxScroll` (fine — the app owns the offset)
- 🟡 **Stream cancel** — `Esc` clears a flag; not a real async cancellation token

## Framework caught up — demo not migrated 🔄

The original blockers here all shipped (v0.2/v0.3); what remains is demo-side adoption.
Each is small, independent, and stress-tests a widget — ROADMAP "What's next" item 2:

- [x] **`Cmd.quit`** — the `quit`/`exit` command exits cleanly via `Cmd.quit` (Ctrl+C still works via the default quit policy). _Done._
- [x] **`Widgets.Spinner`** — `Blocks.spinner` delegates to `Spinner.frameOf Spinner.braille` instead of a hand-rolled frame table. _Done._
- [x] **OSC 52 clipboard (dogfood)** — the `copy` command puts the last assistant message on the system clipboard via `Cmd.setClipboard`. _Done._
- [x] **OSC 8 links (dogfood)** — transcript markdown links carry real URLs through `Widgets.Markdown` → `Style.Link` → `Diff` OSC 8 (no demo code needed; it falls out of the framework wiring). _Done._
- [x] **`Widgets.CommandPalette.filter`** — the palette ranks with the framework's best-first fuzzy `filter` (subsequence) instead of a substring match. _Done._
- [x] **`ScrollView`** — the transcript pane uses `ScrollView.vertical` (track/thumb scrollbar). _Skill-explorer + MCP-tools panes still on bare `Scroll`._
- [x] **`Overlay.centered`** — overlays center via the framework's `Overlay.centered` (`Positioned`/`Center`); the local Dock-spacer helper is gone. _Done (the `opaque` backdrop helper stays — it's used by the anchored completion popup + toasts)._
- [x] **`Widgets.Modal`** — the permission modal, skill explorer, and `/mcp` views render through `Modal.modal` (backdrop + centered titled box); their leading title row moved into Modal's title slot. _Done (the palette keeps its title-less finder chrome)._
- [x] **`Widgets.Table`** — the `TableBlock` card renders through `Table.view` (one `Column` per header, value-toned cells, static). _Done._
- [x] **Mouse** — wheel scrolls the transcript (SGR-1006 `ScrollUp`/`ScrollDown` → `ScrollWheel`), and a left-click activates the permission modal's Accept/Deny buttons (`MouseClick` → `modalButtonHit` → `resolveModal`). _Done. List-row click-through elsewhere still uses keyboard; full retained hit-testing is ROADMAP v0.5._
- [x] **`Mire.Layout.Focus`** — the base prompt ⟷ transcript Tab ring uses the framework focus ring (`Focus.ofOrder`/`next`/`isFocused`), replacing the bespoke `FocusRegion` DU. _Done. Overlay routing intentionally stays a `Model.Overlay` modal match — overlays are a single-at-a-time stack carrying their own state, so trapping would be redundant (unlike `Mire.Demo.Feed`'s genuine two-pane base ring)._

## Blocked by the framework ⬜

- ⬜ **Text selection** — `TextBuffer` has no selection, so neither does the prompt. → ROADMAP `Input`/`TextArea` gaps
- ⬜ **Images** — no Kitty-graphics previews. → ROADMAP v0.5 `ImagePreview`
- ⬜ **Markdown inside a heading** — heading lines aren't inline-parsed (kept whole). Cosmetic; lives in `Widgets.Markdown` now.

_(Resolved since this list was written: OSC 8 hyperlinks, Kitty key release/repeat event types, and large-paste reassembly all shipped in the framework — see the adoption list above and `CHANGELOG.md`.)_

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
