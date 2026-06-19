namespace Mire.Agent

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// Status of a tool call. (Named to avoid clashing with FSharp.Core's `Ok`.)
type ToolStatus =
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

/// Renders a transcript (a list of `TranscriptBlock`s) to layout nodes. Pure —
/// the app owns the block list, the spinner tick, and the scroll offset; wrap the
/// result in a `ScrollView` for scrolling/follow-tail.
module ChatTranscript =

    /// The glyph for a tool status at the given spinner tick.
    let statusGlyph (frame: int) (s: ToolStatus) =
        match s with
        | Running -> Spinner.frameOf Spinner.braille frame
        | Succeeded -> "✓"
        | Failed -> "✗"

    let statusStyle (theme: AppTheme) (s: ToolStatus) =
        match s with
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
        | Thinking s ->
            card
                theme.border
                [ Text.text "thinking" theme.fgSubtle
                  Markdown.wrap theme.markdown (wrapWidth - 2) (theme.fg.WithItalic(true)) s ]
        | ToolCall(name, cmd, status, meta, output) ->
            let statusText =
                match status with
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
        | FileTree paths ->
            card
                theme.border
                (Text.text "workspace" theme.fgSubtle
                 :: (paths |> List.map (fun p -> Text.text p theme.fgMuted)))
        | TaskTimeline items ->
            card
                theme.border
                (Text.text "tasks" theme.fgSubtle
                 :: (items
                     |> List.map (fun (n, s) ->
                         Text.text (sprintf " %s %s" (statusGlyph frame s) n) (statusStyle theme s))))
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
    // measure from "re-wrap every block" into a cache lookup — the append-only /
    // wrap-caching win for long, streaming transcripts. Bounded so unique lines can't
    // grow it without limit.
    let private heightCache =
        System.Collections.Concurrent.ConcurrentDictionary<struct (int * TranscriptBlock), int>()

    let private heightCacheCap = 8192

    /// Test/diagnostic hook: how many (wrapWidth, block) heights are memoized.
    let heightCacheSize () = heightCache.Count

    /// One block's rendered row height — the exact `contentExtent` the layout uses,
    /// so scroll clamping matches what's drawn. Memoized by (wrapWidth, block).
    let blockHeight (theme: AppTheme) (wrapWidth: int) (frame: int) (b: TranscriptBlock) : int =
        let key = struct (wrapWidth, b)

        match heightCache.TryGetValue key with
        | true, h -> h
        | _ ->
            let h = Layout.contentExtent Direction.Vertical (renderBlock theme wrapWidth frame b)

            if heightCache.Count < heightCacheCap then
                heightCache.[key] <- h

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

    /// The live transcript view: a `viewportH`-tall scroll region at `offset` with a
    /// track/thumb scrollbar. **Virtualized** — only the blocks intersecting the
    /// viewport are built into the scroll node (the `Scroll` primitive otherwise
    /// renders the whole transcript onto an off-screen surface every frame), while
    /// the scrollbar reflects the true content height/offset. The app owns `offset`
    /// (clamp with `clampOffset`, follow the tail by holding it at `toBottom`).
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
        // Measure every block from the memo (cheap) — but only *render* the blocks
        // that intersect the viewport, so a long transcript costs O(visible), not
        // O(all), of the expensive wrap/build work each frame.
        let blocksArr = List.toArray blocks
        let heights = blocksArr |> Array.map (blockHeight theme wrapWidth frame)

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
            [ for i in 0 .. blocksArr.Length - 1 do
                  if tops.[i] + heights.[i] > clamped && tops.[i] < visBot then
                      yield i ]

        let bar = ScrollView.scrollbar viewportH contentH clamped trackStyle thumbStyle

        let body =
            match visible with
            | [] -> Spacer.spacer
            | first :: _ ->
                let windowContent =
                    visible
                    |> List.map (fun i -> Stack.sized Length.Content (renderBlock theme wrapWidth frame blocksArr.[i]))
                    |> Stack.vstackOf
                // local offset = how far the first visible block is scrolled off the top
                Scroll.vertical (clamped - tops.[first]) windowContent

        Stack.hstackOf [ Stack.sized Length.Fill body; Stack.sized (Length.Cells 1) bar ]
