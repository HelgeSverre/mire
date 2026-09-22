namespace Mire.Agent

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// Status of a tool call, in lifecycle order. (Named to avoid clashing with
/// FSharp.Core's `Ok` and `DiffView`'s `HunkStatus.Pending`.) `Queued` = awaiting
/// approval / not yet started.
type ToolStatus =
    | Queued
    | Running
    | Succeeded
    | Failed

type DiffLine =
    { Sign: char // '+', '-', or ' '
      Text: string }

/// A block in an agent chat transcript. Each renders to a self-contained,
/// Content-sized card/section built from `Mire` layout primitives, styled by a
/// `Mire.Widgets.AppTheme` — so the agent layer depends only on the framework,
/// never on a specific app's theme module.
type TranscriptBlock =
    | UserMsg of string
    | AssistantMd of string
    | Thinking of string
    | ToolCall of name: string * cmd: string * status: ToolStatus * meta: string * output: string
    | DiffBlock of file: string * lines: DiffLine list
    | TableBlock of headers: string list * rows: string list list
    | ErrorBlock of string
    | Notice of AppTheme.Tone * string
    | FileTree of string list
    | TaskTimeline of (string * ToolStatus) list
    | PlanBlock of (bool * string) list

/// A session-owned render cache for a transcript whose blocks have stable keys
/// (e.g. `Conversation`'s `MessageId`s). For each (key, wrap width) it keeps only
/// the *latest* revision of the block — its measured height and, when the block
/// has no animated content, its built layout node. When a streamed message grows,
/// its entry is replaced, so memory is bounded by one revision per message instead
/// of every streamed prefix; an unchanged block costs a reference check per frame.
/// Not thread-safe: use it from the runtime thread (view/update), one per session.
type TranscriptCache() =
    let entries =
        System.Collections.Generic.Dictionary<struct (int * int), TranscriptBlock * int * obj * obj>()

    /// Number of (key, width) entries held.
    member _.Count = entries.Count

    /// Drop everything (e.g. after clearing the conversation).
    member _.Clear() = entries.Clear()

    /// Remove entries whose key isn't in `live` (messages deleted from the model).
    member _.Retain(live: System.Collections.Generic.HashSet<int>) =
        let dead =
            [ for KeyValue(struct (k, w), _) in entries do
                  if not (live.Contains k) then
                      yield struct (k, w) ]

        for d in dead do
            entries.Remove d |> ignore

    /// The cached (height, node, theme) for `key` at `width` if the stored block
    /// is still `block`.
    member internal _.TryGet(key: int, width: int, block: TranscriptBlock) =
        match entries.TryGetValue(struct (key, width)) with
        | true, (b, h, node, theme) when obj.ReferenceEquals(b, block) || b = block -> Some(h, node, theme)
        | _ -> None

    member internal _.Set(key: int, width: int, block: TranscriptBlock, height: int, node: obj, theme: obj) =
        entries.[struct (key, width)] <- (block, height, node, theme)

