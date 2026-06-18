namespace Mire.App

open System
open System.Threading
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout

type Cmd<'msg> =
    | NoOp
    | Batch of Cmd<'msg> list
    | AsyncCmd of (('msg -> unit) -> Async<unit>)
    | OfMsg of 'msg
    /// Request a clean shutdown of the runtime loop from `update`. The runtime
    /// recognizes this and breaks its loop so the normal teardown (alt-screen
    /// exit, raw-mode restore) runs — the same exit path as the Ctrl+C intercept.
    | Quit
    /// Write a raw escape string straight to the terminal, outside the cell diff.
    /// The escape hatch for out-of-band terminal effects that paint no cells — OSC
    /// clipboard, Kitty graphics, window-title sets, notifications. `Cmd.setClipboard`
    /// and `Cmd.kittyImage` are built on it.
    | WriteRaw of string

module Cmd =
    let none = NoOp
    let batch cmds = Batch cmds
    let ofAsync (f: ('msg -> unit) -> Async<unit>) = AsyncCmd f
    let ofMsg msg = OfMsg msg

    /// Request a clean exit of the runtime loop from `update`.
    let quit: Cmd<'msg> = Quit

    /// Write a raw escape sequence to the terminal, outside the diff (see `WriteRaw`).
    let writeRaw (s: string) : Cmd<'msg> = WriteRaw s

    /// Copy `text` to the system clipboard (OSC 52). Works on terminals with
    /// clipboard write enabled (Ghostty/Kitty/iTerm2); silently ignored elsewhere.
    let setClipboard (text: string) : Cmd<'msg> = WriteRaw(ANSI.setClipboard text)

    /// Display a PNG (already base64-encoded) via the Kitty graphics protocol at
    /// `(col, row)` (0-based cell coords), sized to a `cols`×`rows` cell box. On a
    /// terminal without Kitty graphics this paints nothing (the `ImagePreview`
    /// widget's text fallback is what shows there). The image is an overlay on top
    /// of the cell grid — re-issue it after a frame that would repaint its region.
    let kittyImage (col: int) (row: int) (cols: int) (rows: int) (pngBase64: string) : Cmd<'msg> =
        WriteRaw(ANSI.cursorTo (col, row) + ANSI.kittyImage cols rows pngBase64)

    /// Clear all Kitty-graphics images.
    let clearImages: Cmd<'msg> = WriteRaw ANSI.deleteImages

    /// Execute a command. `requestQuit` is the runtime's hook for `Cmd.quit`;
    /// `writeRaw` writes an escape straight to the terminal (clipboard, graphics).
    let rec dispatch
        (requestQuit: unit -> unit)
        (writeRaw: string -> unit)
        (send: 'msg -> unit)
        (cmd: Cmd<'msg>)
        : unit =
        match cmd with
        | NoOp -> ()
        | Batch cmds -> cmds |> List.iter (dispatch requestQuit writeRaw send)
        | AsyncCmd f -> Async.Start(f send)
        | OfMsg msg -> send msg
        | Quit -> requestQuit ()
        | WriteRaw s -> writeRaw s

type Sub<'msg> =
    | TerminalResize of (Size -> 'msg)
    | Every of TimeSpan * (unit -> 'msg)

type Program<'model, 'msg> =
    {
        Init: unit -> 'model * Cmd<'msg>
        Update: 'msg -> 'model -> 'model * Cmd<'msg>
        View: 'model -> LayoutNode<'msg>
        MapInput: InputEvent -> 'msg option
        Subscriptions: 'model -> Sub<'msg> list
        OnError: exn -> unit
        /// Decides which input events end the loop. Runs *before* `MapInput`, so a
        /// matched event is consumed as "quit" and never reaches the app. Default:
        /// Ctrl+C. The quit *binding* is app-owned, not baked into the runtime.
        QuitOn: InputEvent -> bool
        /// Forward Kitty key *release* events to `MapInput`. Default `false`: the
        /// runtime requests event-type reporting from the terminal, so every key
        /// now produces a press *and* a release — dropping releases keeps the
        /// common case (one message per keystroke) unchanged. Opt in for apps that
        /// track key-down/up (games, chords). `Repeat` events always pass through.
        KeyReleases: bool
        /// Ask the terminal to report light/dark color-scheme changes (DEC mode
        /// 2031). Default `false`. When on, the runtime enables the mode and queries
        /// the current scheme at startup, and `ThemeChanged` events flow through
        /// `MapInput` like any other input — let an app retheme on the fly.
        ThemeNotifications: bool
    }

module Program =
    let mkProgram
        (init: unit -> 'model * Cmd<'msg>)
        (update: 'msg -> 'model -> 'model * Cmd<'msg>)
        (view: 'model -> LayoutNode<'msg>)
        : Program<'model, 'msg> =
        { Init = init
          Update = update
          View = view
          MapInput = fun _ -> None
          Subscriptions = fun _ -> []
          OnError = fun ex -> eprintfn "Mire runtime error: %O" ex
          QuitOn =
            fun e ->
                match e with
                | Key ke -> ke.Key = Key.Char "c" && ke.Modifiers.Ctrl
                | _ -> false
          KeyReleases = false
          ThemeNotifications = false }

    let withMapInput (f: InputEvent -> 'msg option) (program: Program<'model, 'msg>) = { program with MapInput = f }

    let withSubscriptions (subs: 'model -> Sub<'msg> list) (program: Program<'model, 'msg>) =
        { program with Subscriptions = subs }

    let withOnError (handler: exn -> unit) (program: Program<'model, 'msg>) = { program with OnError = handler }

    /// Replace the quit policy — the predicate deciding which input ends the loop
    /// (default: Ctrl+C). It runs *before* `MapInput`, so a matched event is
    /// consumed as quit and never reaches the app. Set `fun _ -> false` to make
    /// Ctrl+C a normal key and exit only via `Cmd.quit`.
    let withQuitOn (f: InputEvent -> bool) (program: Program<'model, 'msg>) = { program with QuitOn = f }

    /// Opt in to receiving Kitty key *release* events in `MapInput` (default off —
    /// releases are dropped so each keystroke is one message). `Repeat` always
    /// passes through regardless.
    let withKeyReleases (enabled: bool) (program: Program<'model, 'msg>) = { program with KeyReleases = enabled }

    /// Opt in to light/dark theme-change notifications (DEC mode 2031). When on, the
    /// runtime enables the mode and queries the current scheme at startup; the app
    /// receives `ThemeChanged Dark`/`ThemeChanged Light` through `MapInput`.
    let withThemeNotifications (enabled: bool) (program: Program<'model, 'msg>) =
        { program with
            ThemeNotifications = enabled }

type RuntimeState<'model, 'msg> =
    { Model: 'model
      PreviousSurface: Surface option
      Running: bool
      NeedsRender: bool
      LastSize: Size }

module Runtime =

    let private renderFrame (view: LayoutNode<'msg>) (size: Size) =
        let surface = Surface(size)
        let laidOut = Layout.measure (Rect.FromOrigin(size)) view
        Layout.render surface laidOut
        surface

    let run (program: Program<'model, 'msg>) =
        // Setup terminal
        TerminalMode.setupRawMode ()
        Console.Out.Write(ANSI.enterAltScreen)
        Console.Out.Write(ANSI.clearScreen)
        Console.Out.Write(ANSI.enableMouse)
        Console.Out.Write(ANSI.enableFocusEvents)
        Console.Out.Write(ANSI.enableBracketedPaste)
        Console.Out.Write(ANSI.enableKittyKeyboard)

        if program.ThemeNotifications then
            Console.Out.Write(ANSI.enableThemeNotifications)
            Console.Out.Write(ANSI.queryColorScheme)

        Console.Out.Flush()

        let initialModel, initialCmd = program.Init()

        let mutable state =
            { Model = initialModel
              PreviousSurface = None
              Running = true
              NeedsRender = true
              LastSize = TerminalMode.getTerminalSize () |> Option.defaultValue (Size.Create(80, 24)) }

        let msgQueue = Collections.Generic.Queue<'msg>()
        let queueLock = obj ()

        let sendMsg msg =
            lock queueLock (fun () -> msgQueue.Enqueue(msg))

        // Set by `Cmd.quit`. The loop folds it into `state.Running` *after* the
        // message pump rather than here, because `dispatch` runs inside the pump
        // where `state` is reassigned per message — a direct `Running <- false`
        // would be clobbered by the next message's update. Same flag-then-fold
        // shape as the Ctrl+C intercept.
        let mutable quitRequested = false
        let requestQuit () = quitRequested <- true

        // `Cmd.writeRaw` hook (clipboard, Kitty graphics, …): write the escape
        // straight to the terminal — it paints no cells, so it bypasses the diff.
        let writeRaw (s: string) =
            Console.Out.Write(s)
            Console.Out.Flush()

        // Bracketed-paste reassembly: bytes carried from a read that ended mid-paste
        // (no end marker yet) are prepended to the next read. Capped so a lost end
        // marker can't grow the buffer without bound.
        let mutable pasteCarry: byte[] = [||]
        let pasteCap = 1 <<< 20 // 1 MiB

        // Dispatch initial command
        Cmd.dispatch requestQuit writeRaw sendMsg initialCmd

        let sw = Diagnostics.Stopwatch.StartNew()
        let mutable lastTick = TimeSpan.Zero
        let frameInterval = TimeSpan.FromMilliseconds(33.0) // ~30 FPS

        try
            while state.Running do
                try
                    let loopStart = sw.Elapsed

                    // Check for resize
                    let currentSize =
                        TerminalMode.getTerminalSize () |> Option.defaultValue state.LastSize

                    if currentSize <> state.LastSize then
                        // Drop the previous surface so the render block takes the
                        // no-previous full-repaint path. `Diff.compute` only diffs
                        // the min(old,new) overlap, so a grow would otherwise leave
                        // the newly-exposed rows/cols unpainted (and a shrink leaves
                        // stale cells past the new edge). Repainting from scratch at
                        // the new size fixes both.
                        state <-
                            { state with
                                LastSize = currentSize
                                PreviousSurface = None
                                NeedsRender = true }

                        let subs = program.Subscriptions state.Model

                        for sub in subs do
                            match sub with
                            | TerminalResize f -> sendMsg (f currentSize)
                            | _ -> ()

                    // Process input. Read raw bytes (not readEvent) so a bracketed
                    // paste split across reads can be reassembled before parsing.
                    let rawBytes = InputParser.readRawBytes ()
                    let toParse, newCarry = InputParser.stepPasteBuffer pasteCap pasteCarry rawBytes
                    pasteCarry <- newCarry

                    if toParse.Length > 0 then
                        match InputParser.parseBytes toParse with
                        | Some inputEvent ->
                            // Event-type reporting is enabled, so keys arrive as
                            // press + release. Drop releases unless the app opted in
                            // (else every keystroke would fire twice).
                            let isDroppedRelease =
                                match inputEvent with
                                | Key ke -> ke.EventType = Release && not program.KeyReleases
                                | _ -> false

                            if isDroppedRelease then
                                ()
                            // the quit policy (default Ctrl+C) is consulted before MapInput
                            elif program.QuitOn inputEvent then
                                state <- { state with Running = false }
                            else
                                match program.MapInput inputEvent with
                                | Some msg -> sendMsg msg
                                | None -> ()
                        | None -> ()

                    // Process messages
                    let mutable hasMsgs = true

                    while hasMsgs do
                        let msg =
                            lock queueLock (fun () ->
                                if msgQueue.Count > 0 then
                                    Some(msgQueue.Dequeue())
                                else
                                    None)

                        match msg with
                        | Some m ->
                            let newModel, cmd = program.Update m state.Model

                            state <-
                                { state with
                                    Model = newModel
                                    NeedsRender = true }

                            Cmd.dispatch requestQuit writeRaw sendMsg cmd
                        | None -> hasMsgs <- false

                    // A command (or the initial cmd) requested shutdown. Fold it in
                    // now so this frame still renders the final state; the
                    // `while state.Running` check then exits on the next iteration
                    // and the `finally` teardown runs.
                    if quitRequested then
                        state <- { state with Running = false }

                    // Render if needed
                    if state.NeedsRender then
                        let view = program.View state.Model
                        let surface = renderFrame view state.LastSize
                        let diff = Diff.compute state.PreviousSurface surface

                        match state.PreviousSurface with
                        | None ->
                            Diff.clearScreen Console.Out

                            let allRuns =
                                let runs = ResizeArray<DiffRun>()

                                for y in 0 .. surface.Size.Height - 1 do
                                    let mutable x = 0

                                    while x < surface.Size.Width do
                                        let cell = surface.[x, y]

                                        if not cell.IsEmpty then
                                            let style = cell.Style
                                            let startX = x
                                            let sb = Text.StringBuilder()

                                            while x < surface.Size.Width
                                                  && surface.[x, y].Style = style
                                                  && not surface.[x, y].IsEmpty do
                                                sb.Append(surface.[x, y].Grapheme) |> ignore
                                                x <- x + 1

                                            runs.Add
                                                { X = startX
                                                  Y = y
                                                  Text = sb.ToString()
                                                  Style = style }
                                        else
                                            x <- x + 1

                                runs |> Seq.toList

                            Diff.renderToTerminal allRuns Console.Out
                        | Some _ -> Diff.renderToTerminal diff Console.Out

                        state <-
                            { state with
                                PreviousSurface = Some surface
                                NeedsRender = false }

                    // Throttle to ~30 FPS
                    let elapsed = sw.Elapsed - loopStart

                    if elapsed < frameInterval then
                        Thread.Sleep(frameInterval - elapsed)

                    // Tick subscriptions
                    let currentTick = sw.Elapsed
                    let dt = currentTick - lastTick
                    lastTick <- currentTick
                    let subs = program.Subscriptions state.Model

                    for sub in subs do
                        match sub with
                        | Every(interval, f) ->
                            if
                                int (currentTick.TotalMilliseconds / interval.TotalMilliseconds) > int (
                                    (currentTick - dt).TotalMilliseconds / interval.TotalMilliseconds
                                )
                            then
                                sendMsg (f ())
                        | _ -> ()
                with ex ->
                    program.OnError ex

        finally
            // Cleanup
            if program.ThemeNotifications then
                Console.Out.Write(ANSI.disableThemeNotifications)

            Console.Out.Write(ANSI.disableKittyKeyboard)
            Console.Out.Write(ANSI.disableBracketedPaste)
            Console.Out.Write(ANSI.disableFocusEvents)
            Console.Out.Write(ANSI.disableMouse)
            Console.Out.Write(ANSI.exitAltScreen)
            Console.Out.Write(ANSI.cursorShow)
            Console.Out.Write(ANSI.resetStyle)
            Console.Out.Flush()
            TerminalMode.restoreMode ()
            Console.WriteLine()
            Console.WriteLine("Mire exited.")
