# Architecture — the loop

Mire follows The Elm Architecture (MVU), adapted for the terminal. You describe an
app as a `Program`, and `Runtime.run` (in `Mire.App`) drives it.

```fsharp
type Program<'model, 'msg> =
    { Init:          unit -> 'model * Cmd<'msg>
      Update:        'msg -> 'model -> 'model * Cmd<'msg>
      View:          'model -> LayoutNode<'msg>
      MapInput:      InputEvent -> 'msg option
      Subscriptions: 'model -> Sub<'msg> list
      // … plus opt-in fields set by the builders below
    }
```

Each frame, the runtime:

```
read input → decode to InputEvent → MapInput → 'msg
          → Update model → View → measure onto a Surface
          → Diff against the previous frame → write only changed cell runs
```

```
model → view tree → layout → surface → diff → terminal
```

There is no browser. Mire *is* the browser: it owns layout, scroll state, focus
routing, input normalization, and terminal-protocol control.

## Building a Program

Start with `Program.create init update view`, then compose opt-in behavior with
the `with*` builders (each returns a new `Program`):

| Builder | Purpose |
| --- | --- |
| `withMapInput (InputEvent -> 'msg option)` | Turn input into messages. Default: ignore everything. |
| `withSubscriptions ('model -> Sub<'msg> list)` | External event sources (timers, resize). |
| `withQuitOn (InputEvent -> bool)` | Which input ends the loop. Default: Ctrl+C. Set `fun _ -> false` to quit only via `Cmd.quit`. |
| `withOnError (exn -> unit)` | Handle exceptions thrown inside the loop. |
| `withKeyReleases bool` | Forward Kitty key *release* events (default off — see [Input](input.md)). |
| `withThemeNotifications bool` | Ask the terminal to report light/dark scheme changes (DEC 2031). |
| `withMouseRegion (RegionId option -> MouseEvent -> 'msg option)` | Route mouse events via the retained region table (see [Input](input.md#mouse-hit-testing)). |

```fsharp
Program.create init update view
|> Program.withMapInput mapInput
|> Program.withSubscriptions subscriptions
|> Runtime.run
```

## Commands — `Cmd<'msg>`

`init` and `update` return a `model` *and* a `Cmd` — work for the runtime to perform,
side effects kept out of your pure functions.

```fsharp
type Cmd<'msg> =
    | NoOp
    | Batch of Cmd<'msg> list
    | AsyncCmd of (('msg -> unit) -> Async<unit>)
    | OfMsg of 'msg
    | Quit
    | WriteRaw of string   // out-of-band escape (clipboard, graphics, …)
```

Constructors and helpers:

- `Cmd.none` — do nothing.
- `Cmd.ofMsg msg` — enqueue a message (e.g. to chain updates).
- `Cmd.batch [a; b]` — run several commands.
- `Cmd.ofAsync (fun dispatch -> async { … dispatch msg … })` — run async work and dispatch results back (this is how you do I/O — fetch a feed, run a tool — without blocking the loop).
- `Cmd.quit` — request a clean shutdown (same exit path as the Ctrl+C intercept; the terminal is restored).
- `Cmd.writeRaw s` / `Cmd.setClipboard text` / `Cmd.kittyImage …` / `Cmd.clearImages` — out-of-band terminal effects that paint no cells (see [Terminal protocol](terminal-protocol.md)).

```fsharp
let update msg model =
    match msg with
    | Refresh -> { model with Loading = true }, Cmd.ofAsync (fun dispatch -> async {
        let! data = fetchAsync ()
        dispatch (Loaded data) })
    | Loaded d -> { model with Loading = false; Data = d }, Cmd.none
    | Copy -> model, Cmd.setClipboard model.Data
    | Done -> model, Cmd.quit
```

## Subscriptions — `Sub<'msg>`

Subscriptions are long-lived event sources, recomputed from the model each frame:

```fsharp
type Sub<'msg> =
    | TerminalResize of (Size -> 'msg)
    | Every of TimeSpan * (unit -> 'msg)
```

- `Sub.TerminalResize (fun size -> Resized size)` — fires when the terminal is resized. (The runtime also seeds the initial size; read it via `Mire.Protocol.TerminalMode.getTerminalSize ()` in `init`.)
- `Sub.Every (TimeSpan.FromMilliseconds 120.0, fun () -> Tick)` — a recurring tick, e.g. to advance a spinner or a streaming animation.

```fsharp
let subscriptions model =
    [ Sub.TerminalResize Resized
      if model.Busy then Sub.Every(TimeSpan.FromMilliseconds 100.0, fun () -> SpinnerTick) ]
```

## The render pipeline

You never draw to the terminal directly. `view` returns a `LayoutNode<'msg>` tree;
the runtime measures it onto a fresh `Surface` (a `Width × Height` grid of `Cell`s),
then `Diff.compute` finds the minimal set of changed cell runs between this frame and
the last, and writes only those — bracketed in synchronized output (`?2026`) so the
frame appears atomically. This is why a Mire view is a *pure description*: rebuild the
whole tree every frame and let the diff make updates cheap.

Keep `view` free of side effects and `update` free of I/O (push I/O into `Cmd.ofAsync`).
That separation is what makes the headless dump and snapshot tests possible.

## Headless rendering

Because layout is pure, you can render any view to a `Surface` without a terminal —
ideal for verifying layout and for snapshot tests:

```fsharp
open Mire.Core
open Mire.Renderer
open Mire.Layout

let renderToText (width, height) (node: LayoutNode<_>) =
    let surface = Surface(Size.Create(width, height))
    Layout.measure (Rect.FromOrigin(Size.Create(width, height))) node
    |> Layout.render surface
    // read surface.[x, y].Grapheme to print or assert on the cell grid
    surface
```

The demos wrap this in a `-- --dump` entry point; the Expecto suite uses the same path
to assert on rendered cells. Verifying through `Layout.measure`/`Layout.render` (the
exact runtime path) means your tests exercise real layout, not a mock.