/// Renders a transcript (a list of `TranscriptBlock`s) to layout nodes. Pure —
/// the app owns the block list, the spinner tick, and the scroll offset; wrap the
/// result in a `ScrollView` for scrolling/follow-tail.
module ChatTranscript =

    /// The glyph for a tool status at the given spinner tick.
    let statusGlyph (frame: int) (s: ToolStatus) =
        match s with
        | Queued -> "○"
        | Running -> Spinner.frameOf Spinner.braille frame
        | Succeeded -> "✓"
        | Failed -> "✗"

    let statusStyle (theme: AppTheme) (s: ToolStatus) =
        match s with
        | Queued -> theme.fgSubtle
        | Running -> theme.fgMuted
        | Succeeded -> theme.success
        | Failed -> theme.danger

    /// A bordered card. Box children share one inner rect (they overlap), so the
    /// content MUST be a single child — here a vstack of the supplied rows.
    let private card (border: Style) (rows: LayoutNode<'msg> list) : LayoutNode<'msg> =
        Box.box border [ Stack.vstack rows ]

    /// A header row with a left label and a right-aligned status, separated by a
    /// `Stack.flex` spacer that pushes them to opposite ends.
    let private headerRow (left: LayoutNode<'msg>) (right: LayoutNode<'msg>) : LayoutNode<'msg> =
        Stack.hstackOf
            [ Stack.sized Length.Content left
              Stack.flex
              Stack.sized Length.Content right ]

    let private padRight (w: int) (s: string) =
        let sw = Grapheme.stringWidth s
        if sw >= w then s else s + System.String(' ', w - sw)

    let private cellTone (theme: AppTheme) (v: string) =
        match v.ToLowerInvariant() with
        | "done"
        | "ok"
        | "ready"
        | "passed" -> theme.success
        | "open"
        | "todo"
        | "pending"
        | "queued" -> theme.warning
        | "fail"
        | "failed"
        | "error" -> theme.danger
        | _ -> theme.fg

    // --- first-class block widgets ------------------------------------------
    // The same renderings `renderBlock` produces, exposed as standalone widgets so
    // they compose outside a transcript too (in a panel, a sidebar, an overlay).

    /// A tool call: a bordered card with a `name · command` header, a right-aligned
    /// status (spinner while `Running`), and any output lines below.
    let toolCallView
        (theme: AppTheme)
        (frame: int)
        (name: string)
        (cmd: string)
        (status: ToolStatus)
        (meta: string)
        (output: string)
        : LayoutNode<'msg> =
        let statusText =
            match status with
            | Queued -> "○ queued"
            | Running -> sprintf "%s running…" (statusGlyph frame Running)
            | Succeeded -> sprintf "✓ %s" meta
            | Failed -> sprintf "✗ %s" meta

        let header =
            headerRow
                (Text.text (sprintf "%s · %s" name cmd) theme.fgMuted)
                (Text.text statusText (statusStyle theme status))

        let outLines =
            if output = "" then
                []
            else
                output.Split('\n')
                |> Array.toList
                |> List.map (fun l -> Text.text l theme.fgSubtle)

        card theme.border (header :: outLines)

    /// A "thinking" card — italic, wrapped markdown.
    let thinkingView (theme: AppTheme) (wrapWidth: int) (text: string) : LayoutNode<'msg> =
        card
            theme.border
            [ Text.text "thinking" theme.fgSubtle
              Markdown.wrap theme.markdown (wrapWidth - 2) (theme.fg.WithItalic(true)) text ]

    /// A workspace file-tree card (static paths).
    let fileTreeView (theme: AppTheme) (paths: string list) : LayoutNode<'msg> =
        card
            theme.border
            (Text.text "workspace" theme.fgSubtle
             :: (paths |> List.map (fun p -> Text.text p theme.fgMuted)))

    /// A task-timeline card — each task with its status glyph/colour (spinner while running).
    let taskTimelineView (theme: AppTheme) (frame: int) (items: (string * ToolStatus) list) : LayoutNode<'msg> =
        card
            theme.border
            (Text.text "tasks" theme.fgSubtle
             :: (items
                 |> List.map (fun (n, s) -> Text.text (sprintf " %s %s" (statusGlyph frame s) n) (statusStyle theme s))))

    /// Render one block to a Content-sized node. `wrapWidth` is the transcript
    /// inner width; `frame` drives the running-tool spinner.
    let renderBlock (theme: AppTheme) (wrapWidth: int) (frame: int) (block: TranscriptBlock) : LayoutNode<'msg> =
        match block with
        | UserMsg s ->
            Stack.vstack
                [ Text.text "❯ you" theme.fgSubtle
                  Markdown.wrap theme.markdown wrapWidth theme.fg s ]
        | AssistantMd s ->
            Stack.vstack
                [ Text.text "◆ assistant" theme.fgSubtle
                  Markdown.render theme.markdown wrapWidth s ]
        | Thinking s -> thinkingView theme wrapWidth s
        | ToolCall(name, cmd, status, meta, output) -> toolCallView theme frame name cmd status meta output
        | DiffBlock(file, lines) ->
            let body =
                lines
                |> List.map (fun dl ->
                    let style =
                        match dl.Sign with
                        | '+' -> theme.success
                        | '-' -> theme.danger
                        | _ -> theme.fgSubtle

                    Text.text (sprintf "%c %s" dl.Sign dl.Text) style)

            card theme.border (Text.text file theme.fgSubtle :: body)
        | TableBlock(headers, rows) ->
            let cols = headers.Length

            let widths =
                Array.init cols (fun c ->
                    let hw = Grapheme.stringWidth headers.[c]

                    let rw =
                        rows
                        |> List.fold
                            (fun mx r ->
                                if c < r.Length then
                                    max mx (Grapheme.stringWidth r.[c])
                                else
                                    mx)
                            0

                    (max hw rw) + 2)

            let cellOf (r: string list) (c: int) = if c < r.Length then r.[c] else ""

            let columns: Column<string list, 'msg> list =
                headers
                |> List.mapi (fun c h ->
                    { Header = padRight widths.[c] h
                      Width = Length.Cells widths.[c]
                      Render = fun r -> Text.text (padRight widths.[c] (cellOf r c)) (cellTone theme (cellOf r c)) })

            card theme.border [ Table.view (List.length rows) theme.fgSubtle theme.fg 0 (fun _ -> false) columns rows ]
        | ErrorBlock s ->
            card
                theme.danger
                [ Text.text "error" theme.danger
                  Markdown.wrap theme.markdown (wrapWidth - 2) theme.fg s ]
        | Notice(tone, s) ->
            let toneStyle = AppTheme.toneStyle theme tone

            card
                toneStyle
                [ Text.text "notice" toneStyle
                  Markdown.wrap theme.markdown (wrapWidth - 2) theme.fgMuted s ]
        | FileTree paths -> fileTreeView theme paths
        | TaskTimeline items -> taskTimelineView theme frame items
        | PlanBlock steps ->
            card
                theme.border
                (Text.text "plan" theme.fgSubtle
                 :: (steps
                     |> List.map (fun (d, t) ->
                         Text.text
                             (sprintf "%s %s" (if d then "[x]" else "[ ]") t)
                             (if d then theme.fgSubtle else theme.fg))))

    /// Render a whole transcript as a Content-sized vstack of block nodes (newest
    /// last). The app wraps this in a `ScrollView` and owns the offset/follow-tail.
    /// (`view` does the wrapping + virtualization; prefer it for the live UI.)
    let render (theme: AppTheme) (wrapWidth: int) (frame: int) (blocks: TranscriptBlock list) : LayoutNode<'msg> =
        blocks
        |> List.map (fun b -> Stack.sized Length.Content (renderBlock theme wrapWidth frame b))
        |> Stack.vstackOf

    // --- scroll math + follow-tail (the offset stays app-owned MVU state) ------

    // A block's rendered height depends only on (wrapWidth, block) — not on the
    // theme (colours don't change row counts) nor the `frame` tick (the spinner glyph
    // is one cell). So it's safe to memoize, which turns the per-frame transcript
    // measure from "re-wrap every block" into a cache lookup.
    //
    // This shared, key-by-content cache can't tell a streaming message's revisions
    // apart (every streamed prefix is a distinct key), so it's bounded by *retained
    // text* as well as entry count, and when either bound is hit it's cleared — the
    // live working set re-populates it within a frame, instead of the cache freezing
    // full of stale prefixes. Sessions should prefer a `TranscriptCache` (keyed by
    // message id, one revision per message) via the `*Keyed` functions.
    let private heightCache =
        System.Collections.Concurrent.ConcurrentDictionary<struct (int * TranscriptBlock), int>()

    let private heightCacheCap = 8192
    let private heightCacheCharCap = 4 * 1024 * 1024
    let mutable private heightCacheChars = 0

    /// Approximate retained size of a block: the total length of its strings.
    let blockChars (b: TranscriptBlock) : int =
        match b with
        | UserMsg s
        | AssistantMd s
        | Thinking s
        | ErrorBlock s -> s.Length
        | Notice(_, s) -> s.Length
        | ToolCall(n, c, _, m, o) -> n.Length + c.Length + m.Length + o.Length
        | DiffBlock(f, lines) -> f.Length + (lines |> List.sumBy (fun l -> l.Text.Length + 1))
        | TableBlock(h, rows) ->
            (h |> List.sumBy String.length)
            + (rows |> List.sumBy (List.sumBy String.length))
        | FileTree ps -> ps |> List.sumBy String.length
        | TaskTimeline items -> items |> List.sumBy (fun (n, _) -> n.Length + 1)
        | PlanBlock steps -> steps |> List.sumBy (fun (_, t) -> t.Length + 1)

    /// Test/diagnostic hook: how many (wrapWidth, block) heights are memoized.
    let heightCacheSize () = heightCache.Count

    /// Test/diagnostic hook: approximate text retained by the shared height cache.
    let heightCacheRetainedChars () = heightCacheChars

    /// Test hook: is `(wrapWidth, block)` memoized? Race-free under parallel tests
    /// (unlike observing `heightCacheSize` deltas against a shared global cache).
    let heightCacheContains (wrapWidth: int) (block: TranscriptBlock) =
        heightCache.ContainsKey(struct (wrapWidth, block))

    /// Measure a block's height directly (no cache).
    let measureHeight (theme: AppTheme) (wrapWidth: int) (frame: int) (b: TranscriptBlock) : int =
        Layout.contentExtent Direction.Vertical (renderBlock theme wrapWidth frame b)

    /// One block's rendered row height — the exact `contentExtent` the layout uses,
    /// so scroll clamping matches what's drawn. Memoized by (wrapWidth, block) in a
    /// shared cache bounded by entries and retained text.
    let blockHeight (theme: AppTheme) (wrapWidth: int) (frame: int) (b: TranscriptBlock) : int =
        let key = struct (wrapWidth, b)

        match heightCache.TryGetValue key with
        | true, h -> h
        | _ ->
            let h = measureHeight theme wrapWidth frame b
            let chars = blockChars b

            lock heightCache (fun () ->
                if
                    heightCache.Count >= heightCacheCap
                    || heightCacheChars + chars > heightCacheCharCap
                then
                    heightCache.Clear()
                    heightCacheChars <- 0

                if chars <= heightCacheCharCap && heightCache.TryAdd(key, h) then
                    heightCacheChars <- heightCacheChars + chars)

            h

    /// Each block's rendered row height. O(n) lookups via the memo, so an unchanged
    /// block isn't re-wrapped every frame.
    let blockHeights (theme: AppTheme) (wrapWidth: int) (frame: int) (blocks: TranscriptBlock list) : int list =
        blocks |> List.map (blockHeight theme wrapWidth frame)

    /// Total transcript height in rows.
    let contentHeight (theme: AppTheme) (wrapWidth: int) (frame: int) (blocks: TranscriptBlock list) : int =
        blockHeights theme wrapWidth frame blocks |> List.sum

    /// The follow-tail / jump-to-bottom offset (scrolled all the way down).
    let toBottom (theme: AppTheme) (wrapWidth: int) (frame: int) (viewportH: int) (blocks: TranscriptBlock list) : int =
        ScrollView.toBottom viewportH (contentHeight theme wrapWidth frame blocks)

    /// Clamp an app-held offset to the transcript's scroll range.
    let clampOffset
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (viewportH: int)
        (offset: int)
        (blocks: TranscriptBlock list)
        : int =
        ScrollView.clampOffset viewportH (contentHeight theme wrapWidth frame blocks) offset

    /// True when `offset` is at the bottom — keep it true to follow the tail.
    let atBottom
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (viewportH: int)
        (offset: int)
        (blocks: TranscriptBlock list)
        : bool =
        ScrollView.atBottom viewportH (contentHeight theme wrapWidth frame blocks) offset

    /// True if the block's rendering depends on the spinner `frame` (a running
    /// tool / task) — such nodes are rebuilt every frame, never cached.
    let private isAnimated (b: TranscriptBlock) =
        match b with
        | ToolCall(_, _, Running, _, _) -> true
        | TaskTimeline items -> items |> List.exists (fun (_, s) -> s = Running)
        | _ -> false

    /// A block's height via a session `TranscriptCache` (latest revision per key).
    let keyedHeight
        (cache: TranscriptCache)
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (key: int)
        (b: TranscriptBlock)
        : int =
        match cache.TryGet(key, wrapWidth, b) with
        | Some(h, _, _) -> h
        | None ->
            let node: LayoutNode<unit> = renderBlock theme wrapWidth frame b
            let h = Layout.contentExtent Direction.Vertical node
            cache.Set(key, wrapWidth, b, h, null, null)
            h

    /// A block's layout node via the cache (rebuilt when the block, theme, or —
    /// for animated blocks — the frame changes).
    let private keyedNode
        (cache: TranscriptCache)
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (key: int)
        (b: TranscriptBlock)
        : LayoutNode<'msg> =
        let build () = renderBlock theme wrapWidth frame b

        if isAnimated b then
            build ()
        else
            match cache.TryGet(key, wrapWidth, b) with
            | Some(h, (:? LayoutNode<'msg> as node), t) when obj.ReferenceEquals(t, theme) -> node
            | cached ->
                let node = build ()

                let h =
                    match cached with
                    | Some(h, _, _) -> h
                    | None -> Layout.contentExtent Direction.Vertical node

                cache.Set(key, wrapWidth, b, h, box node, box theme)
                node

    /// Total height of a keyed transcript (see `TranscriptCache`).
    let contentHeightKeyed
        (cache: TranscriptCache)
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (items: (int * TranscriptBlock) list)
        : int =
        items |> List.sumBy (fun (k, b) -> keyedHeight cache theme wrapWidth frame k b)

    /// The follow-tail offset of a keyed transcript.
    let toBottomKeyed
        (cache: TranscriptCache)
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (viewportH: int)
        (items: (int * TranscriptBlock) list)
        : int =
        ScrollView.toBottom viewportH (contentHeightKeyed cache theme wrapWidth frame items)

    /// Shared body of `view`/`viewKeyed`: heights for every block (cheap, cached),
    /// nodes only for the blocks intersecting the viewport.
    let private viewWith
        (heightOf: int -> int)
        (nodeOf: int -> LayoutNode<'msg>)
        (count: int)
        (viewportH: int)
        (offset: int)
        (trackStyle: Style)
        (thumbStyle: Style)
        : LayoutNode<'msg> =
        let heights = Array.init count heightOf
        let contentH = Array.sum heights
        let clamped = ScrollView.clampOffset viewportH contentH offset

        // cumulative top of each block
        let tops = Array.zeroCreate heights.Length
        let mutable acc = 0

        for i in 0 .. heights.Length - 1 do
            tops.[i] <- acc
            acc <- acc + heights.[i]

        // blocks intersecting [clamped, clamped + viewportH)
        let visBot = clamped + viewportH

        let visible =
            [ for i in 0 .. count - 1 do
                  if tops.[i] + heights.[i] > clamped && tops.[i] < visBot then
                      yield i ]

        let bar = ScrollView.scrollbar viewportH contentH clamped trackStyle thumbStyle

        let body =
            match visible with
            | [] -> Spacer.spacer
            | first :: _ ->
                // Each visible block is sized to its (cached) height, so the scroll's
                // content extent is exact without re-measuring. The scroll paints
                // straight into the viewport, so rows of a huge block that are off
                // screen are skipped rather than drawn to an off-screen surface.
                let windowContent =
                    visible
                    |> List.map (fun i -> Stack.sized (Length.Cells heights.[i]) (nodeOf i))
                    |> Stack.vstackOf
                // local offset = how far the first visible block is scrolled off the top
                Scroll.vertical (clamped - tops.[first]) windowContent

        Stack.hstackOf [ Stack.sized Length.Fill body; Stack.sized (Length.Cells 1) bar ]

    /// The live transcript view: a `viewportH`-tall scroll region at `offset` with a
    /// track/thumb scrollbar. **Virtualized** — only the blocks intersecting the
    /// viewport are built into the scroll node, while the scrollbar reflects the
    /// true content height/offset. The app owns `offset` (clamp with `clampOffset`,
    /// follow the tail by holding it at `toBottom`, or pass `Int32.MaxValue`, which
    /// clamps to the bottom).
    let view
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (viewportH: int)
        (offset: int)
        (trackStyle: Style)
        (thumbStyle: Style)
        (blocks: TranscriptBlock list)
        : LayoutNode<'msg> =
        let arr = List.toArray blocks

        viewWith
            (fun i -> blockHeight theme wrapWidth frame arr.[i])
            (fun i -> renderBlock theme wrapWidth frame arr.[i])
            arr.Length
            viewportH
            offset
            trackStyle
            thumbStyle

    /// `view` over keyed blocks with a session `TranscriptCache`: heights *and*
    /// built nodes are reused across frames (one revision per key), so an
    /// unchanged message costs a lookup and a streaming one is rebuilt only when
    /// its text changed.
    let viewKeyed
        (cache: TranscriptCache)
        (theme: AppTheme)
        (wrapWidth: int)
        (frame: int)
        (viewportH: int)
        (offset: int)
        (trackStyle: Style)
        (thumbStyle: Style)
        (items: (int * TranscriptBlock) list)
        : LayoutNode<'msg> =
        let arr = List.toArray items

        viewWith
            (fun i -> keyedHeight cache theme wrapWidth frame (fst arr.[i]) (snd arr.[i]))
            (fun i -> keyedNode cache theme wrapWidth frame (fst arr.[i]) (snd arr.[i]))
            arr.Length
            viewportH
            offset
            trackStyle
            thumbStyle
