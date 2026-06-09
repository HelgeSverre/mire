open System
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.Demo.KitchenSink

// ---------------------------------------------------------------------------
// Mire — KitchenSink. A comprehensive showcase of every layout node, widget,
// and positioning feature in the framework, styled on-brand via the Mire
// palette (brand/palette.fs → Theme.fs → AppTheme).
//
// The sidebar lists the categories; the detail pane renders the selected one.
// Spinners/progress animate via a Sub.Every tick; the data/editing entries
// (ListView, Table, ScrollView, Input, TextArea, Tabs, Toggle, ProgressBar,
// CommandPalette) are driveable when focused (Tab into the pane).
//
// `Detail.render` is the single path shared by the live view and `--dump`, so
// every showcase is verifiable headlessly (and is the golden-frame source).
// The app's own sidebar|detail layout dogfoods `SplitView`.
// ---------------------------------------------------------------------------

// ── shorthand for the themed styles ─────────────────────────────────────────
let private T = Theme.theme

// ── demo catalog ────────────────────────────────────────────────────────────
type Demo =
    | DText
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
    | DOverlay
    | DSplit
    | DMarkdown
    | DPalette
    | DDock
    | DLayout
    | DCommandPalette

type Entry =
    { Demo: Demo
      Title: string
      Notes: string }

module Catalog =
    let entries: Entry list =
        [ { Demo = DText; Title = "Text"; Notes = "Styled, multi-line, grapheme-width aware." }
          { Demo = DBox; Title = "Box / Panel"; Notes = "Bordered container, titled panel, status bar." }
          { Demo = DChrome; Title = "Separator·Badge·KeyHint"; Notes = "Rules, toned pills, key+label chips." }
          { Demo = DSpinner; Title = "Spinner"; Notes = "Braille frames driven by a Sub.Every tick." }
          { Demo = DProgress; Title = "ProgressBar / Gauge"; Notes = "Determinate bar + centered % gauge. ←/→ adjust." }
          { Demo = DTabs; Title = "Tabs"; Notes = "Tab strip with an active indicator. ←/→ select." }
          { Demo = DToggle; Title = "Toggle"; Notes = "checkbox / radio / switch. Space · ↑/↓ · s." }
          { Demo = DList; Title = "ListView"; Notes = "Virtualized single-select, full-width highlight. ↑/↓." }
          { Demo = DTable; Title = "Table"; Notes = "Sticky header, Length columns, row select. ↑/↓." }
          { Demo = DScroll; Title = "ScrollView"; Notes = "Viewport + track/thumb scrollbar. ↑/↓ PgUp/PgDn." }
          { Demo = DInput; Title = "Input"; Notes = "Single-line TextBuffer + block cursor. Type." }
          { Demo = DTextArea; Title = "TextArea"; Notes = "Multi-line editor; word ops + paste. Type." }
          { Demo = DModal; Title = "Modal"; Notes = "Centered box over an opaque backdrop." }
          { Demo = DToast; Title = "Toast"; Notes = "A placed stack of toned notification cards." }
          { Demo = DCompletion; Title = "Completion"; Notes = "Cursor-anchored selectable popup (flips above)." }
          { Demo = DTooltip; Title = "Tooltip"; Notes = "Anchored bordered doc popup, clamped on-screen." }
          { Demo = DOverlay; Title = "Overlay / Positioned"; Notes = "9-point placement, atPoint popup, z-order layers." }
          { Demo = DSplit; Title = "SplitView"; Notes = "Multi-pane splits: Fraction vs Cells, nested." }
          { Demo = DMarkdown; Title = "Markdown"; Notes = "Headings, emphasis, lists, fenced code, quotes." }
          { Demo = DPalette; Title = "Palette & Styles"; Notes = "Brand swatches + themed style samples." }
          { Demo = DDock; Title = "Dock"; Notes = "5-position dock layout: top/bottom/left/right/fill." }
          { Demo = DLayout; Title = "Length & Sizing"; Notes = "Cells vs Fraction vs Content vs Fill side-by-side." }
          { Demo = DCommandPalette; Title = "CommandPalette"; Notes = "Fuzzy-filtered centered palette. Type/↑↓/Enter." } ]

    let interactive (demo: Demo) : bool =
        match demo with
        | DProgress | DTabs | DToggle | DList | DTable | DScroll | DInput | DTextArea | DCommandPalette -> true
        | _ -> false

