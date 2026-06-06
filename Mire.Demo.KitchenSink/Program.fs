open System
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets

// ---------------------------------------------------------------------------
// Mire — KitchenSink. A comprehensive, cyclable showcase of every widget in the
// framework, each shown in several configurations/states. A sidebar lists the
// categories; the detail pane renders the selected one. Spinners/progress
// animate via a Sub.Every tick; the data/editing entries (ListView, Table,
// ScrollView, Input, TextArea, Tabs, Toggle, ProgressBar) are driveable when
// focused (Tab into the pane).
//
// `Detail.render` is the single path shared by the live view and `--dump`, so
// every showcase is verifiable headlessly (and is the golden-frame source).
// The app's own sidebar|detail layout dogfoods `SplitView`.
// ---------------------------------------------------------------------------

type Demo =
    | DText
    | DStyles
    | DBox
    | DChrome
    | DSpinner
    | DProgress
    | DTabs
    | DToggle
    | DList
    | DTable
    | DScroll
    | DInput
    | DTextArea
    | DModal
    | DToast
    | DCompletion
    | DTooltip
    | DPositioned
    | DSplit
    | DMarkdown

type Entry =
    { Demo: Demo
      Title: string
      Notes: string }

module Catalog =
    let entries: Entry list =
        [ { Demo = DText
            Title = "Text"
            Notes = "Styled, multi-line, grapheme-width aware." }
          { Demo = DStyles
            Title = "Styles"
            Notes = "The predefined Mire.Widgets.Style palette." }
          { Demo = DBox
            Title = "Box / Panel"
            Notes = "Bordered container, titled panel, status bar." }
          { Demo = DChrome
            Title = "Separator·Badge·KeyHint"
            Notes = "Rules, toned pills, key+label chips." }
          { Demo = DSpinner
            Title = "Spinner"
            Notes = "Braille frames driven by a Sub.Every tick." }
          { Demo = DProgress
            Title = "ProgressBar / Gauge"
            Notes = "Determinate bar + centered % gauge. ←/→ adjust." }
          { Demo = DTabs
            Title = "Tabs"
            Notes = "Tab strip with an active indicator. ←/→ select." }
          { Demo = DToggle
            Title = "Toggle"
            Notes = "checkbox / radio / switch. Space · ↑/↓ · s." }
          { Demo = DList
            Title = "ListView"
            Notes = "Virtualized single-select, full-width highlight. ↑/↓." }
          { Demo = DTable
            Title = "Table"
            Notes = "Sticky header, Length columns, row select. ↑/↓." }
          { Demo = DScroll
            Title = "ScrollView"
            Notes = "Viewport + track/thumb scrollbar. ↑/↓ PgUp/PgDn." }
          { Demo = DInput
            Title = "Input"
            Notes = "Single-line TextBuffer + block cursor. Type." }
          { Demo = DTextArea
            Title = "TextArea"
            Notes = "Multi-line editor; word ops + paste. Type." }
          { Demo = DModal
            Title = "Modal"
            Notes = "Centered box over an opaque backdrop." }
          { Demo = DToast
            Title = "Toast"
            Notes = "A placed stack of toned notification cards." }
          { Demo = DCompletion
            Title = "Completion"
            Notes = "Cursor-anchored selectable popup (flips above)." }
          { Demo = DTooltip
            Title = "Tooltip"
            Notes = "Anchored bordered doc popup, clamped on-screen." }
          { Demo = DPositioned
            Title = "Positioned"
            Notes = "9-point placement within the area." }
          { Demo = DSplit
            Title = "SplitView"
            Notes = "Two panes + a divider; nested here." }
          { Demo = DMarkdown
            Title = "Markdown"
            Notes = "Headings, emphasis, lists, fenced code, quotes." } ]

    let interactive (demo: Demo) : bool =
        match demo with
        | DProgress
        | DTabs
        | DToggle
        | DList
        | DTable
        | DScroll
        | DInput
        | DTextArea -> true
        | _ -> false

// ── sample data ────────────────────────────────────────────────────────────
let private fruits =
    [ "apple"
      "banana"
      "cherry"
      "date"
      "elderberry"
      "fig"
      "grape"
      "honeydew"
      "kiwi"
      "lemon"
      "mango"
      "nectarine" ]

type SkuRow =
    { Name: string
      Qty: int
      Price: string }

