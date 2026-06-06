open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.Demo.Spreadsheet

// ---------------------------------------------------------------------------
// Mire.Demo.Spreadsheet — an A1 grid with a cell cursor, in-cell editing, and a
// small formula engine (=B2*C2, =SUM(A1:A3), …). Dogfoods Backdrop.behind for
// the full-cell cursor highlight and Mire.Core.TextBuffer + Widgets.Input for the
// cell editor; hand-rolls the grid itself (no Table widget exists yet — that's
// the gap this demo surfaces).
// ---------------------------------------------------------------------------

let private columnWidth = 9
let private rowLabelWidth = 4
let private cellPitch = columnWidth + 1 // a cell plus its left │ gridline

module private Styles =
    let text = Style.Default.WithForeground(Color.Rgb(0xCBuy, 0xD0uy, 0xD8uy))
    let number = Style.Default.WithForeground(Color.Rgb(0x8Fuy, 0xD6uy, 0xFFuy))

    let header =
        Style.Default.WithForeground(Color.Rgb(0x7Cuy, 0x84uy, 0x90uy)).WithBold(true)

    let border = Style.Default.WithForeground(Color.Rgb(0x33uy, 0x3Auy, 0x44uy))
    let error = Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy))
    let hint = Style.Default.WithForeground(Color.Rgb(0x80uy, 0x86uy, 0x90uy))

    // active-cell cursor = dark text on light grey
    let cursor =
        Style.Default
            .WithForeground(Color.Rgb(0x10uy, 0x14uy, 0x18uy))
            .WithBackground(Color.Rgb(0x9Auy, 0xA2uy, 0xAEuy))

    // editing cell = dark text on emerald
    let editing =
        Style.Default
            .WithForeground(Color.Rgb(0x06uy, 0x16uy, 0x0Auy))
            .WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))

    // a cell referenced by the current formula = subtle lighter slate fill
    let referencedBg = Color.Rgb(0x2Auy, 0x32uy, 0x40uy)

    // the live reference-picker target = slightly brighter than `referenced`
    let targetBg = Color.Rgb(0x3Euy, 0x4Cuy, 0x66uy)

    // the block caret inside the editing cell
    let caret =
        Style.Default.WithForeground(Color.Rgb(0x06uy, 0x16uy, 0x0Auy)).WithBackground(Color.White)

// text fitting (cell content is short/ASCII-ish; char-width is fine here) ----
let private fitLeft (width: int) (s: string) =
    if s.Length >= width then
        s.Substring(0, width)
    else
        s + String(' ', width - s.Length)

let private fitRight (width: int) (s: string) =
    if s.Length >= width then
        s.Substring(0, width)
    else
        String(' ', width - s.Length) + s

let private fitCenter (width: int) (s: string) =
    if s.Length >= width then
        s.Substring(0, width)
    else
        let pad = width - s.Length
        let leftPad = pad / 2
        String(' ', leftPad) + s + String(' ', pad - leftPad)

/// Format a value into a fixed-width cell string (numbers right-aligned).
let private formatCell (value: Sheet.Value) : string =
    match value with
    | Sheet.Blank -> String(' ', columnWidth)
    | Sheet.Num _ -> fitRight (columnWidth - 1) (Sheet.formatValue value) + " "
    | _ -> " " + fitLeft (columnWidth - 1) (Sheet.formatValue value)

// model --------------------------------------------------------------------
type Model =
    { Inputs: Map<Sheet.CellRef, string> // raw text entered per cell
      Values: Map<Sheet.CellRef, Sheet.Value> // computed from Inputs
      Cursor: Sheet.CellRef // the active cell
      Editing: TextBuffer option // Some while editing the active cell
      Target: Sheet.CellRef option // Some while picking a cell reference (Shift+arrows)
      TopRow: int // first visible row (vertical scroll)
      LeftCol: int // first visible column (horizontal scroll)
      Size: Size }

type Msg =
    | Move of int * int // (dRow, dCol)
    | MoveTarget of int * int // (dRow, dCol) — move the reference-picker target
    | Typed of string
    | Commit
    | Back // backspace
    | Cancel
    | Clear // Del: forward-delete while editing, else clear the cell
    | CursorHome
    | CursorEnd
    | Resized of Size
    | Ignore

let private seedCells =
    [ "A1", "Item"
      "B1", "Qty"
      "C1", "Price"
      "D1", "Total"
      "A2", "Widget"
      "B2", "3"
      "C2", "4"
      "D2", "=B2*C2"
      "A3", "Gadget"
      "B3", "5"
      "C3", "2.5"
      "D3", "=B3*C3"
      "A4", "Gizmo"
      "B4", "2"
      "C4", "9"
      "D4", "=B4*C4"
      "A6", "Totals"
      "B6", "=COUNT(B2:B4)"
      "C6", "=AVG(C2:C4)"
      "D6", "=SUM(D2:D4)" ]

