open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.MinesweeperDemo

// ---------------------------------------------------------------------------
// Mire.MinesweeperDemo — a keyboard-driven Minesweeper, modelled on
// mia1024/terminal-minesweeper. Dogfoods the hand-rolled grid (no Table widget
// yet), Backdrop.behind for the cell cursor, per-cell styling, and an
// Every-driven timer subscription. All game rules live in Board.fs.
//
//   arrows / WASD  move      Space  reveal     F  flag
//   C  chord                 R  restart        1/2/3  difficulty     Ctrl+C  quit
// ---------------------------------------------------------------------------

let private cellWidth = 2 // each cell is one glyph plus a trailing space

module private Styles =
    let frame = Style.Default.WithForeground(Color.Rgb(0x33uy, 0x3Auy, 0x44uy))
    let hidden = Style.Default.WithForeground(Color.Rgb(0x5Cuy, 0x63uy, 0x70uy))

    let flag =
        Style.Default.WithForeground(Color.Rgb(0xFFuy, 0xCAuy, 0x28uy)).WithBold(true)

    let mine =
        Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy)).WithBold(true)

    let zero = Style.Default.WithForeground(Color.Rgb(0x40uy, 0x46uy, 0x52uy))
    let status = Style.Default.WithForeground(Color.Rgb(0xCBuy, 0xD0uy, 0xD8uy))
    let hint = Style.Default.WithForeground(Color.Rgb(0x80uy, 0x86uy, 0x90uy))

    let won =
        Style.Default.WithForeground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)).WithBold(true)

    let lost =
        Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy)).WithBold(true)

    // cursor = dark glyph on light grey, like the spreadsheet demo
    let cursor =
        Style.Default
            .WithForeground(Color.Rgb(0x10uy, 0x14uy, 0x18uy))
            .WithBackground(Color.Rgb(0x9Auy, 0xA2uy, 0xAEuy))

    // classic 1-8 number colours
    let private num1 = Style.Default.WithForeground(Color.Rgb(0x42uy, 0xA5uy, 0xF5uy)) // blue
    let private num2 = Style.Default.WithForeground(Color.Rgb(0x66uy, 0xBBuy, 0x6Auy)) // green
    let private num3 = Style.Default.WithForeground(Color.Rgb(0xEFuy, 0x53uy, 0x50uy)) // red
    let private num4 = Style.Default.WithForeground(Color.Rgb(0x3Fuy, 0x51uy, 0xB5uy)) // navy
    let private num5 = Style.Default.WithForeground(Color.Rgb(0xB7uy, 0x1Cuy, 0x1Cuy)) // maroon
    let private num6 = Style.Default.WithForeground(Color.Rgb(0x26uy, 0xC6uy, 0xDAuy)) // teal
    let private num7 = Style.Default.WithForeground(Color.Rgb(0xECuy, 0xEFuy, 0xF1uy)) // near-white
    let private num8 = Style.Default.WithForeground(Color.Rgb(0x90uy, 0x90uy, 0x90uy)) // grey

    let number (n: int) : Style =
        match n with
        | 1 -> num1
        | 2 -> num2
        | 3 -> num3
        | 4 -> num4
        | 5 -> num5
        | 6 -> num6
        | 7 -> num7
        | _ -> num8

// model --------------------------------------------------------------------
type Model =
    { Board: Board
      Cursor: int * int // (row, col)
      Size: Size
      StartTime: DateTime option // set on the first reveal; None until then
      ElapsedMs: int // refreshed on each Tick while Playing
      Seed: int } // bumped on restart / difficulty change to vary layouts

type Msg =
    | Move of int * int // (dRow, dCol)
    | Reveal
    | Flag
    | Chord
    | Restart
    | SetDifficulty of Difficulty
    | Tick
    | Resized of Size
    | Ignore

let private clamp lo hi v = max lo (min hi v)

let private newBoard (difficulty: Difficulty) (seed: int) (size: Size) : Model =
    { Board = Board.empty difficulty
      Cursor = (0, 0)
      Size = size
      StartTime = None
      ElapsedMs = 0
      Seed = seed }

let init () =
    newBoard Beginner 0 (Size.Create(80, 24)), Cmd.none

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | Resized size -> { m with Size = size }, Cmd.none

    | Move(dRow, dCol) ->
        let r = clamp 0 (m.Board.Rows - 1) (fst m.Cursor + dRow)
        let c = clamp 0 (m.Board.Cols - 1) (snd m.Cursor + dCol)
        { m with Cursor = (r, c) }, Cmd.none

    | Reveal ->
        let r, c = m.Cursor
        let rng = Random(m.Seed)
        let board = Board.reveal rng r c m.Board

        let startTime =
            if m.StartTime.IsNone then
                Some DateTime.Now
            else
                m.StartTime

        { m with
            Board = board
            StartTime = startTime },
        Cmd.none

    | Flag ->
        let r, c = m.Cursor

        { m with
            Board = Board.toggleFlag r c m.Board },
        Cmd.none

    | Chord ->
        let r, c = m.Cursor
        let rng = Random(m.Seed)

        { m with
            Board = Board.chord rng r c m.Board },
        Cmd.none

    | Restart -> newBoard m.Board.Difficulty (m.Seed + 1) m.Size, Cmd.none

    | SetDifficulty d -> newBoard d (m.Seed + 1) m.Size, Cmd.none

    | Tick ->
        match m.StartTime with
        | Some start when m.Board.Status = Playing ->
            { m with
                ElapsedMs = int (DateTime.Now - start).TotalMilliseconds },
            Cmd.none
        | _ -> m, Cmd.none

    | Ignore -> m, Cmd.none