let private tableRows =
    [ { Name = "Widgets"
        Qty = 12
        Price = "$3.40" }
      { Name = "Gadgets"
        Qty = 4
        Price = "$9.95" }
      { Name = "Gizmos"
        Qty = 27
        Price = "$1.20" }
      { Name = "Sprockets"
        Qty = 9
        Price = "$5.55" }
      { Name = "Cogs"
        Qty = 150
        Price = "$0.30" }
      { Name = "Flanges"
        Qty = 3
        Price = "$12.00" }
      { Name = "Grommets"
        Qty = 88
        Price = "$0.75" } ]

let private mdDoc =
    "# Markdown\n\nBody with **bold**, *italic* and `inline code`.\n\n- bullet one\n- bullet two\n\n```\nlet answer = 42  // a comment\n```\n\n> a block quote\n"

// ── model ──────────────────────────────────────────────────────────────────
type FocusArea =
    | Sidebar
    | Detail

type Model =
    { Size: Size
      Selected: int
      Focus: FocusArea
      Tick: int
      ScrollOffset: int
      ListSel: int
      TableSel: int
      TableTop: int
      TabSel: int
      Checked: bool
      RadioSel: int
      SwitchOn: bool
      Frac: float
      Input: TextBuffer
      Area: TextBuffer }

type Msg =
    | KeyMsg of KeyEvent
    | PasteMsg of string
    | TickMsg
    | ResizedMsg of Size
    | IgnoreMsg

let private clamp lo hi v = max lo (min hi v)
let private current (m: Model) = Catalog.entries.[m.Selected].Demo

let init () =
    { Size = TerminalMode.getTerminalSize () |> Option.defaultValue (Size.Create(96, 32))
      Selected = 0
      Focus = Sidebar
      Tick = 0
      ScrollOffset = 0
      ListSel = 0
      TableSel = 0
      TableTop = 0
      TabSel = 0
      Checked = true
      RadioSel = 0
      SwitchOn = true
      Frac = 0.45
      Input = TextBuffer.Of "edit me — ←/→ Home/End"
      Area = TextBuffer.Of "multi-line\nTextArea — type,\nCtrl+Backspace word-delete" },
    Cmd.none

// ── detail routing (Detail focus) ──────────────────────────────────────────
let private routeDetail (demo: Demo) (ke: KeyEvent) (m: Model) : Model =
    let editArea f = { m with Area = f m.Area }

    match demo with
    | DProgress ->
        match ke.Key with
        | ArrowLeft -> { m with Frac = max 0.0 (m.Frac - 0.1) }
        | ArrowRight -> { m with Frac = min 1.0 (m.Frac + 0.1) }
        | _ -> m
    | DTabs ->
        match ke.Key with
        | ArrowLeft ->
            { m with
                TabSel = clamp 0 2 (m.TabSel - 1) }
        | ArrowRight ->
            { m with
                TabSel = clamp 0 2 (m.TabSel + 1) }
        | _ -> m
    | DToggle ->
        match ke.Key with
        | Space -> { m with Checked = not m.Checked }
        | ArrowUp ->
            { m with
                RadioSel = clamp 0 2 (m.RadioSel - 1) }
        | ArrowDown ->
            { m with
                RadioSel = clamp 0 2 (m.RadioSel + 1) }
        | Char "s" -> { m with SwitchOn = not m.SwitchOn }
        | _ -> m
    | DList ->
        match ke.Key with
        | ArrowUp ->
            { m with
                ListSel = clamp 0 (fruits.Length - 1) (m.ListSel - 1) }
        | ArrowDown ->
            { m with
                ListSel = clamp 0 (fruits.Length - 1) (m.ListSel + 1) }
        | _ -> m
    | DTable ->
        let maxSel = tableRows.Length - 1

        match ke.Key with
        | ArrowUp ->
            { m with
                TableSel = clamp 0 maxSel (m.TableSel - 1) }
        | ArrowDown ->
            { m with
                TableSel = clamp 0 maxSel (m.TableSel + 1) }
        | _ -> m
    | DScroll ->
        match ke.Key with
        | ArrowUp ->
            { m with
                ScrollOffset = max 0 (m.ScrollOffset - 1) }
        | ArrowDown ->
            { m with
                ScrollOffset = min 29 (m.ScrollOffset + 1) }
        | PageUp ->
            { m with
                ScrollOffset = max 0 (m.ScrollOffset - 5) }
        | PageDown ->
            { m with
                ScrollOffset = min 29 (m.ScrollOffset + 5) }
        | _ -> m
    | DInput ->
        { m with
            Input = TextEdit.applyInput (Key ke) m.Input }
    | DTextArea -> editArea (TextEdit.applyInput (Key ke))
    | _ -> m

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | IgnoreMsg -> m, Cmd.none
    | TickMsg -> { m with Tick = m.Tick + 1 }, Cmd.none
    | ResizedMsg sz -> { m with Size = sz }, Cmd.none
    | PasteMsg s ->
        match m.Focus, current m with
        | Detail, DInput ->
            { m with
                Input = TextEdit.applyInput (Paste s) m.Input },
            Cmd.none
        | Detail, DTextArea ->
            { m with
                Area = TextEdit.applyInput (Paste s) m.Area },
            Cmd.none
        | _ -> m, Cmd.none
    | KeyMsg ke ->
        let n = Catalog.entries.Length

        match m.Focus with
        | Sidebar ->
            match ke.Key with
            | ArrowUp ->
                { m with
                    Selected = clamp 0 (n - 1) (m.Selected - 1) },
                Cmd.none
            | ArrowDown ->
                { m with
                    Selected = clamp 0 (n - 1) (m.Selected + 1) },
                Cmd.none
            | Enter
            | Tab ->
                (if Catalog.interactive (current m) then
                     { m with Focus = Detail }
                 else
                     m),
                Cmd.none
            | _ -> m, Cmd.none
        | Detail ->
            match ke.Key with
            | Escape
            | Tab -> { m with Focus = Sidebar }, Cmd.none
            | _ -> routeDetail (current m) ke m, Cmd.none

