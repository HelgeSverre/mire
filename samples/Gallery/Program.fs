// The Mire widget gallery — a pure-framework showcase of every base widget in its
// key states, on the default brand theme (no app-specific theme code). Tab / Shift+Tab
// or ←/→ switch pages; Ctrl+C quits. `-- --dump` renders every page headlessly.

open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.Widgets
open Mire.App

let private theme = AppTheme.defaultTheme

// ── model ──────────────────────────────────────────────────────────────────
type Model = { Tab: int; Spinner: int; Size: Size }

let private pages =
    [ "Text"; "Boxes"; "Inputs"; "Lists"; "Controls"; "Overlays"; "Media" ]

let init () =
    { Tab = 0
      Spinner = 0
      Size =
        Mire.Protocol.TerminalMode.getTerminalSize ()
        |> Option.defaultValue (Size.Create(80, 30)) },
    Cmd.none

type Msg =
    | NextTab
    | PrevTab
    | Tick
    | Resized of Size

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | NextTab ->
        { m with
            Tab = (m.Tab + 1) % pages.Length },
        Cmd.none
    | PrevTab ->
        { m with
            Tab = (m.Tab + pages.Length - 1) % pages.Length },
        Cmd.none
    | Tick -> { m with Spinner = m.Spinner + 1 }, Cmd.none
    | Resized s -> { m with Size = s }, Cmd.none

// ── per-page content ─────────────────────────────────────────────────────────
// A small labelled-row helper: a dim caption above the widget.
let private demo (label: string) (node: LayoutNode<Msg>) : LayoutNode<Msg> =
    Stack.vstack [ Text.text label theme.fgSubtle; node; Text.text "" theme.fgSubtle ]

let private textPage () : LayoutNode<Msg> =
    Stack.vstack
        [ demo
              "Type hierarchy"
              (Stack.vstack
                  [ Text.title "Title — bold acc|primary"
                    Text.text "Body text — the default foreground." theme.fg
                    Text.text "Muted — secondary information." theme.fgMuted
                    Text.text "Subtle — the quietest tier." theme.fgSubtle
                    Text.text "Accent — the one emerald moment." theme.accent ])
          demo
              "Status colors"
              (Stack.hstack
                  [ Text.text "success " theme.success
                    Text.text "warning " theme.warning
                    Text.text "danger " theme.danger
                    Text.text "info" theme.info ])
          demo
              "Badges"
              (Stack.hstack
                  [ Badge.badge theme.accentStrong "NEW"
                    Text.text " " theme.fg
                    Badge.badge theme.selection "12"
                    Text.text " " theme.fg
                    Badge.badge (theme.danger.WithBackground(Color.Rgb(0x3Auy, 0x14uy, 0x14uy))) "ERR" ])
          demo
              "Key hints"
              (Stack.hstack
                  [ KeyHint.hint theme.key theme.fgMuted "⏎" "submit"
                    Text.text "   " theme.fg
                    KeyHint.hint theme.key theme.fgMuted "^C" "quit" ]) ]

let private boxesPage (m: Model) : LayoutNode<Msg> =
    let w = max 20 (m.Size.Width - 6)

    Stack.vstack
        [ demo "Panel (titled box)" (Box.panel "settings" theme.border [ Text.text "a child line" theme.fg ])
          demo "Plain box" (Box.box theme.border [ Text.text " bordered content " theme.fg ])
          demo "Horizontal separator" (Separator.horizontal (min 40 w) theme.divider)
          demo
              "Split view (½ | ½)"
              (SplitView.horizontal
                  (Length.Fraction 0.5)
                  theme.divider
                  (Box.box theme.border [ Text.text " left " theme.fg ])
                  (Box.box theme.border [ Text.text " right " theme.fg ]))
          demo
              "Full-bleed row highlight (Backdrop.behind)"
              (Backdrop.behind theme.selection (Text.text "selected row spans the full width" theme.fg)) ]

let private inputsPage () : LayoutNode<Msg> =
    let selBuf =
        { Text = "hello world"
          Cursor = 5
          Anchor = Some 0 }

    let wrapped =
        { Text = "This is a longer paragraph that soft-wraps across several visual rows inside the editor."
          Cursor = 0
          Anchor = None }

    Stack.vstack
        [ demo "Input — placeholder/empty (focused)" (Input.render 32 theme.fg theme.selection true TextBuffer.Empty)
          demo
              "Input — text + block cursor (focused)"
              (Input.render 32 theme.fg theme.selection true (TextBuffer.Of "edit me"))
          demo "Input — selection highlight" (Input.render 32 theme.fg theme.selection true selBuf)
          demo
              "TextArea — soft-wrapped (renderWrapped)"
              (Box.box theme.border [ TextArea.renderWrapped 40 4 theme.fg theme.selection false wrapped ]) ]

let private listsPage () : LayoutNode<Msg> =
    let items = [ "alpha"; "bravo (selected)"; "charlie"; "delta"; "echo" ]

    let rows =
        [ [ "App.fs"; "modified"; "+42" ]
          [ "Theme.fs"; "added"; "+10" ]
          [ "Old.fs"; "deleted"; "-99" ] ]

    let columns: Column<string list, Msg> list =
        [ Table.textColumn "file" (Length.Cells 14) theme.fg (fun r -> r.[0])
          Table.textColumn "status" (Length.Cells 12) theme.fgMuted (fun r -> r.[1])
          Table.textColumn "Δ" (Length.Cells 6) theme.accent (fun r -> r.[2]) ]

    Stack.vstack
        [ demo
              "ListView — single select (row 1)"
              (Box.box theme.border [ ListView.view 5 theme.selection theme.fg 1 items ])
          demo
              "Table — sticky header, selected row 0"
              (Box.box theme.border [ Table.view 3 theme.fgSubtle theme.selection 0 (fun i -> i = 0) columns rows ]) ]

