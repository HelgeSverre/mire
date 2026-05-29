open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.SpreadsheetDemo

// ---------------------------------------------------------------------------
// Mire.SpreadsheetDemo — an A1 grid with a cell cursor, in-cell editing, and a
// small formula engine (=B2*C2, =SUM(A1:A3), …). Dogfoods Backdrop.behind for
// the full-cell cursor highlight; hand-rolls the grid + text editing (no Table
// or text-input widget exists yet — that's the gap this demo surfaces).
// ---------------------------------------------------------------------------

let private colW = 9
let private rowHdrW = 4

// styles -------------------------------------------------------------------
let private sNormal = Style.Default.WithForeground(Color.Rgb(0xCBuy, 0xD0uy, 0xD8uy))
let private sNum    = Style.Default.WithForeground(Color.Rgb(0x8Fuy, 0xD6uy, 0xFFuy))
let private sHeader = Style.Default.WithForeground(Color.Rgb(0x7Cuy, 0x84uy, 0x90uy)).WithBold(true)
let private sBorder = Style.Default.WithForeground(Color.Rgb(0x33uy, 0x3Auy, 0x44uy))
let private sErr    = Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy))
let private sDim    = Style.Default.WithForeground(Color.Rgb(0x80uy, 0x86uy, 0x90uy))
// cursor cell = dark on light grey; editing cell = dark on emerald
let private sSel    = Style.Default.WithForeground(Color.Rgb(0x10uy, 0x14uy, 0x18uy)).WithBackground(Color.Rgb(0x9Auy, 0xA2uy, 0xAEuy))
let private sEdit   = Style.Default.WithForeground(Color.Rgb(0x06uy, 0x16uy, 0x0Auy)).WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))

// text fitting (cell content is short/ASCII-ish; char-width is fine here) ----
let private fit (w: int) (s: string) =
    if s.Length >= w then s.Substring(0, w) else s + String(' ', w - s.Length)

let private fitRight (w: int) (s: string) =
    if s.Length >= w then s.Substring(0, w) else String(' ', w - s.Length) + s

let private fitCenter (w: int) (s: string) =
    if s.Length >= w then s.Substring(0, w)
    else
        let total = w - s.Length
        let left = total / 2
        String(' ', left) + s + String(' ', total - left)

let private cellText (v: Sheet.Value) : string =
    match v with
    | Sheet.Blank -> String(' ', colW)
    | Sheet.Num _ -> fitRight (colW - 1) (Sheet.show v) + " "   // numbers right-aligned
    | _ -> " " + fit (colW - 1) (Sheet.show v)                  // text/errors left-aligned

// model --------------------------------------------------------------------
type Model =
    { Raw: Map<int * int, string>
      Values: Map<int * int, Sheet.Value>
      Cur: int * int
      Editing: string option
      Top: int
      Size: Size }

type Msg =
    | Move of int * int
    | Typed of string
    | Commit
    | Back
    | Cancel
    | Clear
    | Resized of Size
    | Ignore

let private seed =
    [ "A1", "Item";   "B1", "Qty"; "C1", "Price"; "D1", "Total"
      "A2", "Widget"; "B2", "3";   "C2", "4";     "D2", "=B2*C2"
      "A3", "Gadget"; "B3", "5";   "C3", "2.5";   "D3", "=B3*C3"
      "A4", "Gizmo";  "B4", "2";   "C4", "9";     "D4", "=B4*C4"
      "A6", "Totals"; "B6", "=COUNT(B2:B4)"; "C6", "=AVG(C2:C4)"; "D6", "=SUM(D2:D4)" ]

let private seededRaw =
    seed
    |> List.choose (fun (n, v) -> Sheet.parseRef n |> Option.map (fun rc -> rc, v))
    |> Map.ofList

let private recompute (m: Model) = { m with Values = Sheet.compute m.Raw }

let init () =
    recompute
        { Raw = seededRaw
          Values = Map.empty
          Cur = (1, 1)
          Editing = None
          Top = 0
          Size = Size.Create(100, 30) },
    Cmd.none

let private clamp lo hi v = max lo (min hi v)

/// How many data rows fit below the header in the grid box.
let private visibleRows (size: Size) = max 1 (size.Height - 3 - 1 - 2 - 1)

let private keepInView (m: Model) =
    let r = fst m.Cur
    let vis = visibleRows m.Size
    let top =
        if r < m.Top then r
        elif r >= m.Top + vis then r - vis + 1
        else m.Top
    { m with Top = clamp 0 (max 0 (Sheet.rows - vis)) top }

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | Resized s -> { m with Size = s }, Cmd.none
    | Move(dr, dc) ->
        match m.Editing with
        | Some _ -> m, Cmd.none // ignore movement mid-edit; commit/cancel first
        | None ->
            let r = clamp 0 (Sheet.rows - 1) (fst m.Cur + dr)
            let c = clamp 0 (Sheet.cols - 1) (snd m.Cur + dc)
            keepInView { m with Cur = (r, c) }, Cmd.none
    | Typed s ->
        match m.Editing with
        | Some b -> { m with Editing = Some(b + s) }, Cmd.none
        | None -> { m with Editing = Some s }, Cmd.none // start editing, replacing the cell
    | Back ->
        match m.Editing with
        | Some b when b.Length > 0 -> { m with Editing = Some(b.Substring(0, b.Length - 1)) }, Cmd.none
        | _ -> m, Cmd.none
    | Cancel -> { m with Editing = None }, Cmd.none
    | Commit ->
        match m.Editing with
        | None -> { m with Editing = Some(Map.tryFind m.Cur m.Raw |> Option.defaultValue "") }, Cmd.none
        | Some b ->
            let raw = if b.Trim() = "" then Map.remove m.Cur m.Raw else Map.add m.Cur b m.Raw
            let r = clamp 0 (Sheet.rows - 1) (fst m.Cur + 1)
            recompute (keepInView { m with Raw = raw; Editing = None; Cur = (r, snd m.Cur) }), Cmd.none
    | Clear ->
        match m.Editing with
        | Some _ -> m, Cmd.none
        | None -> recompute { m with Raw = Map.remove m.Cur m.Raw }, Cmd.none
    | Ignore -> m, Cmd.none

