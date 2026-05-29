open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.FeedDemo

// ---------------------------------------------------------------------------
// Mire.FeedDemo — a terminal RSS reader (two-pane: article list + reader).
// Exercises async Cmd loading, scrolling lists with full-width selection, text
// wrapping, and HTML-to-text — and surfaces where the framework is still thin.
// ---------------------------------------------------------------------------

let feedUrl = "https://helgesver.re/rss/feed.xml"

// styles -------------------------------------------------------------------
let private sTitle  = Style.Default.WithForeground(Color.White).WithBold(true)
let private sDim    = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x8Fuy))
let private sBody   = Style.Default.WithForeground(Color.Rgb(0xCCuy, 0xCCuy, 0xD2uy))
let private sBorder = Style.Default.WithForeground(Color.Rgb(0x3Cuy, 0x44uy, 0x50uy))
let private sAccent = Style.Default.WithForeground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)).WithBold(true)
let private sRow    = Style.Default.WithForeground(Color.Rgb(0xB6uy, 0xBAuy, 0xC2uy))
let private sErr    = Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy))
// selection = dark text on emerald, applied as a full-width row fill
let private sSel    = Style.Default.WithForeground(Color.Rgb(0x08uy, 0x14uy, 0x0Auy)).WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))

let private spinnerFrames = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]

// model --------------------------------------------------------------------
type Pane = ListPane | ReaderPane
type Status = Loading | Ready | LoadFailed of string

type Model =
    { FeedTitle: string
      Items: Article list
      Sel: int
      Pane: Pane
      ReaderScroll: int
      Status: Status
      Spinner: int
      Size: Size }

type Msg =
    | Loaded of string * Article list
    | LoadError of string
    | NavUp
    | NavDown
    | NavPageUp
    | NavPageDown
    | TogglePane
    | FocusReader
    | Reload
    | Resized of Size
    | Spin
    | Ignore

let private clamp lo hi v = max lo (min hi v)

let private loadCmd : Cmd<Msg> =
    Cmd.ofAsync (fun dispatch ->
        async {
            let! result = Feed.fetchAsync feedUrl
            match result with
            | Ok(title, items) -> dispatch (Loaded(title, items))
            | Error e -> dispatch (LoadError e)
        })

let init () =
    { FeedTitle = "RSS"
      Items = []
      Sel = 0
      Pane = ListPane
      ReaderScroll = 0
      Status = Loading
      Spinner = 0
      Size = Size.Create(100, 30) },
    loadCmd

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    let count = List.length m.Items
    match msg with
    | Loaded(title, items) ->
        { m with FeedTitle = title; Items = items; Status = Ready; Sel = 0; ReaderScroll = 0 }, Cmd.none
    | LoadError e -> { m with Status = LoadFailed e }, Cmd.none
    | Reload -> { m with Status = Loading }, loadCmd
    | Resized s -> { m with Size = s }, Cmd.none
    | Spin -> { m with Spinner = m.Spinner + 1 }, Cmd.none
    | TogglePane -> { m with Pane = (if m.Pane = ListPane then ReaderPane else ListPane) }, Cmd.none
    | FocusReader -> { m with Pane = ReaderPane }, Cmd.none
    | NavUp ->
        match m.Pane with
        | ListPane -> { m with Sel = clamp 0 (max 0 (count - 1)) (m.Sel - 1); ReaderScroll = 0 }, Cmd.none
        | ReaderPane -> { m with ReaderScroll = max 0 (m.ReaderScroll - 1) }, Cmd.none
    | NavDown ->
        match m.Pane with
        | ListPane -> { m with Sel = clamp 0 (max 0 (count - 1)) (m.Sel + 1); ReaderScroll = 0 }, Cmd.none
        | ReaderPane -> { m with ReaderScroll = m.ReaderScroll + 1 }, Cmd.none
    | NavPageUp ->
        match m.Pane with
        | ListPane -> { m with Sel = clamp 0 (max 0 (count - 1)) (m.Sel - 5); ReaderScroll = 0 }, Cmd.none
        | ReaderPane -> { m with ReaderScroll = max 0 (m.ReaderScroll - 10) }, Cmd.none
    | NavPageDown ->
        match m.Pane with
        | ListPane -> { m with Sel = clamp 0 (max 0 (count - 1)) (m.Sel + 5); ReaderScroll = 0 }, Cmd.none
        | ReaderPane -> { m with ReaderScroll = m.ReaderScroll + 10 }, Cmd.none
    | Ignore -> m, Cmd.none

// view helpers -------------------------------------------------------------

/// Truncate to `width` columns (grapheme-aware), adding an ellipsis if cut.
let private truncate (width: int) (s0: string) : string =
    let s = s0.Replace("\n", " ").Trim()
    if Grapheme.stringWidth s <= width then s
    elif width <= 1 then "…"
    else
        let sb = Text.StringBuilder()
        let mutable w = 0
        let mutable stop = false
        for ch in s do
            if not stop then
                let cw = Grapheme.charWidth ch
                if w + cw <= width - 1 then
                    sb.Append(ch) |> ignore
                    w <- w + cw
                else
                    stop <- true
        sb.Append('…').ToString()