let private controlsPage (m: Model) : LayoutNode<Msg> =
    Stack.vstack
        [ demo
              "Toggles"
              (Stack.vstack
                  [ Toggle.checkbox theme.fg true "checked"
                    Toggle.checkbox theme.fg false "unchecked"
                    Toggle.radio theme.fg true "selected"
                    Toggle.radio theme.fg false "unselected"
                    Stack.hstack
                        [ Toggle.switch theme.accentStrong theme.fgMuted true
                          Toggle.switch theme.accentStrong theme.fgMuted false ] ])
          demo
              "Progress bars"
              (Stack.vstack
                  [ ProgressBar.view 30 theme.accent theme.fgSubtle 0.25
                    ProgressBar.view 30 theme.accent theme.fgSubtle 0.6
                    ProgressBar.view 30 theme.success theme.fgSubtle 1.0 ])
          demo "Spinner (animated)" (Spinner.labeled theme.accent theme.fgMuted m.Spinner "working…")
          demo
              "Status bar"
              (StatusBar.statusBar
                  [ Text.text "NORMAL" theme.accent ]
                  [ Text.text "gallery.fs" theme.fgMuted ]
                  [ Text.text "ln 1" theme.fgSubtle ]) ]

let private overlaysPage (m: Model) : LayoutNode<Msg> =
    // Each overlay fills the page area; show one per render via the spinner tick so
    // the gallery cycles modal ↔ completion without extra keys.
    let w = max 30 (min 56 (m.Size.Width - 6))
    let h = max 8 (min 12 (m.Size.Height - 8))

    if (m.Spinner / 12) % 2 = 0 then
        Modal.modal
            Style.Default
            theme.border
            theme.title
            w
            h
            "Confirm"
            (Stack.vstack
                [ Text.text "Apply 3 changes to the working tree?" theme.fg
                  Text.text "" theme.fg
                  Stack.hstack
                      [ Badge.badge theme.accentStrong " Apply "
                        Text.text "  " theme.fg
                        Badge.badge theme.selection " Cancel " ] ])
    else
        Completion.view
            (m.Size.Width - 4)
            (m.Size.Height - 6)
            6
            2
            28
            5
            theme.border
            theme.selection
            theme.fg
            1
            [ "/help"; "/model (selected)"; "/clear"; "/quit" ]

let private mediaPage () : LayoutNode<Msg> =
    let md =
        "# Markdown\n\nA paragraph with **bold**, *italic*, and `code`.\n\n- first bullet\n- second bullet\n\n> a block quote"

    Stack.hstackOf
        [ Stack.sized
              (Length.Cells 22)
              (demo "ImagePreview" (ImagePreview.render 20 7 theme.border theme.fgMuted "logo.png" (Some(640, 480))))
          Stack.sized Length.Fill (demo "Markdown" (Markdown.render theme.markdown 44 md)) ]

let private pageContent (m: Model) : LayoutNode<Msg> =
    match m.Tab with
    | 0 -> textPage ()
    | 1 -> boxesPage m
    | 2 -> inputsPage ()
    | 3 -> listsPage ()
    | 4 -> controlsPage m
    | 5 -> overlaysPage m
    | _ -> mediaPage ()

// ── view ───────────────────────────────────────────────────────────────────
let view (m: Model) : LayoutNode<Msg> =
    let header =
        Box.box theme.border [ Tabs.strip theme.accentStrong theme.fgMuted m.Tab pages ]

    let footer =
        StatusBar.statusBar
            [ Text.text "Mire widget gallery" theme.title ]
            [ KeyHint.hint theme.key theme.fgMuted "⇥/→" "next page" ]
            [ KeyHint.hint theme.key theme.fgMuted "^C" "quit" ]

    Dock.dock
        [ Dock.top 3 header
          Dock.bottom 1 footer
          Dock.fill (Box.box theme.border [ pageContent m ]) ]

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | Tab when ke.Modifiers.Shift -> Some PrevTab
        | Tab -> Some NextTab
        | ArrowRight -> Some NextTab
        | ArrowLeft -> Some PrevTab
        | _ -> None
    | Resize s -> Some(Resized s)
    | _ -> None

let subscriptions (_: Model) : Sub<Msg> list =
    [ Sub.TerminalResize Resized
      Sub.Every(TimeSpan.FromMilliseconds 120.0, fun () -> Tick) ]

// ── headless --dump ──────────────────────────────────────────────────────────
let private printSurface (label: string) (surface: Surface) =
    let w = surface.Size.Width
    let bar = String.replicate w "─"
    printfn ""
    printfn "  %s  (%d×%d)" label w surface.Size.Height
    printfn "  ┌%s┐" bar

    for y in 0 .. surface.Size.Height - 1 do
        let sb = System.Text.StringBuilder()

        for x in 0 .. w - 1 do
            let g = surface.[x, y].Grapheme
            sb.Append(if String.IsNullOrEmpty g then " " else g) |> ignore

        printfn "  │%s│" (sb.ToString())

    printfn "  └%s┘" bar

let private dump () =
    let size = Size.Create(74, 22)

    pages
    |> List.iteri (fun i name ->
        let m = { Tab = i; Spinner = 0; Size = size }
        let surface = Surface(size)
        Layout.render surface (Layout.measure (Rect.FromOrigin size) (view m))
        printSurface (sprintf "%d. %s" (i + 1) name) surface)

[<EntryPoint>]
let main argv =
    if argv |> Array.contains "--dump" then
        dump ()
        0
    else
        Program.create init update view
        |> Program.withMapInput mapInput
        |> Program.withSubscriptions subscriptions
        |> Runtime.run

        0
