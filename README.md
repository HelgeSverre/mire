# Mire

A small, composable F# runtime for building **modern terminal UIs** — coding agents, chat interfaces, log/diff viewers, command palettes, dashboards. Think Elmish/TEA, but with the terminal as the platform: cell-diffed rendering, region-based layout, raw input decoding, and direct control over the terminal protocol.

Mire is **opinionated about its target**. It assumes a modern, Kitty-compatible terminal (Ghostty first) and uses truecolor, the alternate screen, the Kitty keyboard protocol, bracketed paste, mouse tracking, OSC 8 hyperlinks, and synchronized output. There is intentionally no support for legacy consoles, 16-color fallbacks, or "works over every SSH/tmux setup ever."

> **Status: early / foundation.** The runtime, rendering pipeline, layout engine, and primitives exist and run. The widget and agent component libraries described in [`SPEC.md`](SPEC.md) are design, not code yet. See [Current status](#current-status).

## Quick start

Requires the **.NET 10 SDK** (`dotnet --version` should report `10.x`).

```sh
# Build everything
dotnet build Mire.slnx

# Run the interactive demo (a scrollable list)
dotnet run --project Mire.Demo

# Run the agent-shell demo (a feature testbed — not wired to an LLM)
dotnet run --project Mire.AgentDemo

# Verify layout headlessly — prints sample layouts as text, no raw mode
dotnet run --project Mire.Demo -- --dump
dotnet run --project Mire.AgentDemo -- --dump

# Run the test suite (Expecto)
dotnet run --project Mire.Tests
```

The demos open on the alternate screen.

- **Mire.Demo** (scrollable list): `↑`/`↓` scroll, `PgUp`/`PgDn` jump by 10, `Home`/`End` top/bottom, `Ctrl+C` quits.
- **Mire.AgentDemo** (agent shell): type a command and press Enter — try `markdown`, `stream:long`, `tool:run`, `diff`, `permission`, or `help`. `Ctrl+P` command palette, `Ctrl+O` skill explorer, `Shift+Tab` switches mode, `Esc` closes overlays, `Ctrl+C` quits. A `Dummy` module supplies canned responses; what's real vs. faked is tracked in [`DEMO-TODOS.md`](DEMO-TODOS.md), and [`prototype/agent-harness.html`](prototype/agent-harness.html) is a visual design reference.

## The model

Mire follows The Elm Architecture, adapted for terminal realities. You describe your app as a `Program`:

```fsharp
type Program<'model, 'msg> =
    { Init:          unit -> 'model * Cmd<'msg>
      Update:        'msg -> 'model -> 'model * Cmd<'msg>
      View:          'model -> LayoutNode<'msg>
      MapInput:      InputEvent -> 'msg option
      Subscriptions: 'model -> Sub<'msg> list }
```

`Runtime.run` (in `Mire.App`) drives the loop at ~30 FPS: read input → decode to an `InputEvent` → `MapInput` to a message → `Update` the model → build the `View` → lay it out onto a `Surface` → diff against the previous frame → write only the changed cell runs to the terminal.

```
model → view tree → layout → surface → diff → terminal
```

The difference from web UI: there is no browser. Mire *is* the browser — it owns layout, scroll state, focus routing, input normalization, and terminal protocol control as first-class concerns.

## Architecture

Four projects (`Mire.slnx`):

| Project | Depends on | What it holds |
|---|---|---|
| **Mire** | — | The framework, one assembly layered by folder (below). |
| **Mire.Demo** | Mire | A runnable example (`Exe`) — a scrollable list. |
| **Mire.AgentDemo** | Mire | A runnable agent-shell demo (`Exe`) and feature testbed; agent-domain UI built at the app level, not in the framework. |
| **Mire.Tests** | Mire | [Expecto](https://github.com/haf/expecto) tests for the pure functions (`Layout.measure`, `Diff.compute`, `Grapheme` width, `InputParser`). |

The framework is a single assembly organized by folder; the folder order is the layering, enforced by the `<Compile>` order in `Mire/Mire.fsproj`. Each folder is also its namespace, so you still `open Mire.Layout`, `open Mire.Widgets`, etc.

| Folder (namespace) | Depends on | What it holds |
|---|---|---|
| **Mire/Core** | — | Pure value types: `Point`, `Size`, `Rect`, `Color`, `Style`, `Cell`, `Region`, `Grapheme`, and input events. All structs / immutable records. |
| **Mire/Protocol** | Core | `ANSI` escape sequence strings; `TerminalMode` (raw mode setup via `stty` + libc `poll`/`read`); `InputParser` (raw bytes → `InputEvent`). |
| **Mire/Renderer** | Core, Protocol | `Surface` (a `Width × Height` grid of `Cell`s with drawing primitives) and `Diff` (computes minimal `DiffRun`s between two surfaces and writes them out). |
| **Mire/Layout** | Core, Renderer | `LayoutNode<'msg>` tree (`Dock`, `Stack`, `Box`, `Text`, `Filled`, `Scroll`, `Overlay`) plus `measure` (assign rects) and `render` (paint to a surface). |
| **Mire/Widgets** | Core, Layout | Convenience widgets: `Text`, `Box`, `Panel`, `StatusBar`, `Spacer`, `Dock`, `Stack`, `Scroll`, `Backdrop` helpers, and predefined semantic styles. |
| **Mire/App** | Core, Protocol, Renderer, Layout | The runtime: `Cmd<'msg>`, `Sub<'msg>`, `Program<'model,'msg>`, `Program` builders, and `Runtime.run`. |

The layering is deliberate: **Core** is pure types, **Protocol** is terminal I/O, **Renderer** turns a virtual screen into a terminal diff, **Layout** turns a node tree into positioned draw calls, and **App** ties it together with an Elmish loop. The widget layer sits on Layout; an (optional) agent-domain layer would sit on top in the design — the base framework should never need to know what an LLM is. It's one assembly today because the whole framework is ~1.5k lines; the folder seams (and `<Compile>` order) keep the layering honest without six `.fsproj` files of ceremony.

## What modern-terminal support means here

`Mire.Protocol.ANSI` and the runtime's setup/teardown opt into:

- Alternate screen (`?1049h`) and synchronized output (`?2026h`)
- Truecolor foreground/background (`38;2` / `48;2`)
- Kitty keyboard protocol (`>1u`)
- Mouse tracking (`?1002` / `?1006`) and focus events (`?1004`)
- Bracketed paste (`?2004`)
- OSC 8 hyperlinks and OSC 52 clipboard
- Underline styles beyond single (double, curly, dotted, dashed)

## Current status

This repo is the foundation, not the full framework. What works today:

- ✅ Core value types, color/style → ANSI, cell model
- ✅ Raw-mode terminal setup and byte-level input parsing (keys, arrows, function keys, Ctrl chords)
- ✅ `Surface` drawing primitives + frame-to-frame `Diff`
- ✅ `Dock` layout with `Cells`, `Fraction`, `Content`, and `Fill` sizing on every side
- ✅ `Stack` (vertical/horizontal flow) with per-child `Cells`/`Fraction`/`Content`/`Fill` lengths
- ✅ `Scroll` regions with a scroll offset and viewport clipping (off-screen blit)
- ✅ `Filled` opaque-rectangle node — backdrops/highlights, and real `Overlay` opacity
- ✅ Elmish-style `Runtime.run` with commands, subscriptions, resize handling
- ✅ `Program` builder API (`mkProgram`, `withMapInput`, `withSubscriptions`, `withOnError`)
- ✅ Basic widget library (`Text`, `Box`, `Panel`, `StatusBar`, `Spacer`, `Dock`, `Stack`, `Scroll`, `Backdrop`)
- ✅ Grapheme-cluster-aware width handling (wide chars, combining marks)
- ✅ Headless `--dump` mode for verifying layout without raw mode

Not yet implemented (described in `SPEC.md` as the target):

- ⏳ Focus manager (tab order, focus trapping) and overlay *positioning* (centering/anchoring); `Overlay` z-orders and `Filled` occludes, but overlay layers still take the full area
- ⏳ Mouse, paste, and focus-event decoding (the sequences are *enabled* but `InputParser` only handles keys)
- ⏳ Rich widget library (tables, lists, modals, toasts, text input, markdown, command palette, …)
- ⏳ The agent-domain components (chat transcript, tool-call views, diff viewer, file tree, prompt box)

## Roadmap & design document

- [`ROADMAP.md`](ROADMAP.md) is the plan of record: a widget/node reference table with status, and the phased plan (v0.1–v0.5) with checkboxes. Start here to see what's next.
- [`SPEC.md`](SPEC.md) is the full design exploration — the rationale, the region model, the layout primitives, the widget and agent component catalogs, and the API shape Mire is aiming for. Read it for the *why* and the intended destination; read the code for *what's built*.

## License

[MIT](LICENSE) © 2026 Helge Sverre.
