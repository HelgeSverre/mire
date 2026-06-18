// Mire.Agent MVP — a minimal coding-agent shell (SPEC.md's headline example).
// Composes the agent layer — ChatTranscript + PromptBox + ApprovalModal — on the
// framework's default brand theme, with NO app-specific theme code. Type a
// message and press Enter; type `run` to trigger a tool-approval modal
// (←/→ choose, Enter confirms, Esc denies). Ctrl+C quits. `-- --dump` is headless.

open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.Widgets
open Mire.Agent
open Mire.App

/// The whole app theme is the framework default — an agent app needs none of its own.
let private theme = AppTheme.defaultTheme

// ── model ──────────────────────────────────────────────────────────────────
type Model =
    {
        Transcript: TranscriptBlock list
        Prompt: PromptBox
        /// An open approval prompt: the command awaiting accept/deny + which button is focused.
        Approval: (string * bool) option
        /// Hunks for the diff reviewer (their `Status` is mutated by accept/reject).
        Hunks: DiffHunk list
        /// An open diff reviewer: the render mode + the selected hunk index.
        Diff: (DiffMode * int) option
        Offset: int
        Size: Size
    }

type Msg =
    | Edit of InputEvent
    | Submit
    | EscKey
    | ToggleButton
    | Resized of Size

let private greeting =
    AssistantMd
        "Welcome to the **Mire** agent shell. Type a message and press Enter — `run` triggers a tool approval, `diff` opens the diff reviewer."

/// A canned change set for the `diff` reviewer.
let private sampleHunks =
    [ { Header = "@@ src/App.fs"
        Status = Pending
        Lines =
          [ { Sign = ' '; Text = "let view m =" }
            { Sign = '-'; Text = "    text \"hi\"" }
            { Sign = '+'
              Text = "    title \"hi\"" }
            { Sign = ' '; Text = "    |> render" } ] }
      { Header = "@@ src/Theme.fs"
        Status = Pending
        Lines =
          [ { Sign = '+'
              Text = "let accent = emerald" } ] } ]

let init () =
    { Transcript = [ greeting ]
      Prompt = PromptBox.empty
      Approval = None
      Hunks = sampleHunks
      Diff = None
      Offset = 0
      Size =
        Mire.Protocol.TerminalMode.getTerminalSize ()
        |> Option.defaultValue (Size.Create(80, 24)) },
    Cmd.none

// ── update ─────────────────────────────────────────────────────────────────
let private wrapWidth (m: Model) = max 10 (m.Size.Width - 4)
let private viewportRows (m: Model) = max 1 (m.Size.Height - 8) // header(3) + prompt(3) + transcript box border(2)

let private contentHeight (m: Model) =
    m.Transcript
    |> List.sumBy (fun b ->
        Layout.contentExtent Direction.Vertical (ChatTranscript.renderBlock theme (wrapWidth m) 0 b))

let private maxScroll (m: Model) =
    max 0 (contentHeight m - viewportRows m)

let private followTail (m: Model) = { m with Offset = maxScroll m }

let private submit (m: Model) =
    let text = (PromptBox.value m.Prompt).Trim()

    if text = "" then
        m
    elif text = "run" then
        // a tool wants to run a command → open the approval modal
        { m with
            Transcript = m.Transcript @ [ UserMsg "run" ]
            Prompt = PromptBox.empty
            Approval = Some("rm -rf build && cargo test", true) }
        |> followTail
    elif text = "diff" then
        // open the diff reviewer over a canned change set
        { m with
            Transcript = m.Transcript @ [ UserMsg "diff" ]
            Prompt = PromptBox.empty
            Hunks = sampleHunks
            Diff = Some(Split, 0) }
        |> followTail
    else
        { m with
            Transcript =
                m.Transcript
                @ [ UserMsg text
                    AssistantMd(sprintf "You said: *%s*. (This shell isn't wired to an LLM.)" text) ]
            Prompt = PromptBox.empty }
        |> followTail

let private resolve (accepted: bool) (m: Model) =
    let cmd =
        match m.Approval with
        | Some(c, _) -> c
        | None -> ""

    let block =
        if accepted then
            ToolCall("shell", cmd, Succeeded, "0.4s", "ok")
        else
            Notice(AppTheme.Warning, sprintf "denied: %s" cmd)

    { m with
        Transcript = m.Transcript @ [ block ]
        Approval = None }
    |> followTail

