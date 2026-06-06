# CLAUDE.md

Guidance for working in this repository.

## What this is

**Mire** is an early-stage F# retained-mode TUI runtime — Elmish/TEA adapted for the terminal. Targets **.NET 10** (`net10.0`) and modern Kitty-compatible terminals (Ghostty first). Legacy/16-color/Windows-console support is explicitly out of scope.

[`SPEC.md`](SPEC.md) is the design vision (~3900 lines) and describes far more than is built. When in doubt, **the code is the source of truth for what exists**; the spec is the source of truth for intended direction. Don't assume a widget/feature exists just because the spec describes it — check the actual `.fs` files first.

[`ROADMAP.md`](ROADMAP.md) is the plan of record — a widget/node reference table with status (✅/🟡/⬜) and the phased plan (v0.1–v0.5). Consult it for what's done and what's next; keep its checkboxes honest as you ship.

## Build & run

```sh
dotnet build Mire.slnx                     # build the whole solution (solution is .slnx, the modern XML format)
dotnet run --project Mire.Demo.List        # the scrollable-list demo
dotnet run --project Mire.Demo.Agent       # the agent-shell demo (feature testbed; not wired to an LLM)
dotnet run --project Mire.Demo.Minesweeper # keyboard Minesweeper (arrows/WASD, Space reveal, F flag, C chord)
dotnet run --project Mire.Demo.KitchenSink # comprehensive widget showcase (sidebar gallery; ↑/↓ select, Tab to drive)
dotnet run --project Mire.Demo.List -- --dump  # headless: print sample layouts as text (no raw mode) — use this to verify layout
dotnet run --project Mire.Tests           # run the Expecto test suite
dotnet build Mire/Mire.fsproj             # build just the framework
```

There **is** a test project now: **Mire.Tests** (Expecto). Run it with `dotnet run --project Mire.Tests` (or `dotnet test`). It covers the pure functions — `Layout.measure`, `Diff.compute`, `Grapheme` width, `InputParser`. When you change those, add/extend a test. There is no linter/formatter configured.

The `--dump` mode (in `Mire.Demo.List`, `Mire.Demo.Agent`, and `Mire.Demo.KitchenSink`) complements the tests: it lays representative trees through `Layout.measure`/`Layout.render` (the exact path the runtime uses) onto a `Surface` and prints the cell grid as plain text, so layout changes can be eyeballed without taking over the terminal.

Verifying TUI changes is hard to automate: the demo takes over the terminal (alternate screen, raw mode). Prefer to verify correctness by building (`dotnet build`) and by reasoning about the pure functions (`Diff.compute`, `Layout.measure`, `Style.ToAnsi`, parsers). Don't claim runtime behavior works unless you've actually run it.

## Project layout & dependency order

Eight projects — the framework, six `Mire.Demo.*` example exes, and the tests:

```
Mire                   the framework — one assembly, layered by folder
Mire.Demo.List        ─┐  scrollable-list demo
Mire.Demo.Agent        │  agent-shell demo / testbed
Mire.Demo.Feed         │  RSS reader
Mire.Demo.Spreadsheet  ├─ all reference Mire
Mire.Demo.Minesweeper  │  keyboard Minesweeper
Mire.Demo.KitchenSink  │  widget showcase / gallery
Mire.Tests            ─┘  Expecto tests
```

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

- New layout nodes go in `Mire/Layout/Layout.fs` — add the case to the `LayoutNode<'msg>` DU and handle it in **both** `measure` and `render`. `Stack`/`Scroll`/`Dock` (with `Content`/`Fill`) and the `Filled`/`Positioned` nodes are implemented; `Overlay` z-orders + occludes, and `Positioned` places a sized layer at a 9-point `Placement` within the area (see `ROADMAP.md`).
- New input decoding goes in `Mire/Protocol/InputParser.fs`. Keys, Ctrl chords, Shift+Tab, the Kitty `CSI u` modifier form, and **mouse (SGR 1006) / bracketed paste / focus events all decode now**. The remaining gap is Kitty release/repeat **event types** (only `Press` is emitted) — not a bug to "rediscover."
- The base widgets live in `Mire/Widgets/`. Agent-domain components (chat transcript, tool calls, diff view, …) are **prototyped at the app level in `Mire.Demo.Agent`**, not yet extracted into a reusable layer. The spec puts the reusable versions in an optional layer above `App`; preserve that separation — the framework must not depend on agent concepts.