// ── sample data ────────────────────────────────────────────────────────────
let private fruits =
    [ "apple"; "banana"; "cherry"; "date"; "elderberry"
      "fig"; "grape"; "honeydew"; "kiwi"; "lemon"; "mango"; "nectarine" ]

type SkuRow =
    { Name: string; Qty: int; Price: string }

let private tableRows =
    [ { Name = "Widgets"; Qty = 12; Price = "$3.40" }
      { Name = "Gadgets"; Qty = 4; Price = "$9.95" }
      { Name = "Gizmos"; Qty = 27; Price = "$1.20" }
      { Name = "Sprockets"; Qty = 9; Price = "$5.55" }
      { Name = "Cogs"; Qty = 150; Price = "$0.30" }
      { Name = "Flanges"; Qty = 3; Price = "$12.00" }
      { Name = "Grommets"; Qty = 88; Price = "$0.75" } ]

let private mdDoc =
    "# Markdown\n\nBody with **bold**, *italic* and `inline code`.\n\n- bullet one\n- bullet two\n\n```\nlet answer = 42  // a comment\n```\n\n> a block quote\n"

let private paletteCommands =
    [ "openFile"; "openFolder"; "openRecent"; "openSettings"
      "toggleSidebar"; "toggleMode"; "clearTranscript"; "showWelcome" ]

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
      Area: TextBuffer
      PaletteQuery: string
      PaletteSel: int }

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
      Area = TextBuffer.Of "multi-line\nTextArea — type,\nCtrl+Backspace word-delete"
      PaletteQuery = ""
      PaletteSel = 0 },
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
        | ArrowLeft -> { m with TabSel = clamp 0 2 (m.TabSel - 1) }
        | ArrowRight -> { m with TabSel = clamp 0 2 (m.TabSel + 1) }
        | _ -> m
    | DToggle ->
        match ke.Key with
        | Space -> { m with Checked = not m.Checked }
        | ArrowUp -> { m with RadioSel = clamp 0 2 (m.RadioSel - 1) }
        | ArrowDown -> { m with RadioSel = clamp 0 2 (m.RadioSel + 1) }
        | Char "s" -> { m with SwitchOn = not m.SwitchOn }
        | _ -> m
    | DList ->
        match ke.Key with
        | ArrowUp -> { m with ListSel = clamp 0 (fruits.Length - 1) (m.ListSel - 1) }
        | ArrowDown -> { m with ListSel = clamp 0 (fruits.Length - 1) (m.ListSel + 1) }
        | _ -> m
    | DTable ->
        let maxSel = tableRows.Length - 1
        match ke.Key with
        | ArrowUp -> { m with TableSel = clamp 0 maxSel (m.TableSel - 1) }
        | ArrowDown -> { m with TableSel = clamp 0 maxSel (m.TableSel + 1) }
        | _ -> m
    | DScroll ->
        match ke.Key with
        | ArrowUp -> { m with ScrollOffset = max 0 (m.ScrollOffset - 1) }
        | ArrowDown -> { m with ScrollOffset = min 29 (m.ScrollOffset + 1) }
        | PageUp -> { m with ScrollOffset = max 0 (m.ScrollOffset - 5) }
        | PageDown -> { m with ScrollOffset = min 29 (m.ScrollOffset + 5) }
        | _ -> m
    | DInput -> { m with Input = TextEdit.applyInput (Key ke) m.Input }
    | DTextArea -> editArea (TextEdit.applyInput (Key ke))
    | DCommandPalette ->
        match ke.Key with
        | Char c when not ke.Modifiers.Ctrl && not ke.Modifiers.Alt ->
            { m with PaletteQuery = m.PaletteQuery + c; PaletteSel = 0 }
        | Space -> { m with PaletteQuery = m.PaletteQuery + " "; PaletteSel = 0 }
        | Backspace ->
            let q = if m.PaletteQuery = "" then "" else m.PaletteQuery.Substring(0, m.PaletteQuery.Length - 1)
            { m with PaletteQuery = q; PaletteSel = 0 }
        | ArrowUp -> { m with PaletteSel = max 0 (m.PaletteSel - 1) }
        | ArrowDown ->
            let filtered = CommandPalette.filter m.PaletteQuery paletteCommands
            { m with PaletteSel = clamp 0 (max 0 (List.length filtered - 1)) (m.PaletteSel + 1) }
        | Enter ->
            let filtered = CommandPalette.filter m.PaletteQuery paletteCommands
            match List.tryItem m.PaletteSel filtered with
            | Some cmd ->
                { m with
                    PaletteQuery = ""
                    PaletteSel = 0
                    Input = TextBuffer.Of(sprintf "ran: %s" cmd) }
            | None -> m
        | _ -> m
    | _ -> m

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | IgnoreMsg -> m, Cmd.none
    | TickMsg -> { m with Tick = m.Tick + 1 }, Cmd.none
    | ResizedMsg sz -> { m with Size = sz }, Cmd.none
    | PasteMsg s ->
        match m.Focus, current m with
        | Detail, DInput -> { m with Input = TextEdit.applyInput (Paste s) m.Input }, Cmd.none
        | Detail, DTextArea -> { m with Area = TextEdit.applyInput (Paste s) m.Area }, Cmd.none
        | Detail, DCommandPalette -> { m with PaletteQuery = m.PaletteQuery + s; PaletteSel = 0 }, Cmd.none
        | _ -> m, Cmd.none
    | KeyMsg ke ->
        let n = Catalog.entries.Length
        match m.Focus with
        | Sidebar ->
            match ke.Key with
            | ArrowUp -> { m with Selected = clamp 0 (n - 1) (m.Selected - 1) }, Cmd.none
            | ArrowDown -> { m with Selected = clamp 0 (n - 1) (m.Selected + 1) }, Cmd.none
            | Enter | Tab ->
                (if Catalog.interactive (current m) then { m with Focus = Detail } else m), Cmd.none
            | _ -> m, Cmd.none
        | Detail ->
            match ke.Key with
            | Escape | Tab -> { m with Focus = Sidebar }, Cmd.none
            | _ -> routeDetail (current m) ke m, Cmd.none

