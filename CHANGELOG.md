# Changelog

All notable changes to the Mire project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **`TextBuffer` (`Mire.Core`) + `Input` (`Mire.Widgets`)** — the missing text-editing primitive. `TextBuffer` is a pure single-line buffer (`{ Text; Cursor }`) with `insert`/`backspace`/`delete`/`left`/`right`/`home`/`toEnd`/`deleteWordBack` — every op returns a new buffer. `Input.render width textStyle cursorStyle focused buf` draws it as one line with a block cursor and horizontal scroll-to-cursor. `Mire.SpreadsheetDemo`'s cell editor now uses them (real mid-line cursor: ←/→ move within the cell, Home/End, forward-Delete), replacing the append-only string. 8 new tests (`TextBuffer` ops + `Input` cursor/scroll). Next consumer: the agent demo's `PromptInput`.
- **Mire.SpreadsheetDemo** — an A1 spreadsheet (`dotnet run --project Mire.SpreadsheetDemo`, or `-- --dump`): a navigable grid with a cell cursor, in-cell editing, a formula/edit bar, and a small formula engine — `+ - * /`, parentheses, cell refs, ranges, and `SUM`/`AVG`/`MIN`/`MAX`/`COUNT`, recomputed with memoization and cycle detection (`#CYCLE`/`#DIV/0`/`#SYNTAX`). The cell cursor's **full-cell highlight comes for free from the new `Backdrop.behind`**; the gaps it had to hand-roll are the signal: no `Table`/grid widget (nested `Stack`s + fixed column widths), no `TextBuffer`/`Input` (append+backspace editing with the cursor pinned at the end — same shape as the agent demo's `PromptInput`), no number formatting/alignment, no text-clip ellipsis (the footer hard-clips), and no quit-from-`update` (Ctrl+C only).
- **`Backdrop.behind` + `ListView` (`Mire.Widgets`)** — the full-bleed selection primitive and a small selectable list built on it. `Backdrop.behind style child` fills the node's whole rect with `style`, then draws the child on top — fixing the long-standing "highlight only covers the glyphs" problem (a styled `Text` colours only the cells under its characters). `ListView.view height selStyle rowStyle sel labels` renders a single-selection, scrollable list that highlights the selected row **full width** and auto-scrolls to keep it visible. The agent-demo command palette and `Mire.FeedDemo`'s article list both use it.
- **Mire.FeedDemo** — a terminal RSS reader (`dotnet run --project Mire.FeedDemo`, or `-- --dump`). Fetches a feed (default `https://helgesver.re/rss/feed.xml`) via `HttpClient`, parses RSS 2.0 with `System.Xml.Linq`, reduces `content:encoded` HTML to wrapped plain text, and renders a two-pane list/reader: async load (`Cmd.ofAsync`), loading spinner (`Sub.Every`), keyboard navigation (list ↔ reader panes), and a **full-width selection highlight** done as a `Filled` backdrop under the row (the row-fill a bare `Text` node can't produce — see below). Built only on existing layout primitives; the gaps it had to work around are useful signal: no `List`/`ScrollView`/`Table` widget, no text-wrap or HTML widget (hand-rolled `Feed.wrap` / `htmlToText`), no intrinsic measurement available in `view` (pane sizes computed by hand from `Size`), `Box.panel`'s multi-child overlap (hand-rolled a single-child panel), and no quit-from-`update` (relies on the Ctrl+C intercept).
- **Mire.Tests** — an [Expecto](https://github.com/haf/expecto) test project (`dotnet run --project Mire.Tests`, or `dotnet test`) covering the pure functions: `Grapheme` width, `InputParser.parseBytes` (printable, Enter, arrows, Shift+Tab, Ctrl+C), `Diff.compute` (no-change, single-cell, full render), and `Layout.measure`/`render` (stack flow, dock `Content` sizing, `Fill` distribution, scroll offset+clipping). 18 tests.
- **Mire.AgentDemo** — a second runnable demo (`dotnet run --project Mire.AgentDemo`): an interactive agent shell that showcases the agentic-TUI vision from `SPEC.md` as a testbed. It is **not** wired to an LLM — a `Dummy` module maps the submitted prompt to canned responses (`markdown`, `stream:long`, `tool`/`tool:error`/`tool:run`, `diff`, `table`, `thinking`, `permission`, `warning`, … — type `help`). Demonstrates, at the app level: a markdown-ish renderer, transcript/tool/diff/table/thinking cards, streaming (`Sub.Every`) and async tool resolution (`Cmd.ofAsync`), a toast stack, a command palette (`Ctrl+P`), a **skill-explorer overlay with dual scroll panes** (`Ctrl+O`), a focusable Accept/Deny permission modal, and a `Shift+Tab` mode switch. These are built on the existing layout primitives — the agent-domain widgets remain unbuilt in the framework (see `ROADMAP.md`). Honest about its approximations in [`DEMO-TODOS.md`](DEMO-TODOS.md). Includes a headless `--dump` mode like `Mire.Demo`.
- **`prototype/agent-harness.html`** — a brand-faithful HTML/Alpine.js design prototype of the agent-shell screens (transcript, palette, skill explorer, a native `<dialog>` permission modal with real mouse-clickable buttons, toasts, mode switch). A visual reference for where the TUI is headed.
- **Layout engine completed** — the `Stack`/`Scroll`/`Overlay` pass-through stubs now do real work:
  - `Stack` flows children sequentially along its axis with per-child `Length`. New `StackChild<'msg>` record (`{ Length; Child }`); `Stack` case is now `Stack of Rect * Direction * StackChild<'msg> list`. `Cells`/`Fraction`/`Content` are measured first, then `Fill` children split the remainder evenly (leftover cells go to the first `Fill` children).
  - `Scroll` carries a `ScrollState` (`Scroll of Rect * ScrollState * LayoutNode<'msg>`). Its child is measured onto an off-screen content surface, then the window selected by the offset is blitted into the viewport — giving genuine scroll offset **and** clipping. Offsets are clamped so over-scroll can't reveal blank gaps.
  - `Content` and `Fill` dock lengths now work on every side (`Top`/`Bottom`/`Left`/`Right`), not just `Cells`/`Fraction`. `Content` sizes to the child's intrinsic extent; `Fill` consumes the remaining axis.
  - `Layout.contentExtent` — intrinsic size of a node along an axis (line count / max line width for `Text`, border-aware for `Box`, axis-aware for `Stack`), used to size `Content` children and `Scroll` backing surfaces.
- **`Filled of Rect * Style`** layout node — an opaque rectangle (spaces in a style). Serves as a panel background, modal backdrop, or selection highlight, and gives `Overlay` real opacity: a `Filled` layer occludes whatever it covers.
- **Mire.Widgets** new helpers for the above:
  - `Stack.sized`, `Stack.stackOf`/`vstackOf`/`hstackOf` (explicit per-child lengths); `vstack`/`hstack` now default each child to `Content`.
  - `Scroll.scrollState` / `Scroll.vertical`
  - `Backdrop.solid`
- **Headless `--dump` mode** in `Mire.Demo` (`dotnet run --project Mire.Demo -- --dump`) — lays representative trees through `Layout.measure`/`Layout.render` onto a `Surface` and prints the cell grid as text. Verifies layout (dock `Content` sizing, stack `Fill` distribution, scroll offset/clipping, overlay opacity) without taking over the terminal.
- **Mire.Demo** rewritten from the counter into a scrollable-list app demonstrating `Stack` + `Scroll` (`↑`/`↓` scroll, `PgUp`/`PgDn`, `Home`/`End`).

- **Mire.Widgets** — new project providing a convenience widget layer:
  - `Text.text`, `Text.title`, `Text.dimText` for styled text nodes
  - `Box.box`, `Box.panel` for bordered containers with titles
  - `StatusBar.statusBar`, `StatusBar.statusBarSimple` for horizontal status bars
  - `Spacer.spacer` for empty fill space
  - `Dock.top`/`bottom`/`left`/`right`/`fill` helpers for dock layouts
  - `Stack.vstack`, `Stack.hstack` for vertical/horizontal stacks
  - Predefined semantic styles: `border`, `title`, `text`, `dim`, `counter`, `highlight`, `success`, `warning`, `danger`, `info`
- **Grapheme width handling** in `Mire.Core.Grapheme`:
  - `charWidth(c)` returns 0, 1, or 2 for combining marks, normal, and wide characters (CJK, fullwidth forms, hangul, kana)
  - `stringWidth(s)` sums widths across a string
  - `Surface.Write` and `Surface.WriteClipped` now advance the cursor by the correct width
  - Combining diacritical marks are appended to the previous cell's grapheme instead of overwriting
- **Program builder API** in `Mire.App`:
  - `Program.mkProgram init update view`
  - `Program.withMapInput`
  - `Program.withSubscriptions`
  - `Program.withOnError`

### Changed

- **Project structure consolidated.** The six per-layer framework assemblies (`Mire.Core`, `Mire.Protocol`, `Mire.Renderer`, `Mire.Layout`, `Mire.Widgets`, `Mire.App`) were merged into a **single `Mire` project** organized by folder (`Core/`, `Protocol/`, `Renderer/`, `Layout/`, `Widgets/`, `App/`). Namespaces are unchanged (`Mire.Core`, `Mire.Layout`, …), so no source code changed — `open Mire.Layout` etc. still work. The layering is now enforced by the `<Compile>` order in `Mire/Mire.fsproj` rather than by `ProjectReference`s. Rationale: ~1.5k LOC didn't justify six assemblies of build/reference ceremony; F#'s linear compile model already enforces the dependency direction within a project.
- The solution (`Mire.slnx`) is now four projects: `Mire`, `Mire.Demo`, `Mire.AgentDemo`, `Mire.Tests`.
- **InputParser** now decodes **Shift+Tab** (legacy backtab `ESC [ Z`) as `Tab` with the `Shift` modifier — the first modified-key sequence beyond `Ctrl+letter`. (Terminals in full Kitty-keyboard mode that send the `CSI u` form are still not decoded; see `DEMO-TODOS.md` / ROADMAP.)
- **InputParser** completely rewritten for raw terminal mode:
  - Replaced `Console.KeyAvailable` / `Console.ReadKey` (which crashed in raw mode) with `TerminalMode.stdinAvailable()` + `TerminalMode.readStdinBytes()` via libc `poll()` / `read()` P/Invoke
  - Now parses ANSI escape sequences for arrows, function keys (F1–F12), Home, End, Page Up, Page Down, Insert, Delete
  - Control characters (Ctrl+A–Ctrl+Z) are mapped correctly
- **Layout.Text** now carries a `Rect` for positioning:
  - `Text of Rect * string * Style` instead of `Text of string * Style`
  - `measure` assigns the available rect; `render` paints via `Surface.WriteClipped`
- **Mire.Demo** rewritten to use the Elmish `Program` runtime instead of hand-rolled terminal I/O
  - Reduced from ~200 lines of direct Surface/diff/render code to ~50 lines of model/update/view
- **Runtime.run** exception handling:
  - Per-loop `try/with` wraps the frame so `program.OnError` is called without crashing the terminal
  - Cleanup (alternate screen exit, cursor restore, style reset, `stty sane`) still runs in `finally`

### Fixed

- **Selection highlight only covered the text, not the full row** (command palette; any styled-background row). A styled `Text` node paints a background only under its glyphs, leaving the gaps and the row's tail at the default colour. Selected rows now wrap their content in `Backdrop.behind`, filling the whole row with the selection colour.
- **Arrow keys didn't work in application-cursor-key mode (DECCKM)** — notably in JetBrains' JediTerm, which sends `ESC O A`/`B`/`C`/`D` for the arrows where most terminals send `ESC [ A`/…. The SS3 (`ESC O …`) branch only decoded F1–F4/Home/End, so those arrows were dropped (they worked in Kitty/Ghostty, which use the `ESC [` form). Added `ESC O A/B/C/D` → arrow keys. 3 new tests; verified end-to-end (SS3 Down scrolls `Mire.Demo`).
- **Modifier keys (Ctrl/Alt/Shift/Super) were never detected** in Kitty-protocol terminals (Ghostty, Kitty) — e.g. `Ctrl+P` / `Ctrl+O` did nothing while plain keys worked. `Runtime.run` enables the Kitty keyboard protocol (`CSI > 1 u`), so those terminals send modified keys in the Kitty **`CSI u`** form (`Ctrl+P` → `ESC [ 112 ; 5 u`), but `InputParser` only decoded legacy sequences and dropped `CSI u`. (Diagnosed by capturing raw bytes from Kitty, Ghostty, and JediTerm — JediTerm ignores the protocol and still sends legacy `0x10`, which already worked.) Fixed by rewriting the CSI parser to decode the Kitty encoding — `ESC [ <codepoint> ; <modifiers> u` → `Char`/named key + `KeyModifiers` (Kitty _super_ → `Meta`) — plus modified navigation keys (`ESC [ 1 ; <mod> A/B/C/D/H/F`, `ESC [ <n> ; <mod> ~`). All legacy decoding (plain arrows, `Ctrl+letter`, `Shift+Tab`, F-keys) is preserved. 7 new `InputParser` tests; verified end-to-end (real Kitty `Ctrl+P` opens the palette, `Ctrl+O` the skill explorer in `Mire.AgentDemo`). Release/repeat event types are still not requested (only the disambiguate flag is pushed).
- **No keyboard input was detected at all** — every demo looked frozen (no key, arrow, or scroll registered). `TerminalMode.stdinAvailable()` called libc `poll` with the `pollfd` passed as a managed **array** (`poll([|pfd|], …)`); the kernel's write to `revents` was never marshaled back to managed memory, so `pfd.Revents &&& POLLIN` was always `0`, `stdinAvailable` always returned `false`, and `InputParser.readEvent` always returned `None`. Fixed by passing the struct **by reference** — `extern int poll(PollFd& fds, …)` called as `poll(&pfd, …)` — which marshals as `pollfd*` (exactly what poll wants for `nfds = 1`) and is in/out, so `revents` is written back. Verified end-to-end on macOS (.NET 10): arrows, characters, and Home/End now drive `Mire.Demo`. (`read`'s `byte[]` buffer was unaffected — primitive arrays are pinned in place, so its writeback always worked.)
- `Console.KeyAvailable` `InvalidOperationException` in raw mode — the App runtime now works end-to-end
- `Surface` constructor no longer crashes on invalid terminal sizes (0×N or negative dimensions); clamps to minimum 1×1
- `TerminalMode.getTerminalSize()` now returns `None` instead of invalid `Size` when `Console.WindowWidth/Height` report zero or negative
- `Mire.App` `AsyncCmd` dispatch now correctly passes the `Async<unit>` to `Async.Start` without wrapping in a redundant `async { do ... }` block
- `Mire.Layout` incomplete pattern match warning on `DockPosition` — added catch-all cases for `Content`/`Fill` lengths inside `Top`/`Bottom`/`Left`/`Right`

## [0.1.0] — 2025-05-28

### Added

- Initial foundation with 6 projects:
  - **Mire.Core** — `Point`, `Size`, `Rect`, `Color`, `Style`, `Cell`, `Region`, `InputEvent` types
  - **Mire.Protocol** — ANSI escape sequences, raw terminal mode via `stty`, terminal size detection
  - **Mire.Renderer** — `Surface` cell grid, `Diff` engine with run-based output, `Diff.renderToTerminal`
  - **Mire.Layout** — `Dock`, `Stack`, `Box`, `Scroll`, `Overlay` layout nodes with `measure` and `render`
  - **Mire.App** — Elmish `Program<'model,'msg>`, `Cmd<'msg>`, `Sub<'msg>`, `Runtime.run` loop
  - **Mire.Demo** — interactive counter demo with direct Surface rendering
- Truecolor RGB styles with fluent `WithForeground` / `WithBold` / `WithDim` helpers
- Box-drawing primitives (`DrawBox`, `DrawFilledBox`, `DrawHorizontalLine`, `DrawVerticalLine`)
- Kitty keyboard protocol enable/disable sequences
- Mouse tracking, focus events, bracketed paste, synchronized output sequences
