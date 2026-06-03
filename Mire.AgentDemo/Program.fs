// Mire.AgentDemo — an interactive agent-shell demo and feature testbed.
// Not wired to an LLM: the Dummy module supplies canned responses. This file is the
// Elmish app (model/update/view), input routing, the headless --dump mode, and main.

open System
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Widgets
open Mire.AgentDemo
open Mire.AgentDemo.Dummy

// ── model ────────────────────────────────────────────────────────────────────
type Mode =
    | Normal
    | AutoAccept
    | Plan

type Pane =
    | ListPane
    | PreviewPane

type ButtonFocus =
    | AcceptBtn
    | DenyBtn

type FocusRegion =
    | PromptFocus
    | TranscriptFocus

type ModalState = { Spec: ModalSpec; Focus: ButtonFocus }
type PaletteState = { Query: string; Sel: int }

type SkillState =
    { Sel: int
      ListScroll: int
      PreviewScroll: int
      Pane: Pane }

/// Prompt-driven completion popups (coexist with typing): @file mentions and /slash commands.
type CompletionKind =
    | MentionC
    | SlashC

/// A fake MCP server for the /mcp manager.
type McpServer =
    { Name: string
      Transport: string
      Status: string
      Tools: string list }

type McpView =
    | McpList
    | McpActions
    | McpTools

type McpState =
    { View: McpView
      ServerSel: int
      ActionSel: int
      ToolScroll: int }

type Overlay =
    | NoOverlay
    | PaletteOverlay of PaletteState
    | SkillOverlay of SkillState
    | ModalOverlay of ModalState
    | McpOverlay of McpState

type Toast =
    { Id: int
      Tone: Theme.Tone
      Title: string
      Body: string
      Ttl: int }

type Streaming =
    { Words: string[]
      Index: int
      BlockIndex: int }

type Model =
    { Size: Size
      Transcript: Block list
      Prompt: PromptInput
      Offset: int
      Mode: Mode
      Streaming: Streaming option
      Toasts: Toast list
      Overlay: Overlay
      SidebarOpen: bool
      Tasks: (string * ToolStatus) list
      Files: string list
      Spinner: int
      Focus: FocusRegion
      Completion: (CompletionKind * int) option // active @mention / /slash popup + selected index
      Pastes: (int * string) list // pasted chunks collapsed to chips in the current prompt
      Mcp: McpServer list
      Tokens: int
      NextId: int }

/// Normalized keys the app routes on (MapInput can't see the model, so routing by
/// overlay state happens in update; this DU carries the key across that boundary).
type Key2 =
    | KChar of string
    | KBackspace
    | KEnter
    | KEsc
    | KTab
    | KShiftTab
    | KUp
    | KDown
    | KLeft
    | KRight
    | KPageUp
    | KPageDown
    | KHome
    | KEnd
    | KCtrlP
    | KCtrlO

type Msg =
    | KeyMsg of Key2
    | RunCommand of string
    | StreamChunk
    | StopStream
    | ToolResolve of int * ToolStatus * string * string
    | SpinnerTick
    | Resized of Size
    | Ignore

// fake MCP servers for the /mcp manager
let private mcpInitial: McpServer list =
    [ { Name = "github"
        Transport = "http"
        Status = "connected"
        Tools = [ "search_issues"; "create_pr"; "list_repos"; "get_file"; "add_comment" ] }
      { Name = "filesystem"
        Transport = "stdio"
        Status = "connected"
        Tools = [ "read_file"; "write_file"; "list_dir" ] }
      { Name = "linear"
        Transport = "sse"
        Status = "needs-auth"
        Tools = [ "list_issues"; "create_issue"; "update_issue" ] }
      { Name = "postgres"
        Transport = "stdio"
        Status = "disconnected"
        Tools = [ "query"; "schema" ] }
      { Name = "context7"
        Transport = "http"
        Status = "connected"
        Tools = [ "resolve_library"; "get_docs" ] } ]

let private mcpActions = [ "Connect"; "Authenticate"; "View tools"; "Uninstall" ]

// ── small helpers ─────────────────────────────────────────────────────────────
let private clamp lo hi v = max lo (min hi v)

let private modeLabel =
    function
    | Normal -> "normal"
    | AutoAccept -> "auto-accept"
    | Plan -> "plan"

let private nextMode =
    function
    | Normal -> AutoAccept
    | AutoAccept -> Plan
    | Plan -> Normal

let private modeStyle =
    function
    | Normal -> Theme.subtle
    | AutoAccept -> Theme.okStyle
    | Plan -> Theme.infoStyle

let private transcriptWrapWidth (m: Model) =
    let bodyW = if m.SidebarOpen then m.Size.Width - 30 else m.Size.Width
    max 10 (bodyW - 3)

let private viewportRows (m: Model) = max 1 (m.Size.Height - 9)

/// Total rendered height of the transcript — uses the exact `contentExtent` the layout
/// engine uses to size Content children, so scroll clamping matches what's drawn.
let private contentHeight (m: Model) =
    let w = transcriptWrapWidth m

    m.Transcript
    |> List.sumBy (fun b -> Layout.contentExtent Direction.Vertical (Blocks.renderBlock w m.Spinner b))

let private maxScroll (m: Model) =
    max 0 (contentHeight m - viewportRows m)

let private followTail (m: Model) = { m with Offset = maxScroll m }

let private filterCommands (q: string) =
    let ql = q.ToLowerInvariant()

    Dummy.commands
    |> List.filter (fun (t, d) -> t.Contains(ql) || d.ToLowerInvariant().Contains(ql))

let private addToast (tone: Theme.Tone) (title: string) (body: string) (m: Model) =
    { m with
        Toasts =
            m.Toasts
            @ [ { Id = m.NextId
                  Tone = tone
                  Title = title
                  Body = body
                  Ttl = 28 } ]
        NextId = m.NextId + 1 }

