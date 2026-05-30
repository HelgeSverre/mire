open System
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets

// ---------------------------------------------------------------------------
// Mire Widget Gallery — a sidebar + detail-pane app showcasing every layout
// node and widget builder that actually exists in the framework. The sidebar
// lists entries; the detail pane renders the selected one live. "Live where it
// matters": the Scroll, ListView and Input entries are driveable when focused.
//
// `Detail.render` is the single render path shared by the live `view` and the
// headless `--dump` mode, so every showcase stays verifiable without a tty.
// ---------------------------------------------------------------------------

type Demo =
    | TextDemo
    | FilledDemo
    | BoxDemo
    | StatusBarDemo
    | StackDemo
    | DockDemo
    | ScrollDemo
    | OverlayDemo
    | ListViewDemo
    | InputDemo
    | StylesDemo

type Entry =
    { Demo: Demo
      Title: string
      Status: string
      Notes: string }

/// The catalog — single source of truth. Drives both the sidebar list and the
/// `--dump` walk, so the two can never drift.
module Catalog =
    let entries: Entry list =
        [ { Demo = TextDemo
            Title = "Text"
            Status = "✅"
            Notes = "Styled, multi-line (\\n), grapheme-width aware, clipped." }
          { Demo = FilledDemo
            Title = "Filled"
            Status = "✅"
            Notes = "Opaque style-filled rectangles — backdrops / swatches." }
          { Demo = BoxDemo
            Title = "Box"
            Status = "🟡"
            Notes = "Border + children; children share the inner rect (nest a Stack)." }
          { Demo = StatusBarDemo
            Title = "StatusBar"
            Status = "✅"
            Notes = "Left / center / right item groups in a bordered bar." }
          { Demo = StackDemo
            Title = "Stack"
            Status = "✅"
            Notes = "Flow layout; per-child Cells / Fraction / Content / Fill." }
          { Demo = DockDemo
            Title = "Dock"
            Status = "✅"
            Notes = "Edge-anchored regions: Top / Bottom / Left / Right / Fill." }
          { Demo = ScrollDemo
            Title = "Scroll"
            Status = "🟡"
            Notes = "Offset + viewport clipping. ↑/↓ PgUp/PgDn when focused." }
          { Demo = OverlayDemo
            Title = "Overlay"
            Status = "🟡"
            Notes = "Z-order; Filled occludes. Layers take the full area (no anchor)." }
          { Demo = ListViewDemo
            Title = "ListView"
            Status = "🟡"
            Notes = "Single-selection + full-width highlight + auto-scroll. ↑/↓ focused." }
          { Demo = InputDemo
            Title = "Input"
            Status = "🟡"
            Notes = "TextBuffer + block cursor. Type when focused; ←/→ Home/End." }
          { Demo = StylesDemo
            Title = "Styles"
            Status = "✅"
            Notes = "The predefined Mire.Widgets.Style palette." } ]

    let interactive (demo: Demo) : bool =
        match demo with
        | ScrollDemo
        | ListViewDemo
        | InputDemo -> true
        | _ -> false

// Sample data reused by the live view and the dump.
let private scrollRows =
    [ for i in 1..30 -> Text.text (sprintf "  %2d  scrollable content row %d" i i) Style.text ]

let private listLabels =
    [ "apple"
      "banana"
      "cherry"
      "date"
      "elderberry"
      "fig"
      "grape"
      "honeydew"
      "kiwi"
      "lemon" ]

// ---------------------------------------------------------------------------

type Focus =
    | Sidebar
    | Detail

type Model =
    { Selected: int
      Focus: Focus
      ScrollOffset: int
      ListSel: int
      Input: TextBuffer }

type Msg =
    | KeyPressed of KeyEvent
    | Ignore

let init () =
    { Selected = 0
      Focus = Sidebar
      ScrollOffset = 0
      ListSel = 0
      Input = TextBuffer.Of "edit me" },
    Cmd.none

let private clamp lo hi v = max lo (min hi v)

let private current (model: Model) = Catalog.entries.[model.Selected].Demo