// view ----------------------------------------------------------------------

/// (glyph-string of width `cellWidth`, style) for a cell in its current state.
let private cellContent (cell: Cell) : string * Style =
    match cell.State with
    | Hidden -> "· ", Styles.hidden
    | Flagged -> "⚑ ", Styles.flag
    | Revealed ->
        if cell.Mine then "✸ ", Styles.mine
        elif cell.Adjacent = 0 then "  ", Styles.zero
        else string cell.Adjacent + " ", Styles.number cell.Adjacent

let private renderCell (m: Model) (r: int) (c: int) : LayoutNode<Msg> =
    let text, style = cellContent m.Board.Cells.[r, c]

    if (r, c) = m.Cursor then
        Backdrop.behind Styles.cursor (Text.text text Styles.cursor)
    else
        Text.text text style

let private renderRow (m: Model) (r: int) : LayoutNode<Msg> =
    Stack.hstackOf [ for c in 0 .. m.Board.Cols - 1 -> Stack.sized (Length.Cells cellWidth) (renderCell m r c) ]

let private formatTime (ms: int) : string =
    let totalSeconds = ms / 1000
    sprintf "%02d:%02d" (totalSeconds / 60) (totalSeconds % 60)

let view (m: Model) : LayoutNode<Msg> =
    let face, faceStyle =
        match m.Board.Status with
        | Playing -> "🙂", Styles.status
        | Won -> "😎 you win!", Styles.won
        | Lost -> "😵 boom", Styles.lost

    let statusBar =
        Text.text (sprintf " %s   ⚑ %d   ⏱ %s" face (Board.minesRemaining m.Board) (formatTime m.ElapsedMs)) faceStyle

    let grid =
        Box.box
            Styles.frame
            [ Stack.vstackOf [ for r in 0 .. m.Board.Rows - 1 -> Stack.sized (Length.Cells 1) (renderRow m r) ] ]

    let footer =
        Text.text
            " arrows/WASD move · Space reveal · F flag · C chord · R restart · 1/2/3 size · Ctrl+C quit"
            Styles.hint

    Dock.dock [ Dock.top 1 statusBar; Dock.bottom 1 footer; Dock.fill grid ]

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | ArrowUp -> Some(Move(-1, 0))
        | ArrowDown -> Some(Move(1, 0))
        | ArrowLeft -> Some(Move(0, -1))
        | ArrowRight -> Some(Move(0, 1))
        | Space -> Some Reveal
        | Char s ->
            match s.ToLowerInvariant() with
            | "w" -> Some(Move(-1, 0))
            | "s" -> Some(Move(1, 0))
            | "a" -> Some(Move(0, -1))
            | "d" -> Some(Move(0, 1))
            | "f" -> Some Flag
            | "c" -> Some Chord
            | "r" -> Some Restart
            | "1" -> Some(SetDifficulty Beginner)
            | "2" -> Some(SetDifficulty Intermediate)
            | "3" -> Some(SetDifficulty Expert)
            | _ -> Some Ignore
        | _ -> Some Ignore
    | _ -> Some Ignore

let subscriptions (_: Model) : Sub<Msg> list =
    [ TerminalResize Resized
      Every(TimeSpan.FromMilliseconds 100.0, fun () -> Tick) ]

// headless verification -----------------------------------------------------
let private printSurface (surface: Surface) =
    let width = surface.Size.Width
    let rule = String.replicate width "─"
    printfn "  ┌%s┐" rule

    for y in 0 .. surface.Size.Height - 1 do
        let sb = Text.StringBuilder()

        for x in 0 .. width - 1 do
            let g = surface.[x, y].Grapheme
            sb.Append(if String.IsNullOrEmpty g then " " else g) |> ignore

        printfn "  │%s│" (sb.ToString())

    printfn "  └%s┘" rule

let private runDump () =
    let size = Size.Create(40, 16)
    // A Beginner board with a fixed seed: reveal the centre (opens a region),
    // flag a corner, and park the cursor so the highlight is visible.
    let rng = Random(7)
    let board = Board.reveal rng 4 4 (Board.empty Beginner) |> Board.toggleFlag 8 0

    let model =
        { newBoard Beginner 7 size with
            Board = board
            Cursor = (2, 3) }

    printfn "Mire.MinesweeperDemo — Beginner, centre revealed, (8,0) flagged, cursor at (2,3)\n"
    let surface = Surface(size)
    Layout.measure (Rect.FromOrigin size) (view model) |> Layout.render surface
    printSurface surface

[<EntryPoint>]
let main argv =
    if argv |> Array.contains "--dump" then
        runDump ()
        0
    else
        Program.mkProgram init update view
        |> Program.withMapInput mapInput
        |> Program.withSubscriptions subscriptions
        |> Runtime.run

        0