// ── @file mentions ──────────────────────────────────────────────────────────────
let private mentionFiles =
    [ "Mire/Core/Input.fs"
      "Mire/Layout/Layout.fs"
      "Mire/Renderer/Surface.fs"
      "Mire.AgentDemo/Program.fs"
      "Mire.AgentDemo/Theme.fs"
      "README.md"
      "ROADMAP.md"
      "SPEC.md"
      "DEMO-TODOS.md" ]

/// The active `@token` — the last whitespace-delimited word if it starts with '@',
/// returned without the leading '@'.
let private activeMention (value: string) : string option =
    let lastSpace = value.LastIndexOf(' ')

    let token =
        if lastSpace < 0 then
            value
        else
            value.Substring(lastSpace + 1)

    if token.StartsWith("@") then
        Some(token.Substring(1))
    else
        None

let private mentionMatches (q: string) =
    let ql = q.ToLowerInvariant()
    mentionFiles |> List.filter (fun f -> f.ToLowerInvariant().Contains(ql))

/// Replace the trailing `@token` with `@path ` (selected file + trailing space).
let private acceptMention (path: string) (value: string) : string =
    let lastSpace = value.LastIndexOf(' ')

    let prefix =
        if lastSpace < 0 then
            ""
        else
            value.Substring(0, lastSpace + 1)

    prefix + "@" + path + " "

/// The active completion (kind + query): `/slash` when the whole prompt is one
/// slash-token, else an `@mention` token, else none.
let private activeCompletion (value: string) : (CompletionKind * string) option =
    if value.StartsWith("/") && not (value.Contains(" ")) then
        Some(SlashC, value.Substring(1))
    else
        match activeMention value with
        | Some q -> Some(MentionC, q)
        | None -> None

/// Candidate list for a completion kind: (acceptValue, displayLabel, sublabel).
let private completionList (kind: CompletionKind) (q: string) : (string * string * string) list =
    match kind with
    | MentionC -> mentionMatches q |> List.map (fun f -> (f, "@" + f, ""))
    | SlashC ->
        let ql = q.ToLowerInvariant()

        Dummy.commands
        |> List.filter (fun (t, _) -> t.Contains(ql))
        |> List.map (fun (t, d) -> (t, "/" + t, d))

/// Open / refresh / close the completion popup based on the current prompt text.
let private refreshCompletion (m: Model) : Model =
    match activeCompletion m.Prompt.Value with
    | Some(kind, q) ->
        let n = List.length (completionList kind q)

        if n = 0 then
            { m with Completion = None }
        else
            let prev =
                match m.Completion with
                | Some(k, s) when k = kind -> s
                | _ -> 0

            { m with
                Completion = Some(kind, clamp 0 (n - 1) prev) }
    | None -> { m with Completion = None }

let private truncate (s: string) =
    if s.Length > 160 then s.Substring(0, 160) + "…" else s

// ── dummy → model ──────────────────────────────────────────────────────────────
let private applyResponse (rawText: string) (m: Model) : Model * Cmd<Msg> =
    let lower = rawText.ToLowerInvariant().Trim()

    match Dummy.respond lower with
    | AppendBlocks bs ->
        followTail
            { m with
                Transcript = m.Transcript @ bs },
        Cmd.none
    | StreamMarkdown md ->
        let words = md.Split(' ')
        let idx = List.length m.Transcript

        let m' =
            { m with
                Transcript = m.Transcript @ [ AssistantMd "" ]
                Streaming =
                    Some
                        { Words = words
                          Index = 0
                          BlockIndex = idx } }

        followTail m', Cmd.none
    | RunningTool tr ->
        let idx = List.length m.Transcript

        let m' =
            { m with
                Transcript = m.Transcript @ [ ToolCall(tr.Name, tr.Cmd, Running, "", tr.RunningOut) ] }

        let cmd =
            Cmd.ofAsync (fun send ->
                async {
                    do! Async.Sleep tr.DelayMs
                    send (ToolResolve(idx, tr.FinalStatus, tr.FinalMeta, tr.FinalOut))
                })

        followTail m', cmd
    | SpawnToast(tone, title, body) -> addToast tone title body m, Cmd.none
    | OpenModal spec ->
        { m with
            Overlay = ModalOverlay { Spec = spec; Focus = AcceptBtn } },
        Cmd.none
    | ClearTranscript -> { m with Transcript = []; Offset = 0 }, Cmd.none
    | MetaCmd meta ->
        match meta with
        | ToggleSidebar ->
            { m with
                SidebarOpen = not m.SidebarOpen },
            Cmd.none
        | CycleMode -> { m with Mode = nextMode m.Mode }, Cmd.none
        | OpenSkills ->
            { m with
                Overlay =
                    SkillOverlay
                        { Sel = 0
                          ListScroll = 0
                          PreviewScroll = 0
                          Pane = ListPane } },
            Cmd.none
        | OpenPalette ->
            { m with
                Overlay = PaletteOverlay { Query = ""; Sel = 0 } },
            Cmd.none
        | OpenMcp ->
            { m with
                Overlay =
                    McpOverlay
                        { View = McpList
                          ServerSel = 0
                          ActionSel = 0
                          ToolScroll = 0 } },
            Cmd.none
        | ShowWelcome ->
            followTail
                { m with
                    Transcript = m.Transcript @ [ Dummy.welcomeBlock ] },
            Cmd.none

let private startCommand (text: string) (m: Model) : Model * Cmd<Msg> =
    let m1 =
        { m with
            Transcript = m.Transcript @ [ UserMsg text ]
            Tokens = m.Tokens + 120 + text.Length * 3 }

    applyResponse text m1

let private submit (m: Model) : Model * Cmd<Msg> =
    let text = m.Prompt.Value.Trim()

    if text = "" then
        m, Cmd.none
    else
        // Flush any pasted chunks into notes alongside the user message.
        let pasteBlocks =
            m.Pastes
            |> List.map (fun (id, content) ->
                Notice(Theme.Neutral, sprintf "pasted content #%d — %d chars\n%s" id content.Length (truncate content)))

        let m1 =
            { m with
                Prompt = PromptInput.empty
                Pastes = []
                Completion = None
                Transcript = m.Transcript @ [ UserMsg text ] @ pasteBlocks
                Tokens = m.Tokens + 120 + text.Length * 3 }

        applyResponse text m1

