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

    /// Execute a command, handing `Cmd.ofAsync` work to `startAsync`. The runtime
    /// passes a supervised starter (`CommandSupervisor.Start`) so async work runs
    /// under the runtime's cancellation scope and its failures reach `OnError`.
    /// The async is built lazily inside `startAsync`'s workflow, so even an
    /// exception thrown while *constructing* it is reported through that path.
    let rec dispatchWith
        (requestQuit: unit -> unit)
        (writeRaw: string -> unit)
        (send: 'msg -> unit)
        (startAsync: Async<unit> -> unit)
        (cmd: Cmd<'msg>)
        : unit =
        match cmd with
        | NoOp -> ()
        | Batch cmds -> cmds |> List.iter (dispatchWith requestQuit writeRaw send startAsync)
        | AsyncCmd f -> startAsync (async.Delay(fun () -> f send))
        | OfMsg msg -> send msg
        | Quit -> requestQuit ()
        | WriteRaw s -> writeRaw s

    /// Execute a command. `requestQuit` is the runtime's hook for `Cmd.quit`;
    /// `writeRaw` writes an escape straight to the terminal (clipboard, graphics).
    /// Async work is started detached (`Async.Start`) — unsupervised; the runtime
    /// itself uses `dispatchWith` and a `CommandSupervisor` instead.
    let dispatch (requestQuit: unit -> unit) (writeRaw: string -> unit) (send: 'msg -> unit) (cmd: Cmd<'msg>) : unit =
        dispatchWith requestQuit writeRaw send (fun work -> Async.Start work) cmd

/// Runtime-owned lifecycle for `Cmd.ofAsync` work. Every command starts under
/// one cancellation scope tied to the runtime: `Stop` (on quit/teardown) cancels
/// all outstanding work — `Async.Sleep`, `Async.AwaitTask` with the ambient
/// token, and any `let! ct = Async.CancellationToken` check observe it — and
/// from then on sends are dropped rather than enqueued into a dead loop.
/// Exceptions raised *after* an async yield (which `Async.Start` would otherwise
/// throw on a thread-pool thread, out of reach of the loop's handler) are queued
/// and handed to `Program.OnError` on the runtime thread via `TryTakeError`.
type CommandSupervisor<'msg>(send: 'msg -> unit) =
    let cts = new CancellationTokenSource()
    let errors = Collections.Concurrent.ConcurrentQueue<exn>()
    let mutable stopped = 0
    let mutable active = 0

    /// The runtime-scope token every supervised command runs under.
    member _.Token = cts.Token

    /// True once `Stop` has been called.
    member _.IsStopped = Volatile.Read(&stopped) = 1

    /// Number of supervised commands still running.
    member _.ActiveCount = Volatile.Read(&active)

    /// Forward `msg` to the runtime, or drop it if the runtime has stopped.
    member this.Send(msg: 'msg) =
        if not this.IsStopped then
            send msg

    /// Start `work` under the runtime's cancellation scope. Failures (other than
    /// cancellation) are queued for `TryTakeError`; nothing starts after `Stop`.
    member this.Start(work: Async<unit>) =
        if not this.IsStopped then
            Interlocked.Increment(&active) |> ignore

            // Started as a task so the continuation below runs in *every* outcome —
            // including cancellation before the work was ever scheduled, where an
            // async `try/finally` would never have been entered.
            let task = Async.StartAsTask(work, cancellationToken = cts.Token)

            task.ContinueWith(
                (fun (t: Tasks.Task<unit>) ->
                    try
                        if t.IsFaulted && not this.IsStopped then
                            let ex = t.Exception.Flatten()

                            let inner =
                                if ex.InnerExceptions.Count = 1 then
                                    ex.InnerExceptions.[0]
                                else
                                    ex :> exn

                            match inner with
                            | :? OperationCanceledException when cts.IsCancellationRequested -> ()
                            | e -> errors.Enqueue e
                    finally
                        Interlocked.Decrement(&active) |> ignore),
                Tasks.TaskContinuationOptions.ExecuteSynchronously
            )
            |> ignore

    /// The next queued async failure, if any.
    member _.TryTakeError() : exn option =
        match errors.TryDequeue() with
        | true, ex -> Some ex
        | _ -> None

    /// Cancel all outstanding work and reject further sends/starts. Idempotent.
    member _.Stop() =
        if Interlocked.Exchange(&stopped, 1) = 0 then
            try
                cts.Cancel()
            with _ ->
                ()

    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cts.Dispose()

/// The runtime's message pump, factored out so its fairness is testable without
/// a terminal. Handles queued messages until the queue is empty, `maxMessages`
/// have been handled, `budget` has elapsed, or `stop ()` turns true — whichever
/// comes first. Messages still queued stay there for the next loop iteration, so
/// a self-rescheduling `Cmd.ofMsg` chain or a fast producer can't starve
/// rendering, input, or shutdown.
module MessagePump =
    /// Default cap on messages per loop iteration.
    let defaultMaxMessages = 4096

    /// Default time slice for one pump (roughly half a 30 FPS frame).
    let defaultBudget = TimeSpan.FromMilliseconds 16.0

    /// Run one bounded pump; returns how many messages were handled.
    let run
        (tryDequeue: unit -> 'msg option)
        (handle: 'msg -> unit)
        (stop: unit -> bool)
        (maxMessages: int)
        (budget: TimeSpan)
        : int =
        let sw = Diagnostics.Stopwatch.StartNew()
        let mutable handled = 0
        let mutable go = true

        while go do
            if handled >= maxMessages || sw.Elapsed >= budget || stop () then
                go <- false
            else
                match tryDequeue () with
                | Some m ->
                    handle m
                    handled <- handled + 1
                | None -> go <- false

        handled

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
        /// Hit-test mouse events against the previous frame's `Focusable` regions.
        /// Called with the topmost region under the cursor (`None` if none) and the
        /// mouse event; returning `Some msg` consumes the event (it does *not* also
        /// go to `MapInput`). Default ignores it (mouse goes straight to `MapInput`),
        /// so apps that hand-compute hit rects are unaffected. Lets an app focus or
        /// activate by `RegionId` instead of re-deriving rects (see `Focusable.region`).
        OnMouseRegion: RegionId option -> MouseEvent -> 'msg option
    }

module Program =
    /// Create a program from the three core functions. Compose the optional behavior
    /// (input, subscriptions, quit policy, …) with the `with*` builders, then hand it
    /// to `Runtime.run`.
    let create
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
          ThemeNotifications = false
          OnMouseRegion = fun _ _ -> None }

    /// Former name of `create`, kept as an alias for source compatibility. Prefer
    /// `Program.create`.
    let mkProgram = create

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

    /// Route mouse events through the previous frame's `Focusable` region table: the
    /// handler gets the topmost `RegionId` under the cursor (or `None`) and the mouse
    /// event, and `Some msg` consumes it. Lets an app focus/activate by id rather than
    /// hand-computing hit rects. Tag regions in the view with `Focusable.region`.
    /// Regions inside a `Scroll` are hit-tested in screen space, `Filled`/`Scrim`
    /// layers block regions beneath them, and a press captures its region for
    /// the drag and release that follow (see `PointerCapture`).
    let withMouseRegion (f: RegionId option -> MouseEvent -> 'msg option) (program: Program<'model, 'msg>) =
        { program with OnMouseRegion = f }

/// Pointer capture for region-routed mouse input (`Program.withMouseRegion`).
/// A button press captures the region under the pointer; every drag motion and
/// the release that follow are routed to *that* region, even after the pointer
/// leaves it (or passes over another region), until the release ends the
/// capture. Wheel events never capture.
module PointerCapture =
    let private isWheel (me: MouseEvent) =
        match me.Button with
        | ScrollUp
        | ScrollDown
        | ScrollLeft
        | ScrollRight -> true
        | _ -> false

    /// Given the current capture, the region hit-tested under the pointer, and
    /// the event, return `(target region, new capture)`.
    let route (captured: RegionId option) (hit: RegionId option) (me: MouseEvent) : RegionId option * RegionId option =
        if isWheel me then
            hit, captured
        elif me.Pressed && not me.Moved then
            // Press: capture whatever is under the pointer (None clears a stale one).
            hit, hit
        elif me.Pressed && me.Moved then
            // Drag: stays with the captured region, if any.
            (match captured with
             | Some _ -> captured
             | None -> hit),
            captured
        else
            // Release: delivered to the captured region, then capture ends.
            (match captured with
             | Some _ -> captured
             | None -> hit),
            None

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
        surface, laidOut

    /// How long a lone `ESC` (or a split escape sequence) may wait for the rest
    /// of its bytes before it's settled as the Escape key.
    let escapeTimeout = TimeSpan.FromMilliseconds 30.0

    /// How long a bracketed paste may stall (no bytes at all) before the decoder
    /// gives up on its end marker and emits what it has — still as a `Paste`,
    /// never as keys.
    let pasteStallTimeout = TimeSpan.FromSeconds 3.0

    /// Run `f`, swallowing (but not hiding from `onError`) any exception — used
    /// for teardown steps so one failing write can't skip the rest of cleanup.
    let private attempt (onError: exn -> unit) (f: unit -> unit) =
        try
            f ()
        with ex ->
            try
                onError ex
            with _ ->
                ()

    let run (program: Program<'model, 'msg>) =
        // Terminal ownership is bracketed from the *first* mutation: every setup
        // step records what it changed, and the `finally` undoes exactly that —
        // even if `Init`, the initial command, or a setup write throws.
        let mutable rawModeOn = false
        let mutable altScreenOn = false
        let mutable modesOn = false
        let mutable themeOn = false

        let msgQueue = Collections.Generic.Queue<'msg>()
        let queueLock = obj ()

        let sendMsg msg =
            lock queueLock (fun () -> msgQueue.Enqueue(msg))

        let tryDequeue () =
            lock queueLock (fun () ->
                if msgQueue.Count > 0 then
                    Some(msgQueue.Dequeue())
                else
                    None)

        let backlog () =
            lock queueLock (fun () -> msgQueue.Count > 0)

        use supervisor = new CommandSupervisor<'msg>(sendMsg)

        // Set by `Cmd.quit` (and the quit key). Checked inside the message pump so
        // a busy queue can't delay shutdown, then folded into `state.Running`.
        let mutable quitRequested = false
        let requestQuit () = quitRequested <- true

        // `Cmd.writeRaw` hook (clipboard, Kitty graphics, …): write the escape
        // straight to the terminal — it paints no cells, so it bypasses the diff.
        let writeRaw (s: string) =
            Console.Out.Write(s)
            Console.Out.Flush()

        let dispatch cmd =
            Cmd.dispatchWith requestQuit writeRaw supervisor.Send supervisor.Start cmd

        try
            match TerminalMode.trySetupRawMode () with
            | Ok() -> rawModeOn <- true
            | Error reason -> program.OnError(InvalidOperationException($"Mire: raw mode setup failed: {reason}"))

            altScreenOn <- true
            Console.Out.Write(ANSI.enterAltScreen)
            Console.Out.Write(ANSI.clearScreen)
            modesOn <- true
            Console.Out.Write(ANSI.enableMouse)
            Console.Out.Write(ANSI.enableFocusEvents)
            Console.Out.Write(ANSI.enableBracketedPaste)
            Console.Out.Write(ANSI.enableKittyKeyboard)

            if program.ThemeNotifications then
                themeOn <- true
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

            // Incremental input decoder: retains split escape sequences, partial
            // UTF-8, and unfinished pastes across reads (see `InputDecoder`).
            use decoder = new InputDecoder(1 <<< 20)

            // Retained region table from the last rendered frame — what mouse events are
            // hit-tested against (`Program.withMouseRegion`).
            let mutable lastRegions: (RegionId * Rect) list = []
            // The region holding pointer capture during a drag (`PointerCapture`).
            let mutable captured: RegionId option = None

            // Dispatch initial command
            dispatch initialCmd

            let sw = Diagnostics.Stopwatch.StartNew()
            let mutable lastTick = TimeSpan.Zero
            let mutable lastInputAt = TimeSpan.Zero
            let mutable lastRenderAt = TimeSpan.Zero - TimeSpan.FromSeconds 1.0
            let frameInterval = TimeSpan.FromMilliseconds(33.0) // ~30 FPS

            let routeInput (inputEvent: InputEvent) =
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
                    quitRequested <- true
                else
                    // Mouse events first hit-test the retained region
                    // table; a handled hit consumes the event.
                    let regionMsg =
                        match inputEvent with
                        | Mouse me ->
                            let hit = Layout.regionAt lastRegions (Point.Create(me.X, me.Y))
                            let target, nextCapture = PointerCapture.route captured hit me
                            captured <- nextCapture
                            program.OnMouseRegion target me
                        | _ -> None

                    match regionMsg with
                    | Some msg -> sendMsg msg
                    | None ->
                        match program.MapInput inputEvent with
                        | Some msg -> sendMsg msg
                        | None -> ()

            while state.Running do
                try
                    let loopStart = sw.Elapsed

                    // Async command failures surface here, on the runtime thread.
                    let mutable pendingError = supervisor.TryTakeError()

                    while pendingError.IsSome do
                        program.OnError pendingError.Value
                        pendingError <- supervisor.TryTakeError()

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

                    // Process input: a bounded, chunked read, fed to the persistent
                    // decoder (which emits only complete events and keeps any split
                    // tail for the next read). One read() can carry several events.
                    let rawBytes = InputParser.readRawBytes ()

                    let events =
                        if rawBytes.Length > 0 then
                            lastInputAt <- sw.Elapsed
                            decoder.Feed rawBytes
                        elif decoder.HasPending then
                            // Nothing new arrived: settle a lone ESC after the escape
                            // timeout, or a stalled paste after the paste timeout.
                            let idle = sw.Elapsed - lastInputAt
                            let limit = if decoder.InPaste then pasteStallTimeout else escapeTimeout
                            if idle >= limit then decoder.Flush() else []
                        else
                            []

                    for inputEvent in events do
                        routeInput inputEvent

                    // Process messages — bounded (count + time slice), so a
                    // self-rescheduling chain or a flood of async sends can't starve
                    // rendering or shutdown. Leftovers run next iteration. The frame
                    // renders once per pump (throttled below), so a streaming burst of
                    // N messages costs N `Update`s and one render.
                    MessagePump.run
                        tryDequeue
                        (fun m ->
                            let newModel, cmd = program.Update m state.Model

                            state <-
                                { state with
                                    Model = newModel
                                    NeedsRender = true }

                            dispatch cmd)
                        (fun () -> quitRequested)
                        MessagePump.defaultMaxMessages
                        MessagePump.defaultBudget
                    |> ignore

                    // A command, the quit key, or the initial cmd requested
                    // shutdown. Fold it in now so this frame still renders the final
                    // state; the loop then exits and the `finally` teardown runs.
                    if quitRequested then
                        state <- { state with Running = false }

                    let hasBacklog = backlog ()

                    // Render if needed — immediately when idle, and at most once per
                    // frame interval while a backlog is being worked through.
                    if
                        state.NeedsRender
                        && (not hasBacklog
                            || not state.Running
                            || sw.Elapsed - lastRenderAt >= frameInterval)
                    then
                        let view = program.View state.Model
                        let surface, laidOut = renderFrame view state.LastSize
                        lastRegions <- Layout.collectRegions laidOut

                        let runs = Diff.compute state.PreviousSurface surface

                        match state.PreviousSurface with
                        | None ->
                            Diff.clearScreen Console.Out
                            Diff.renderToTerminal runs Console.Out
                        | Some _ ->
                            // An unchanged frame writes nothing (not even the
                            // synchronized-update brackets).
                            if not (List.isEmpty runs) then
                                Diff.renderToTerminal runs Console.Out

                        lastRenderAt <- sw.Elapsed

                        state <-
                            { state with
                                PreviousSurface = Some surface
                                NeedsRender = false }

                    // Throttle to ~30 FPS — but don't idle while messages are queued.
                    let elapsed = sw.Elapsed - loopStart

                    if not hasBacklog && state.Running && elapsed < frameInterval then
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
            // Cancel outstanding async commands first so nothing races teardown;
            // late sends are dropped from here on.
            supervisor.Stop()

            // Undo exactly what setup did, each step independently — a failing
            // write must not skip restoring the terminal's line discipline.
            let step f = attempt (fun _ -> ()) f

            if themeOn then
                step (fun () -> Console.Out.Write(ANSI.disableThemeNotifications))

            if modesOn then
                step (fun () -> Console.Out.Write(ANSI.disableKittyKeyboard))
                step (fun () -> Console.Out.Write(ANSI.disableBracketedPaste))
                step (fun () -> Console.Out.Write(ANSI.disableFocusEvents))
                step (fun () -> Console.Out.Write(ANSI.disableMouse))

            if altScreenOn then
                step (fun () -> Console.Out.Write(ANSI.exitAltScreen))

            step (fun () -> Console.Out.Write(ANSI.cursorShow))
            step (fun () -> Console.Out.Write(ANSI.resetStyle))
            step (fun () -> Console.Out.Flush())

            if rawModeOn then
                step (fun () -> TerminalMode.restoreMode ())

            step (fun () -> Console.WriteLine())
            step (fun () -> Console.WriteLine("Mire exited."))
