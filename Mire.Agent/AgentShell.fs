namespace Mire.Agent

open System
open Mire.Core
open Mire.Layout
open Mire.Widgets
open Mire.App

/// The shell's high-level phase. Drives the header hint and (with the spinner tick)
/// any "working" affordance. Idle = waiting for input; Streaming = a reply is being
/// appended; AwaitingApproval = blocked on the approval modal.
type Session =
    | Idle
    | Streaming
    | AwaitingApproval

/// The agent shell's MVU model: the `Conversation`, the prompt, an optional pending
/// approval (command + which button is focused), the scroll offset, the terminal
/// size, the session phase, and a spinner tick. The theme is carried so the helper
/// functions (which the app's callbacks use) need only the model.
type ShellModel =
    { Conversation: Conversation
      Prompt: PromptBox
      Approval: (string * bool) option
      Offset: int
      Size: Size
      Session: Session
      Frame: int
      Theme: AppTheme }

type ShellMsg =
    | Edit of InputEvent
    | Submit
    | EscKey
    | HistoryPrev
    | HistoryNext
    | ToggleButton
    | Resized of Size
    | Tick
    /// Inject a model update from a command — the streaming hook. An async reply
    /// dispatches `Apply (AgentShell.stream id chunk)` (and friends) to fold results
    /// back into the conversation between frames.
    | Apply of (ShellModel -> ShellModel)

/// What an agent shell needs from its app: the theme, a header title, the prompt
/// placeholder, and the two behavior callbacks. `OnSubmit` runs when the user submits
/// a line (the user message has already been appended); `OnApprove` runs when an
/// approval is accepted/denied. Both return the updated model + a command — return a
/// `Cmd.ofAsync` that dispatches `Apply` to stream a reply in.
type ShellConfig =
    { Theme: AppTheme
      Title: string
      Placeholder: string
      OnSubmit: string -> ShellModel -> ShellModel * Cmd<ShellMsg>
      OnApprove: bool -> string -> ShellModel -> ShellModel * Cmd<ShellMsg> }

