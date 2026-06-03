open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.FeedDemo

// ---------------------------------------------------------------------------
// Mire.FeedDemo — a terminal RSS reader.
//
// Multi-feed: a managed list of feeds, merged into one newest-first stream, with
// a per-feed filter. Two panes (article list + reader) view the merged set; a
// Feeds manager (Ctrl+U) docks to the top third for add/delete/reload, and a
// filter picker (f) toggles which feeds contribute. Exercises async Cmd loading,
// scrolling lists, text wrapping, HTML-to-text — and app-level prototypes of the
// modal / text-input / checkbox patterns the framework doesn't ship yet.
// ---------------------------------------------------------------------------

let private rect0 = Rect.Create(0, 0, 0, 0)

// the feeds the demo starts with — all begin FLoading and fetch concurrently.
let private seedUrls =
    [ "https://helgesver.re/rss/feed.xml"
      "https://hnrss.org/frontpage"
      "https://www.theverge.com/rss/index.xml" ]

// styles -------------------------------------------------------------------
let private sTitle = Style.Default.WithForeground(Color.White).WithBold(true)
let private sDim = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x8Fuy))
let private sBody = Style.Default.WithForeground(Color.Rgb(0xCCuy, 0xCCuy, 0xD2uy))

let private sBorder =
    Style.Default.WithForeground(Color.Rgb(0x3Cuy, 0x44uy, 0x50uy))

let private sAccent =
    Style.Default.WithForeground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)).WithBold(true)

let private sRow = Style.Default.WithForeground(Color.Rgb(0xB6uy, 0xBAuy, 0xC2uy))
let private sErr = Style.Default.WithForeground(Color.Rgb(0xFFuy, 0x57uy, 0x22uy))
// selection = dark text on emerald, applied as a full-width row fill
let private sSel =
    Style.Default.WithForeground(Color.Rgb(0x08uy, 0x14uy, 0x0Auy)).WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))
// opaque fills behind overlays so they occlude the panes underneath
let private sPanelBg =
    Style.Default.WithBackground(Color.Rgb(0x12uy, 0x16uy, 0x1Cuy))

let private sModalBg =
    Style.Default.WithBackground(Color.Rgb(0x06uy, 0x08uy, 0x0Buy))

let private spinnerFrames = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]

// model --------------------------------------------------------------------
type Pane =
    | ListPane
    | ReaderPane

/// Why an add attempt failed. `InvalidUrl`/`Duplicate` are decided instantly;
/// `LoadFailedErr` arrives after the fetch spinner.
type AddError =
    | InvalidUrl
    | Duplicate
    | LoadFailedErr of string

type AddModalState =
    { Input: string
      Error: AddError option
      Submitting: bool }

/// Cursor 0 is the "+ Add new feed" row; 1..n select feeds[cursor-1].
type FeedsPanelState = { Cursor: int }

/// Working copy of the filter; applied to `Model.Selected` only on Enter.
type FilterState = { Checked: Set<string>; Cursor: int }

type OverlayState =
    | NoOverlay
    | FeedsPanel of FeedsPanelState
    | AddModal of AddModalState
    | Filter of FilterState

type Model =
    { Feeds: Feed list
      Selected: Set<string> // feed URLs shown; empty ⇒ all
      Sel: int
      Pane: Pane
      ReaderScroll: int
      Spinner: int
      Overlay: OverlayState
      Size: Size }

/// One terminal key, normalised so `update` can route it per-overlay.
type Key2 =
    | KChar of string
    | KSpace
    | KBackspace
    | KEnter
    | KEsc
    | KTab
    | KUp
    | KDown
    | KPageUp
    | KPageDown
    | KCtrlU

type Msg =
    | KeyMsg of Key2
    | FeedLoaded of string * string * Article list // url, title, articles
    | FeedLoadError of string * string // url, error
    | AddFetched of string * Result<string * Article list, string> // url, result
    | Resized of Size
    | Spin
    | Ignore

let private clamp lo hi v = max lo (min hi v)

// commands -----------------------------------------------------------------

