// Mire.Agent MVP — a minimal coding-agent shell (SPEC.md's headline example),
// now built on the `AgentShell` program builder: the agent layer composes itself
// (ChatTranscript + PromptBox + ApprovalModal + the Conversation model), and the
// app supplies only *what a submission/approval does*. Type a message and press
// Enter to get a streamed reply; type `run` to trigger a tool-approval modal
// (←/→ choose, Enter confirms, Esc denies, or click); ↑/↓ recall history; `/`
// pops command completions; Shift+Enter adds a line; PgUp/PgDn or the wheel
// scroll; Esc cancels a streaming reply. Ctrl+C quits.
// `-- --dump` is headless.

open System
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.Widgets
open Mire.Agent
open Mire.App

let private theme = AppTheme.defaultTheme

// ── app behavior: the only two callbacks the shell needs ─────────────────────

/// A submitted line: `run` raises an approval; anything else gets a canned reply
/// streamed in token-by-token (the streaming API — an LLM would drive this the same
/// way, dispatching `Apply` as chunks arrive).
let private onSubmit (text: string) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
    if text = "run" then
        AgentShell.requestApproval "rm -rf build && cargo test" m, Cmd.none
    else
        let id, m = AgentShell.startReply m

        let words =
            (sprintf "You said: *%s*. This shell streams a canned reply, one word at a time." text).Split(' ')

        let cmd =
            Cmd.ofAsync (fun dispatch ->
                async {
                    for w in words do
                        do! Async.Sleep 55
                        dispatch (Apply(AgentShell.stream id (w + " ")))

                    dispatch (Apply(AgentShell.finishReply id))
                })

        m, cmd

/// An approval decision: accept → run the tool (mark it Running, then Succeeded a
/// moment later); deny → a warning notice.
let private onApprove (accepted: bool) (cmd: string) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
    if accepted then
        let id, m = AgentShell.addTool "shell" cmd Running m

        let finish =
            Cmd.ofAsync (fun dispatch ->
                async {
                    do! Async.Sleep 300
                    dispatch (Apply(AgentShell.setTool id Succeeded "0.3s" "ok"))
                })

        m, finish
    else
        AgentShell.addNotice AppTheme.Warning (sprintf "denied: %s" cmd) m, Cmd.none

/// Slash-command completions: typing `/` pops a list (↑/↓ select, Tab/Enter accept).
let private completions (tok: CompletionToken) : string list =
    if tok.Trigger = '/' && tok.Start = 0 then
        [ "help"; "clear"; "model"; "run" ]
        |> List.filter (fun c -> c.StartsWith(tok.Query, StringComparison.OrdinalIgnoreCase))
    else
        []

let private config: ShellConfig =
    { AgentShell.config theme "└ mire · agent shell" "type a message — or `run`" onSubmit onApprove with
        Completions = completions }

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

let private dumpNode (label: string) (size: Size) (node: LayoutNode<ShellMsg>) =
    let surface = Surface(size)
    Layout.render surface (Layout.measure (Rect.FromOrigin size) node)
    printSurface label surface

let private mkModel (size: Size) (conv: Conversation) (approval: string option) : ShellModel =
    let m0 = fst (AgentShell.init config ())

    let m =
        { m0 with
            Conversation = conv
            Size = size
            Streams =
                conv.Entries
                |> List.filter (fun e -> e.Streaming)
                |> List.map (fun e -> e.Id)
                |> Set.ofList }

    let m =
        match approval with
        | Some c -> AgentShell.requestApproval c m
        | None -> m

    m |> AgentShell.sync |> AgentShell.followTail

let private dump () =
    let size = Size.Create(74, 20)

    // A finished conversation: greeting, a user turn, a streamed reply, a tool call.
    let baseConv =
        let c =
            Conversation.empty
            |> Conversation.addAssistant
                "Welcome to the **Mire** agent shell. Type a message — `run` triggers a tool approval."
            |> Conversation.addUser "build the project"
            |> Conversation.addAssistant "Sure — running the build."

        let id, c = Conversation.addToolCall "shell" "cargo build" Running c
        Conversation.setTool id Succeeded "1.1s" "Compiling… ok" c

    dumpNode "A. agent shell (idle)" size (AgentShell.view config (mkModel size baseConv None))

    dumpNode
        "B. agent shell + approval"
        size
        (AgentShell.view config (mkModel size baseConv (Some "rm -rf build && cargo test")))

    // Mid-stream: an assistant reply still being appended (Session = Streaming).
    let streamingConv =
        let id, c =
            Conversation.empty
            |> Conversation.addUser "explain the layout engine"
            |> Conversation.startAssistant

        c
        |> Conversation.appendText id "The layout engine measures a tree of nodes into"

    dumpNode "C. agent shell (streaming a reply)" size (AgentShell.view config (mkModel size streamingConv None))

[<EntryPoint>]
let main argv =
    if argv |> Array.contains "--dump" then
        dump ()
        0
    else
        AgentShell.program config |> Runtime.run
        0