/// Composes the agent layer — `ChatTranscript` + `PromptBox` + `ApprovalModal` +
/// `Conversation` — into a ready-made MVU `Program`, parameterized by app callbacks.
/// The shell owns transcript scroll/follow-tail, prompt editing + history, the
/// approval modal, key routing, and the spinner tick; the app supplies only *what a
/// submission/approval does*. `AgentShell.program config |> Runtime.run` is a working
/// shell. The helper functions below are what `OnSubmit`/`OnApprove` (and `Apply`
/// updaters) use to mutate the conversation.
module AgentShell =

    let private rect0 = Rect.Create(0, 0, 0, 0)
    let private headerH = 3
    let private promptH = 3

    /// Wrap width for transcript/prompt content (the box borders eat the margins).
    let wrapWidth (m: ShellModel) = max 10 (m.Size.Width - 4)

    /// Rows available to the transcript viewport (screen minus header + prompt + box border).
    let viewportRows (m: ShellModel) =
        max 1 (m.Size.Height - headerH - promptH - 2)

    // --- model helpers (used by the app's OnSubmit/OnApprove/Apply updaters) -----

    /// Scroll so the newest output is in view.
    let followTail (m: ShellModel) : ShellModel =
        { m with
            Offset =
                ChatTranscript.toBottom
                    m.Theme
                    (wrapWidth m)
                    m.Frame
                    (viewportRows m)
                    (Conversation.blocks m.Conversation) }

    let addUser (text: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.addUser text m.Conversation }
        |> followTail

    let addAssistant (md: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.addAssistant md m.Conversation }
        |> followTail

    let addNotice (tone: AppTheme.Tone) (s: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.addNotice tone s m.Conversation }
        |> followTail

    let addBlock (block: TranscriptBlock) (m: ShellModel) =
        { m with
            Conversation = Conversation.addBlock block m.Conversation }
        |> followTail

    /// Start a streaming assistant reply; returns its id and the model (now `Streaming`).
    let startReply (m: ShellModel) : MessageId * ShellModel =
        let id, conv = Conversation.startAssistant m.Conversation

        id,
        { m with
            Conversation = conv
            Session = Streaming }
        |> followTail

    /// Stream a chunk into reply `id`, keeping the tail in view (curries to an `Apply`).
    let stream (id: MessageId) (chunk: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.appendText id chunk m.Conversation }
        |> followTail

    /// Finish a streaming reply (back to `Idle`).
    let finishReply (id: MessageId) (m: ShellModel) =
        { m with
            Conversation = Conversation.finishStreaming id m.Conversation
            Session = Idle }

    /// Append a tool call; returns its id for later `setTool` transitions.
    let addTool (name: string) (cmd: string) (status: ToolStatus) (m: ShellModel) : MessageId * ShellModel =
        let id, conv = Conversation.addToolCall name cmd status m.Conversation
        id, { m with Conversation = conv } |> followTail

    let setTool (id: MessageId) (status: ToolStatus) (meta: string) (output: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.setTool id status meta output m.Conversation }
        |> followTail

    /// Raise an approval prompt for `command` (Accept focused); session → `AwaitingApproval`.
    let requestApproval (command: string) (m: ShellModel) =
        { m with
            Approval = Some(command, true)
            Session = AwaitingApproval }

    // --- MVU --------------------------------------------------------------------

    let init (cfg: ShellConfig) () : ShellModel * Cmd<ShellMsg> =
        { Conversation = Conversation.empty
          Prompt = PromptBox.empty
          Approval = None
          Offset = 0
          Size =
            Mire.Protocol.TerminalMode.getTerminalSize ()
            |> Option.defaultValue (Size.Create(80, 24))
          Session = Idle
          Frame = 0
          Theme = cfg.Theme },
        Cmd.none

    let update (cfg: ShellConfig) (msg: ShellMsg) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
        match msg with
        | Resized s -> { m with Size = s } |> followTail, Cmd.none
        | Tick ->
            let m = { m with Frame = m.Frame + 1 }
            (if m.Session = Streaming then followTail m else m), Cmd.none
        | Apply f -> f m, Cmd.none
        | Edit e ->
            match m.Approval with
            | Some _ -> m, Cmd.none // the modal swallows editing
            | None ->
                { m with
                    Prompt = PromptBox.applyInput e m.Prompt },
                Cmd.none
        | HistoryPrev ->
            match m.Approval with
            | Some _ -> m, Cmd.none
            | None ->
                { m with
                    Prompt = PromptBox.historyPrev m.Prompt },
                Cmd.none
        | HistoryNext ->
            match m.Approval with
            | Some _ -> m, Cmd.none
            | None ->
                { m with
                    Prompt = PromptBox.historyNext m.Prompt },
                Cmd.none
        | ToggleButton ->
            match m.Approval with
            | Some(c, f) -> { m with Approval = Some(c, not f) }, Cmd.none
            | None -> m, Cmd.none
        | Submit ->
            match m.Approval with
            | Some(cmd, accepted) ->
                let m =
                    { m with
                        Approval = None
                        Session = Idle }

                cfg.OnApprove accepted cmd m
            | None ->
                let text = (PromptBox.value m.Prompt).Trim()

                if text = "" then
                    m, Cmd.none
                else
                    let _, prompt = PromptBox.submit m.Prompt
                    let m = { m with Prompt = prompt } |> addUser text
                    cfg.OnSubmit text m
        | EscKey ->
            match m.Approval with
            | Some(cmd, _) ->
                let m =
                    { m with
                        Approval = None
                        Session = Idle }

                cfg.OnApprove false cmd m
            | None -> m, Cmd.none

    let private sessionLabel =
        function
        | Idle -> ""
        | Streaming -> "streaming…"
        | AwaitingApproval -> "approval"

    let view (cfg: ShellConfig) (m: ShellModel) : LayoutNode<ShellMsg> =
        let theme = m.Theme

        let header =
            Box.box
                theme.border
                [ Stack.hstackOf
                      [ Stack.sized Length.Content (Text.text (" " + cfg.Title) theme.title)
                        Stack.flex
                        Stack.sized Length.Content (Text.text (sessionLabel m.Session) theme.fgSubtle) ] ]

        let prompt =
            Box.box
                theme.border
                [ PromptBox.render
                      (m.Size.Width - 2)
                      theme.accent
                      theme.fg
                      theme.selection
                      theme.fgSubtle
                      cfg.Placeholder
                      m.Approval.IsNone
                      m.Prompt ]

        let transcript =
            Box.box
                theme.border
                [ ChatTranscript.view
                      theme
                      (wrapWidth m)
                      m.Frame
                      (viewportRows m)
                      m.Offset
                      theme.border
                      theme.fgMuted
                      (Conversation.blocks m.Conversation) ]

        let baseTree =
            Dock.dock [ Dock.top headerH header; Dock.bottom promptH prompt; Dock.fill transcript ]

        match m.Approval with
        | None -> baseTree
        | Some(cmd, accepted) ->
            LayoutNode.Overlay(
                rect0,
                [ baseTree
                  ApprovalModal.view
                      theme
                      "Permission required"
                      "A tool wants to run:"
                      cmd
                      (Some "writes files")
                      "Accept"
                      "Deny"
                      accepted ]
            )

    let mapInput (e: InputEvent) : ShellMsg option =
        match e with
        | Key ke ->
            match ke.Key with
            | Enter -> Some Submit
            | Escape -> Some EscKey
            | Tab
            | ArrowLeft
            | ArrowRight -> Some ToggleButton // moves the modal's focus when one is open
            | ArrowUp -> Some HistoryPrev
            | ArrowDown -> Some HistoryNext
            | Char _
            | Space
            | Backspace
            | Delete -> Some(Edit e)
            | _ -> None
        | Paste _ -> Some(Edit e)
        | Resize s -> Some(Resized s)
        | _ -> None

    let subscriptions (_: ShellModel) : Sub<ShellMsg> list =
        [ Sub.TerminalResize Resized
          Sub.Every(TimeSpan.FromMilliseconds 120.0, fun () -> Tick) ]

    /// Build the complete shell `Program` — `AgentShell.program config |> Runtime.run`.
    let program (cfg: ShellConfig) : Program<ShellModel, ShellMsg> =
        Program.create (init cfg) (update cfg) (view cfg)
        |> Program.withMapInput mapInput
        |> Program.withSubscriptions subscriptions