let private routeDetail (demo: Demo) (ke: KeyEvent) (model: Model) : Model =
    match demo with
    | ScrollDemo ->
        let maxOff = scrollRows.Length - 1

        match ke.Key with
        | ArrowUp ->
            { model with
                ScrollOffset = clamp 0 maxOff (model.ScrollOffset - 1) }
        | ArrowDown ->
            { model with
                ScrollOffset = clamp 0 maxOff (model.ScrollOffset + 1) }
        | PageUp ->
            { model with
                ScrollOffset = clamp 0 maxOff (model.ScrollOffset - 5) }
        | PageDown ->
            { model with
                ScrollOffset = clamp 0 maxOff (model.ScrollOffset + 5) }
        | _ -> model
    | ListViewDemo ->
        let maxSel = listLabels.Length - 1

        match ke.Key with
        | ArrowUp ->
            { model with
                ListSel = clamp 0 maxSel (model.ListSel - 1) }
        | ArrowDown ->
            { model with
                ListSel = clamp 0 maxSel (model.ListSel + 1) }
        | _ -> model
    | InputDemo ->
        let edit f = { model with Input = f model.Input }

        match ke.Key with
        | Char c -> edit (TextBuffer.insert c)
        | Space -> edit (TextBuffer.insert " ")
        | Backspace -> edit TextBuffer.backspace
        | Delete -> edit TextBuffer.delete
        | ArrowLeft -> edit TextBuffer.left
        | ArrowRight -> edit TextBuffer.right
        | Home -> edit TextBuffer.home
        | End -> edit TextBuffer.toEnd
        | _ -> model
    | _ -> model

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Ignore -> model, Cmd.none
    | KeyPressed ke ->
        let n = Catalog.entries.Length

        match model.Focus with
        | Sidebar ->
            match ke.Key with
            | ArrowUp ->
                { model with
                    Selected = clamp 0 (n - 1) (model.Selected - 1) },
                Cmd.none
            | ArrowDown ->
                { model with
                    Selected = clamp 0 (n - 1) (model.Selected + 1) },
                Cmd.none
            | Tab ->
                (if Catalog.interactive (current model) then
                     { model with Focus = Detail }
                 else
                     model),
                Cmd.none
            | _ -> model, Cmd.none
        | Detail ->
            match ke.Key with
            | Tab
            | Escape -> { model with Focus = Sidebar }, Cmd.none
            | _ -> routeDetail (current model) ke model, Cmd.none

// ---------------------------------------------------------------------------
// Detail rendering — one arm per Demo, reading interactive state from Model.
// Shared by the live view and `--dump`.
// ---------------------------------------------------------------------------

module Detail =
    let private bg (r: byte) (g: byte) (b: byte) =
        Style.Default.WithBackground(Color.Rgb(r, g, b))

    /// A full-width colored swatch row carrying a label.
    let private swatch (style: Style) (label: string) =
        Backdrop.behind style (Text.text label style)

    let render (model: Model) (demo: Demo) : LayoutNode<Msg> =
        match demo with
        | TextDemo ->
            Stack.vstack
                [ Text.text "plain text" Style.text
                  Text.title "a bold title"
                  Text.dimText "dimmed secondary text"
                  Text.text "wide glyphs: 日本語 emoji 🚀 ok" Style.text
                  Text.text "two\nlines via \\n" Style.text ]
        | FilledDemo ->
            Stack.vstackOf
                [ Stack.sized (Length.Cells 1) (swatch (bg 0x4Auy 0x90uy 0xD9uy) " info blue ")
                  Stack.sized (Length.Cells 1) (swatch (bg 0x4Cuy 0xAFuy 0x50uy) " success green ")
                  Stack.sized (Length.Cells 1) (swatch (bg 0xFFuy 0xA0uy 0x00uy) " warning amber ")
                  Stack.sized (Length.Cells 1) (swatch (bg 0xFFuy 0x57uy 0x22uy) " danger orange ") ]
        | BoxDemo ->
            Box.box
                Style.border
                [ Stack.vstack
                      [ Text.title " Panel "
                        Text.text "bordered container" Style.text
                        Text.dimText "title + body via a nested Stack" ] ]
        | StatusBarDemo ->
            // NOTE: StatusBar.statusBar flattens its groups into one Box, whose
            // children share the inner rect and overlap — so only the last group
            // survives. Until that builder flows via a Stack, compose the bar
            // here with an explicit hstack to show the left/center/right intent.
            Box.box
                Style.border
                [ Stack.hstackOf
                      [ Stack.sized Length.Content (Text.text " gallery " Style.title)
                        Stack.sized Length.Fill (Text.text "" Style.text)
                        Stack.sized Length.Content (Text.text " center " Style.text)
                        Stack.sized Length.Fill (Text.text "" Style.text)
                        Stack.sized Length.Content (Text.text " right " Style.highlight) ] ]
        | StackDemo ->
            Stack.vstack
                [ Text.dimText "hstack (Content):"
                  Stack.hstack
                      [ Text.text "[ a ]" Style.info
                        Text.text "[ b ]" Style.success
                        Text.text "[ c ]" Style.warning ]
                  Text.dimText "vstackOf Cells/Fill/Content:"
                  Stack.vstackOf
                      [ Stack.sized (Length.Cells 1) (Text.text "Cells 1 — fixed" Style.text)
                        Stack.sized Length.Fill (Box.box Style.border [ Text.text "Fill" Style.text ])
                        Stack.sized Length.Content (Text.text "Content" Style.dim) ] ]
        | DockDemo ->
            Dock.dock
                [ Dock.top 1 (Text.text "Top (1)" Style.title)
                  Dock.bottom 1 (Text.text "Bottom (1)" Style.highlight)
                  Dock.left 8 (Box.box Style.border [ Text.text "Left" Style.info ])
                  Dock.fill (Box.box Style.border [ Text.text "Fill" Style.text ]) ]
        | ScrollDemo -> Scroll.vertical model.ScrollOffset (Stack.vstack scrollRows)
        | OverlayDemo ->
            let bgRows =
                Stack.vstack [ for i in 1..6 -> Text.text (sprintf "background row %d" i) Style.dim ]

            LayoutNode.Overlay(
                Rect.Create(0, 0, 0, 0),
                [ bgRows
                  Backdrop.solid Style.bg
                  Box.box
                      Style.border
                      [ Stack.vstack [ Text.title " MODAL "; Text.text "occludes the rows behind" Style.text ] ] ]
            )
        | ListViewDemo -> ListView.view (List.length listLabels) Style.highlight Style.text model.ListSel listLabels
        | InputDemo ->
            let focused = model.Focus = Detail

            Stack.vstack
                [ Text.dimText "single-line editor over TextBuffer:"
                  Box.box Style.border [ Input.render 30 Style.text Style.highlight focused model.Input ]
                  Text.dimText (
                      if focused then
                          "typing is live"
                      else
                          "Tab into this pane to type"
                  ) ]
        | StylesDemo ->
            Stack.vstack
                [ Text.text "text" Style.text
                  Text.text "title" Style.title
                  Text.text "dim" Style.dim
                  Text.text "success" Style.success
                  Text.text "warning" Style.warning
                  Text.text "danger" Style.danger
                  Text.text "info" Style.info
                  Text.text "key" Style.key
                  Text.text "counter" Style.counter
                  Text.text "highlight" Style.highlight ]