/// Fetch a feed already in the list, keyed by url (init seed + manual reload).
let private loadFeedCmd (url: string) : Cmd<Msg> =
    Cmd.ofAsync (fun dispatch ->
        async {
            let! result = Feed.fetchAsync url

            match result with
            | Ok(title, items) -> dispatch (FeedLoaded(url, title, items))
            | Error e -> dispatch (FeedLoadError(url, e))
        })

/// Fetch a candidate url for the Add modal — result drives accept/reject.
let private addFetchCmd (url: string) : Cmd<Msg> =
    Cmd.ofAsync (fun dispatch ->
        async {
            let! result = Feed.fetchAsync url
            dispatch (AddFetched(url, result))
        })

let init () =
    let now = DateTime.Now

    let feeds =
        seedUrls
        |> List.map (fun u ->
            { Name = u
              Url = u
              AddedAt = now
              Articles = []
              Status = FLoading })

    { Feeds = feeds
      Selected = Set.empty
      Sel = 0
      Pane = ListPane
      ReaderScroll = 0
      Spinner = 0
      Overlay = NoOverlay
      Size = Size.Create(100, 30) },
    Cmd.batch (seedUrls |> List.map loadFeedCmd)

// derived ------------------------------------------------------------------

/// Feeds contributing to the stream: the filter set, or all when it's empty.
let private activeFeeds (m: Model) : Feed list =
    if Set.isEmpty m.Selected then
        m.Feeds
    else
        m.Feeds |> List.filter (fun f -> Set.contains f.Url m.Selected)

/// All articles from active feeds, newest first.
let private mergedArticles (m: Model) : Article list =
    activeFeeds m
    |> List.collect (fun f -> f.Articles)
    |> List.sortByDescending (fun a -> Feed.parseDate a.Date)

let private anyLoading (m: Model) : bool =
    (m.Feeds |> List.exists (fun f -> f.Status = FLoading))
    || (match m.Overlay with
        | AddModal ms -> ms.Submitting
        | _ -> false)

// update -------------------------------------------------------------------

let private setSourceFeed (name: string) (items: Article list) : Article list =
    items |> List.map (fun a -> { a with SourceFeed = name })

let private updateBase (k: Key2) (m: Model) : Model * Cmd<Msg> =
    let count = List.length (mergedArticles m)

    match k with
    | KCtrlU ->
        { m with
            Overlay = FeedsPanel { Cursor = 0 } },
        Cmd.none
    | KChar "f"
    | KChar "F" ->
        // Seed the picker from the active filter so reopening it preserves the
        // current subset; only the unfiltered state (Selected empty ⇒ show all)
        // falls back to every feed checked.
        { m with
            Overlay =
                Filter
                    { Checked =
                        if Set.isEmpty m.Selected then
                            m.Feeds |> List.map (fun f -> f.Url) |> Set.ofList
                        else
                            m.Selected
                      Cursor = 0 } },
        Cmd.none
    | KChar "r" ->
        // reload every feed
        { m with
            Feeds = m.Feeds |> List.map (fun f -> { f with Status = FLoading }) },
        Cmd.batch (m.Feeds |> List.map (fun f -> loadFeedCmd f.Url))
    | KTab ->
        { m with
            Pane = (if m.Pane = ListPane then ReaderPane else ListPane) },
        Cmd.none
    | KEnter -> { m with Pane = ReaderPane }, Cmd.none
    | KUp
    | KChar "k" ->
        match m.Pane with
        | ListPane ->
            { m with
                Sel = clamp 0 (max 0 (count - 1)) (m.Sel - 1)
                ReaderScroll = 0 },
            Cmd.none
        | ReaderPane ->
            { m with
                ReaderScroll = max 0 (m.ReaderScroll - 1) },
            Cmd.none
    | KDown
    | KChar "j" ->
        match m.Pane with
        | ListPane ->
            { m with
                Sel = clamp 0 (max 0 (count - 1)) (m.Sel + 1)
                ReaderScroll = 0 },
            Cmd.none
        | ReaderPane ->
            { m with
                ReaderScroll = m.ReaderScroll + 1 },
            Cmd.none
    | KPageUp ->
        match m.Pane with
        | ListPane ->
            { m with
                Sel = clamp 0 (max 0 (count - 1)) (m.Sel - 5)
                ReaderScroll = 0 },
            Cmd.none
        | ReaderPane ->
            { m with
                ReaderScroll = max 0 (m.ReaderScroll - 10) },
            Cmd.none
    | KPageDown ->
        match m.Pane with
        | ListPane ->
            { m with
                Sel = clamp 0 (max 0 (count - 1)) (m.Sel + 5)
                ReaderScroll = 0 },
            Cmd.none
        | ReaderPane ->
            { m with
                ReaderScroll = m.ReaderScroll + 10 },
            Cmd.none
    | _ -> m, Cmd.none