// ── detail rendering ───────────────────────────────────────────────────────
module Detail =
    let private rect0 = Rect.Create(0, 0, 0, 0)

    let private pillOn =
        T.accentStrong

    let private pillOff =
        T.fgMuted.WithBackground(Theme.fgMuted)

    let private gaugeLabel =
        T.bg.WithForeground(Theme.fgMuted)

    let render (m: Model) (demo: Demo) : LayoutNode<Msg> =
        match demo with
        | DText ->
            Stack.vstack
                [ Text.text "plain text" T.fg
                  Text.title "a bold title"
                  Text.dimText "dimmed secondary text"
                  Text.text "wide glyphs: 日本語 emoji 🚀 ok" T.fg
                  Text.text "two\nlines via \\n" T.fg
                  Text.text "accent moment" T.accent ]
        | DBox ->
            Stack.vstack
                [ Box.box T.border [ Text.text "Box.box — bordered" T.fg ]
                  Box.panel "Panel" T.border [ Text.text "title + body" T.fg ]
                  StatusBar.statusBar
                      [ Text.text " left " T.title ]
                      [ Text.text " center " T.fg ]
                      [ Text.text " right " T.accent ] ]
        | DChrome ->
            Stack.vstack
                [ Text.dimText "Separator.horizontal:"
                  Separator.horizontal 30 T.fgMuted
                  Text.dimText "Badge (tones):"
                  Stack.hstack
                      [ Badge.badge T.success "ok"
                        Text.text " " T.fgMuted
                        Badge.badge T.warning "warn"
                        Text.text " " T.fgMuted
                        Badge.badge T.danger "err" ]
                  Text.dimText "KeyHint:"
                  KeyHint.hint T.key T.fgMuted "Ctrl+P" "palette" ]
        | DSpinner ->
            Stack.vstack
                [ Spinner.labeled T.info T.fg m.Tick "loading…"
                  Stack.hstack
                      [ for k in 0..4 -> Text.text (Spinner.frameOf Spinner.braille (m.Tick + k * 2)) T.success ]
                  Text.dimText "animates via a Sub.Every tick" ]
        | DProgress ->
            Stack.vstack
                [ Text.dimText "ProgressBar.view (←/→ adjust):"
                  ProgressBar.view 30 T.success T.fgMuted m.Frac
                  Text.dimText "ProgressBar.gauge:"
                  ProgressBar.gauge 30 T.success T.fgMuted gaugeLabel m.Frac
                  Text.dimText "animated:"
                  ProgressBar.view 30 T.info T.fgMuted (float (m.Tick % 31) / 30.0) ]
        | DTabs ->
            let labels = [ "Overview"; "Detail"; "Raw" ]
            Stack.vstack
                [ Tabs.strip T.accent T.fgMuted m.TabSel labels
                  Separator.horizontal 30 T.fgMuted
                  Text.text (sprintf "  body of: %s" (List.item (clamp 0 2 m.TabSel) labels)) T.fg ]
        | DToggle ->
            Stack.vstack
                [ Toggle.checkbox T.fg m.Checked "enable feature"
                  Toggle.radio T.fg (m.RadioSel = 0) "option A"
                  Toggle.radio T.fg (m.RadioSel = 1) "option B"
                  Toggle.radio T.fg (m.RadioSel = 2) "option C"
                  Stack.hstack
                      [ Text.dimText "switch: "
                        Toggle.switch pillOn pillOff m.SwitchOn ] ]
        | DList -> ListView.view 8 T.selection T.fg m.ListSel fruits
        | DTable ->
            let cols =
                [ Table.textColumn "Item" (Length.Cells 14) T.fg (fun r -> r.Name)
                  Table.textColumn "Qty" (Length.Cells 6) T.info (fun r -> string r.Qty)
                  Table.textColumn "Price" (Length.Cells 8) T.success (fun r -> r.Price) ]
            Table.view 8 T.title T.selection m.TableTop (fun i -> i = m.TableSel) cols tableRows
        | DScroll ->
            let rows =
                Stack.vstack [ for i in 1..30 -> Text.text (sprintf "  scrollable row %d" i) T.fg ]
            ScrollView.vertical 8 30 m.ScrollOffset T.fgMuted T.selection rows
        | DInput ->
            let focused = m.Focus = Detail
            Box.box T.border [ Input.render 32 T.fg T.selection focused m.Input ]
        | DTextArea ->
            let focused = m.Focus = Detail
            Box.box T.border [ TextArea.render 34 5 T.fg T.selection focused m.Area ]
        | DModal ->
            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for i in 1..6 -> Text.text (sprintf " base content row %d" i) T.fgMuted ]
                  Modal.modal T.bg T.border T.title 30 6 "Confirm"
                      (Stack.vstack
                          [ Text.text "Proceed with the action?" T.fg
                            Text.dimText "Enter ok · Esc cancel" ]) ]
            )
        | DToast ->
            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for i in 1..6 -> Text.text (sprintf " app row %d" i) T.fgMuted ]
                  Mire.Widgets.Toast.stack
                      TopRight 26 3
                      [ Mire.Widgets.Toast.card T.border T.success T.fg "✓ Saved" "changes written"
                        Mire.Widgets.Toast.card T.border T.warning T.fg "! Heads up" "check the logs" ] ]
            )
        | DCompletion ->
            let aw, ah = 40, 8
            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for _ in 1..ah -> Text.text (String.replicate aw "·") T.fgMuted ]
                  Completion.view aw ah 6 1 22 5 T.border T.selection T.fg 1
                      [ "openFile"; "openFolder"; "openRecent"; "openSettings" ] ]
            )
        | DTooltip ->
            let aw, ah = 40, 8
            LayoutNode.Overlay(
                rect0,
                [ Stack.vstack [ for _ in 1..ah -> Text.text (String.replicate aw "·") T.fgMuted ]
                  Tooltip.view aw ah 8 2 22 T.border T.fg [ "An anchored tooltip."; "Clamped on-screen." ] ]
            )
        | DOverlay ->
            let mkPlacement p label =
                Overlay.positioned p (Length.Cells(String.length label + 2)) (Length.Cells 1)
                    (Badge.badge T.info label)

            let positionedDemo =
                LayoutNode.Overlay(
                    rect0,
                    [ Backdrop.solid T.bgElevated
                      mkPlacement TopLeft "TL"
                      mkPlacement TopCenter "TC"
                      mkPlacement TopRight "TR"
                      mkPlacement CenterLeft "CL"
                      mkPlacement Center "MID"
                      mkPlacement CenterRight "CR"
                      mkPlacement BottomLeft "BL"
                      mkPlacement BottomCenter "BC"
                      mkPlacement BottomRight "BR" ]
                )

            let aw2, ah2 = 44, 8
            let atPointDemo =
                LayoutNode.Overlay(
                    rect0,
                    [ Stack.vstack [ for _ in 1..ah2 -> Text.text (String.replicate aw2 "·") T.fgMuted ]
                      Overlay.atPoint 10 5 20 3 aw2 ah2
                          (Box.box T.borderFocus [ Text.text " anchored at (10,5) " T.accent ]) ]
                )

            let zOrderDemo =
                LayoutNode.Overlay(
                    rect0,
                    [ Backdrop.solid T.bg
                      Modal.modal T.bg T.border T.title 28 5 "Modal"
                          (Text.text "centered over backdrop" T.fg)
                      Mire.Widgets.Toast.stack TopRight 22 3
                          [ Mire.Widgets.Toast.card T.border T.success T.fg "✓ ok" "layer 3" ] ]
                )

            Stack.vstackOf
                [ Stack.sized Length.Content (Text.text "9-point Positioned:" T.title)
                  Stack.sized (Length.Cells 6) positionedDemo
                  Stack.sized Length.Content Spacer.spacer
                  Stack.sized Length.Content (Text.text "Overlay.atPoint (cursor-anchored):" T.title)
                  Stack.sized (Length.Cells 10) atPointDemo
                  Stack.sized Length.Content Spacer.spacer
                  Stack.sized Length.Content (Text.text "Z-order layering:" T.title)
                  Stack.sized (Length.Cells 8) zOrderDemo ]
        | DSplit ->
            let leftPane =
                Box.box T.border [ Stack.vstack [ Text.text " left 40% " T.info; Text.dimText "Fraction 0.4" ] ]
            let rightTop =
                Box.box T.border [ Text.text " top (Cells 3) " T.success ]
            let rightBottom =
                Box.box T.border [ Text.text " bottom (Fill) " T.fg ]
            let rightPane =
                SplitView.vertical (Length.Cells 3) T.divider rightTop rightBottom

            let nested =
                SplitView.horizontal (Length.Fraction 0.4) T.divider leftPane rightPane

            let cellsSplit =
                SplitView.horizontal (Length.Cells 20) T.divider
                    (Box.box T.border [ Text.text " Cells 20 " T.info ])
                    (Box.box T.border [ Text.text " fill " T.fg ])

            Stack.vstackOf
                [ Stack.sized Length.Content (Text.text "Nested: Fraction left + vertical right" T.title)
                  Stack.sized (Length.Cells 10) nested
                  Stack.sized Length.Content Spacer.spacer
                  Stack.sized Length.Content (Text.text "Cells vs Fill" T.title)
                  Stack.sized (Length.Cells 5) cellsSplit ]
        | DMarkdown -> Markdown.render T.markdown 42 mdDoc
        | DPalette ->
            let ofP (c: Mire.Brand.Palette.Color) =
                let (r, g, b) = c.Rgb
                Color.Rgb(r, g, b)

            let swatch (c: Mire.Brand.Palette.Color) (label: string) =
                let bgStyle = Style.Default.WithBackground(ofP c)
                let fgForSwatch =
                    if c.Hex = "#050505" || c.Hex = "#121212" || c.Hex = "#292929"
                       || c.Hex = "#003724" || c.Hex = "#006750" then
                        Style.Default.WithForeground(Color.White).WithBackground(ofP c)
                    else
                        Style.Default.WithForeground(Color.Rgb(0x12uy, 0x12uy, 0x12uy)).WithBackground(ofP c)
                Backdrop.behind bgStyle (Text.text (sprintf " %s " label) fgForSwatch)

            let neutralSwatches =
                Stack.hstackOf
                    [ for (c, label) in
                          [ (Mire.Brand.Palette.Neutrals.n50, "n50")
                            (Mire.Brand.Palette.Neutrals.n100, "n100")
                            (Mire.Brand.Palette.Neutrals.n200, "n200")
                            (Mire.Brand.Palette.Neutrals.n300, "n300")
                            (Mire.Brand.Palette.Neutrals.n400, "n400")
                            (Mire.Brand.Palette.Neutrals.n500, "n500")
                            (Mire.Brand.Palette.Neutrals.n600, "n600")
                            (Mire.Brand.Palette.Neutrals.n700, "n700")
                            (Mire.Brand.Palette.Neutrals.n800, "n800")
                            (Mire.Brand.Palette.Neutrals.n900, "n900")
                            (Mire.Brand.Palette.Neutrals.n950, "n950") ] ->
                          Stack.sized Length.Content (swatch c label) ]

            let accentSwatches =
                Stack.hstackOf
                    [ for (c, label) in
                          [ (Mire.Brand.Palette.Accent.a100, "a100")
                            (Mire.Brand.Palette.Accent.a300, "a300")
                            (Mire.Brand.Palette.Accent.a500, "a500")
                            (Mire.Brand.Palette.Accent.a700, "a700")
                            (Mire.Brand.Palette.Accent.a900, "a900") ] ->
                          Stack.sized Length.Content (swatch c label) ]

            let styleRow (name: string) (style: Style) =
                Stack.hstackOf
                    [ Stack.sized (Length.Cells 16) (Text.text (sprintf "  %-14s" name) T.fgMuted)
                      Stack.sized Length.Content (Text.text "the quick brown fox" style) ]

            Stack.vstackOf
                [ Stack.sized Length.Content (Text.text "Neutrals:" T.title)
                  Stack.sized Length.Content neutralSwatches
                  Stack.sized Length.Content (Text.text "Accent (Emerald):" T.title)
                  Stack.sized Length.Content accentSwatches
                  Stack.sized Length.Content Spacer.spacer
                  Stack.sized Length.Content (Text.text "Themed styles:" T.title)
                  Stack.sized Length.Content (styleRow "fg" T.fg)
                  Stack.sized Length.Content (styleRow "fgMuted" T.fgMuted)
                  Stack.sized Length.Content (styleRow "fgSubtle" T.fgSubtle)
                  Stack.sized Length.Content (styleRow "title" T.title)
                  Stack.sized Length.Content (styleRow "accent" T.accent)
                  Stack.sized Length.Content (styleRow "success" T.success)
                  Stack.sized Length.Content (styleRow "warning" T.warning)
                  Stack.sized Length.Content (styleRow "danger" T.danger)
                  Stack.sized Length.Content (styleRow "info" T.info)
                  Stack.sized Length.Content (styleRow "border" T.border)
                  Stack.sized Length.Content (styleRow "selection" T.selection)
                  Stack.sized Length.Content (styleRow "key" T.key) ]
        | DDock ->
            let dockHeader =
                Box.box T.borderFocus [ Stack.hstackOf
                    [ Stack.sized Length.Content (Text.text " └" T.accent)
                      Stack.sized Length.Content (Text.text " header (Dock.top 3)" T.title)
                      Stack.flex
                      Stack.sized Length.Content (Text.text "accent " T.accent) ] ]
            let dockLeft =
                Box.box T.border [ Stack.vstack [ Text.text " left" T.fgMuted; Text.text " Dock.left" T.fgMuted; Text.text " 18" T.fgMuted ] ]
            let dockRight =
                Box.box T.border [ Stack.vstack [ Text.text "right" T.fgMuted; Text.text "Dock.right" T.fgMuted; Text.text "18" T.fgMuted ] ]
            let dockCenter =
                Box.box T.border [ Text.text " Dock.fill — center area " T.fg ]
            let dockFooter =
                Box.box T.border [ Text.text " status bar (Dock.bottom 3) " T.fgMuted ]

            Dock.dock
                [ Dock.top 3 dockHeader
                  Dock.bottom 3 dockFooter
                  Dock.left 18 dockLeft
                  Dock.right 18 dockRight
                  Dock.fill dockCenter ]
        | DLayout ->
            let bar (fillStyle: Style) (label: string) =
                let textStyle =
                    match fillStyle.Background with
                    | Some bg ->
                        match bg with
                        | Color.Rgb(r, g, b) when r < 80uy && g < 80uy && b < 80uy ->
                            Style.Default.WithForeground(Color.White)
                        | _ -> Style.Default.WithForeground(Color.Rgb(0x12uy, 0x12uy, 0x12uy))
                    | None -> T.fg
                Backdrop.behind fillStyle (Text.text (sprintf " %s " label) textStyle)

            Stack.vstackOf
                [ Stack.sized Length.Content (Text.text "Length variants inside a sized container:" T.title)
                  Stack.sized (Length.Cells 14)
                      (Stack.vstackOf
                          [ Stack.sized (Length.Cells 3) (bar T.selection "Cells 3")
                            Stack.sized (Length.Fraction 0.25) (bar T.accent "Fraction 0.25")
                            Stack.sized Length.Content (bar T.info "Content (intrinsic)")
                            Stack.sized Length.Fill (bar T.fgMuted "Fill (absorbs remainder)") ]) ]
        | DCommandPalette ->
            let filtered = CommandPalette.filter m.PaletteQuery paletteCommands
            CommandPalette.view 40 12 T.bg T.border T.accent T.selection T.fg
                "Commands" m.PaletteQuery m.PaletteSel filtered