let private initialInputs =
    seedCells
    |> List.choose (fun (name, value) -> Sheet.parseCellRef name |> Option.map (fun cell -> cell, value))
    |> Map.ofList

let private recompute (m: Model) =
    { m with
        Values = Sheet.recalculate m.Inputs }

let init () =
    recompute
        { Inputs = initialInputs
          Values = Map.empty
          Cursor = (1, 1)
          Editing = None
          Target = None
          TopRow = 0
          LeftCol = 0
          Size = Size.Create(100, 30) },
    Cmd.none

let private clamp lo hi v = max lo (min hi v)

/// How many data rows fit in the grid box. The grid box is the Dock.fill region
/// (total height minus the 3-row formula bar and the 1-row footer); inside it the
/// box border eats 2 rows, the column-label header + its rule eat 2 more, and each
/// data row is drawn as 2 lines (the row itself + the gridline beneath it).
let private visibleRowCount (size: Size) =
    let interior = size.Height - 3 - 1 - 2 - 2
    max 1 (interior / 2)

/// How many columns fit across the grid box: full width minus the box border and
/// the row-label gutter, divided by the per-column pitch (cell + its gridline).
let private visibleColCount (size: Size) =
    let interior = size.Width - 2 - rowLabelWidth
    max 1 (min Sheet.columnCount (interior / cellPitch))

/// A horizontal gridline spanning the row-label gutter + every visible column,
/// with ┼ junctions under each vertical gridline (├ at the left edge).
let private separatorLine (cols: int) : string =
    let sb = Text.StringBuilder()
    sb.Append(String(' ', rowLabelWidth)) |> ignore

    for c in 0 .. cols - 1 do
        sb.Append(if c = 0 then "├" else "┼") |> ignore
        sb.Append(String('─', columnWidth)) |> ignore

    sb.ToString()

let private keepRowInView (row: int) (m: Model) =
    let visible = visibleRowCount m.Size

    let topRow =
        if row < m.TopRow then row
        elif row >= m.TopRow + visible then row - visible + 1
        else m.TopRow

    { m with
        TopRow = clamp 0 (max 0 (Sheet.rowCount - visible)) topRow }

let private keepColInView (col: int) (m: Model) =
    let visible = visibleColCount m.Size

    let leftCol =
        if col < m.LeftCol then col
        elif col >= m.LeftCol + visible then col - visible + 1
        else m.LeftCol

    { m with
        LeftCol = clamp 0 (max 0 (Sheet.columnCount - visible)) leftCol }

