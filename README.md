# Mire

A small, composable F# runtime for building **modern terminal UIs** — coding agents, chat interfaces, log/diff viewers, command palettes, dashboards. Think Elmish/TEA, but with the terminal as the platform: cell-diffed rendering, region-based layout, raw input decoding, and direct control over the terminal protocol.

<!-- Links in this file are absolute so they resolve on the NuGet listing page too. -->

Mire is **opinionated about its target**. It assumes a modern, Kitty-compatible terminal (Ghostty first) and uses truecolor, the alternate screen, the Kitty keyboard protocol, bracketed paste, mouse tracking, OSC 8 hyperlinks, and synchronized output. There is intentionally no support for legacy consoles, 16-color fallbacks, or "works over every SSH/tmux setup ever."

> **Status: 0.4.0 — the core framework, first published release.** The runtime, rendering pipeline, layout engine, a real widget library (virtualized lists & tables, a fuzzy command palette, cursor-anchored completion, single- and multi-line text editing, split views, tooltips, progress bars, spinners, tabs, toggles, markdown, overlay positioning + modals, toasts, a scrollview, a keyboard focus manager, plus separators/badges/key-hints), full keyboard/mouse/paste/focus input decoding, and **three** demo apps exist and run. The agent-domain component library in [`SPEC.md`](https://github.com/HelgeSverre/mire/blob/main/SPEC.md) is still design — prototyped only at the app level in `Mire.Demo.Agent`. See [Current status](#current-status).

## Installation

Mire ships as a single [NuGet](https://www.nuget.org/packages/Mire) package targeting `net10.0`:

```sh
dotnet add package Mire
```

Or as a `<PackageReference>` in your `.fsproj`:

```xml
<PackageReference Include="Mire" Version="0.4.0" />
```

> `0.4.0` is the current release on nuget.org, published via the tagged-release
> workflow. (Pin an exact version — the API still moves between minors, pre-1.0.)

A minimal app — wire an Elmish `Program` and hand it to `Runtime.run` (see `Mire.Demo.Agent` for a complete one):

```fsharp
open Mire.Widgets
open Mire.App

type Msg = Increment

let init () = 0, Cmd.none
let update Increment model = model + 1, Cmd.none
let view model = Text.text (sprintf "count: %d" model) Style.text
let mapInput _ = Some Increment   // InputEvent -> 'msg option; here every key counts

Program.mkProgram init update view
|> Program.withMapInput mapInput
|> Runtime.run
```

> **Pre-1.0:** the public API still moves between minor versions — pin an exact version. Mire targets modern Kitty-compatible terminals only (see below); it is not a `System.Console` drop-in.

## Quick start

Requires the **.NET 10 SDK** (`dotnet --version` should report `10.x`).

A `justfile` wraps the common commands (`just build`, `just test`, `just run`, `just dump`, `just format`); the raw `dotnet` invocations:

```sh
# Build everything
dotnet build Mire.slnx

# Demos — each takes over the alternate screen; Ctrl+C quits
dotnet run --project Mire.Demo.Agent        # an agent-shell testbed (not wired to an LLM)
dotnet run --project Mire.Demo.Feed         # a multi-feed RSS reader
dotnet run --project Mire.Demo.Spreadsheet  # an A1 grid + formula engine
dotnet run --project samples/AgentShell     # the Mire.Agent MVP — a minimal agent shell

# Verify layout headlessly — prints sample layouts as text, no raw mode
dotnet run --project Mire.Demo.Agent -- --dump
dotnet run --project Mire.Demo.Feed -- --dump

# Run the test suite (Expecto)
dotnet run --project Mire.Tests
```

- **Mire.Demo.Agent** (agent shell): the comprehensive/canonical showcase demo. Type a command and press Enter — try `markdown`, `stream:long`, `tool:run`, `diff`, `permission`, or `help`. `Ctrl+P` command palette, `Ctrl+O` skill explorer, `Shift+Tab` switches mode, `Esc` closes overlays. A `Dummy` module supplies canned responses; what's real vs. faked is in [`DEMO-TODOS.md`](https://github.com/HelgeSverre/mire/blob/main/DEMO-TODOS.md), and [`prototype/agent-harness.html`](https://github.com/HelgeSverre/mire/blob/main/prototype/agent-harness.html) is a visual design reference.
- **Mire.Demo.Feed** (RSS reader): a managed feed list merged into one newest-first stream, two panes + a per-feed filter, async loading. The first app migrated to the framework's `Focus` manager.
- **Mire.Demo.Spreadsheet** (spreadsheet): a 26×100 A1 grid with in-cell editing (`TextBuffer` + `Input`), formula references, and a small engine (`=B2*C2`, `=SUM(A1:A3)`, …).

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

The difference from web UI: there is no browser. Mire _is_ the browser — it owns layout, scroll state, focus routing, input normalization, and terminal protocol control as first-class concerns.

## Architecture

Five projects (`Mire.slnx`): the framework, three demo exes, and the test project.

| Project                   | Depends on | What it holds                                                                              |
| ------------------------- | ---------- | ------------------------------------------------------------------------------------------ |
| **Mire**                  | —          | The framework, one assembly layered by folder (below).                                     |
| **Mire.Demo.Agent**       | Mire       | Agent-shell testbed (`Exe`); the comprehensive/canonical showcase demo, agent-domain UI built at the app level, not in the framework. |
| **Mire.Demo.Feed**        | Mire       | Multi-feed RSS reader (`Exe`); first adopter of the `Focus` manager.                       |
| **Mire.Demo.Spreadsheet** | Mire       | A1 grid + formula engine (`Exe`).                                                          |
| **Mire.Tests**            | Mire       | [Expecto](https://github.com/haf/expecto) tests for the pure functions.        |

The framework is a single assembly organized by folder; the folder order is the layering, enforced by the `<Compile>` order in `Mire/Mire.fsproj`. Each folder is also its namespace, so you still `open Mire.Layout`, `open Mire.Widgets`, etc.

| Folder (namespace) | Depends on                       | What it holds                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| ------------------ | -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Mire/Core**      | —                                | Pure value types: `Point`, `Size`, `Rect`, `Color`, `Style`, `Cell`, `Region`/`RegionId`, `Grapheme`, `TextBuffer`, and input events. All structs / immutable records.                                                                                                                                                                                                                                                                                           |
| **Mire/Protocol**  | Core                             | `ANSI` escape sequence strings; `TerminalMode` (raw mode setup via `stty` + libc `poll`/`read`); `InputParser` (raw bytes → `InputEvent`).                                                                                                                                                                                                                                                                                                                       |
| **Mire/Renderer**  | Core, Protocol                   | `Surface` (a `Width × Height` grid of `Cell`s with drawing primitives) and `Diff` (computes minimal `DiffRun`s between two surfaces and writes them out).                                                                                                                                                                                                                                                                                                        |
| **Mire/Layout**    | Core, Renderer                   | `LayoutNode<'msg>` tree (`Dock`, `Stack`, `Box`, `Text`, `Filled`, `Scroll`, `Overlay`, `Positioned`) + `measure`/`render`, and `Focus` (a pure keyboard focus ring + modal trap).                                                                                                                                                                                                                                                                               |
| **Mire/Widgets**   | Core, Layout                     | Convenience widgets: `Text`, `Box`/`Panel`, `StatusBar`, `Stack`/`Dock`/`Spacer`/`Backdrop` helpers, virtualized `ListView` and `Table`, `Input` (single-line over `TextBuffer`), `TextArea` (multi-line), `Overlay`/`Modal`, `Toast`, `ScrollView`, `CommandPalette`, `Completion`, `SplitView`, `Tooltip`, `Spinner`, `ProgressBar`, `Tabs`, `Toggle`, `Separator`/`Badge`/`KeyHint`, `Markdown` (with `MarkdownStyle`), and `AppTheme` (swappable style set). |
| **Mire/App**       | Core, Protocol, Renderer, Layout | The runtime: `Cmd<'msg>`, `Sub<'msg>`, `Program<'model,'msg>`, `Program` builders, and `Runtime.run`.                                                                                                                                                                                                                                                                                                                                                            |

The layering is deliberate: **Core** is pure types, **Protocol** is terminal I/O, **Renderer** turns a virtual screen into a terminal diff, **Layout** turns a node tree into positioned draw calls, and **App** ties it together with an Elmish loop. The widget layer sits on Layout; an (optional) agent-domain layer would sit on top in the design — the base framework should never need to know what an LLM is. It's one assembly today because the whole framework is ~3.5k lines; the folder seams (and `<Compile>` order) keep the layering honest without six `.fsproj` files of ceremony.

## What modern-terminal support means here

`Mire.Protocol.ANSI` and the runtime's setup/teardown opt into:

- Alternate screen (`?1049h`) and synchronized output (`?2026h`)
- Truecolor foreground/background (`38;2` / `48;2`)
- Kitty keyboard protocol with event-type reporting (`>3u`) — modifier chords plus press/repeat/release (releases are dropped unless an app opts in via `Program.withKeyReleases`)
- Mouse tracking (`?1002` / `?1006`) and focus events (`?1004`)
- Bracketed paste (`?2004`), reassembled across reads
- Underline styles beyond single (double, curly, dotted, dashed)
- OSC 8 hyperlinks — `Style.WithLink url` makes a run clickable (the `Diff` writer brackets it); `Markdown` links carry their real URL
- OSC 52 clipboard — `Cmd.setClipboard text` copies to the system clipboard

## Current status

This repo is a working foundation — the 0.4.0 core widget layer. What works today:

- ✅ Core value types, color/style → ANSI, cell model, grapheme-cluster-aware widths
- ✅ Raw-mode terminal setup; byte-level input decoding — keys (incl. Kitty `CSI u` chords + press/repeat/release event types + legacy fallbacks), **mouse (SGR 1006), bracketed paste (reassembled across reads), and focus events**
- ✅ OSC 8 hyperlinks (`Style.WithLink`, emitted by `Diff`) and OSC 52 clipboard (`Cmd.setClipboard`)
- ✅ `Surface` drawing primitives + frame-to-frame `Diff`, bracketed in synchronized output (`?2026`)
- ✅ Layout: `Dock`, `Stack`, `Box`, `Filled`, `Scroll`, `Overlay`, and `Positioned` (9-point placement) — `measure`/`render` with `Cells`/`Fraction`/`Content`/`Fill` sizing
- ✅ Elmish `Runtime.run` (commands, subscriptions, resize, `Cmd.quit`) + the `Program` builder API
- ✅ Widgets: virtualized `ListView` + `Table` (sticky header, windowed rows, single/multi-select), `CommandPalette` (fuzzy), `Completion` (cursor-anchored), `Input` (over `TextBuffer`), `Modal` + `Overlay` positioning, `Toast`, `ScrollView` (with scrollbar), `StatusBar`, `Separator`/`Badge`/`KeyHint`, `Backdrop`, flex `Spacer`
- ✅ A keyboard **`Focus` manager** — a tab-order ring + a modal focus-trap stack, dogfooded in `Mire.Demo.Feed`
- ✅ Headless `--dump` mode and the Expecto suite
- ✅ **`AppTheme`** record — a swappable theme set; **`AppTheme.defaultTheme` is the Mire brand** (emerald accent, neutral hierarchy, inverse-video selection), built from `Mire.Brand.Palette`. The `Style.*` primitives and `Markdown.defaultStyle` are brand-sourced too, so the default look is on-brand with no per-app theme code

Not yet (described in `SPEC.md` as the target):

- ⏳ Remaining widget: `ImagePreview` (Kitty graphics protocol)
- ⏳ The runtime-owned / mouse-driven half of focus (spatial hit-testing)
- ⏳ Kitty graphics (images) and light/dark theme notifications
- ⏳ Text selection in `TextBuffer` (so `Input`/`TextArea` are cursor-only)
- 🚧 The agent-domain layer (`Mire.Agent`) — **underway for 0.5.0.** `ChatTranscript`, `PromptBox`, `ApprovalModal`, and `DiffView` (unified/split + accept-reject) now ship in `Mire.Agent`, composed by the `samples/AgentShell` MVP (`just shell`). Remaining: folding completion/history + virtualization into the widgets.

## Roadmap & design document

- [`ROADMAP.md`](https://github.com/HelgeSverre/mire/blob/main/ROADMAP.md) is the plan of record: a widget/node reference table with status, and the phased plan (v0.1–v0.5) with checkboxes. Start here to see what's next.
- [`SPEC.md`](https://github.com/HelgeSverre/mire/blob/main/SPEC.md) is the full design exploration — the rationale, the region model, the layout primitives, the widget and agent component catalogs, and the API shape Mire is aiming for. Read it for the _why_ and the intended destination; read the code for _what's built_.

## License

[MIT](https://github.com/HelgeSverre/mire/blob/main/LICENSE) © 2026 Helge Sverre.