/// A bordered panel with a one-line title row above its content. Built as a
/// single-child Box (avoiding the multi-child Box overlap), so title and content
/// don't collide.
let private panel (heading: string) (content: LayoutNode<Msg>) : LayoutNode<Msg> =
    Box.box sBorder
        [ Stack.vstackOf
            [ Stack.sized (Length.Cells 1) (Text.text (" " + heading) sAccent)
              Stack.sized Length.Fill content ] ]

let private statusText (m: Model) : string =
    match m.Status with
    | Loading -> sprintf "%s loading %s" spinnerFrames.[m.Spinner % spinnerFrames.Length] feedUrl
    | LoadFailed e -> "✗ " + e
    | Ready ->
        let n = List.length m.Items
        if n = 0 then "no articles"
        else sprintf "✓ %d articles · %d/%d" n (m.Sel + 1) n

let view (m: Model) : LayoutNode<Msg> =
    let w = max 24 m.Size.Width
    let h = max 8 m.Size.Height
    let bodyH = max 1 (h - 4) // top header(3) + footer(1)
    let listW = clamp 26 46 (w / 3)
    let listInnerW = max 4 (listW - 2)
    let listInnerH = max 1 (bodyH - 3) // border(2) + title row(1)
    let readerInnerW = max 8 (w - listW - 2)
    let count = List.length m.Items

    // header
    let header =
        Box.box sBorder
            [ Text.text (sprintf " %s   %s" m.FeedTitle (statusText m)) sTitle ]

    // article list — the framework ListView handles full-width highlight + scroll
    let labels = m.Items |> List.map (fun a -> truncate (listInnerW - 1) a.Title)
    let listBorder = if m.Pane = ListPane then "Articles ▸" else "Articles"
    let listPane =
        panel (sprintf "%s (%d)" listBorder count)
            (ListView.view listInnerH sSel sRow m.Sel labels)

    // reader pane
    let readerContent =
        match m.Status, List.tryItem m.Sel m.Items with
        | Loading, _ -> Text.text "  fetching…" sDim
        | LoadFailed e, _ -> Text.text ("  " + e) sErr
        | Ready, None -> Text.text "  (no article selected)" sDim
        | Ready, Some a ->
            let titleLines = Feed.wrap readerInnerW a.Title
            let bodyLines = Feed.wrap readerInnerW a.Body |> List.map (fun l -> Text.text l sBody)
            Stack.vstackOf
                [ Stack.sized (Length.Cells(max 1 titleLines.Length)) (Text.text (String.concat "\n" titleLines) sTitle)
                  Stack.sized (Length.Cells 1) (Text.text (truncate readerInnerW a.Date) sDim)
                  Stack.sized (Length.Cells 1) (Text.text (truncate readerInnerW a.Link) (sDim.WithUnderline(UnderlineStyle.Single)))
                  Stack.sized (Length.Cells 1) (Text.text (String('─', readerInnerW)) sBorder)
                  Stack.sized Length.Fill (Scroll.vertical m.ReaderScroll (Stack.vstack bodyLines)) ]
    let readerHeading = if m.Pane = ReaderPane then "Reading ▸" else "Reading"
    let readerPane = panel readerHeading readerContent

    let body =
        Stack.hstackOf
            [ Stack.sized (Length.Cells listW) listPane
              Stack.sized Length.Fill readerPane ]

    let footer =
        Text.text
            " ↑/↓ navigate · Tab switch pane · Enter read · r reload · Ctrl+C quit"
            sDim

    Dock.dock
        [ Dock.top 3 header
          Dock.bottom 1 footer
          Dock.fill body ]

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | ArrowUp -> Some NavUp
        | ArrowDown -> Some NavDown
        | Char "k" -> Some NavUp
        | Char "j" -> Some NavDown
        | PageUp -> Some NavPageUp
        | PageDown -> Some NavPageDown
        | Tab -> Some TogglePane
        | Enter -> Some FocusReader
        | Char "r" -> Some Reload
        | _ -> Some Ignore
    | _ -> Some Ignore

let subscriptions (m: Model) : Sub<Msg> list =
    [ yield TerminalResize Resized
      if m.Status = Loading then
          yield Every(TimeSpan.FromMilliseconds 120.0, fun () -> Spin) ]

// ---------------------------------------------------------------------------
// Headless verification: fetch the real feed (fallback to a stub) and print the
// rendered cell grid. `dotnet run --project Mire.FeedDemo -- --dump`
// ---------------------------------------------------------------------------

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
    let title, items =
        match Feed.fetchAsync feedUrl |> Async.RunSynchronously with
        | Ok(t, items) -> t, items
        | Error e ->
            "Sample Feed (offline: " + e + ")",
            [ { Title = "Why combining marks break in DomPDF"
                Link = "https://helgesver.re/articles/…"
                Date = "Mon, 18 May 2026"
                Summary = "Non-shaping PDF engines render combining marks separately."
                Body = "Norwegian å becomes 'a' plus a drifting ring.\n\nThe fix: two passes of Unicode normalization." } ]
    let size = Size.Create(104, 30)
    let model = { (fst (init ())) with FeedTitle = title; Items = items; Status = Ready; Size = size }
    printfn "Mire.FeedDemo — %d articles from %s\n" (List.length items) feedUrl
    let surface = Surface(size)
    Layout.measure (Rect.FromOrigin size) (view model)
    |> Layout.render surface
    printSurface surface
    printfn "\n  selected: %s" (items |> List.tryHead |> Option.map (fun a -> a.Title) |> Option.defaultValue "—")

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