let private keepCursorInView (m: Model) =
    m |> keepRowInView (fst m.Cursor) |> keepColInView (snd m.Cursor)

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | Resized size -> { m with Size = size }, Cmd.none
    | Move(dRow, dCol) ->
        match m.Editing with
        | Some buffer when dCol <> 0 ->
            // Left/Right move the edit caret within the cell.
            { m with
                Editing =
                    Some(
                        if dCol < 0 then
                            TextBuffer.left buffer
                        else
                            TextBuffer.right buffer
                    ) },
            Cmd.none
        | Some _ -> m, Cmd.none // Up/Down mid-edit: commit or cancel first
        | None ->
            let row = clamp 0 (Sheet.rowCount - 1) (fst m.Cursor + dRow)
            let col = clamp 0 (Sheet.columnCount - 1) (snd m.Cursor + dCol)
            keepCursorInView { m with Cursor = (row, col) }, Cmd.none
    | MoveTarget(dRow, dCol) ->
        // Only active while editing a formula. Seed the target at the cursor, then
        // move it around the grid; scroll so it stays visible.
        match m.Editing with
        | Some buffer when buffer.Text.StartsWith "=" ->
            let baseCell = m.Target |> Option.defaultValue m.Cursor
            let row = clamp 0 (Sheet.rowCount - 1) (fst baseCell + dRow)
            let col = clamp 0 (Sheet.columnCount - 1) (snd baseCell + dCol)
            { m with Target = Some(row, col) } |> keepRowInView row |> keepColInView col, Cmd.none
        | _ -> m, Cmd.none
    | Typed s ->
        match m.Editing with
        | Some buffer ->
            { m with
                Editing = Some(TextBuffer.insert s buffer) },
            Cmd.none
        | None ->
            { m with
                Editing = Some(TextBuffer.ofString s) },
            Cmd.none // start editing, replacing the cell
    | Back ->
        match m.Editing with
        | Some buffer ->
            { m with
                Editing = Some(TextBuffer.backspace buffer) },
            Cmd.none
        | None -> m, Cmd.none
    | Cancel ->
        match m.Target with
        | Some _ -> { m with Target = None }, Cmd.none // dismiss the picker, stay editing
        | None -> { m with Editing = None }, Cmd.none
    | CursorHome ->
        match m.Editing with
        | Some buffer ->
            { m with
                Editing = Some(TextBuffer.home buffer) },
            Cmd.none
        | None -> keepCursorInView { m with Cursor = (fst m.Cursor, 0) }, Cmd.none
    | CursorEnd ->
        match m.Editing with
        | Some buffer ->
            { m with
                Editing = Some(TextBuffer.toEnd buffer) },
            Cmd.none
        | None ->
            keepCursorInView
                { m with
                    Cursor = (fst m.Cursor, Sheet.columnCount - 1) },
            Cmd.none
    | Commit when m.Target.IsSome ->
        // Insert the picked cell's A1 reference at the caret, then resume typing.
        let (tr, tc) = m.Target.Value

        let editing =
            m.Editing
            |> Option.map (fun buf -> TextBuffer.insert (Sheet.cellName tr tc) buf)

        { m with
            Editing = editing
            Target = None },
        Cmd.none
    | Commit ->
        match m.Editing with
        | None ->
            { m with
                Editing = Some(TextBuffer.ofString (Map.tryFind m.Cursor m.Inputs |> Option.defaultValue "")) },
            Cmd.none
        | Some buffer ->
            let entered = buffer.Text

            let inputs =
                if entered.Trim() = "" then
                    Map.remove m.Cursor m.Inputs
                else
                    Map.add m.Cursor entered m.Inputs

            let nextRow = clamp 0 (Sheet.rowCount - 1) (fst m.Cursor + 1)

            recompute (
                keepCursorInView
                    { m with
                        Inputs = inputs
                        Editing = None
                        Cursor = (nextRow, snd m.Cursor) }
            ),
            Cmd.none
    | Clear ->
        match m.Editing with
        | Some buffer ->
            { m with
                Editing = Some(TextBuffer.delete buffer) },
            Cmd.none // forward-delete while editing
        | None ->
            recompute
                { m with
                    Inputs = Map.remove m.Cursor m.Inputs },
            Cmd.none
    | Ignore -> m, Cmd.none

// view ---------------------------------------------------------------------
let private renderCell (m: Model) (refs: Set<Sheet.CellRef>) (row: int) (col: int) : LayoutNode<Msg> =
    let focused = (row, col) = m.Cursor

    match m.Editing with
    | Some buffer when focused ->
        Backdrop.behind Styles.editing (Input.render columnWidth Styles.editing Styles.caret true buffer)
    | _ ->
        let value = Map.tryFind (row, col) m.Values |> Option.defaultValue Sheet.Blank
        let cellStr = formatCell value

        if focused then
            Backdrop.behind Styles.cursor (Text.text cellStr Styles.cursor)
        else
            let fg =
                match value with
                | Sheet.Err _ -> Styles.error
                | Sheet.Num _ -> Styles.number
                | _ -> Styles.text

            // tint cells the current formula references (and the live picker target)
            let highlightBg =
                if m.Target = Some(row, col) then Some Styles.targetBg
                elif refs.Contains(row, col) then Some Styles.referencedBg
                else None

            match highlightBg with
            | Some bg -> Text.text cellStr (fg.WithBackground bg)
            | None -> Text.text cellStr fg

let private gridline = Stack.sized (Length.Cells 1) (Text.text "│" Styles.border)

let private renderRow
    (m: Model)
    (refs: Set<Sheet.CellRef>)
    (firstCol: int)
    (lastCol: int)
    (row: int)
    : LayoutNode<Msg> =
    Stack.hstackOf
        [ yield
              Stack.sized
                  (Length.Cells rowLabelWidth)
                  (Text.text (fitRight rowLabelWidth (string (row + 1))) Styles.header)
          for col in firstCol..lastCol do
              yield gridline
              yield Stack.sized (Length.Cells columnWidth) (renderCell m refs row col) ]

