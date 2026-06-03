# AgentDemo — demo TODOs

What the `Mire.AgentDemo` demo can and can't do yet. The demo deliberately renders
the _shape_ of agentic-TUI features even where the framework doesn't support them, so this
file tracks which parts are real and which are faked/blocked. Each gap links to the
[`ROADMAP.md`](ROADMAP.md) item that would make it real.

> The harness builds agent-domain UI at the _app level_ (it must — `CLAUDE.md`/ROADMAP keep
> agent concepts out of the core libraries). So "done" here means "works in the demo", not
> "exists as a reusable widget". The widget/agent boxes in ROADMAP stay ⬜ regardless.

**Legend:** ✅ works in the harness · 🟡 partial / approximate · ⬜ faked or blocked

---

## Works today ✅

- ✅ Interactive agent shell: status header, scrolling transcript, prompt, footer hints
- ✅ Canned responses via the `Dummy` module (see the table in `Dummy.fs` / type `help`)
- ✅ Streaming text — one word per `Sub.Every 45ms` tick; `Esc` interrupts
- ✅ Async tool resolution — `tool:run` fires `Cmd.ofAsync`, resolves after a delay
- ✅ Braille spinner for running tools / sidebar tasks via `Sub.Every`
- ✅ Toast stack (top-right) with TTL auto-dismiss
- ✅ Command palette (`Ctrl+P`) with live substring filter over `Dummy.commands`
- ✅ Skill explorer (`Ctrl+O`) — two independent `Scroll` panes (list + markdown preview), `Tab` switches pane focus
- ✅ Permission / approval / confirm modal with **keyboard-focusable** Accept/Deny buttons (`←/→`/`Tab` move, `Enter` confirm, `Esc` deny)
- ✅ Mode switch on `Shift+Tab` (`normal → auto-accept → plan`) — proves modifier-key detection
- ✅ `Ctrl+P` / `Ctrl+O` bindings (decoded as `Char` + `Ctrl` modifier)
- ✅ Resize-aware text wrapping (tracks terminal `Size` via `Sub.TerminalResize`)
- ✅ Sidebar toggle (`panel` / `split`), transcript scroll (arrows / PgUp/PgDn / Home/End)
- ✅ Headless `--dump` mode (renders sample screens A–G as text)

## Approximate 🟡

- 🟡 **Markdown** — line-oriented and minimal: headings, emphasis (`**`/`*`/`` ` ``/`~~`), lists, blockquote, fenced code, rule, links. No CommonMark, no nested-block parsing, no syntax highlighting. → ROADMAP `Markdown` widget (v0.3)
- 🟡 **Diff** — unified only, colored `+`/`-`; no split view, no accept/reject. → ROADMAP `DiffView` (v0.4)
- 🟡 **Table** — fixed column widths from content; no virtualization, sticky header, or sort. → ROADMAP `Table` (v0.3)
- 🟡 **Transcript scroll** — follows tail by jumping to the bottom on append; real follow-tail/scrollback nuance lives in `ScrollView`. → ROADMAP `ScrollView` (v0.2/v0.3)
- 🟡 **Stream cancel** — `Esc` clears a flag; not a real async cancellation token.

## Faked or blocked ⬜

- ⬜ **Mouse clicks** on modal buttons / list rows — `InputParser` doesn't decode mouse (SGR 1006). Buttons are laid out as discrete nodes so a hit-test can target them later. Keyboard works today. → ROADMAP input decoding (v0.2), richer mouse (v0.5)
- ⬜ **Real text input** — the prompt is a placeholder (`PromptInput.fs`): append + Backspace only, cursor pinned at the end. No mid-line cursor, selection, or paste. → ROADMAP `Input`/`TextArea` (v0.3)
- ⬜ **Full Kitty keyboard modifiers** — only `Ctrl+letter` and `Shift+Tab` (legacy `CSI Z`) are decoded. Under full Kitty mode some terminals send the `CSI u` form, which isn't parsed — `Shift+Tab` may not register there. → ROADMAP full Kitty decode (v0.2/v0.5)
- ⬜ **Overlay positioning** — modals/palette are centered by insetting with `Dock` margin spacers; `Overlay` can't anchor/center natively. Opacity is faked with a `Filled` backdrop that occludes the base. → ROADMAP overlay positioning (v0.2)
- ⬜ **Focus trap** — no focus manager; the harness routes keys manually based on its own `Overlay` field. → ROADMAP focus manager (v0.2)
- ⬜ **Scrollbars** — `Scroll` has no track/thumb; panes show `▲▼` hints only. → ROADMAP `Scrollbar`/`ScrollView` (v0.2)
- ⬜ **Hyperlinks** — markdown links render underlined; cells don't carry OSC 8 links. → ROADMAP `Cell.Link` (v0.5)
- ⬜ **Quit from update** — Deny/`Esc` only close the modal; the app exits only via the runtime's hard-coded `Ctrl+C`. → ROADMAP `Cmd.quit` (v0.2)
- ⬜ **Images** — no Kitty-graphics previews attempted. → ROADMAP `ImagePreview` (v0.5)
- ⬜ **Markdown inside a heading** — heading lines aren't inline-parsed (kept whole). Cosmetic.

---

## Notes for whoever picks this up

- When `Mire.Widgets`/`Mire.Core` grows a real `Input`/`TextBuffer`, replace `PromptInput.fs`
  and the prompt's `mapInput` cases — nothing else should need to change.
- When the framework gets a focus manager + overlay anchoring, the `centered`/`opaque`
  helpers and the manual key routing in `Program.update` collapse into framework calls.
- The HTML/Alpine prototype (`prototype/agent-harness.html`) is the design reference — it
  shows the _intended_ behavior (real buttons, mouse clicks, `<dialog>`) the TUI is chasing.