// ── detail rendering ───────────────────────────────────────────────────────
module Detail =
    let private rect0 = Rect.Create(0, 0, 0, 0)

    let private bg r g b =
        Style.Default.WithBackground(Color.Rgb(r, g, b))

    let private pill r g b = (bg r g b).WithForeground(Color.White)
    let private divider = bg 0x44uy 0x44uy 0x44uy
    let private gaugeLabel = (bg 0x10uy 0x10uy 0x10uy).WithForeground(Color.White)

    let render (m: Model) (demo: Demo) : LayoutNode<Msg> =
        match demo with
        | DText ->
            Stack.vstack
                [ Text.text "plain text" Style.text
                  Text.title "a bold title"
                  Text.dimText "dimmed secondary text"
                  Text.text "wide glyphs: 日本語 emoji 🚀 ok" Style.text
                  Text.text "two\nlines via \\n" Style.text ]
        | DStyles ->
            Stack.vstack
                [ Text.text "text" Style.text
                  Text.text "title" Style.title
                  Text.text "dim" Style.dim
                  Text.text "success" Style.success
                  Text.text "warning" Style.warning
                  Text.text "danger" Style.danger
                  Text.text "info" Style.info
                  Text.text "key" Style.key
                  Text.text "highlight" Style.highlight ]
        | DBox ->
            Stack.vstack
                [ Box.box Style.border [ Text.text "Box.box — bordered" Style.text ]
                  Box.panel "Panel" Style.border [ Text.text "title + body" Style.text ]
                  StatusBar.statusBar
                      [ Text.text " left " Style.title ]
                      [ Text.text " center " Style.text ]
                      [ Text.text " right " Style.highlight ] ]
        | DChrome ->
            Stack.vstack
                [ Text.dimText "Separator.horizontal:"
                  Separator.horizontal 30 Style.dim
                  Text.dimText "Badge (tones):"
                  Stack.hstack
                      [ Badge.badge (pill 0x4Cuy 0xAFuy 0x50uy) "ok"
                        Text.text " " Style.dim
                        Badge.badge (pill 0xFFuy 0xA0uy 0x00uy) "warn"
                        Text.text " " Style.dim
                        Badge.badge (pill 0xFFuy 0x57uy 0x22uy) "err" ]
                  Text.dimText "KeyHint:"
                  KeyHint.hint Style.key Style.dim "Ctrl+P" "palette" ]
        | DSpinner ->
            Stack.vstack
                [ Spinner.labeled Style.info Style.text m.Tick "loading…"
                  Stack.hstack
                      [ for k in 0..4 -> Text.text (Spinner.frameOf Spinner.braille (m.Tick + k * 2)) Style.success ]
                  Text.dimText "animates via a Sub.Every tick" ]
        | DProgress ->
            Stack.vstack
                [ Text.dimText "ProgressBar.view (←/→ adjust):"
                  ProgressBar.view 30 Style.success Style.dim m.Frac
                  Text.dimText "ProgressBar.gauge:"
                  ProgressBar.gauge 30 Style.success Style.dim gaugeLabel m.Frac
                  Text.dimText "animated:"
                  ProgressBar.view 30 Style.info Style.dim (float (m.Tick % 31) / 30.0) ]
        | DTabs ->
            let labels = [ "Overview"; "Detail"; "Raw" ]

            Stack.vstack
                [ Tabs.strip Style.highlight Style.dim m.TabSel labels
                  Separator.horizontal 30 Style.dim
                  Text.text (sprintf "  body of: %s" (List.item (clamp 0 2 m.TabSel) labels)) Style.text ]
        | DToggle ->
            Stack.vstack
                [ Toggle.checkbox Style.text m.Checked "enable feature"
                  Toggle.radio Style.text (m.RadioSel = 0) "option A"
                  Toggle.radio Style.text (m.RadioSel = 1) "option B"
                  Toggle.radio Style.text (m.RadioSel = 2) "option C"
                  Stack.hstack
                      [ Text.dimText "switch: "
                        Toggle.switch (pill 0x4Cuy 0xAFuy 0x50uy) (pill 0x60uy 0x60uy 0x60uy) m.SwitchOn ] ]
        | DList -> ListView.view 8 Style.highlight Style.text m.ListSel fruits
        | DTable ->
            let cols =
                [ Table.textColumn "Item" (Length.Cells 14) Style.text (fun r -> r.Name)
                  Table.textColumn "Qty" (Length.Cells 6) Style.info (fun r -> string r.Qty)
                  Table.textColumn "Price" (Length.Cells 8) Style.success (fun r -> r.Price) ]

            Table.view 8 Style.title Style.highlight m.TableTop (fun i -> i = m.TableSel) cols tableRows
        | DScroll ->
            let rows =
                Stack.vstack [ for i in 1..30 -> Text.text (sprintf "  scrollable row %d" i) Style.text ]

            ScrollView.vertical 8 30 m.ScrollOffset Style.dim Style.highlight rows
        | DInput ->
            let focused = m.Focus = Detail
            Box.box Style.border [ Input.render 32 Style.text Style.highlight focused m.Input ]
        | DTextArea ->
            let focused = m.Focus = Detail
            Box.box Style.border [ TextArea.render 34 5 Style.text Style.highlight focused m.Area ]
        | DModal ->
            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for i in 1..6 -> Text.text (sprintf " base content row %d" i) Style.dim ]
                  Modal.modal
                      Style.bg
                      Style.border
                      Style.title
                      30
                      6
                      "Confirm"
                      (Stack.vstack
                          [ Text.text "Proceed with the action?" Style.text
                            Text.dimText "Enter ok · Esc cancel" ]) ]
            )
        | DToast ->
            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for i in 1..6 -> Text.text (sprintf " app row %d" i) Style.dim ]
                  Toast.stack
                      TopRight
                      26
                      3
                      [ Toast.card Style.border Style.success Style.text "✓ Saved" "changes written"
                        Toast.card Style.border Style.warning Style.text "! Heads up" "check the logs" ] ]
            )
        | DCompletion ->
            let aw, ah = 40, 8

            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for _ in 1..ah -> Text.text (String.replicate aw "·") Style.dim ]
                  Completion.view
                      aw
                      ah
                      6
                      1
                      22
                      5
                      Style.border
                      Style.highlight
                      Style.text
                      1
                      [ "openFile"; "openFolder"; "openRecent"; "openSettings" ] ]
            )
        | DTooltip ->
            let aw, ah = 40, 8

            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for _ in 1..ah -> Text.text (String.replicate aw "·") Style.dim ]
                  Tooltip.view aw ah 8 2 22 Style.border Style.text [ "An anchored tooltip."; "Clamped on-screen." ] ]
            )
        | DPositioned ->
            let mk p label =
                Overlay.positioned
                    p
                    (Length.Cells(String.length label + 2))
                    (Length.Cells 1)
                    (Badge.badge (pill 0x4Auy 0x90uy 0xD9uy) label)

            LayoutNode.Overlay(
                rect0,
                [ Backdrop.solid Style.bg
                  mk TopLeft "TL"
                  mk TopCenter "TC"
                  mk TopRight "TR"
                  mk CenterLeft "CL"
                  mk Center "MID"
                  mk CenterRight "CR"
                  mk BottomLeft "BL"
                  mk BottomCenter "BC"
                  mk BottomRight "BR" ]
            )
        | DSplit ->
            SplitView.horizontal
                (Length.Fraction 0.4)
                divider
                (Box.box Style.border [ Text.text " left 40% " Style.info ])
                (SplitView.vertical
                    (Length.Cells 1)
                    divider
                    (Text.text " top (Cells 1) " Style.success)
                    (Box.box Style.border [ Text.text " bottom fill " Style.text ]))
        | DMarkdown -> Markdown.render Markdown.defaultStyle 42 mdDoc