/// Set the status of hunk `i`.
let private setStatus (i: int) (st: HunkStatus) (hunks: DiffHunk list) =
    hunks |> List.mapi (fun j h -> if j = i then { h with Status = st } else h)

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | Resized s ->
        { m with
            Size = s
            Offset = maxScroll { m with Size = s } },
        Cmd.none
    | Edit e ->
        match m.Diff, m.Approval with
        // Diff reviewer: j/k select a hunk, a/r accept/reject it, s toggle mode.
        | Some(mode, sel), _ ->
            let ch =
                match e with
                | Key ke ->
                    match ke.Key with
                    | Char c -> c
                    | _ -> ""
                | _ -> ""

            match ch with
            | "j" ->
                { m with
                    Diff = Some(mode, min (m.Hunks.Length - 1) (sel + 1)) },
                Cmd.none
            | "k" ->
                { m with
                    Diff = Some(mode, max 0 (sel - 1)) },
                Cmd.none
            | "s" ->
                { m with
                    Diff = Some((if mode = Unified then Split else Unified), sel) },
                Cmd.none
            | "a" ->
                { m with
                    Hunks = setStatus sel Accepted m.Hunks },
                Cmd.none
            | "r" ->
                { m with
                    Hunks = setStatus sel Rejected m.Hunks },
                Cmd.none
            | _ -> m, Cmd.none
        | None, Some _ -> m, Cmd.none // the modal swallows editing
        | None, None ->
            { m with
                Prompt = PromptBox.applyInput e m.Prompt },
            Cmd.none
    | Submit ->
        match m.Diff, m.Approval with
        | Some _, _ -> { m with Diff = None }, Cmd.none // Enter closes the reviewer
        | None, Some(_, acceptFocused) -> resolve acceptFocused m, Cmd.none
        | None, None -> submit m, Cmd.none
    | EscKey ->
        match m.Diff, m.Approval with
        | Some _, _ -> { m with Diff = None }, Cmd.none
        | None, Some _ -> resolve false m, Cmd.none
        | None, None -> m, Cmd.none
    | ToggleButton ->
        match m.Approval with
        | Some(c, f) -> { m with Approval = Some(c, not f) }, Cmd.none
        | None -> m, Cmd.none

// ── view ───────────────────────────────────────────────────────────────────
let private header =
    Box.box theme.border [ Text.text "└ mire · agent shell" theme.title ]

let private promptView (m: Model) =
    Box.box
        theme.border
        [ PromptBox.render
              (m.Size.Width - 2)
              theme.accent
              theme.fg
              theme.selection
              theme.fgSubtle
              "type a message — or `run`"
              m.Approval.IsNone
              m.Prompt ]

let private transcriptView (m: Model) =
    Box.box
        theme.border
        [ ScrollView.vertical
              (viewportRows m)
              (contentHeight m)
              m.Offset
              theme.border
              theme.fgMuted
              (ChatTranscript.render theme (wrapWidth m) 0 m.Transcript) ]

let view (m: Model) : LayoutNode<Msg> =
    let baseTree =
        Dock.dock
            [ Dock.top 3 header
              Dock.bottom 3 (promptView m)
              Dock.fill (transcriptView m) ]

    match m.Diff, m.Approval with
    | Some(mode, sel), _ ->
        // Layer the diff reviewer over the shell.
        let w = min 72 (m.Size.Width - 4)

        let rows =
            m.Hunks
            |> List.sumBy (fun h ->
                1
                + (match mode with
                   | Unified -> h.Lines.Length
                   | Split -> fst (DiffView.splitColumns h.Lines) |> List.length))

        let body =
            Stack.vstack
                [ DiffView.render theme mode (w - 2) sel m.Hunks
                  Text.text " j/k move · a accept · r reject · s split/unified · Esc close" theme.fgSubtle ]

        let title =
            sprintf
                "review changes (%s)"
                (match mode with
                 | Split -> "split"
                 | Unified -> "unified")

        LayoutNode.Overlay(
            Rect.Create(0, 0, 0, 0),
            [ baseTree
              Modal.modal Style.Default theme.border theme.title w (min (m.Size.Height - 4) (rows + 4)) title body ]
        )
    | None, Some(cmd, acceptFocused) ->
        // Layer the approval modal over the shell (its own backdrop dims the base).
        LayoutNode.Overlay(
            Rect.Create(0, 0, 0, 0),
            [ baseTree
              ApprovalModal.view
                  theme
                  "Permission required"
                  "A tool wants to run:"
                  cmd
                  (Some "writes files")
                  "Accept"
                  "Deny"
                  acceptFocused ]
        )
    | None, None -> baseTree

let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | Enter -> Some Submit
        | Escape -> Some EscKey
        | Tab
        | ArrowLeft
        | ArrowRight -> Some ToggleButton
        | Char _
        | Space
        | Backspace
        | Delete -> Some(Edit e)
        | _ -> None
    | Paste _ -> Some(Edit e)
    | Resize s -> Some(Resized s)
    | _ -> None

let subscriptions (_: Model) : Sub<Msg> list = [ Sub.TerminalResize Resized ]

// ── headless --dump (no raw mode) — render representative states as text ─────
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

let private dumpNode (label: string) (size: Size) (node: LayoutNode<Msg>) =
    let surface = Surface(size)
    Layout.render surface (Layout.measure (Rect.FromOrigin size) node)
    printSurface label surface

let private dump () =
    let size = Size.Create(74, 20)

    let baseModel =
        { fst (init ()) with
            Size = size
            Transcript =
                [ greeting
                  UserMsg "build the project"
                  AssistantMd "Sure — running the build."
                  ToolCall("shell", "cargo build", Succeeded, "1.1s", "Compiling… ok") ] }

    dumpNode "A. agent shell" size (view baseModel)

    dumpNode
        "B. agent shell + approval"
        size
        (view
            { baseModel with
                Approval = Some("rm -rf build && cargo test", true) })

    dumpNode
        "C. diff reviewer (split, hunk 0 accepted)"
        size
        (view
            { baseModel with
                Diff = Some(Split, 0)
                Hunks =
                    sampleHunks
                    |> List.mapi (fun i h -> if i = 0 then { h with Status = Accepted } else h) })

[<EntryPoint>]
let main argv =
    if argv |> Array.contains "--dump" then
        dump ()
        0
    else
        Program.mkProgram init update view
        |> Program.withMapInput mapInput
        |> Program.withSubscriptions subscriptions
        |> Runtime.run

        0
