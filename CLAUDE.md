# CLAUDE.md

Guidance for working in this repository.

## What this is

**Mire** is an early-stage F# retained-mode TUI runtime — Elmish/TEA adapted for the terminal. Targets **.NET 10** (`net10.0`) and modern Kitty-compatible terminals (Ghostty first). Legacy/16-color/Windows-console support is explicitly out of scope.

[`SPEC.md`](SPEC.md) is the design vision (~3900 lines) and describes far more than is built. When in doubt, **the code is the source of truth for what exists**; the spec is the source of truth for intended direction. Don't assume a widget/feature exists just because the spec describes it — check the actual `.fs` files first.

[`ROADMAP.md`](ROADMAP.md) is the plan of record — a widget/node reference table with status (✅/🟡/⬜) and the release plan. Releases 0.4.0–0.7.0 have shipped to NuGet (core framework, the agent layer, input/perf completeness); what's left is a few deferred stretch items and an eventual 1.0 hardening pass. Consult it for what's done; keep its checkboxes honest as you ship.

## Build & run

```sh
dotnet build Mire.slnx                     # build the whole solution (solution is .slnx, the modern XML format)
dotnet run --project Mire.Demo.Agent       # the agent-shell demo (comprehensive/canonical showcase; not wired to an LLM)
dotnet run --project Mire.Demo.Feed        # multi-feed RSS reader
dotnet run --project Mire.Demo.Spreadsheet # A1 grid + formula engine
dotnet run --project samples/AgentShell    # MVP shell on the Mire.Agent `AgentShell.program` builder
dotnet run --project samples/Gallery       # every base widget in its states, on the default theme
dotnet run --project Mire.Demo.Agent -- --dump  # headless: print sample layouts as text (no raw mode) — use this to verify layout
dotnet run --project Mire.Tests            # run the Expecto test suite
dotnet build Mire/Mire.fsproj              # build just the framework
```

Tests live in **Mire.Tests** (Expecto): `dotnet run --project Mire.Tests` (or `dotnet test`). They cover the pure functions — `Layout.measure`, `Diff.compute`, `Grapheme`, `InputParser`, the editing stack, and the `Mire.Agent` model/shell logic. When you change those, add/extend a test.