let view (m: Model) : LayoutNode<Msg> =
    let visible = visibleRowCount m.Size

    // formula / edit bar — shows the active cell's raw text, with a ▏ caret while editing
    let cursorInput = Map.tryFind m.Cursor m.Inputs |> Option.defaultValue ""

    let barText, barStyle =
        match m.Editing with
        | Some buffer ->
            (buffer.Text.Substring(0, buffer.Cursor)
             + "▏"
             + buffer.Text.Substring(buffer.Cursor)),
            Style.Default.WithForeground(Color.Rgb(0x7Fuy, 0xE0uy, 0x9Cuy))
        | None -> cursorInput, Styles.text

    let bar =
        Box.box
            Styles.border
            [ Text.text (sprintf " %s   │ %s" (Sheet.cellName (fst m.Cursor) (snd m.Cursor)) barText) barStyle ]

    let cols = visibleColCount m.Size
    let firstCol = m.LeftCol
    let lastCol = min (Sheet.columnCount - 1) (m.LeftCol + cols - 1)
    let visibleCols = lastCol - firstCol + 1

    // grid: a column-label header row + the visible window of data rows, with a
    // box-drawing gridline beneath the header and each data row. Rows scroll
    // vertically through TopRow; columns scroll horizontally through LeftCol.
    let header =
        Stack.hstackOf
            [ yield Stack.sized (Length.Cells rowLabelWidth) (Text.text (String(' ', rowLabelWidth)) Styles.header)
              for col in firstCol..lastCol do
                  yield gridline

                  yield
                      Stack.sized
                          (Length.Cells columnWidth)
                          (Text.text (fitCenter columnWidth (Sheet.columnLabel col)) Styles.header) ]

    let rule =
        Stack.sized (Length.Cells 1) (Text.text (separatorLine visibleCols) Styles.border)

    let lastRow = min (Sheet.rowCount - 1) (m.TopRow + visible - 1)

    // cells the cursor's current formula references (live buffer text while editing)
    let refs =
        let cursorText =
            match m.Editing with
            | Some buffer -> buffer.Text
            | None -> cursorInput

        Sheet.referencesOf cursorText |> Set.ofList

    let gridChildren =
        [ yield Stack.sized (Length.Cells 1) header
          yield rule
          for row in m.TopRow .. lastRow do
              yield Stack.sized (Length.Cells 1) (renderRow m refs firstCol lastCol row)
              yield rule ]

    let grid = Box.box Styles.border [ Stack.vstackOf gridChildren ]

    let footer =
        Text.text
            " arrows move · type or Enter to edit · in a =formula Shift+arrows pick a ref, Enter inserts, Esc cancels · Ctrl+C quit"
            Styles.hint

    Dock.dock [ Dock.top 3 bar; Dock.bottom 1 footer; Dock.fill grid ]

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        let shift = ke.Modifiers.Shift

        match ke.Key with
        | ArrowUp -> Some(if shift then MoveTarget(-1, 0) else Move(-1, 0))
        | ArrowDown -> Some(if shift then MoveTarget(1, 0) else Move(1, 0))
        | ArrowLeft -> Some(if shift then MoveTarget(0, -1) else Move(0, -1))
        | ArrowRight -> Some(if shift then MoveTarget(0, 1) else Move(0, 1))
        | Tab -> Some(Move(0, 1))
        | Enter -> Some Commit
        | Escape -> Some Cancel
        | Backspace -> Some Back
        | Delete -> Some Clear
        | Home -> Some CursorHome
        | End -> Some CursorEnd
        | Space -> Some(Typed " ") // spacebar types a space into the cell
        | Char c when not ke.Modifiers.Ctrl -> Some(Typed c)
        | _ -> Some Ignore
    | _ -> Some Ignore

let subscriptions (_: Model) : Sub<Msg> list = [ TerminalResize Resized ]

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
    let size = Size.Create(86, 16)

    let model =
        keepCursorInView (
            recompute
                { (fst (init ())) with
                    Cursor = Sheet.parseCellRef "D6" |> Option.get
                    Size = size }
        )

    printfn "Mire.Demo.Spreadsheet — cursor on D6 = SUM(D2:D4)\n"
    let surface = Surface(size)
    Layout.measure (Rect.FromOrigin size) (view model) |> Layout.render surface
    printSurface surface

    printfn
        "\n  D2=%s  D3=%s  D4=%s  D6=%s"
        (Sheet.formatValue model.Values.[(1, 3)])
        (Sheet.formatValue model.Values.[(2, 3)])
        (Sheet.formatValue model.Values.[(3, 3)])
        (Sheet.formatValue model.Values.[(5, 3)])

    // Narrow terminal + a far-right cursor: the grid scrolls horizontally so the
    // active column stays visible instead of falling off the rendered range — the
    // header row shows the scrolled-to columns (F/G/H), not A/B/C.
    let narrow = Size.Create(40, 12)

    let scrolled =
        keepCursorInView
            { (fst (init ())) with
                Cursor = Sheet.parseCellRef "H1" |> Option.get
                Size = narrow }

    printfn "\nMire.Demo.Spreadsheet — cursor on H1 in a %d-col terminal (LeftCol=%d)\n" narrow.Width scrolled.LeftCol
    let scrolledSurface = Surface(narrow)

    Layout.measure (Rect.FromOrigin narrow) (view scrolled)
    |> Layout.render scrolledSurface

    printSurface scrolledSurface

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
