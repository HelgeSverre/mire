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
    let render (theme: AppTheme) (wrapWidth: int) (frame: int) (blocks: TranscriptBlock list) : LayoutNode<'msg> =
        blocks
        |> List.map (fun b -> Stack.sized Length.Content (renderBlock theme wrapWidth frame b))
        |> Stack.vstackOf