Formatting is **Fantomas** (F#) + oxfmt (markdown), wired as `just format` / `just lint` / `just check` (Fantomas is pinned in `.config/dotnet-tools.json` — `dotnet tool restore` first). CI (`.github/workflows/ci.yml`) builds the solution and runs the tests but does **not** enforce formatting, so run `just format` yourself before committing F# changes.

The `--dump` mode (every `Mire.Demo.*`, `samples/AgentShell`, and `samples/Gallery`) complements the tests: it lays representative trees through `Layout.measure`/`Layout.render` (the exact path the runtime uses) onto a `Surface` and prints the cell grid as plain text, so layout changes can be eyeballed without taking over the terminal.

Verifying TUI changes is hard to automate: the demo takes over the terminal (alternate screen, raw mode). Prefer to verify correctness by building (`dotnet build`) and by reasoning about the pure functions (`Diff.compute`, `Layout.measure`, `Style.ToAnsi`, parsers). Don't claim runtime behavior works unless you've actually run it.

## Project layout & dependency order

Projects in `Mire.slnx` — the framework, the optional agent layer, three `Mire.Demo.*` exes, two `samples/`, and the tests:

```
Mire                   the framework — one assembly, layered by folder
Mire.Agent             optional agent-UI layer (ChatTranscript, PromptBox, ApprovalModal,
                       DiffView, Conversation, AgentShell) — references Mire ONLY
Mire.Demo.Agent       ─┐  agent-shell demo (comprehensive/canonical showcase)
Mire.Demo.Feed         ├─ reference Mire (+ Mire.Agent for the Agent demo)
Mire.Demo.Spreadsheet  │  A1 grid + formula engine
Mire.Tests             │  Expecto tests
samples/AgentShell     │  MVP shell on the AgentShell builder
samples/Gallery       ─┘  pure-framework widget gallery
```

`Mire.Agent` sits above `Mire.App` and must never be depended on by the base framework — the dependency chain is one-directional (the framework never knows what an LLM is).

The **framework is a single project** (`Mire/`) organized by folder, where the
folder order encodes the layering. Each folder is also its namespace
(`Mire.Core`, `Mire.Protocol`, …) so consumers still `open Mire.Layout` etc.

```
Mire/Core/       pure value types (Point, Size, Rect, Color, Style, Cell, Region, Grapheme, input events). No I/O.
Mire/Protocol/   ANSI (escape-sequence strings), TerminalMode (raw mode via stty + libc poll/read), InputParser (bytes → InputEvent).
Mire/Renderer/   Surface (cell grid + draw primitives), Diff (DiffRun computation + writing to a TextWriter). Diff uses Protocol.ANSI.
Mire/Layout/     LayoutNode<'msg> DU, Layout.measure (assigns rects), Layout.render (paints to a Surface).
Mire/Widgets/    convenience widgets (Text, Box, Panel, StatusBar, Spacer, Dock, Stack, Scroll, Backdrop, ListView, Table, ScrollView, Input, TextArea, Modal, Toast, Completion, CommandPalette, Tooltip, SplitView, Separator, Badge, KeyHint, Spinner, ProgressBar, Tabs, Toggle) + predefined styles; Markdown.fs is the markdown renderer.
Mire/App/        Cmd, Sub, Program, Program builders, Runtime.run (the ~30 FPS Elmish loop).
```

The dependency direction (Core → Protocol → Renderer → Layout → {Widgets, App})
is enforced by the **`<Compile>` order in `Mire/Mire.fsproj`**, not by assembly
boundaries. Keep that order one-directional: a file must appear after everything
it depends on. Widgets and App both sit on Layout and don't depend on each other.

## F#-specific things that will bite you

- **Compile order is significant — and it's the whole layering mechanism now.** F# requires definitions before use, both within a file and across files. The `<Compile Include=.../>` order in `Mire/Mire.fsproj` is the build order _and_ the dependency contract: `Core/* → Protocol/* → Renderer/* → Layout/*.fs → Widgets/*.fs → App/Program.fs`. If you add a file, insert its `<Compile>` entry in the right folder block, in the right position. Referencing a type defined in a later file fails the build — that's the guardrail that keeps the layering honest within the single assembly.
- Value types use `[<Struct>]` (`Point`, `Size`, `Rect`, `Color`, `Style`, `Cell`). Records are immutable; "mutation" is done with `with` copies. `Style` exposes fluent `WithForeground`/`WithBold`/… helpers that return new records — follow that pattern rather than adding mutable fields.
- `Color` is a struct DU (`Rgb of byte*byte*byte | Default`) — colors are truecolor RGB. Style/color → ANSI conversion lives on the types themselves (`Color.ToAnsiFg`, `Style.ToAnsi`).
- The runtime loop is hand-rolled and uses a `mutable state` record reassigned with `{ state with ... }`, plus a locked message queue. Match that style if you extend the loop.

## Conventions

- Namespaces match the framework's folder names (`Mire.Core`, `Mire.Protocol`, `Mire.Renderer`, `Mire.Layout`, `Mire.Widgets`, `Mire.App`) — all live in the single `Mire` assembly.
- ANSI sequences belong in `Mire.Protocol/ANSI.fs` — don't scatter raw `\x1b[...]` literals through rendering code; add a named binding there and reference it (see how `Diff` and `Runtime` use `ANSI.cursorTo`, `ANSI.resetStyle`, etc.).
- Drawing always goes through `Surface` primitives (`Write`, `WriteClipped`, `DrawBox`, `FillRect`, `DrawHorizontalLine`, …). All of them already bounds-check against the surface size — keep new primitives safe the same way.
- Rendering must stay diff-based: build a full `Surface`, let `Diff.compute` find the changed runs. Don't write directly to the terminal from view/layout code.

## When extending the framework

- New layout nodes go in `Mire/Layout/Layout.fs` — add the case to the `LayoutNode<'msg>` DU and handle it in **both** `measure` and `render`. `Stack`/`Scroll`/`Dock` (with `Content`/`Fill`), `Filled`, `Scrim`, and `Positioned` are implemented; `Overlay` z-orders (`Filled` occludes, `Scrim` fades the layers beneath it), and `Positioned` places a sized layer at a 9-point `Placement` within the area (see `ROADMAP.md`).
- New input decoding goes in `Mire/Protocol/InputParser.fs`. Decoding is **feature-complete for the targeted terminals**: keys + Ctrl chords + Shift+Tab, the Kitty `CSI u` chords with press/repeat/release **event types** and functional codepoints, mouse (SGR 1006, incl. the drag-motion bit → `MouseEvent.Moved`), bracketed paste (reassembled across reads), and focus events; `parseAll` splits a multi-sequence buffer into per-event spans. The remaining gaps are stretch items (hover mode 1003, Kitty flags 4/8/16) — not bugs to "rediscover." See `docs/PROTOCOLS.md`.
- The base widgets live in `Mire/Widgets/`. Agent-domain components live in the optional **`Mire.Agent`** layer (`ChatTranscript`, `PromptBox`, `ApprovalModal`, `DiffView`, the `Conversation` model, and the `AgentShell` program builder), which references `Mire` only — the base framework must never depend on agent concepts. New agent UI goes there, not in `Mire/`.