let private runCommand (text: string) (m: Model) : Model * Cmd<Msg> =
    startCommand text { m with Overlay = NoOverlay }

/// Accept the highlighted completion. @mentions insert the path; /slash commands run.
let private acceptCompletion (m: Model) : Model * Cmd<Msg> =
    match m.Completion, activeCompletion m.Prompt.Value with
    | Some(kind, sel), Some(kind2, q) when kind = kind2 ->
        let items = completionList kind q

        match List.tryItem (clamp 0 (max 0 (List.length items - 1)) sel) items with
        | Some(value, _, _) ->
            match kind with
            | MentionC ->
                { m with
                    Prompt = { Value = acceptMention value m.Prompt.Value }
                    Completion = None },
                Cmd.none
            | SlashC ->
                runCommand
                    value
                    { m with
                        Prompt = PromptInput.empty
                        Completion = None }
        | None -> { m with Completion = None }, Cmd.none
    | _ -> { m with Completion = None }, Cmd.none

let private resolveModal (which: ButtonFocus) (ms: ModalState) (m: Model) : Model * Cmd<Msg> =
    let m0 = { m with Overlay = NoOverlay }

    match which with
    | AcceptBtn ->
        match ms.Spec.Kind with
        | ConfirmClearModal -> { m0 with Transcript = []; Offset = 0 }, Cmd.none
        | _ ->
            let body =
                if ms.Spec.Command = "" then
                    "proceeding"
                else
                    ms.Spec.Command

            addToast Theme.Success "✓ accepted" body m0, Cmd.none
    | DenyBtn -> addToast Theme.Info "denied" "no action taken" m0, Cmd.none

let private scrollBy (d: int) (m: Model) : Model * Cmd<Msg> =
    { m with
        Offset = clamp 0 (maxScroll m) (m.Offset + d) },
    Cmd.none

// ── key routing per overlay ─────────────────────────────────────────────────────
let private updateBase (k: Key2) (m: Model) : Model * Cmd<Msg> =
    match k with
    | KChar c ->
        // A long or multi-line chunk is a paste → collapse it into a [Pasted] chip.
        if c.Contains("\n") || c.Length >= 16 then
            let id = m.NextId
            let chip = sprintf "[Pasted #%d · %d chars] " id c.Length

            { m with
                Prompt = PromptInput.append chip m.Prompt
                Pastes = m.Pastes @ [ id, c ]
                NextId = id + 1
                Completion = None },
            Cmd.none
        else
            refreshCompletion
                { m with
                    Prompt = PromptInput.append c m.Prompt },
            Cmd.none
    | KBackspace ->
        refreshCompletion
            { m with
                Prompt = PromptInput.backspace m.Prompt },
        Cmd.none
    | KEnter ->
        match m.Completion with
        | Some _ -> acceptCompletion m
        | None -> submit m
    | KShiftTab -> { m with Mode = nextMode m.Mode }, Cmd.none
    | KTab ->
        match m.Completion with
        | Some _ -> acceptCompletion m
        | None ->
            { m with
                Focus =
                    (if m.Focus = PromptFocus then
                         TranscriptFocus
                     else
                         PromptFocus) },
            Cmd.none
    | KUp ->
        match m.Completion with
        | Some(kind, s) ->
            { m with
                Completion = Some(kind, max 0 (s - 1)) },
            Cmd.none
        | None -> scrollBy -1 m
    | KDown ->
        match m.Completion with
        | Some(kind, s) ->
            let q = defaultArg (activeCompletion m.Prompt.Value |> Option.map snd) ""
            let n = List.length (completionList kind q)

            { m with
                Completion = Some(kind, clamp 0 (max 0 (n - 1)) (s + 1)) },
            Cmd.none
        | None -> scrollBy 1 m
    | KPageUp -> scrollBy (-(viewportRows m)) m
    | KPageDown -> scrollBy (viewportRows m) m
    | KHome -> { m with Offset = 0 }, Cmd.none
    | KEnd -> { m with Offset = maxScroll m }, Cmd.none
    | KCtrlP ->
        { m with
            Overlay = PaletteOverlay { Query = ""; Sel = 0 }
            Completion = None },
        Cmd.none
    | KCtrlO ->
        { m with
            Overlay =
                SkillOverlay
                    { Sel = 0
                      ListScroll = 0
                      PreviewScroll = 0
                      Pane = ListPane }
            Completion = None },
        Cmd.none
    | KEsc ->
        match m.Completion with
        | Some _ -> { m with Completion = None }, Cmd.none
        | None ->
            (match m.Streaming with
             | Some _ -> { m with Streaming = None }
             | None -> m),
            Cmd.none
    | _ -> m, Cmd.none

let private updatePalette (k: Key2) (ps: PaletteState) (m: Model) : Model * Cmd<Msg> =
    let setP p = { m with Overlay = PaletteOverlay p }

    match k with
    | KEsc
    | KCtrlP -> { m with Overlay = NoOverlay }, Cmd.none
    | KChar c ->
        setP
            { ps with
                Query = ps.Query + c
                Sel = 0 },
        Cmd.none
    | KBackspace ->
        let q =
            if ps.Query = "" then
                ""
            else
                ps.Query.Substring(0, ps.Query.Length - 1)

        setP { ps with Query = q; Sel = 0 }, Cmd.none
    | KUp -> setP { ps with Sel = max 0 (ps.Sel - 1) }, Cmd.none
    | KDown ->
        let n = List.length (filterCommands ps.Query)

        setP
            { ps with
                Sel = clamp 0 (max 0 (n - 1)) (ps.Sel + 1) },
        Cmd.none
    | KEnter ->
        match List.tryItem ps.Sel (filterCommands ps.Query) with
        | Some(trig, _) -> runCommand trig m
        | None -> { m with Overlay = NoOverlay }, Cmd.none
    | _ -> m, Cmd.none