// ── chrome ─────────────────────────────────────────────────────────────────
let private sidebarNode (m: Model) : LayoutNode<Msg> =
    let labels = Catalog.entries |> List.map (fun e -> e.Title)
    let border = if m.Focus = Sidebar then T.borderFocus else T.border

    Box.box
        border
        [ Stack.vstackOf
              [ Stack.sized (Length.Cells 1) (Text.title " Widgets ")
                Stack.sized
                    Length.Fill
                    (ListView.view (List.length labels) T.selection T.fg m.Selected labels) ] ]

let private detailNode (m: Model) : LayoutNode<Msg> =
    let entry = Catalog.entries.[m.Selected]
    let border = if m.Focus = Detail then T.borderFocus else T.border

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
        | DList | DTable -> " ↑/↓ select   Tab/Esc back "
        | DScroll -> " ↑/↓ scroll · PgUp/PgDn ±5   Tab/Esc back "
        | DInput | DTextArea -> " type to edit · ←/→ · Ctrl+Backspace   Tab/Esc back "
        | DCommandPalette -> " type query · ↑/↓ select · Enter run   Tab/Esc back "
        | _ -> " Tab/Esc back "

let view (m: Model) : LayoutNode<Msg> =
    let middle =
        SplitView.horizontal
            (Length.Cells 24)
            T.divider
            (sidebarNode m)
            (detailNode m)

    Dock.dock
        [ Dock.top 3 (Box.box T.border [ Stack.hstackOf
            [ Stack.sized Length.Content (Text.text " └" T.accent)
              Stack.sized Length.Content (Text.title " mire · kitchen-sink ")
              Stack.flex
              Stack.sized Length.Content (Text.text (sprintf "%d/%d " (m.Selected + 1) Catalog.entries.Length) T.fgMuted) ] ])
          Dock.bottom 3 (Box.box T.border [ Text.text (hints m) T.fgSubtle ])
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