// view ---------------------------------------------------------------------
let private cellNode (m: Model) (r: int) (c: int) : LayoutNode<Msg> =
    let focused = (r, c) = m.Cur
    match m.Editing with
    | Some buf when focused -> Backdrop.behind sEdit (Text.text (fit colW (" " + buf + "▏")) sEdit)
    | _ ->
        let v = Map.tryFind (r, c) m.Values |> Option.defaultValue Sheet.Blank
        let txt = cellText v
        if focused then
            Backdrop.behind sSel (Text.text txt sSel)
        else
            let st =
                match v with
                | Sheet.Err _ -> sErr
                | Sheet.Num _ -> sNum
                | _ -> sNormal
            Text.text txt st

let private gridRow (m: Model) (r: int) : LayoutNode<Msg> =
    Stack.hstackOf
        [ yield Stack.sized (Length.Cells rowHdrW) (Text.text (fitRight (rowHdrW - 1) (string (r + 1)) + " ") sHeader)
          for c in 0 .. Sheet.cols - 1 -> Stack.sized (Length.Cells colW) (cellNode m r c) ]

let view (m: Model) : LayoutNode<Msg> =
    let vis = visibleRows m.Size

    // formula / edit bar
    let curRaw = Map.tryFind m.Cur m.Raw |> Option.defaultValue ""
    let barText, barStyle =
        match m.Editing with
        | Some b -> b + "▏", sEdit.WithBackground(Color.Default).WithForeground(Color.Rgb(0x7Fuy, 0xE0uy, 0x9Cuy))
        | None -> curRaw, sNormal
    let bar =
        Box.box sBorder
            [ Text.text (sprintf " %s   │ %s" (Sheet.name (fst m.Cur) (snd m.Cur)) barText) barStyle ]

    // grid: header row + the visible window of data rows
    let header =
        Stack.hstackOf
            [ yield Stack.sized (Length.Cells rowHdrW) (Text.text (String(' ', rowHdrW)) sHeader)
              for c in 0 .. Sheet.cols - 1 -> Stack.sized (Length.Cells colW) (Text.text (fitCenter colW (Sheet.colLetter c)) sHeader) ]
    let lastRow = min (Sheet.rows - 1) (m.Top + vis - 1)
    let gridChildren =
        [ yield Stack.sized (Length.Cells 1) header
          for r in m.Top .. lastRow -> Stack.sized (Length.Cells 1) (gridRow m r) ]
    let grid = Box.box sBorder [ Stack.vstackOf gridChildren ]

    let footer =
        Text.text
            " arrows move · type or Enter to edit · Enter commit · Esc cancel · Del clear · try =SUM(D2:D4) · Ctrl+C quit"
            sDim

    Dock.dock
        [ Dock.top 3 bar
          Dock.bottom 1 footer
          Dock.fill grid ]

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | ArrowUp -> Some(Move(-1, 0))
        | ArrowDown -> Some(Move(1, 0))
        | ArrowLeft -> Some(Move(0, -1))
        | ArrowRight -> Some(Move(0, 1))
        | Tab -> Some(Move(0, 1))
        | Enter -> Some Commit
        | Escape -> Some Cancel
        | Backspace -> Some Back
        | Delete -> Some Clear
        | Char c when not ke.Modifiers.Ctrl -> Some(Typed c)
        | _ -> Some Ignore
    | _ -> Some Ignore

let subscriptions (_: Model) : Sub<Msg> list = [ TerminalResize Resized ]

// headless verification -----------------------------------------------------
let private printSurface (surface: Surface) =
    let w = surface.Size.Width
    let bar = String.replicate w "─"
    printfn "  ┌%s┐" bar
    for y in 0 .. surface.Size.Height - 1 do
        let sb = Text.StringBuilder()
        for x in 0 .. w - 1 do
            let g = surface.[x, y].Grapheme
            sb.Append(if String.IsNullOrEmpty g then " " else g) |> ignore
        printfn "  │%s│" (sb.ToString())
    printfn "  └%s┘" bar

let private runDump () =
    let size = Size.Create(86, 16)
    let model = recompute { (fst (init ())) with Cur = Sheet.parseRef "D6" |> Option.get; Size = size }
    printfn "Mire.SpreadsheetDemo — cursor on D6 = SUM(D2:D4)\n"
    let surface = Surface(size)
    Layout.measure (Rect.FromOrigin size) (view model) |> Layout.render surface
    printSurface surface
    printfn "\n  D2=%s  D3=%s  D4=%s  D6=%s"
        (Sheet.show model.Values.[(1, 3)]) (Sheet.show model.Values.[(2, 3)])
        (Sheet.show model.Values.[(3, 3)]) (Sheet.show model.Values.[(5, 3)])

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