let private updateSkills (k: Key2) (ss: SkillState) (m: Model) : Model * Cmd<Msg> =
    let n = List.length Skills.all
    let setS s = { m with Overlay = SkillOverlay s }

    match k with
    | KEsc
    | KCtrlO -> { m with Overlay = NoOverlay }, Cmd.none
    | KTab ->
        setS
            { ss with
                Pane = (if ss.Pane = ListPane then PreviewPane else ListPane) },
        Cmd.none
    | KUp ->
        if ss.Pane = ListPane then
            let sel = max 0 (ss.Sel - 1)

            setS
                { ss with
                    Sel = sel
                    ListScroll = max 0 (sel - 5)
                    PreviewScroll = 0 },
            Cmd.none
        else
            setS
                { ss with
                    PreviewScroll = max 0 (ss.PreviewScroll - 1) },
            Cmd.none
    | KDown ->
        if ss.Pane = ListPane then
            let sel = min (n - 1) (ss.Sel + 1)

            setS
                { ss with
                    Sel = sel
                    ListScroll = max 0 (sel - 5)
                    PreviewScroll = 0 },
            Cmd.none
        else
            setS
                { ss with
                    PreviewScroll = ss.PreviewScroll + 1 },
            Cmd.none
    | KPageUp ->
        setS
            { ss with
                PreviewScroll = max 0 (ss.PreviewScroll - 5) },
        Cmd.none
    | KPageDown ->
        setS
            { ss with
                PreviewScroll = ss.PreviewScroll + 5 },
        Cmd.none
    | KEnter ->
        let name = (List.item ss.Sel Skills.all).Name

        { m with
            Overlay = NoOverlay
            Prompt = PromptInput.append name m.Prompt },
        Cmd.none
    | _ -> m, Cmd.none

let private updateModal (k: Key2) (ms: ModalState) (m: Model) : Model * Cmd<Msg> =
    match k with
    | KEsc -> resolveModal DenyBtn ms m
    | KLeft
    | KRight
    | KTab ->
        { m with
            Overlay =
                ModalOverlay
                    { ms with
                        Focus = (if ms.Focus = AcceptBtn then DenyBtn else AcceptBtn) } },
        Cmd.none
    | KEnter -> resolveModal ms.Focus ms m
    | _ -> m, Cmd.none

/// Perform the selected action on the selected MCP server (all canned).
let private performMcpAction (ms: McpState) (m: Model) : Model * Cmd<Msg> =
    match List.tryItem ms.ServerSel m.Mcp with
    | None -> { m with Overlay = NoOverlay }, Cmd.none
    | Some s ->
        let setStatus st =
            m.Mcp
            |> List.map (fun x -> if x.Name = s.Name then { x with Status = st } else x)

        match ms.ActionSel with
        | 0 ->
            addToast
                Theme.Success
                "✓ connected"
                s.Name
                { m with
                    Mcp = setStatus "connected"
                    Overlay = McpOverlay { ms with View = McpList } },
            Cmd.none
        | 1 ->
            addToast
                Theme.Info
                "authenticated"
                ("opened browser for " + s.Name)
                { m with
                    Mcp = setStatus "connected"
                    Overlay = McpOverlay { ms with View = McpList } },
            Cmd.none
        | 2 ->
            { m with
                Overlay =
                    McpOverlay
                        { ms with
                            View = McpTools
                            ToolScroll = 0 } },
            Cmd.none
        | _ ->
            let mcp = m.Mcp |> List.filter (fun x -> x.Name <> s.Name)

            addToast
                Theme.Warning
                "uninstalled"
                s.Name
                { m with
                    Mcp = mcp
                    Overlay =
                        McpOverlay
                            { ms with
                                View = McpList
                                ServerSel = clamp 0 (max 0 (List.length mcp - 1)) ms.ServerSel } },
            Cmd.none

let private updateMcp (k: Key2) (ms: McpState) (m: Model) : Model * Cmd<Msg> =
    let setM s =
        { m with Overlay = McpOverlay s }, Cmd.none

    let n = List.length m.Mcp

    match ms.View with
    | McpList ->
        match k with
        | KEsc -> { m with Overlay = NoOverlay }, Cmd.none
        | KUp ->
            setM
                { ms with
                    ServerSel = max 0 (ms.ServerSel - 1) }
        | KDown ->
            setM
                { ms with
                    ServerSel = clamp 0 (max 0 (n - 1)) (ms.ServerSel + 1) }
        | KEnter ->
            if n = 0 then
                { m with Overlay = NoOverlay }, Cmd.none
            else
                setM
                    { ms with
                        View = McpActions
                        ActionSel = 0 }
        | _ -> m, Cmd.none
    | McpActions ->
        match k with
        | KEsc -> setM { ms with View = McpList }
        | KUp ->
            setM
                { ms with
                    ActionSel = max 0 (ms.ActionSel - 1) }
        | KDown ->
            setM
                { ms with
                    ActionSel = min (List.length mcpActions - 1) (ms.ActionSel + 1) }
        | KEnter -> performMcpAction ms m
        | _ -> m, Cmd.none
    | McpTools ->
        match k with
        | KEsc -> setM { ms with View = McpActions }
        | KUp ->
            setM
                { ms with
                    ToolScroll = max 0 (ms.ToolScroll - 1) }
        | KDown ->
            setM
                { ms with
                    ToolScroll = ms.ToolScroll + 1 }
        | _ -> m, Cmd.none