// ── chrome ─────────────────────────────────────────────────────────────────
let private sidebarNode (m: Model) : LayoutNode<Msg> =
    let labels = Catalog.entries |> List.map (fun e -> e.Title)

    let border = if m.Focus = Sidebar then Style.highlight else Style.border

    Box.box
        border
        [ Stack.vstackOf
              [ Stack.sized (Length.Cells 1) (Text.title " Widgets ")
                Stack.sized
                    Length.Fill
                    (ListView.view (List.length labels) Style.highlight Style.text m.Selected labels) ] ]

let private detailNode (m: Model) : LayoutNode<Msg> =
    let entry = Catalog.entries.[m.Selected]

    let border = if m.Focus = Detail then Style.highlight else Style.border

    Box.box
        border
        [ Stack.vstackOf
              [ Stack.sized (Length.Cells 1) (Text.title (sprintf " %s " entry.Title))
                Stack.sized Length.Fill (Detail.render m entry.Demo)
                Stack.sized (Length.Cells 1) (Text.dimText (sprintf " %s" entry.Notes)) ] ]

let private hints (m: Model) : string =
    match m.Focus with
    | Sidebar -> " ↑/↓ select   Tab/Enter → focus pane   Ctrl+C quit "
    | Detail ->
        match current m with
        | DProgress -> " ←/→ adjust   Tab/Esc back "
        | DTabs -> " ←/→ select tab   Tab/Esc back "
        | DToggle -> " Space check · ↑/↓ radio · s switch   Tab/Esc back "
        | DList
        | DTable -> " ↑/↓ select   Tab/Esc back "
        | DScroll -> " ↑/↓ scroll · PgUp/PgDn ±5   Tab/Esc back "
        | DInput
        | DTextArea -> " type to edit · ←/→ · Ctrl+Backspace   Tab/Esc back "
        | _ -> " Tab/Esc back "