// ---------------------------------------------------------------------------

let private sidebar (model: Model) : LayoutNode<Msg> =
    let labels = Catalog.entries |> List.map (fun e -> sprintf "%s %s" e.Status e.Title)

    let border =
        if model.Focus = Sidebar then
            Style.highlight
        else
            Style.border

    Box.box
        border
        [ Stack.vstackOf
              [ Stack.sized (Length.Cells 1) (Text.title " Widgets ")
                Stack.sized
                    Length.Fill
                    (ListView.view (List.length labels) Style.highlight Style.text model.Selected labels) ] ]

let private detailPane (model: Model) : LayoutNode<Msg> =
    let entry = Catalog.entries.[model.Selected]

    let border =
        if model.Focus = Detail then
            Style.highlight
        else
            Style.border

    Box.box
        border
        [ Stack.vstackOf
              [ Stack.sized (Length.Cells 1) (Text.title (sprintf " %s " entry.Title))
                Stack.sized Length.Fill (Detail.render model entry.Demo)
                Stack.sized (Length.Cells 1) (Text.dimText (sprintf " %s · %s" entry.Status entry.Notes)) ] ]

let private hints (model: Model) : string =
    match model.Focus with
    | Sidebar -> " ↑/↓ select   Tab → focus pane (interactive entries)   Ctrl+C quit "
    | Detail ->
        match current model with
        | ScrollDemo -> " ↑/↓ scroll   PgUp/PgDn ±5   Tab/Esc back "
        | ListViewDemo -> " ↑/↓ select row   Tab/Esc back "
        | InputDemo -> " type to edit   ←/→ cursor   Home/End   Tab/Esc back "
        | _ -> " Tab/Esc back to sidebar "

let view (model: Model) : LayoutNode<Msg> =
    Dock.dock
        [ Dock.top 3 (Box.box Style.border [ Text.title " Mire — Widget Gallery " ])
          Dock.bottom 3 (Box.box Style.border [ Text.text (hints model) Style.highlight ])
          Dock.fill (Dock.dock [ Dock.left 22 (sidebar model); Dock.fill (detailPane model) ]) ]

let mapInput (input: InputEvent) : Msg option =
    match input with
    | Key ke -> Some(KeyPressed ke)
    | _ -> Some Ignore

// ---------------------------------------------------------------------------
// Headless verification: `dotnet run --project Mire.Demo -- --dump`
// Walks the catalog and lays each entry's detail render onto a Surface, then
// prints the cell grid as plain text. Same `Detail.render` path as the live UI.
// ---------------------------------------------------------------------------

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
    // A fixed, representative model so interactive entries show meaningful state.
    let model =
        { Selected = 0
          Focus = Detail
          ScrollOffset = 4
          ListSel = 2
          Input = TextBuffer.Of "hello world" }

    let size = Size.Create(46, 10)

    for entry in Catalog.entries do
        let node = Detail.render model entry.Demo
        let surface = Surface(size)
        let laidOut = Layout.measure (Rect.FromOrigin size) node
        Layout.render surface laidOut
        printSurface (sprintf "%s %s" entry.Status entry.Title) surface

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
        |> Runtime.run

        0