let update (msg: Msg) (m: Model) : Model * Cmd<Msg> =
    match msg with
    | KeyMsg k ->
        match m.Overlay with
        | NoOverlay -> updateBase k m
        | PaletteOverlay ps -> updatePalette k ps m
        | SkillOverlay ss -> updateSkills k ss m
        | ModalOverlay ms -> updateModal k ms m
        | McpOverlay ms -> updateMcp k ms m
    | RunCommand c -> runCommand c m
    | StreamChunk ->
        match m.Streaming with
        | None -> m, Cmd.none
        | Some st ->
            if st.Index >= st.Words.Length then
                { m with Streaming = None }, Cmd.none
            else
                let word = st.Words.[st.Index]

                let t' =
                    m.Transcript
                    |> List.mapi (fun i b ->
                        if i = st.BlockIndex then
                            match b with
                            | AssistantMd s -> AssistantMd(if s = "" then word else s + " " + word)
                            | other -> other
                        else
                            b)

                let nextIdx = st.Index + 1

                let streaming =
                    if nextIdx >= st.Words.Length then
                        None
                    else
                        Some { st with Index = nextIdx }

                followTail
                    { m with
                        Transcript = t'
                        Streaming = streaming },
                Cmd.none
    | StopStream -> { m with Streaming = None }, Cmd.none
    | ToolResolve(idx, status, meta, out) ->
        let t' =
            m.Transcript
            |> List.mapi (fun i b ->
                if i = idx then
                    match b with
                    | ToolCall(n, c, _, _, _) -> ToolCall(n, c, status, meta, out)
                    | other -> other
                else
                    b)

        followTail { m with Transcript = t' }, Cmd.none
    | SpinnerTick ->
        let toasts =
            m.Toasts
            |> List.choose (fun t -> if t.Ttl <= 1 then None else Some { t with Ttl = t.Ttl - 1 })

        { m with
            Spinner = m.Spinner + 1
            Toasts = toasts },
        Cmd.none
    | Resized sz ->
        { m with
            Size = sz
            Offset = clamp 0 (maxScroll { m with Size = sz }) m.Offset },
        Cmd.none
    | Ignore -> m, Cmd.none

// ── view ─────────────────────────────────────────────────────────────────────
let private rect0 = Rect.Create(0, 0, 0, 0)

/// An opaque panel: a Filled backdrop (occludes whatever is behind it) plus a bordered
/// box around the content. This is how the harness fakes overlay opacity/positioning —
/// `Overlay` layers can't anchor or be transparent-aware yet (ROADMAP v0.2).
let private opaque (border: Style) (content: LayoutNode<Msg>) : LayoutNode<Msg> =
    LayoutNode.Overlay(rect0, [ Backdrop.solid Style.Default; Box.box border [ content ] ])

/// Center a w×h node within `size` by insetting it with margin spacers.
let private centered (w: int) (h: int) (size: Size) (node: LayoutNode<Msg>) : LayoutNode<Msg> =
    let lm = max 0 ((size.Width - w) / 2)
    let tm = max 0 ((size.Height - h) / 2)

    Dock.dock
        [ Dock.top tm Spacer.spacer
          Dock.bottom tm Spacer.spacer
          Dock.left lm Spacer.spacer
          Dock.right lm Spacer.spacer
          Dock.fill node ]

let private header (m: Model) : LayoutNode<Msg> =
    Box.box
        Theme.borderStyle
        [ Stack.hstackOf
              [ Stack.sized Length.Content (Text.text "└ mire · agent" Theme.title)
                Stack.sized Length.Fill Spacer.spacer
                Stack.sized Length.Content (Text.text (sprintf "[%s]" (modeLabel m.Mode)) (modeStyle m.Mode))
                Stack.sized (Length.Cells 2) (Text.text "  " Theme.subtle)
                Stack.sized Length.Content (Text.text (sprintf "opus-4.8 · %d tok" m.Tokens) Theme.subtle) ] ]

let private placeholder (m: Model) =
    match m.Mode with
    | Plan -> "plan mode — describe the change (try: markdown, diff, plan)"
    | _ -> "type a command — try: markdown, tool, diff, permission"

let private promptBox (m: Model) : LayoutNode<Msg> =
    Box.box Theme.borderStyle [ PromptInput.render Theme.accentStyle Theme.text Theme.subtle (placeholder m) m.Prompt ]

let private hints: LayoutNode<Msg> =
    Text.text " Ctrl+P palette · Ctrl+O skills · ⇧Tab mode · Esc close · Ctrl+C quit" Theme.subtle

let private sidebar (m: Model) : LayoutNode<Msg> =
    let taskRows =
        m.Tasks
        |> List.map (fun (n, s) ->
            Text.text (sprintf " %s %s" (Blocks.statusGlyph m.Spinner s) n) (Blocks.statusStyle s))

    let fileRows = m.Files |> List.map (fun f -> Text.text (" " + f) Theme.subtle)

    Box.box
        Theme.borderStyle
        [ Stack.vstack (
              [ Text.text " tasks" Theme.subtle ]
              @ taskRows
              @ [ Text.text "" Theme.text; Text.text " files" Theme.subtle ]
              @ fileRows
          ) ]

let private transcriptRegion (m: Model) : LayoutNode<Msg> =
    let w = transcriptWrapWidth m

    let border =
        if m.Focus = TranscriptFocus then
            Theme.borderFocus
        else
            Theme.borderStyle

    let rows =
        m.Transcript
        |> List.map (fun b -> Stack.sized Length.Content (Blocks.renderBlock w m.Spinner b))

    Box.box border [ Scroll.vertical m.Offset (Stack.vstackOf rows) ]

let private body (m: Model) : LayoutNode<Msg> =
    if m.SidebarOpen then
        Stack.hstackOf
            [ Stack.sized Length.Fill (transcriptRegion m)
              Stack.sized (Length.Cells 30) (sidebar m) ]
    else
        transcriptRegion m

let private palettePanel (ps: PaletteState) (m: Model) : LayoutNode<Msg> =
    let items = filterCommands ps.Query
    let visible = max 1 (min 12 (m.Size.Height - 8))
    let scroll = max 0 (ps.Sel - visible + 2)

    let rows =
        items
        |> List.mapi (fun i (trig, desc) ->
            let selected = i = ps.Sel
            let style = if selected then Theme.selection else Theme.text
            let descStyle = if selected then Theme.selection else Theme.subtle

            let rowContent =
                Stack.hstackOf
                    [ Stack.sized (Length.Cells 16) (Text.text (" " + trig) style)
                      Stack.sized Length.Fill (Text.text desc descStyle) ]
            // Full-width highlight: fill the whole row with the selection colour,
            // not just the cells under the glyphs.
            Stack.sized
                Length.Content
                (if selected then
                     Backdrop.behind Theme.selection rowContent
                 else
                     rowContent))

    let content =
        Stack.vstackOf
            [ Stack.sized (Length.Cells 1) (Text.text (sprintf " ❯ %s▏" ps.Query) Theme.text)
              Stack.sized (Length.Cells 1) (Text.text (String('─', 44)) Theme.borderStyle)
              Stack.sized Length.Fill (Scroll.vertical scroll (Stack.vstackOf rows))
              Stack.sized (Length.Cells 1) (Text.text " ↑↓ select · Enter run · Esc close" Theme.subtle) ]

    centered 50 (visible + 6) m.Size (opaque Theme.borderStyle content)

let private skillPanel (ss: SkillState) (m: Model) : LayoutNode<Msg> =
    let panelW = min 76 (m.Size.Width - 6)
    let panelH = min 22 (m.Size.Height - 4)
    let listW = 22
    let previewW = max 10 (panelW - listW - 5)

    let listBorder =
        if ss.Pane = ListPane then
            Theme.borderFocus
        else
            Theme.borderStyle

    let prevBorder =
        if ss.Pane = PreviewPane then
            Theme.borderFocus
        else
            Theme.borderStyle

    let skillRows =
        Skills.all
        |> List.mapi (fun i s ->
            let style = if i = ss.Sel then Theme.selection else Theme.muted
            Text.text (sprintf " %s" s.Name) style)

    let listNode =
        Box.box listBorder [ Scroll.vertical ss.ListScroll (Stack.vstack skillRows) ]

    let selected = List.item ss.Sel Skills.all

    let previewNode =
        Box.box prevBorder [ Scroll.vertical ss.PreviewScroll (Markdown.render previewW selected.Markdown) ]

    let content =
        Stack.vstackOf
            [ Stack.sized
                  (Length.Cells 1)
                  (Text.text (sprintf " Skill explorer · %d skills" (List.length Skills.all)) Theme.subtle)
              Stack.sized
                  Length.Fill
                  (Stack.hstackOf
                      [ Stack.sized (Length.Cells listW) listNode
                        Stack.sized Length.Fill previewNode ])
              Stack.sized
                  (Length.Cells 1)
                  (Text.text " Tab switch pane · ↑↓ navigate · Enter insert · Esc close" Theme.subtle) ]

    centered panelW panelH m.Size (opaque Theme.borderStyle content)

let private modalPanel (ms: ModalState) (m: Model) : LayoutNode<Msg> =
    let spec = ms.Spec

    let acceptStyle =
        if ms.Focus = AcceptBtn then
            Theme.selection
        else
            Theme.muted

    let denyStyle = if ms.Focus = DenyBtn then Theme.selection else Theme.muted

    let buttons =
        Stack.hstackOf
            [ Stack.sized Length.Content (Text.text (sprintf " [ %s ] " spec.AcceptLabel) acceptStyle)
              Stack.sized (Length.Cells 3) (Text.text "   " Theme.text)
              Stack.sized Length.Content (Text.text (sprintf " ‹ %s › " spec.DenyLabel) denyStyle) ]

    let cmdLines =
        if spec.Command = "" then
            []
        else
            [ Text.text (sprintf " ❯ %s" spec.Command) Theme.text ]

    let riskLines =
        match spec.Risk with
        | Some r -> [ Text.text (sprintf " risk: %s" r) Theme.warnStyle ]
        | None -> []

    let rows =
        [ Text.text (" " + spec.Title) Theme.warnStyle
          Text.text "" Theme.text
          Text.text (" " + spec.Intro) Theme.muted ]
        @ cmdLines
        @ riskLines
        @ [ Text.text "" Theme.text
            buttons
            Text.text " ←/→ or Tab move · Enter confirm · Esc deny" Theme.subtle ]

    centered 52 (List.length rows + 2) m.Size (opaque Theme.borderStyle (Stack.vstack rows))

let private renderToast (t: Toast) : LayoutNode<Msg> =
    opaque
        Theme.borderStyle
        (Stack.vstack
            [ Text.text (" " + t.Title) (Theme.toneStyle t.Tone)
              Text.text (" " + t.Body) Theme.muted ])

let private toastLayer (m: Model) : LayoutNode<Msg> =
    let cards =
        m.Toasts
        |> List.collect (fun t ->
            [ Stack.sized (Length.Cells 4) (renderToast t)
              Stack.sized (Length.Cells 1) Spacer.spacer ])

    let col = Dock.dock [ Dock.top 1 Spacer.spacer; Dock.fill (Stack.vstackOf cards) ]
    Dock.dock [ Dock.right 2 Spacer.spacer; Dock.right 28 col; Dock.fill Spacer.spacer ]

/// The completion popup (@mentions or /slash commands), floated above the prompt.
let private completionPopup (kind: CompletionKind) (sel: int) (m: Model) : LayoutNode<Msg> =
    let q = defaultArg (activeCompletion m.Prompt.Value |> Option.map snd) ""
    let items = completionList kind q

    let title =
        match kind with
        | MentionC -> " files (@mention) — ↑↓ Enter · Esc"
        | SlashC -> " slash commands — ↑↓ Enter · Esc"

    let popupW = 46
    let popupH = min (max 3 (m.Size.Height - 6)) (List.length items + 3)

    let rows =
        items
        |> List.mapi (fun i (_, label, sub) ->
            let style = if i = sel then Theme.selection else Theme.text

            let node =
                if sub = "" then
                    Text.text (" " + label) style
                else
                    let subStyle = if i = sel then Theme.selection else Theme.subtle

                    Stack.hstackOf
                        [ Stack.sized (Length.Cells 18) (Text.text (" " + label) style)
                          Stack.sized Length.Fill (Text.text sub subStyle) ]

            Stack.sized Length.Content node)

    let content =
        Stack.vstackOf (Stack.sized (Length.Cells 1) (Text.text title Theme.subtle) :: rows)

    let band =
        Dock.dock
            [ Dock.left 2 Spacer.spacer
              Dock.left popupW (opaque Theme.borderFocus content)
              Dock.fill Spacer.spacer ]

    Dock.dock
        [ Dock.bottom 4 Spacer.spacer // clear the prompt (3) + hints (1)
          Dock.bottom popupH band
          Dock.fill Spacer.spacer ]

/// The /mcp manager: a server table → per-server actions → tool list.
let private mcpPanel (ms: McpState) (m: Model) : LayoutNode<Msg> =
    let pad w (s: string) =
        if Grapheme.stringWidth s >= w then
            s
        else
            s + System.String(' ', w - Grapheme.stringWidth s)

    let statusStyle st =
        match st with
        | "connected" -> Theme.okStyle
        | "needs-auth" -> Theme.warnStyle
        | "disconnected" -> Theme.errStyle
        | _ -> Theme.muted

    match ms.View with
    | McpList ->
        let header =
            Stack.hstackOf
                [ Stack.sized (Length.Cells 14) (Text.text " server" Theme.subtle)
                  Stack.sized (Length.Cells 11) (Text.text "transport" Theme.subtle)
                  Stack.sized (Length.Cells 14) (Text.text "status" Theme.subtle)
                  Stack.sized Length.Fill (Text.text "tools" Theme.subtle) ]

        let rows =
            m.Mcp
            |> List.mapi (fun i s ->
                let nameStyle = if i = ms.ServerSel then Theme.selection else Theme.text

                Stack.sized
                    Length.Content
                    (Stack.hstackOf
                        [ Stack.sized (Length.Cells 14) (Text.text (" " + pad 13 s.Name) nameStyle)
                          Stack.sized (Length.Cells 11) (Text.text (pad 11 s.Transport) Theme.muted)
                          Stack.sized (Length.Cells 14) (Text.text (pad 14 s.Status) (statusStyle s.Status))
                          Stack.sized Length.Fill (Text.text (sprintf "%d" (List.length s.Tools)) Theme.muted) ]))

        let content =
            Stack.vstackOf (
                [ Stack.sized (Length.Cells 1) (Text.text " MCP servers" Theme.title)
                  Stack.sized (Length.Cells 1) header ]
                @ rows
                @ [ Stack.sized (Length.Cells 1) (Text.text " ↑↓ select · Enter manage · Esc close" Theme.subtle) ]
            )

        centered 60 (List.length m.Mcp + 5) m.Size (opaque Theme.borderStyle content)
    | McpActions ->
        let server = List.tryItem ms.ServerSel m.Mcp

        let name =
            match server with
            | Some x -> x.Name
            | None -> "?"

        let status =
            match server with
            | Some x -> x.Status
            | None -> ""

        let actionRows =
            mcpActions
            |> List.mapi (fun i a ->
                Text.text (sprintf "  %s" a) (if i = ms.ActionSel then Theme.selection else Theme.text))

        let content =
            Stack.vstack (
                [ Text.text (sprintf " %s" name) Theme.title
                  Text.text (sprintf " status: %s" status) (statusStyle status)
                  Text.text "" Theme.text ]
                @ actionRows
                @ [ Text.text "" Theme.text
                    Text.text " ↑↓ select · Enter · Esc back" Theme.subtle ]
            )

        centered 44 (List.length mcpActions + 7) m.Size (opaque Theme.borderStyle content)
    | McpTools ->
        let server = List.tryItem ms.ServerSel m.Mcp

        let name =
            match server with
            | Some x -> x.Name
            | None -> "?"

        let tools =
            match server with
            | Some x -> x.Tools
            | None -> []

        let toolNodes =
            tools |> List.map (fun t -> Text.text (sprintf " • %s" t) Theme.text)

        let content =
            Stack.vstackOf
                [ Stack.sized (Length.Cells 1) (Text.text (sprintf " %s · tools" name) Theme.title)
                  Stack.sized Length.Fill (Scroll.vertical ms.ToolScroll (Stack.vstack toolNodes))
                  Stack.sized (Length.Cells 1) (Text.text " ↑↓ scroll · Esc back" Theme.subtle) ]

        centered 44 (min 14 (List.length tools + 4)) m.Size (opaque Theme.borderStyle content)

let view (m: Model) : LayoutNode<Msg> =
    let baseTree =
        Dock.dock
            [ Dock.top 3 (header m)
              Dock.bottom 1 hints
              Dock.bottom 3 (promptBox m)
              Dock.fill (body m) ]

    let overlayLayer =
        match m.Overlay with
        | NoOverlay -> None
        | PaletteOverlay ps -> Some(palettePanel ps m)
        | SkillOverlay ss -> Some(skillPanel ss m)
        | ModalOverlay ms -> Some(modalPanel ms m)
        | McpOverlay ms -> Some(mcpPanel ms m)

    let completionLayer =
        match m.Overlay, m.Completion with
        | NoOverlay, Some(kind, sel) -> Some(completionPopup kind sel m)
        | _ -> None

    let toasts = if List.isEmpty m.Toasts then None else Some(toastLayer m)

    match [ Some baseTree; overlayLayer; completionLayer; toasts ] |> List.choose id with
    | [ single ] -> single
    | many -> LayoutNode.Overlay(rect0, many)

// ── input / subscriptions / init ───────────────────────────────────────────────
let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        let key =
            match ke.Key with
            | Char c when ke.Modifiers.Ctrl && c = "p" -> Some KCtrlP
            | Char c when ke.Modifiers.Ctrl && c = "o" -> Some KCtrlO
            | Char _ when ke.Modifiers.Ctrl -> None
            | Char c -> Some(KChar c)
            | Space -> Some(KChar " ")
            | Backspace -> Some KBackspace
            | Enter -> Some KEnter
            | Escape -> Some KEsc
            | Tab when ke.Modifiers.Shift -> Some KShiftTab
            | Tab -> Some KTab
            | ArrowUp -> Some KUp
            | ArrowDown -> Some KDown
            | ArrowLeft -> Some KLeft
            | ArrowRight -> Some KRight
            | PageUp -> Some KPageUp
            | PageDown -> Some KPageDown
            | Home -> Some KHome
            | End -> Some KEnd
            | _ -> None

        key |> Option.map KeyMsg
    | Resize sz -> Some(Resized sz)
    | _ -> None

let subscriptions (m: Model) : Sub<Msg> list =
    [ yield Sub.TerminalResize Resized
      if m.Streaming.IsSome then
          yield Sub.Every(TimeSpan.FromMilliseconds 45.0, (fun () -> StreamChunk))
      let anyRunning =
          (m.Transcript
           |> List.exists (function
               | ToolCall(_, _, Running, _, _) -> true
               | _ -> false))
          || (m.Tasks |> List.exists (fun (_, s) -> s = Running))

      if anyRunning || not (List.isEmpty m.Toasts) then
          yield Sub.Every(TimeSpan.FromMilliseconds 110.0, (fun () -> SpinnerTick)) ]

let init () : Model * Cmd<Msg> =
    let size =
        TerminalMode.getTerminalSize () |> Option.defaultValue (Size.Create(96, 32))

    { Size = size
      Transcript = [ Dummy.welcomeBlock ]
      Prompt = PromptInput.empty
      Offset = 0
      Mode = Normal
      Streaming = None
      Toasts = []
      Overlay = NoOverlay
      SidebarOpen = true
      Tasks = [ "build", Running; "tests", Succeeded; "lint", Succeeded ]
      Files = [ "└ src/"; "  App.fs"; "  Theme.fs"; "└ tests/" ]
      Spinner = 0
      Focus = PromptFocus
      Completion = None
      Pastes = []
      Mcp = mcpInitial
      Tokens = 12300
      NextId = 1 },
    Cmd.none

// ── headless --dump ────────────────────────────────────────────────────────────
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
    let laidOut = Layout.measure (Rect.FromOrigin size) node
    Layout.render surface laidOut
    printSurface label surface

let runDump () =
    // The model's Size must match the dump surface so size-derived layout (wrap width,
    // overlay centering) lines up with what's actually rendered.
    let sample (size: Size) =
        { (fst (init ())) with
            Size = size
            Transcript =
                [ UserMsg "markdown"
                  AssistantMd "# Heading\nBody with **bold** and `code`.\n- bullet one"
                  ToolCall("shell", "dotnet build", Succeeded, "1.2s", "Build succeeded.") ] }

    dumpNode "A. Agent shell" (Size.Create(72, 22)) (view (sample (Size.Create(72, 22))))

    dumpNode
        "B. Markdown kitchen-sink"
        (Size.Create(60, 24))
        (Box.box Theme.borderStyle [ Markdown.render 56 Dummy.markdownKitchenSink ])

    dumpNode
        "C. Tool calls (ok / error)"
        (Size.Create(56, 12))
        (Stack.vstack
            [ Blocks.renderBlock 52 0 (ToolCall("shell", "dotnet build", Succeeded, "1.2s", "Build succeeded."))
              Blocks.renderBlock 52 0 (ToolCall("shell", "npm test", Failed, "exit 1", "2 failed, 5 passed")) ])

    dumpNode
        "D. Diff card"
        (Size.Create(64, 7))
        (Blocks.renderBlock
            60
            0
            (DiffBlock(
                "InputParser.fs",
                [ { Sign = ' '; Text = "context" }
                  { Sign = '+'; Text = "added line" }
                  { Sign = '-'; Text = "removed line" } ]
            )))

    dumpNode
        "E. Table card"
        (Size.Create(50, 8))
        (Blocks.renderBlock
            46
            0
            (TableBlock(
                [ "ID"; "Title"; "Status" ],
                [ [ "#1"; "Focus manager"; "open" ]; [ "#2"; "Scroll blit"; "done" ] ]
            )))

    let permSpec =
        match Dummy.respond "permission" with
        | OpenModal s -> s
        | _ -> failwith "unreachable"

    dumpNode
        "F. Permission modal over shell"
        (Size.Create(72, 20))
        (view
            { sample (Size.Create(72, 20)) with
                Overlay = ModalOverlay { Spec = permSpec; Focus = DenyBtn } })

    dumpNode
        "G. Skill explorer overlay"
        (Size.Create(82, 24))
        (view
            { sample (Size.Create(82, 24)) with
                Overlay =
                    SkillOverlay
                        { Sel = 0
                          ListScroll = 0
                          PreviewScroll = 0
                          Pane = ListPane } })

    dumpNode
        "H. Command palette"
        (Size.Create(72, 22))
        (view
            { sample (Size.Create(72, 22)) with
                Overlay = PaletteOverlay { Query = "to"; Sel = 0 } })

    dumpNode
        "I. @mention completion"
        (Size.Create(72, 20))
        (view
            { sample (Size.Create(72, 20)) with
                Prompt = { Value = "see @Mire" }
                Completion = Some(MentionC, 0) })

    dumpNode
        "J. /slash completion"
        (Size.Create(72, 20))
        (view
            { sample (Size.Create(72, 20)) with
                Prompt = { Value = "/to" }
                Completion = Some(SlashC, 0) })

    dumpNode
        "K. /mcp servers"
        (Size.Create(72, 18))
        (view
            { sample (Size.Create(72, 18)) with
                Overlay =
                    McpOverlay
                        { View = McpList
                          ServerSel = 2
                          ActionSel = 0
                          ToolScroll = 0 } })

    dumpNode
        "L. /mcp actions"
        (Size.Create(72, 18))
        (view
            { sample (Size.Create(72, 18)) with
                Overlay =
                    McpOverlay
                        { View = McpActions
                          ServerSel = 0
                          ActionSel = 2
                          ToolScroll = 0 } })

    dumpNode
        "M. /mcp tools"
        (Size.Create(72, 18))
        (view
            { sample (Size.Create(72, 18)) with
                Overlay =
                    McpOverlay
                        { View = McpTools
                          ServerSel = 0
                          ActionSel = 2
                          ToolScroll = 0 } })

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
        |> Program.withSubscriptions subscriptions
        |> Runtime.run

        0