let private updatePanel (k: Key2) (ps: FeedsPanelState) (m: Model) : Model * Cmd<Msg> =
    let n = List.length m.Feeds

    let setCursor c =
        { m with
            Overlay = FeedsPanel { Cursor = c } }

    match k with
    | KEsc
    | KCtrlU -> { m with Overlay = NoOverlay }, Cmd.none
    | KUp
    | KChar "k" -> setCursor (max 0 (ps.Cursor - 1)), Cmd.none
    | KDown
    | KChar "j" -> setCursor (min n (ps.Cursor + 1)), Cmd.none
    | KChar "a" ->
        { m with
            Overlay =
                AddModal
                    { Input = ""
                      Error = None
                      Submitting = false } },
        Cmd.none
    | KEnter when ps.Cursor = 0 ->
        { m with
            Overlay =
                AddModal
                    { Input = ""
                      Error = None
                      Submitting = false } },
        Cmd.none
    | KChar "d"
    | KChar "x" ->
        if ps.Cursor >= 1 && ps.Cursor <= n then
            let url = (List.item (ps.Cursor - 1) m.Feeds).Url
            let feeds = m.Feeds |> List.filter (fun f -> f.Url <> url)

            { m with
                Feeds = feeds
                Selected = Set.remove url m.Selected
                Sel = 0
                Overlay = FeedsPanel { Cursor = clamp 0 (List.length feeds) ps.Cursor } },
            Cmd.none
        else
            m, Cmd.none
    | KChar "r" ->
        if ps.Cursor >= 1 && ps.Cursor <= n then
            let f = List.item (ps.Cursor - 1) m.Feeds

            { m with
                Feeds =
                    m.Feeds
                    |> List.map (fun x -> if x.Url = f.Url then { x with Status = FLoading } else x) },
            loadFeedCmd f.Url
        else
            m, Cmd.none
    | _ -> m, Cmd.none

let private updateAddModal (k: Key2) (ms: AddModalState) (m: Model) : Model * Cmd<Msg> =
    let setM s = { m with Overlay = AddModal s }

    match k with
    | KEsc ->
        { m with
            Overlay = FeedsPanel { Cursor = 0 } },
        Cmd.none
    | _ when ms.Submitting -> m, Cmd.none // ignore input while a fetch is in flight
    | KChar c ->
        setM
            { ms with
                Input = ms.Input + c
                Error = None },
        Cmd.none
    | KSpace -> m, Cmd.none // URLs carry no spaces
    | KBackspace ->
        let v = ms.Input

        setM
            { ms with
                Input = (if v = "" then "" else v.Substring(0, v.Length - 1))
                Error = None },
        Cmd.none
    | KEnter ->
        let url = ms.Input.Trim()

        if not (Feed.isValidUrl url) then
            setM { ms with Error = Some InvalidUrl }, Cmd.none
        elif m.Feeds |> List.exists (fun f -> f.Url = url) then
            setM { ms with Error = Some Duplicate }, Cmd.none
        else
            setM
                { ms with
                    Submitting = true
                    Error = None },
            addFetchCmd url
    | _ -> m, Cmd.none

