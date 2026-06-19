# Getting started

## Requirements

- The **.NET 10 SDK** (`dotnet --version` reports `10.x`).
- A **modern, Kitty-compatible terminal** — Ghostty, Kitty, or WezTerm. Mire uses
  truecolor, the alternate screen, the Kitty keyboard protocol, mouse tracking, and
  bracketed paste. It is _not_ a `System.Console` drop-in and has no 16-color or
  legacy-console fallback.

## Install

Mire ships as a single NuGet package targeting `net10.0`:

```sh
dotnet add package Mire
```

or in your `.fsproj`:

```xml
<PackageReference Include="Mire" Version="0.5.0" />
```

> Pre-1.0, the public API still moves between minor versions — pin an exact version.

## Your first app — a counter

A Mire app is an [Elmish](https://elmish.github.io/) program: a `model`, an `update`
that folds messages into it, a `view` that renders it, and a `mapInput` that turns
terminal input into messages. Hand the assembled `Program` to `Runtime.run`.

```fsharp
open Mire.Core      // InputEvent, Key, …
open Mire.Widgets   // Text, Box, Stack, Style
open Mire.App       // Cmd, Program, Runtime

type Msg =
    | Increment
    | Decrement

let init () = 0, Cmd.none

let update msg model =
    match msg with
    | Increment -> model + 1, Cmd.none
    | Decrement -> model - 1, Cmd.none

let view model =
    Stack.vstack
        [ Text.title (sprintf " count: %d " model)
          Text.text "  ↑ increment · ↓ decrement · Ctrl+C quit" Style.dim ]

// Map raw input events to messages. Return None to ignore an event.
let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | ArrowUp -> Some Increment
        | ArrowDown -> Some Decrement
        | _ -> None
    | _ -> None

[<EntryPoint>]
let main _ =
    Program.create init update view
    |> Program.withMapInput mapInput
    |> Runtime.run

    0
```

`Runtime.run` takes over the alternate screen, puts the terminal in raw mode, and
drives the loop at ~30 FPS. **Ctrl+C** quits by default (and restores the terminal);
you can change that with `Program.withQuitOn`, or exit from `update` by returning
`Cmd.quit`.

That's the whole shape. Everything else — layout, widgets, styling, mouse, focus — is
built on these five fields. The next guide, [Architecture](architecture.md), walks the
loop in detail.

## Running and verifying

Mire apps take over the terminal, which makes them awkward to test in CI. Two tools help:

- **`Runtime.run`** — the real thing; run it in a terminal.
- **Headless layout dump** — render a view to a `Surface` and print the cell grid as
  text, no raw mode. Every demo wires a `-- --dump` flag for exactly this; do the same
  in your app to eyeball layout changes and to snapshot-test screens. See
  [Architecture](architecture.md#headless-rendering) for the three-line recipe.

```sh
dotnet build Mire.slnx                          # build everything
dotnet run --project samples/Gallery            # browse every widget (Tab / ←→ switch pages)
dotnet run --project samples/Gallery -- --dump  # the same, headless, as text
dotnet run --project Mire.Tests                 # the Expecto suite
```

A `justfile` wraps the common commands: `just build`, `just test`, `just gallery`,
`just shell`, `just dump`, `just format`.
