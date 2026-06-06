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

module Cmd =
    let none = NoOp
    let batch cmds = Batch cmds
    let ofAsync (f: ('msg -> unit) -> Async<unit>) = AsyncCmd f
    let ofMsg msg = OfMsg msg

    /// Request a clean exit of the runtime loop from `update`.
    let quit: Cmd<'msg> = Quit

    /// Execute a command. `requestQuit` is the runtime's hook for `Cmd.quit`; it
    /// lets a command signal the loop to stop without abrupt control flow.
    let rec dispatch (requestQuit: unit -> unit) (send: 'msg -> unit) (cmd: Cmd<'msg>) : unit =
        match cmd with
        | NoOp -> ()
        | Batch cmds -> cmds |> List.iter (dispatch requestQuit send)
        | AsyncCmd f -> Async.Start(f send)
        | OfMsg msg -> send msg
        | Quit -> requestQuit ()

type Sub<'msg> =
    | TerminalResize of (Size -> 'msg)
    | Every of TimeSpan * (unit -> 'msg)

type Program<'model, 'msg> =
    { Init: unit -> 'model * Cmd<'msg>
      Update: 'msg -> 'model -> 'model * Cmd<'msg>
      View: 'model -> LayoutNode<'msg>
      MapInput: InputEvent -> 'msg option
      Subscriptions: 'model -> Sub<'msg> list
      OnError: exn -> unit }

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
          OnError = fun ex -> eprintfn "Mire runtime error: %O" ex }

    let withMapInput (f: InputEvent -> 'msg option) (program: Program<'model, 'msg>) = { program with MapInput = f }

    let withSubscriptions (subs: 'model -> Sub<'msg> list) (program: Program<'model, 'msg>) =
        { program with Subscriptions = subs }

    let withOnError (handler: exn -> unit) (program: Program<'model, 'msg>) = { program with OnError = handler }

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

        // Dispatch initial command
        Cmd.dispatch requestQuit sendMsg initialCmd

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

                    // Process input
                    match InputParser.readEvent () with
                    | Some inputEvent ->
                        match inputEvent with
                        | Key keyEvent when keyEvent.Key = Key.Char "c" && keyEvent.Modifiers.Ctrl ->
                            state <- { state with Running = false }
                        | _ ->
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

                            Cmd.dispatch requestQuit sendMsg cmd
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
