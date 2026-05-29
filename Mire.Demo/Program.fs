open System
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets

// ---------------------------------------------------------------------------
// Interactive demo: a scrollable list exercising Stack flow + Scroll clipping.
// ---------------------------------------------------------------------------

type Model =
    { Items: string list
      Offset: int }

type Msg =
    | ScrollBy of int
    | ToTop
    | ToBottom
    | Ignore

let demoItems =
    [ for i in 1 .. 40 ->
        sprintf "%2d   Item line number %d — a scrollable row rendered through a vstack" i i ]

let init () =
    { Items = demoItems; Offset = 0 }, Cmd.none

let private clamp lo hi v = max lo (min hi v)

let update msg model =
    let maxOffset = max 0 (model.Items.Length - 1)
    match msg with
    | ScrollBy d -> { model with Offset = clamp 0 maxOffset (model.Offset + d) }, Cmd.none
    | ToTop -> { model with Offset = 0 }, Cmd.none
    | ToBottom -> { model with Offset = maxOffset }, Cmd.none
    | Ignore -> model, Cmd.none

let view (model: Model) : LayoutNode<Msg> =
    let rows =
        model.Items
        |> List.mapi (fun i s ->
            let style = if i = model.Offset then Style.highlight else Style.text
            Text.text s style)

    Dock.dock [
        Dock.top 3 (Box.panel "Mire — Scroll Demo" Style.border [
            Text.title " Stack flow + Scroll offset/clipping "
        ])
        Dock.bottom 3 (Box.box Style.border [
            Text.text
                (sprintf " offset %d/%d   ↑/↓ scroll   PgUp/PgDn ±10   Home/End jump   Ctrl+C quit "
                    model.Offset (model.Items.Length - 1))
                Style.highlight
        ])
        Dock.fill (Box.box Style.border [
            Scroll.vertical model.Offset (Stack.vstack rows)
        ])
    ]

let mapInput (input: InputEvent) : Msg option =
    match input with
    | Key keyEvent ->
        match keyEvent.Key with
        | ArrowUp -> Some (ScrollBy -1)
        | ArrowDown -> Some (ScrollBy 1)
        | PageUp -> Some (ScrollBy -10)
        | PageDown -> Some (ScrollBy 10)
        | Home -> Some ToTop
        | End -> Some ToBottom
        | _ -> Some Ignore
    | _ -> Some Ignore

// ---------------------------------------------------------------------------
// Headless verification: `dotnet run --project Mire.Demo -- --dump`
// Lays out representative trees onto a Surface and prints the cell grid as
// plain text. No alternate screen / raw mode — safe to capture and inspect.
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

let private dump (label: string) (size: Size) (node: LayoutNode<'msg>) =
    let surface = Surface(size)
    let laidOut = Layout.measure (Rect.FromOrigin size) node
    Layout.render surface laidOut
    printSurface label surface

let runDump () =
    // A. Dock with a Content-sized header, a fixed footer, and a filling body.
    dump "A. Dock: top(Content) / fill / bottom(1)" (Size.Create(34, 7))
        (Dock.dock [
            { Position = DockPosition.Top Length.Content; Child = Text.title "HEADER\n(2 content rows)" }
            Dock.bottom 1 (Text.text "FOOTER" Style.highlight)
            Dock.fill (Box.box Style.border [ Text.text "body fills the middle" Style.text ])
        ])

    // B. Vertical stack: fixed / two equal Fill / fixed.
    dump "B. Stack(Vertical): Cells 1 / Fill / Fill / Cells 1" (Size.Create(24, 10))
        (Stack.vstackOf [
            Stack.sized (Length.Cells 1) (Text.text "fixed top" Style.title)
            Stack.sized Length.Fill (Box.box Style.border [ Text.text "fill A" Style.text ])
            Stack.sized Length.Fill (Box.box Style.border [ Text.text "fill B" Style.text ])
            Stack.sized (Length.Cells 1) (Text.text "fixed bottom" Style.highlight)
        ])

    // C. Scroll: same 12-row content at offset 0 then offset 4 → proves clipping.
    let list12 =
        Stack.vstack [ for i in 1 .. 12 -> Text.text (sprintf "row %02d" i) Style.text ]
    dump "C1. Scroll offset 0 (rows 01-05 visible)" (Size.Create(12, 5)) (Scroll.vertical 0 list12)
    dump "C2. Scroll offset 4 (rows 05-09 visible)" (Size.Create(12, 5)) (Scroll.vertical 4 list12)

    // D. Overlay opacity: transparent box (bg shows through) vs opaque backdrop.
    let bg = Stack.vstack [ for i in 1 .. 5 -> Text.text (sprintf "background row %d" i) Style.dim ]
    dump "D1. Overlay WITHOUT backdrop (bg shows inside box)" (Size.Create(24, 5))
        (LayoutNode.Overlay(Rect.Create(0, 0, 0, 0),
            [ bg
              Box.box Style.border [ Text.text "MODAL" Style.title ] ]))
    dump "D2. Overlay WITH backdrop (bg occluded)" (Size.Create(24, 5))
        (LayoutNode.Overlay(Rect.Create(0, 0, 0, 0),
            [ bg
              Backdrop.solid Style.text
              Box.box Style.border [ Text.text "MODAL" Style.title ] ]))

    printfn ""
    printfn "  (dump complete)"

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
