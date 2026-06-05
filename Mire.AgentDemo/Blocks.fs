namespace Mire.AgentDemo

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

/// The agent-domain transcript blocks. These live in the demo, NOT the framework —
/// `CLAUDE.md`/`ROADMAP.md` keep agent concepts out of the core libraries. Each block
/// renders to a self-contained, Content-sized card built from layout primitives.
type Block =
    | UserMsg of string
    | AssistantMd of string
    | Thinking of string
    | ToolCall of name: string * cmd: string * status: ToolStatus * meta: string * output: string
    | DiffBlock of file: string * lines: DiffLine list
    | TableBlock of headers: string list * rows: string list list
    | ErrorBlock of string
    | Notice of Theme.Tone * string
    | FileTree of string list
    | TaskTimeline of (string * ToolStatus) list
    | PlanBlock of (bool * string) list

module Blocks =

    let spinnerFrames = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]

    let spinner (frame: int) =
        spinnerFrames.[((frame % spinnerFrames.Length) + spinnerFrames.Length) % spinnerFrames.Length]

    let statusGlyph (frame: int) (s: ToolStatus) =
        match s with
        | Running -> spinner frame
        | Succeeded -> "✓"
        | Failed -> "✗"

    let statusStyle (s: ToolStatus) =
        match s with
        | Running -> Theme.muted
        | Succeeded -> Theme.okStyle
        | Failed -> Theme.errStyle

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

    let private cellTone (v: string) =
        match v.ToLowerInvariant() with
        | "done"
        | "ok"
        | "ready"
        | "passed" -> Theme.okStyle
        | "open"
        | "todo"
        | "pending"
        | "queued" -> Theme.warnStyle
        | "fail"
        | "failed"
        | "error" -> Theme.errStyle
        | _ -> Theme.text

    /// Render one block to a Content-sized node. `wrapWidth` is the transcript inner
    /// width; `frame` drives the running-tool spinner.
    let renderBlock (wrapWidth: int) (frame: int) (block: Block) : LayoutNode<'msg> =
        match block with
        | UserMsg s -> Stack.vstack [ Text.text "❯ you" Theme.subtle; Markdown.wrap wrapWidth Theme.text s ]
        | AssistantMd s -> Stack.vstack [ Text.text "◆ assistant" Theme.subtle; Markdown.render wrapWidth s ]
        | Thinking s ->
            card
                Theme.borderStyle
                [ Text.text "thinking" Theme.subtle
                  Markdown.wrap (wrapWidth - 2) Theme.italic s ]
        | ToolCall(name, cmd, status, meta, output) ->
            let statusText =
                match status with
                | Running -> sprintf "%s running…" (spinner frame)
                | Succeeded -> sprintf "✓ %s" meta
                | Failed -> sprintf "✗ %s" meta

            let header =
                headerRow
                    (Text.text (sprintf "%s · %s" name cmd) Theme.muted)
                    (Text.text statusText (statusStyle status))

            let outLines =
                if output = "" then
                    []
                else
                    output.Split('\n')
                    |> Array.toList
                    |> List.map (fun l -> Text.text l Theme.subtle)

            card Theme.borderStyle (header :: outLines)
        | DiffBlock(file, lines) ->
            let body =
                lines
                |> List.map (fun dl ->
                    let style =
                        match dl.Sign with
                        | '+' -> Theme.okStyle
                        | '-' -> Theme.errStyle
                        | _ -> Theme.subtle

                    Text.text (sprintf "%c %s" dl.Sign dl.Text) style)

            card Theme.borderStyle (Text.text file Theme.subtle :: body)
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

            let cellNode (c: int) (v: string) (style: Style) =
                Stack.sized (Length.Cells widths.[c]) (Text.text (padRight widths.[c] v) style)

            let headerNode =
                Stack.hstackOf (headers |> List.mapi (fun c h -> cellNode c h Theme.subtle))

            let rowNode (r: string list) =
                Stack.hstackOf (r |> List.mapi (fun c v -> cellNode c v (cellTone v)))

            card Theme.borderStyle (headerNode :: (rows |> List.map rowNode))
        | ErrorBlock s ->
            card Theme.errStyle [ Text.text "error" Theme.errStyle; Markdown.wrap (wrapWidth - 2) Theme.text s ]
        | Notice(tone, s) ->
            card
                (Theme.toneStyle tone)
                [ Text.text "notice" (Theme.toneStyle tone)
                  Markdown.wrap (wrapWidth - 2) Theme.muted s ]
        | FileTree paths ->
            card
                Theme.borderStyle
                (Text.text "workspace" Theme.subtle
                 :: (paths |> List.map (fun p -> Text.text p Theme.muted)))
        | TaskTimeline items ->
            card
                Theme.borderStyle
                (Text.text "tasks" Theme.subtle
                 :: (items
                     |> List.map (fun (n, s) -> Text.text (sprintf " %s %s" (statusGlyph frame s) n) (statusStyle s))))
        | PlanBlock steps ->
            card
                Theme.borderStyle
                (Text.text "plan" Theme.subtle
                 :: (steps
                     |> List.map (fun (d, t) ->
                         Text.text
                             (sprintf "%s %s" (if d then "[x]" else "[ ]") t)
                             (if d then Theme.subtle else Theme.text))))
