---
title: Program, Cmd, and Sub
description: The runtime API — the Program record, its builders, commands, and subscriptions.
category: reference
order: 3
---

You describe an app as a `Program` and hand it to `Runtime.run` (both in `Mire.App`).

```fsharp
type Program<'model, 'msg> =
    { Init:          unit -> 'model * Cmd<'msg>
      Update:        'msg -> 'model -> 'model * Cmd<'msg>
      View:          'model -> LayoutNode<'msg>
      MapInput:      InputEvent -> 'msg option
      Subscriptions: 'model -> Sub<'msg> list
      // plus the opt-in fields set by the builders below
    }
```

## Builders

Start with `Program.create init update view`, then compose:

| Builder | Purpose |
| --- | --- |
| `withMapInput (InputEvent -> 'msg option)` | Turn input into messages. Default: ignore everything. |
| `withSubscriptions ('model -> Sub<'msg> list)` | External event sources. |
| `withQuitOn (InputEvent -> bool)` | Which input ends the loop. Default: Ctrl+C. |
| `withOnError (exn -> unit)` | Handle exceptions thrown inside the loop. |
| `withKeyReleases bool` | Forward Kitty key release events (default off). |
| `withThemeNotifications bool` | Report light/dark scheme changes (DEC 2031). |
| `withMouseRegion (RegionId option -> MouseEvent -> 'msg option)` | Route mouse via the region table. |

```fsharp
Program.create init update view
|> Program.withMapInput mapInput
|> Program.withSubscriptions subscriptions
|> Runtime.run
```

## Commands

`init` and `update` return a `model` *and* a `Cmd` — side effects the runtime performs.

```fsharp
type Cmd<'msg> =
    | NoOp
    | Batch of Cmd<'msg> list
    | AsyncCmd of (('msg -> unit) -> Async<unit>)
    | OfMsg of 'msg
    | Quit
    | WriteRaw of string
```

| Constructor | Effect |
| --- | --- |
| `Cmd.none` | Do nothing. |
| `Cmd.ofMsg msg` | Enqueue a message (chain updates). |
| `Cmd.batch [a; b]` | Run several commands. |
| `Cmd.ofAsync (fun dispatch -> async { … dispatch r … })` | Run async work; dispatch results back. This is how you do I/O without blocking the loop. |
| `Cmd.quit` | Request a clean shutdown (restores the terminal). |
| `Cmd.writeRaw s` | Write a raw escape outside the cell diff. |
| `Cmd.setClipboard text` | Copy via OSC 52. |
| `Cmd.kittyImage col row cols rows pngBase64` | Display an image (Kitty graphics). |
| `Cmd.clearImages` | Clear all placed images. |

```fsharp
let update msg model =
    match msg with
    | Refresh -> { model with Loading = true },
        Cmd.ofAsync (fun dispatch -> async {
            let! data = fetchAsync ()
            dispatch (Loaded data) })
    | Loaded d -> { model with Loading = false; Data = d }, Cmd.none
    | Copy -> model, Cmd.setClipboard model.Selected
    | Done -> model, Cmd.quit
```

## Subscriptions

Long-lived event sources, recomputed from the model each frame.

```fsharp
type Sub<'msg> =
    | TerminalResize of (Size -> 'msg)
    | Every of TimeSpan * (unit -> 'msg)
```

```fsharp
let subscriptions model =
    [ Sub.TerminalResize Resized
      if model.Busy then Sub.Every(TimeSpan.FromMilliseconds 100.0, fun () -> SpinnerTick) ]
```

`init` should seed the starting terminal size with
`Mire.Protocol.TerminalMode.getTerminalSize ()`; `TerminalResize` keeps it current.

## The loop

`Runtime.run` drives, each frame: read input → decode to `InputEvent` → `MapInput` to a
message → `Update` the model → build the `View` → `Layout.measure` onto a `Surface` →
`Diff.compute` the changed cell runs → write only those, bracketed in synchronized
output. Keep `view` pure and push I/O into `Cmd.ofAsync`. See
[the loop](/docs/explanation/the-loop/).