let view (m: Model) : LayoutNode<Msg> =
    let middle =
        SplitView.horizontal
            (Length.Cells 24)
            (Style.Default.WithBackground(Color.Rgb(0x33uy, 0x33uy, 0x33uy)))
            (sidebarNode m)
            (detailNode m)

    Dock.dock
        [ Dock.top 3 (Box.box Style.border [ Text.title " Mire — KitchenSink " ])
          Dock.bottom 3 (Box.box Style.border [ Text.text (hints m) Style.highlight ])
          Dock.fill middle ]

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke -> Some(KeyMsg ke)
    | Paste s -> Some(PasteMsg s)
    | Resize sz -> Some(ResizedMsg sz)
    | _ -> Some IgnoreMsg

let subscriptions (_: Model) : Sub<Msg> list =
    [ Sub.Every(TimeSpan.FromMilliseconds 120.0, (fun () -> TickMsg)) ]

// ── headless --dump ────────────────────────────────────────────────────────
let private printSurface (label: string) (surface: Surface) =
    let w = surface.Size.Width
    let bar = String.replicate w "─"
    printfn ""
    printfn "  %s  (%d×%d)" label w surface.Size.Height
    printfn "  ┌%s┐" bar

    for y in 0 .. surface.Size.Height - 1 do
        let sb = Text.StringBuilder()

        for x in 0 .. w - 1 do
            let g = surface.[x, y].Grapheme
            sb.Append(if String.IsNullOrEmpty g then " " else g) |> ignore

        printfn "  │%s│" (sb.ToString())

    printfn "  └%s┘" bar

let runDump () =
    let m =
        { (init () |> fst) with
            Focus = Detail
            Tick = 3
            ScrollOffset = 6
            ListSel = 3
            TableSel = 2
            TableTop = 0
            Size = Size.Create(48, 12) }

    let size = Size.Create(48, 12)

    for entry in Catalog.entries do
        let node = Detail.render m entry.Demo
        let surface = Surface(size)
        Layout.measure (Rect.FromOrigin size) node |> Layout.render surface
        printSurface entry.Title surface

    printfn ""
    printfn "  (dump complete — %d entries)" Catalog.entries.Length

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
