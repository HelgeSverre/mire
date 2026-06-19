# Mire

> Elmish for the terminal — build modern TUIs in F# with cell-diffed rendering, region-based layout, and raw input decoding.

Mire is a small, composable runtime for terminal UIs: coding agents, chat interfaces, log and diff viewers, command palettes, dashboards. You write `update` and `view`; Mire owns the loop, the layout, the input, and the terminal protocol.

It targets **modern, Kitty-compatible terminals** (Ghostty first) and **.NET 10**. There is no 16-color fallback and no legacy-console support — that is a deliberate choice, not a gap.

<!-- Links are absolute so they resolve on the NuGet listing too. -->

**Status: 0.7.0** — the core framework and the optional agent layer, published on NuGet. Pre-1.0, so pin an exact version. See [what's included](#whats-included).

## Install

```sh
dotnet add package Mire
```

```xml
<PackageReference Include="Mire" Version="0.7.0" />
```

## A taste

A counter, start to finish:

```fsharp
open Mire.Core      // InputEvent, Key
open Mire.Widgets   // Text, Stack, Style
open Mire.App       // Cmd, Program, Runtime

type Msg = Increment | Decrement

let init () = 0, Cmd.none

let update msg model =
    match msg with
    | Increment -> model + 1, Cmd.none
    | Decrement -> model - 1, Cmd.none

let view model = Text.text (sprintf "count: %d" model) Style.text

let mapInput e =
    match e with
    | Key ke ->
        match ke.Key with
        | ArrowUp -> Some Increment
        | ArrowDown -> Some Decrement
        | _ -> None
    | _ -> None

Program.create init update view
|> Program.withMapInput mapInput
|> Runtime.run
```

That's the whole shape — a model, an `update`, a `view`, and a `mapInput`. Everything else builds on it.

## How it works

Mire follows The Elm Architecture, adapted for the terminal. Each frame, `Runtime.run` does:

```
read input → decode → map to a message → update the model
           → build the view → lay it out → diff against the last frame
           → write only the cells that changed
```

The `view` is a pure description of the screen. You rebuild the whole tree every frame; the diff makes that cheap. There is no browser — Mire owns layout, scroll state, focus, input decoding, and terminal-protocol control as first-class concerns.

## What's included

**Layout** — `Stack`, `Dock`, `Box`, `Scroll`, `Overlay`, and 9-point `Positioned`, with cell / fraction / content / fill sizing.

**Widgets** — virtualized `ListView` and `Table`, a fuzzy `CommandPalette`, `Completion`, `Modal`, `Toast`, `Tooltip`, `ScrollView`, `Tabs`, `Toggle`, `ProgressBar`, `Spinner`, `SplitView`, `StatusBar`, `Markdown`, `ImagePreview`, and `Separator` / `Badge` / `KeyHint`.

**Text editing** — a pure `TextBuffer` (cursor, selection, word/line motions) and an overridable `TextEdit` keymap, rendered by single-line `Input` and soft-wrapping `TextArea`.

**Input** — the Kitty keyboard protocol (chords, press/repeat/release, keypad/F-keys), SGR mouse, bracketed paste, focus events, and light/dark theme notifications — all decoded for you.

**Rendering** — truecolor, synchronized output, true grapheme-cluster widths (astral, emoji-ZWJ, flags), OSC 8 links, OSC 52 clipboard, and the Kitty graphics protocol.

**Agent layer** (`Mire.Agent`, optional) — `ChatTranscript`, `PromptBox` (history + completion), `ApprovalModal`, and `DiffView` for building coding-agent and chat UIs.

## Documentation

The [user guide](https://github.com/HelgeSverre/mire/blob/main/docs/guide/README.md) is the place to start:

[Getting started](https://github.com/HelgeSverre/mire/blob/main/docs/guide/getting-started.md) ·
[Architecture](https://github.com/HelgeSverre/mire/blob/main/docs/guide/architecture.md) ·
[Layout](https://github.com/HelgeSverre/mire/blob/main/docs/guide/layout.md) ·
[Widgets](https://github.com/HelgeSverre/mire/blob/main/docs/guide/widgets.md) ·
[Styling](https://github.com/HelgeSverre/mire/blob/main/docs/guide/styling-and-theming.md) ·
[Input](https://github.com/HelgeSverre/mire/blob/main/docs/guide/input.md) ·
[Text editing](https://github.com/HelgeSverre/mire/blob/main/docs/guide/text-editing.md) ·
[Agent layer](https://github.com/HelgeSverre/mire/blob/main/docs/guide/agent-layer.md)

A full documentation site (Astro) lives in [`website/`](https://github.com/HelgeSverre/mire/tree/main/website).

## Demos and samples

Each takes over the alternate screen; **Ctrl+C** quits. Add `-- --dump` to render a screen as text instead.

```sh
dotnet run --project samples/Gallery        # every widget in its states
dotnet run --project samples/AgentShell     # a minimal agent shell
dotnet run --project Mire.Demo.Agent        # the comprehensive showcase
dotnet run --project Mire.Demo.Feed         # an RSS reader
dotnet run --project Mire.Demo.Spreadsheet  # an A1 grid + formula engine
```

A `justfile` wraps the common commands: `just build`, `just test`, `just gallery`, `just shell`.

## Project layout

The framework is one assembly, layered by folder; the folder order is the dependency order, enforced by the `<Compile>` order in `Mire/Mire.fsproj`.

| Folder (namespace) | What it holds                                                                                                |
| ------------------ | ------------------------------------------------------------------------------------------------------------ |
| **Core**           | Pure value types: `Point`, `Size`, `Rect`, `Color`, `Style`, `Cell`, `Grapheme`, `TextBuffer`, input events. |
| **Protocol**       | `ANSI` sequences, raw-mode setup, and the byte → `InputEvent` parser.                                        |
| **Renderer**       | `Surface` (the cell grid + draw primitives) and `Diff`.                                                      |
| **Layout**         | The `LayoutNode` tree, `measure` / `render`, and the keyboard `Focus` ring.                                  |
| **Widgets**        | The widget library and `AppTheme`.                                                                           |
| **App**            | `Cmd`, `Sub`, `Program`, and `Runtime.run`.                                                                  |

Alongside it: three `Mire.Demo.*` apps, two `samples/`, and an Expecto test project.

## Roadmap and design

- [`ROADMAP.md`](https://github.com/HelgeSverre/mire/blob/main/ROADMAP.md) — the plan of record: a widget/node status table and the phased plan.
- [`SPEC.md`](https://github.com/HelgeSverre/mire/blob/main/SPEC.md) — the design exploration and the rationale. Read it for the _why_; read the code for _what's built_.

## License

[MIT](https://github.com/HelgeSverre/mire/blob/main/LICENSE) © 2026 Helge Sverre.