let private updateFilter (k: Key2) (fs: FilterState) (m: Model) : Model * Cmd<Msg> =
    let urls = m.Feeds |> List.map (fun f -> f.Url)
    let n = List.length urls
    let setF s = { m with Overlay = Filter s }

    match k with
    | KEsc -> { m with Overlay = NoOverlay }, Cmd.none // cancel: Selected unchanged
    | KUp
    | KChar "k" ->
        setF
            { fs with
                Cursor = max 0 (fs.Cursor - 1) },
        Cmd.none
    | KDown
    | KChar "j" ->
        setF
            { fs with
                Cursor = clamp 0 (max 0 (n - 1)) (fs.Cursor + 1) },
        Cmd.none
    | KSpace ->
        match List.tryItem fs.Cursor urls with
        | Some url ->
            let checked' =
                if Set.contains url fs.Checked then
                    Set.remove url fs.Checked
                else
                    Set.add url fs.Checked

            setF { fs with Checked = checked' }, Cmd.none
        | None -> m, Cmd.none
    | KChar "a"
    | KChar "A" ->
        let checked' =
            if Set.count fs.Checked = n then
                Set.empty
            else
                Set.ofList urls

        setF { fs with Checked = checked' }, Cmd.none
    | KEnter ->
        // all-checked or none-checked ⇒ "show all" (Selected empty)
        let sel =
            if Set.isEmpty fs.Checked || Set.count fs.Checked = n then
                Set.empty
            else
                fs.Checked

        { m with
            Selected = sel
            Overlay = NoOverlay
            Sel = 0
            ReaderScroll = 0 },
        Cmd.none
    | _ -> m, Cmd.none

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | Resized s -> { m with Size = s }, Cmd.none
    | Spin -> { m with Spinner = m.Spinner + 1 }, Cmd.none
    | Ignore -> m, Cmd.none
    | FeedLoaded(url, title, items) ->
        let items' = setSourceFeed title items

        { m with
            Feeds =
                m.Feeds
                |> List.map (fun f ->
                    if f.Url = url then
                        { f with
                            Name = title
                            Articles = items'
                            Status = FReady }
                    else
                        f)
            Sel = 0 },
        Cmd.none
    | FeedLoadError(url, e) ->
        { m with
            Feeds =
                m.Feeds
                |> List.map (fun f -> if f.Url = url then { f with Status = FFailed e } else f) },
        Cmd.none
    | AddFetched(url, result) ->
        match m.Overlay with
        | AddModal ms ->
            match result with
            | Ok(title, items) ->
                let nf =
                    { Name = title
                      Url = url
                      AddedAt = DateTime.Now
                      Articles = setSourceFeed title items
                      Status = FReady }

                { m with
                    Feeds = nf :: m.Feeds // newest at top
                    Selected =
                        (if Set.isEmpty m.Selected then
                             m.Selected
                         else
                             Set.add url m.Selected)
                    Sel = 0
                    Overlay = FeedsPanel { Cursor = 1 } },
                Cmd.none
            | Error e ->
                { m with
                    Overlay =
                        AddModal
                            { ms with
                                Submitting = false
                                Error = Some(LoadFailedErr e) } },
                Cmd.none
        | _ -> m, Cmd.none // modal was dismissed mid-fetch
    | KeyMsg k ->
        match m.Overlay with
        | NoOverlay -> updateBase k m
        | FeedsPanel ps -> updatePanel k ps m
        | AddModal ms -> updateAddModal k ms m
        | Filter fs -> updateFilter k fs m

// view helpers -------------------------------------------------------------

/// Truncate to `width` columns (grapheme-aware), adding an ellipsis if cut.
let private truncate (width: int) (s0: string) : string =
    let s = s0.Replace("\n", " ").Trim()

    if Grapheme.stringWidth s <= width then
        s
    elif width <= 1 then
        "…"
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

/// Pad `s` with spaces to `width` columns (grapheme-aware), truncating if longer.
let private padTo (width: int) (s: string) : string =
    let t = truncate width s
    let pad = max 0 (width - Grapheme.stringWidth t)
    t + String(' ', pad)

/// A bordered panel with a one-line title row above its content.
let private panel (heading: string) (content: LayoutNode<Msg>) : LayoutNode<Msg> =
    Box.box
        sBorder
        [ Stack.vstackOf
              [ Stack.sized (Length.Cells 1) (Text.text (" " + heading) sAccent)
                Stack.sized Length.Fill content ] ]

let private statusText (m: Model) : string =
    let loading = m.Feeds |> List.filter (fun f -> f.Status = FLoading) |> List.length

    let failed =
        m.Feeds
        |> List.filter (fun f ->
            match f.Status with
            | FFailed _ -> true
            | _ -> false)
        |> List.length

    let total = m.Feeds |> List.length
    let articles = List.length (mergedArticles m)

    if loading > 0 then
        sprintf "%s loading %d/%d feeds" spinnerFrames.[m.Spinner % spinnerFrames.Length] loading total
    else
        let filt =
            if Set.isEmpty m.Selected then
                total
            else
                Set.count m.Selected

        let failTxt = if failed > 0 then sprintf " · %d failed" failed else ""
        sprintf "✓ %d/%d feeds · %d articles%s" filt total articles failTxt

let private feedMarker (m: Model) (f: Feed) : string =
    match f.Status with
    | FLoading -> spinnerFrames.[m.Spinner % spinnerFrames.Length]
    | FReady -> "✓"
    | FFailed _ -> "✗"

// overlays -----------------------------------------------------------------

let private feedsPanelLayer (m: Model) (ps: FeedsPanelState) (w: int) (h: int) : LayoutNode<Msg> =
    let panelH = clamp 6 h (h / 3)
    let listH = max 1 (panelH - 5) // border(2) + title + colheader + hint

    let colHeader = sprintf "  %-2s  %-24s %-12s %s" "st" "name" "added" "url"

    let rows =
        "  ＋ Add new feed"
        :: (m.Feeds
            |> List.map (fun f ->
                let name = if f.Name = "" then f.Url else f.Name

                sprintf "  %-2s  %s %-12s %s" (feedMarker m f) (padTo 24 name) (f.AddedAt.ToString("yyyy-MM-dd")) f.Url))

    let content =
        Stack.vstackOf
            [ Stack.sized (Length.Cells 1) (Text.text (sprintf " Feeds (%d)" (List.length m.Feeds)) sAccent)
              Stack.sized (Length.Cells 1) (Text.text colHeader sDim)
              Stack.sized Length.Fill (ListView.view listH sSel sRow ps.Cursor rows)
              Stack.sized (Length.Cells 1) (Text.text " Enter/a add · d delete · r reload · ↑/↓ move · Esc close" sDim) ]

    let box =
        LayoutNode.Overlay(rect0, [ Backdrop.solid sPanelBg; Box.box sBorder [ content ] ])

    Dock.dock [ Dock.top panelH box; Dock.fill Spacer.spacer ]

let private addModalLayer (m: Model) (ms: AddModalState) (w: int) (h: int) : LayoutNode<Msg> =
    let mw = clamp 30 70 (w - 8)
    let mh = 11

    let buttonLabel =
        if ms.Submitting then
            sprintf "[ %s Add ]" spinnerFrames.[m.Spinner % spinnerFrames.Length]
        else
            "[ Add ]"

    let errLine =
        match ms.Error with
        | Some InvalidUrl -> Text.text " ( ! ) Invalid URL" sErr
        | Some Duplicate -> Text.text " ( ! ) Feed already added" sErr
        | Some(LoadFailedErr _) -> Text.text " ( ! ) Could not load feed" sErr
        | None -> Spacer.spacer

    let body =
        Stack.vstackOf
            [ Stack.sized (Length.Cells 1) Spacer.spacer
              Stack.sized (Length.Cells 1) (Text.text " Feed URL:" sDim)
              Stack.sized (Length.Cells 1) (Text.text (" ❯ " + ms.Input + "▏") sBody)
              Stack.sized (Length.Cells 1) errLine
              Stack.sized (Length.Cells 1) Spacer.spacer
              Stack.sized (Length.Cells 1) (Text.text ("  " + buttonLabel) sAccent)
              Stack.sized (Length.Cells 1) (Text.text " Enter add · Esc cancel" sDim) ]

    Modal.modal sModalBg sBorder sAccent mw mh "Add feed" body

let private filterLayer (m: Model) (fs: FilterState) (w: int) (h: int) : LayoutNode<Msg> =
    let n = List.length m.Feeds
    let mw = clamp 30 70 (w - 8)
    let mh = clamp 7 (h - 2) (n + 5)
    let listH = max 1 (mh - 4)

    let rows =
        if n = 0 then
            [ " (no feeds)" ]
        else
            m.Feeds
            |> List.map (fun f ->
                let mark = if Set.contains f.Url fs.Checked then "[x]" else "[ ]"
                let name = if f.Name = "" then f.Url else f.Name
                sprintf " %s %s" mark name)

    let body =
        Stack.vstackOf
            [ Stack.sized Length.Fill (ListView.view listH sSel sRow fs.Cursor rows)
              Stack.sized (Length.Cells 1) (Text.text " Space toggle · a all · Enter apply · Esc cancel" sDim) ]

    Modal.modal sModalBg sBorder sAccent mw mh "Filter feeds" body

let view (m: Model) : LayoutNode<Msg> =
    let w = max 24 m.Size.Width
    let h = max 8 m.Size.Height
    let bodyH = max 1 (h - 4) // top header(3) + footer(1)
    let listW = clamp 26 46 (w / 3)
    let listInnerW = max 4 (listW - 2)
    let listInnerH = max 1 (bodyH - 3) // border(2) + title row(1)
    let readerInnerW = max 8 (w - listW - 2)
    let bodyW = max 4 (readerInnerW - 2) // body inset 1 cell each side
    let merged = mergedArticles m
    let count = List.length merged

    // header
    let header =
        Box.box sBorder [ Text.text (sprintf " Mire Feeds   %s" (statusText m)) sTitle ]

    // article list
    let labels = merged |> List.map (fun a -> truncate (listInnerW - 1) a.Title)
    let listBorder = if m.Pane = ListPane then "Articles ▸" else "Articles"

    let listPane =
        panel (sprintf "%s (%d)" listBorder count) (ListView.view listInnerH sSel sRow m.Sel labels)

    // reader pane
    let readerContent =
        match List.tryItem m.Sel merged with
        | None ->
            if anyLoading m then
                Text.text "  fetching…" sDim
            elif count = 0 then
                Text.text "  (no articles — press Ctrl+U to add a feed)" sDim
            else
                Text.text "  (no article selected)" sDim
        | Some a ->
            let titleLines = Feed.wrap readerInnerW a.Title
            let bodyLines = Feed.wrap bodyW a.Body |> List.map (fun l -> Text.text l sBody)

            Stack.vstackOf
                [ Stack.sized (Length.Cells(max 1 titleLines.Length)) (Text.text (String.concat "\n" titleLines) sTitle)
                  Stack.sized
                      (Length.Cells 1)
                      (Text.text (truncate readerInnerW (sprintf "from %s · %s" a.SourceFeed a.Date)) sDim)
                  Stack.sized
                      (Length.Cells 1)
                      (Text.text (truncate readerInnerW a.Link) (sDim.WithUnderline(UnderlineStyle.Single)))
                  Stack.sized (Length.Cells 1) (Text.text (String('─', readerInnerW)) sBorder)
                  Stack.sized
                      Length.Fill
                      (Dock.dock
                          [ Dock.left 1 Spacer.spacer
                            Dock.right 1 Spacer.spacer
                            Dock.fill (Scroll.vertical m.ReaderScroll (Stack.vstack bodyLines)) ]) ]

    let readerHeading = if m.Pane = ReaderPane then "Reading ▸" else "Reading"
    let readerPane = panel readerHeading readerContent

    let body =
        Stack.hstackOf
            [ Stack.sized (Length.Cells listW) listPane
              Stack.sized Length.Fill readerPane ]

    let footer =
        Text.text " ↑/↓ navigate · Tab pane · Enter read · Ctrl+U feeds · f filter · r reload · Ctrl+C quit" sDim

    let baseTree = Dock.dock [ Dock.top 3 header; Dock.bottom 1 footer; Dock.fill body ]

    match m.Overlay with
    | NoOverlay -> baseTree
    | FeedsPanel ps -> LayoutNode.Overlay(rect0, [ baseTree; feedsPanelLayer m ps w h ])
    | AddModal ms -> LayoutNode.Overlay(rect0, [ baseTree; addModalLayer m ms w h ])
    | Filter fs -> LayoutNode.Overlay(rect0, [ baseTree; filterLayer m fs w h ])

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        let k =
            match ke.Key with
            | Char c when ke.Modifiers.Ctrl && c = "u" -> Some KCtrlU
            | Char _ when ke.Modifiers.Ctrl -> None
            | Char c -> Some(KChar c)
            | Space -> Some KSpace
            | Backspace -> Some KBackspace
            | Enter -> Some KEnter
            | Escape -> Some KEsc
            | Tab -> Some KTab
            | ArrowUp -> Some KUp
            | ArrowDown -> Some KDown
            | PageUp -> Some KPageUp
            | PageDown -> Some KPageDown
            | _ -> None

        k |> Option.map KeyMsg
    | Resize sz -> Some(Resized sz)
    | _ -> None

let subscriptions (m: Model) : Sub<Msg> list =
    [ yield TerminalResize Resized
      if anyLoading m then
          yield Every(TimeSpan.FromMilliseconds 120.0, fun () -> Spin) ]

// ---------------------------------------------------------------------------
// Headless verification: seed a couple of feeds and print rendered cell grids
// for the base view + the feeds panel + the add modal + filter picker.
// `dotnet run --project Mire.FeedDemo -- --dump`
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

let private renderModel (size: Size) (label: string) (model: Model) =
    printfn "\n── %s ──" label
    let surface = Surface(size)
    Layout.measure (Rect.FromOrigin size) (view model) |> Layout.render surface
    printSurface surface

let private runDump () =
    let now = DateTime.Now

    let stub url name (arts: Article list) =
        { Name = name
          Url = url
          AddedAt = now
          Articles = arts |> List.map (fun a -> { a with SourceFeed = name })
          Status = FReady }

    let realFeed =
        match Feed.fetchAsync (List.head seedUrls) |> Async.RunSynchronously with
        | Ok(title, items) -> stub (List.head seedUrls) title items
        | Error e ->
            stub
                (List.head seedUrls)
                ("helgesver.re (offline: " + e + ")")
                [ { Title = "Why combining marks break in DomPDF"
                    Link = "https://helgesver.re/articles/…"
                    Date = "Mon, 18 May 2026 10:00:00 GMT"
                    Summary = "Non-shaping PDF engines render combining marks separately."
                    Body =
                      "Norwegian å becomes 'a' plus a drifting ring.\n\nThe fix: two passes of Unicode normalization."
                    SourceFeed = "" } ]

    let other =
        stub
            "https://hnrss.org/frontpage"
            "Hacker News"
            [ { Title = "Show HN: A retained-mode TUI runtime in F#"
                Link = "https://news.ycombinator.com/item?id=1"
                Date = "Tue, 19 May 2026 09:00:00 GMT"
                Summary = "Elmish for the terminal."
                Body =
                  "A small framework targeting Kitty-compatible terminals.\n\nDiff-based rendering, layout nodes, Elmish loop."
                SourceFeed = "" } ]

    let size = Size.Create(104, 30)

    let model =
        { (fst (init ())) with
            Feeds = [ realFeed; other ]
            Sel = 0
            Size = size }

    printfn
        "Mire.FeedDemo — %d feeds, %d merged articles"
        (List.length model.Feeds)
        (List.length (mergedArticles model))

    renderModel size "stream (base view)" model

    renderModel
        size
        "feeds panel (Ctrl+U)"
        { model with
            Overlay = FeedsPanel { Cursor = 0 } }

    renderModel
        size
        "add modal"
        { model with
            Overlay =
                AddModal
                    { Input = "https://example.com/feed.xml"
                      Error = None
                      Submitting = false } }

    renderModel
        size
        "add modal — error"
        { model with
            Overlay =
                AddModal
                    { Input = "notaurl"
                      Error = Some InvalidUrl
                      Submitting = false } }

    renderModel
        size
        "filter picker (f)"
        { model with
            Overlay =
                Filter
                    { Checked = Set.ofList [ realFeed.Url ]
                      Cursor = 0 } }

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
