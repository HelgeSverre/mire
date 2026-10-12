namespace Mire.Agent

open System
open Mire.Core
open Mire.Layout
open Mire.Widgets
open Mire.App

/// The shell's high-level phase, shown in the header. It is *derived* — never
/// set independently — from the shell's actual work (see `AgentShell.sync`):
/// `AwaitingApproval` while an approval is open, else `Streaming` while any reply
/// is still streaming, else `Idle`. So finishing one of two concurrent replies, or
/// finishing a reply while an approval is open, can't leave a contradictory label.
type Session =
    | Idle
    | Streaming
    | AwaitingApproval

/// An approval the shell is showing or has queued. `Id` is stable (returned by
/// `AgentShell.requestApprovalId`) so an app can withdraw a queued/open approval
/// with `cancelApproval` when the operation that asked for it goes away.
type PendingApproval =
    { Id: int
      Command: string
      AcceptFocused: bool }

/// The shell's completion-popup UI state: the highlighted candidate, and whether
/// the user dismissed the popup (Esc) for the current token.
type CompletionUi = { Selected: int; Dismissed: bool }

/// The agent shell's MVU model: the `Conversation`, the prompt, the open approval
/// and the queue behind it, the transcript scroll position and follow-tail flag,
/// the terminal size, the derived session phase, the ids of replies still
/// streaming, the completion popup state, a spinner tick, and the session's render
/// cache. The theme is carried so the helper functions need only the model.
type ShellModel =
    {
        Conversation: Conversation
        Prompt: PromptBox
        /// The approval on screen (if any). Further requests queue in `ApprovalQueue`.
        Approval: PendingApproval option
        /// Approvals waiting behind the open one, oldest first (FIFO).
        ApprovalQueue: PendingApproval list
        NextApprovalId: int
        /// Transcript scroll offset (rows from the top) — meaningful when not following.
        Offset: int
        /// Follow the tail: the view pins the transcript to the newest output. Turned
        /// off when the user scrolls up, back on when they scroll to the bottom, submit,
        /// or jump to latest. New output never forces it back on.
        Following: bool
        Size: Size
        /// Derived from `Streams`/`Approval` by `AgentShell.sync` — read it, don't set it.
        Session: Session
        /// Ids of assistant replies still streaming (concurrent replies are allowed).
        Streams: Set<MessageId>
        Completion: CompletionUi
        Frame: int
        Theme: AppTheme
        /// Per-session transcript cache (one revision per message; see `TranscriptCache`).
        Cache: TranscriptCache
    }

type ShellMsg =
    /// A key, routed by context in `update` (approval → completion popup → prompt).
    | KeyPress of KeyEvent
    | Edit of InputEvent
    | Submit
    /// Insert a newline into the prompt (Shift/Alt+Enter, Ctrl+J).
    | InsertNewline
    | EscKey
    | HistoryPrev
    | HistoryNext
    | ToggleButton
    /// Click on an approval button (true = Accept).
    | ApprovalClick of bool
    /// Scroll the transcript by rows (negative = up).
    | ScrollBy of int
    /// Scroll the transcript by pages (negative = up).
    | ScrollPage of int
    /// Jump to the newest output and resume following it.
    | JumpToLatest
    /// Cancel the work in progress: stop all streaming replies (late chunks are
    /// dropped) and call the app's `OnCancel`.
    | Cancel
    | Resized of Size
    | Tick
    /// Inject a model update from a command — the streaming hook. An async reply
    /// dispatches `Apply (AgentShell.stream id chunk)` (and friends) to fold results
    /// back into the conversation between frames.
    | Apply of (ShellModel -> ShellModel)

/// What an agent shell needs from its app: the theme, a header title, the prompt
/// placeholder, and the behavior callbacks. `OnSubmit` runs when the user submits
/// a line (the user message has already been appended); `OnApprove` runs when an
/// approval is accepted/denied; `OnCancel` when the user cancels (Esc while a
/// reply streams) — cancel your own async work there. `Completions` supplies
/// candidates for a token starting with one of `Triggers` (return `[]` for none);
/// `MaxPromptRows` caps how tall the prompt grows for multi-line input. Build one
/// with `AgentShell.config` and override fields with `{ c with … }`.
type ShellConfig =
    { Theme: AppTheme
      Title: string
      Placeholder: string
      OnSubmit: string -> ShellModel -> ShellModel * Cmd<ShellMsg>
      OnApprove: bool -> string -> ShellModel -> ShellModel * Cmd<ShellMsg>
      OnCancel: ShellModel -> ShellModel * Cmd<ShellMsg>
      Triggers: char list
      Completions: CompletionToken -> string list
      MaxPromptRows: int }

/// Composes the agent layer — `ChatTranscript` + `PromptBox` + `ApprovalModal` +
/// `Conversation` — into a ready-made MVU `Program`, parameterized by app callbacks.
/// The shell owns transcript scroll/follow-tail, prompt editing + history +
/// completion, the approval modal and its queue, context-aware key routing, and the
/// spinner tick; the app supplies only *what a submission/approval does*.
/// `AgentShell.program config |> Runtime.run` is a working shell. The helper
/// functions below are what `OnSubmit`/`OnApprove` (and `Apply` updaters) use to
/// mutate the conversation.
///
/// Keys: Enter submits (Shift/Alt+Enter or Ctrl+J insert a newline); ←/→/Home/End,
/// Shift-selection and word chords edit the prompt; ↑/↓ move between prompt lines
/// and recall history from the first/last line; PgUp/PgDn and the wheel scroll the
/// transcript; Esc cancels a streaming reply, else jumps back to the latest output.
/// With an approval open: ←/→/Tab move focus, Enter confirms, Esc denies, or click
/// a button. With a completion popup: ↑/↓ select, Tab/Enter accept, Esc dismisses.
module AgentShell =

    let private rect0 = Rect.Create(0, 0, 0, 0)
    let private headerH = 3

    /// A config with the required pieces and conventional defaults: no-op cancel,
    /// no completions (`/` and `@` triggers), and a prompt of up to 6 rows.
    let config
        (theme: AppTheme)
        (title: string)
        (placeholder: string)
        (onSubmit: string -> ShellModel -> ShellModel * Cmd<ShellMsg>)
        (onApprove: bool -> string -> ShellModel -> ShellModel * Cmd<ShellMsg>)
        : ShellConfig =
        { Theme = theme
          Title = title
          Placeholder = placeholder
          OnSubmit = onSubmit
          OnApprove = onApprove
          OnCancel = fun m -> m, Cmd.none
          Triggers = [ '/'; '@' ]
          Completions = fun _ -> []
          MaxPromptRows = 6 }

    /// Wrap width for transcript content (the box borders eat the margins).
    let wrapWidth (m: ShellModel) = max 10 (m.Size.Width - 4)

    /// Columns available to the prompt's text (box border + the `❯ ` glyph).
    let private promptTextWidth (m: ShellModel) = max 1 (m.Size.Width - 4)

    /// Visible prompt rows for the current text, from 1 to `maxRows`.
    let promptRows (maxRows: int) (m: ShellModel) =
        max 1 (min (max 1 maxRows) (PromptBox.visualRows (promptTextWidth m) m.Prompt))

    /// Height of the prompt box (rows + border), sized for its text.
    let private promptHeight (maxRows: int) (m: ShellModel) = promptRows maxRows m + 2

    /// Rows available to the transcript viewport (screen minus header + prompt +
    /// the transcript box border), for a prompt capped at `maxRows` rows.
    let viewportRowsFor (maxRows: int) (m: ShellModel) =
        max 1 (m.Size.Height - headerH - promptHeight maxRows m - 2)

    /// Rows available to the transcript viewport, for a single-row prompt.
    let viewportRows (m: ShellModel) = viewportRowsFor 1 m

    // --- derived state ----------------------------------------------------------

    /// Re-derive `Session` from the actual work: an open approval, else any live
    /// stream, else idle. Every helper and `update` runs this, so the label can't
    /// drift from the state it describes.
    let sync (m: ShellModel) : ShellModel =
        let session =
            if m.Approval.IsSome then AwaitingApproval
            elif not m.Streams.IsEmpty then Streaming
            else Idle

        if session = m.Session then
            m
        else
            { m with Session = session }

    /// True while a reply streams or an approval is pending.
    let isBusy (m: ShellModel) =
        m.Approval.IsSome || not m.Streams.IsEmpty

    // --- scrolling ----------------------------------------------------------------

    let private keyed (m: ShellModel) = Conversation.keyed m.Conversation

    let private bottomFor (maxRows: int) (m: ShellModel) =
        ChatTranscript.toBottomKeyed m.Cache m.Theme (wrapWidth m) m.Frame (viewportRowsFor maxRows m) (keyed m)

    /// The effective top row of the transcript viewport.
    let private currentOffset (maxRows: int) (m: ShellModel) =
        let bottom = bottomFor maxRows m
        if m.Following then bottom else max 0 (min m.Offset bottom)

    /// Pin the view to the newest output and resume following it (the explicit
    /// jump — e.g. after the user submits). Output arriving while the user is
    /// scrolled up does *not* call this.
    let followTail (m: ShellModel) : ShellModel =
        { m with
            Following = true
            Offset = bottomFor 1 m }

    /// Scroll by `delta` rows; reaching the bottom resumes following.
    let scrollBy (maxRows: int) (delta: int) (m: ShellModel) : ShellModel =
        let bottom = bottomFor maxRows m
        let target = max 0 (min bottom (currentOffset maxRows m + delta))

        { m with
            Offset = target
            Following = target >= bottom }

    /// The viewport's anchor: the index of the entry at the top row and how many
    /// of its rows are above the viewport — stable across a re-wrap.
    let private anchorOf (maxRows: int) (m: ShellModel) : (int * int) option =
        let off = currentOffset maxRows m
        let items = keyed m
        let mutable top = 0
        let mutable found = None

        items
        |> List.iteri (fun i (k, b) ->
            if found.IsNone then
                let h = ChatTranscript.keyedHeight m.Cache m.Theme (wrapWidth m) m.Frame k b

                if off < top + h then
                    found <- Some(i, off - top)
                else
                    top <- top + h)

        found

    let private offsetOfAnchor (m: ShellModel) (i: int, rel: int) : int =
        let items = keyed m |> List.toArray
        let mutable top = 0

        for j in 0 .. min (i - 1) (items.Length - 1) do
            let (k, b) = items.[j]
            top <- top + ChatTranscript.keyedHeight m.Cache m.Theme (wrapWidth m) m.Frame k b

        let h =
            if i < items.Length then
                let (k, b) = items.[i]
                ChatTranscript.keyedHeight m.Cache m.Theme (wrapWidth m) m.Frame k b
            else
                1

        top + min rel (max 0 (h - 1))

    // --- model helpers (used by the app's OnSubmit/OnApprove/Apply updaters) -----

    /// Append the user's message and jump to it (submitting is an explicit "show
    /// me the latest").
    let addUser (text: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.addUser text m.Conversation }
        |> followTail

    let addAssistant (md: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.addAssistant md m.Conversation }

    let addNotice (tone: AppTheme.Tone) (s: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.addNotice tone s m.Conversation }

    let addBlock (block: TranscriptBlock) (m: ShellModel) =
        { m with
            Conversation = Conversation.addBlock block m.Conversation }

    /// Start a streaming assistant reply; returns its id and the model (now
    /// `Streaming`). Several replies may stream at once; each is tracked by id.
    let startReply (m: ShellModel) : MessageId * ShellModel =
        let id, conv = Conversation.startAssistant m.Conversation

        id,
        { m with
            Conversation = conv
            Streams = m.Streams.Add id }
        |> sync

    /// Stream a chunk into reply `id` (curries to an `Apply`). Cheap per chunk:
    /// it only appends — the follow-tail scroll is resolved once per frame by the
    /// view, not per token. A chunk for a reply that was finished or cancelled is
    /// dropped.
    let stream (id: MessageId) (chunk: string) (m: ShellModel) =
        if m.Streams.Contains id then
            { m with
                Conversation = Conversation.appendText id chunk m.Conversation }
        else
            m

    /// Finish streaming reply `id`. The session returns to `Idle` only when no
    /// other reply is streaming and no approval is open.
    let finishReply (id: MessageId) (m: ShellModel) =
        { m with
            Conversation = Conversation.finishStreaming id m.Conversation
            Streams = m.Streams.Remove id }
        |> sync

    /// Finish reply `id` as failed, appending an error block with `message`.
    let failReply (id: MessageId) (message: string) (m: ShellModel) =
        let m = finishReply id m

        { m with
            Conversation = Conversation.addError message m.Conversation }

    /// Stop every streaming reply (their text so far is kept); later chunks for
    /// them are dropped. Used by `Cancel`.
    let stopStreams (m: ShellModel) =
        let conv =
            m.Streams
            |> Set.fold (fun c id -> Conversation.finishStreaming id c) m.Conversation

        { m with
            Conversation = conv
            Streams = Set.empty }
        |> sync

    /// Append a tool call; returns its id for later `setTool` transitions.
    let addTool (name: string) (cmd: string) (status: ToolStatus) (m: ShellModel) : MessageId * ShellModel =
        let id, conv = Conversation.addToolCall name cmd status m.Conversation
        id, { m with Conversation = conv }

    let setTool (id: MessageId) (status: ToolStatus) (meta: string) (output: string) (m: ShellModel) =
        { m with
            Conversation = Conversation.setTool id status meta output m.Conversation }

    /// Raise an approval for `command` and return its id. If one is already open,
    /// this one queues behind it (FIFO) and opens when the earlier ones resolve —
    /// a request never silently replaces a pending approval.
    let requestApprovalId (command: string) (m: ShellModel) : int * ShellModel =
        let a =
            { Id = m.NextApprovalId
              Command = command
              AcceptFocused = true }

        let m =
            { m with
                NextApprovalId = m.NextApprovalId + 1 }

        a.Id,
        (match m.Approval with
         | None -> { m with Approval = Some a }
         | Some _ ->
             { m with
                 ApprovalQueue = m.ApprovalQueue @ [ a ] })
        |> sync

    /// Raise an approval prompt for `command` (Accept focused; queued if another
    /// is open); session → `AwaitingApproval`.
    let requestApproval (command: string) (m: ShellModel) = requestApprovalId command m |> snd

    /// Close the open approval and open the next queued one, if any.
    let private advanceApproval (m: ShellModel) =
        match m.ApprovalQueue with
        | next :: rest ->
            { m with
                Approval = Some next
                ApprovalQueue = rest }
        | [] -> { m with Approval = None }
        |> sync

    /// Withdraw approval `id`, whether open or queued (without calling `OnApprove`).
    let cancelApproval (id: int) (m: ShellModel) =
        match m.Approval with
        | Some a when a.Id = id -> advanceApproval m
        | _ ->
            { m with
                ApprovalQueue = m.ApprovalQueue |> List.filter (fun a -> a.Id <> id) }
            |> sync

    // --- MVU --------------------------------------------------------------------

    let init (cfg: ShellConfig) () : ShellModel * Cmd<ShellMsg> =
        { Conversation = Conversation.empty
          Prompt = PromptBox.empty
          Approval = None
          ApprovalQueue = []
          NextApprovalId = 0
          Offset = 0
          Following = true
          Size =
            Mire.Protocol.TerminalMode.getTerminalSize ()
            |> Option.defaultValue (Size.Create(80, 24))
          Session = Idle
          Streams = Set.empty
          Completion = { Selected = 0; Dismissed = false }
          Frame = 0
          Theme = cfg.Theme
          Cache = TranscriptCache() },
        Cmd.none

    /// The completion popup's content right now: the token under the caret and its
    /// candidates (from `cfg.Completions`), unless dismissed or there are none.
    let activeCompletion (cfg: ShellConfig) (m: ShellModel) : (CompletionToken * string list) option =
        if m.Completion.Dismissed || m.Approval.IsSome then
            None
        else
            PromptBox.completion cfg.Triggers cfg.Completions m.Prompt

    let private resetCompletion (m: ShellModel) =
        { m with
            Completion = { Selected = 0; Dismissed = false } }

    /// Resolve the open approval (accept/deny): advance the queue, then `OnApprove`.
    let private resolveApproval (cfg: ShellConfig) (accepted: bool) (m: ShellModel) =
        match m.Approval with
        | Some a -> cfg.OnApprove accepted a.Command (advanceApproval m)
        | None -> m, Cmd.none

    let private editPrompt (e: InputEvent) (m: ShellModel) =
        { m with
            Prompt = PromptBox.applyInput e m.Prompt }
        |> resetCompletion

    let rec private updateCore (cfg: ShellConfig) (msg: ShellMsg) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
        let rows = cfg.MaxPromptRows

        match msg with
        | Resized s ->
            // Keep the same content at the top of the viewport across the re-wrap
            // (unless following, which simply stays pinned to the bottom).
            if m.Following then
                { m with Size = s }, Cmd.none
            else
                let anchor = anchorOf rows m
                let m = { m with Size = s }

                (match anchor with
                 | Some a -> { m with Offset = offsetOfAnchor m a }
                 | None -> m),
                Cmd.none
        | Tick -> { m with Frame = m.Frame + 1 }, Cmd.none
        | Apply f -> f m, Cmd.none
        | ScrollBy d -> scrollBy rows d m, Cmd.none
        | ScrollPage n -> scrollBy rows (n * max 1 (viewportRowsFor rows m - 1)) m, Cmd.none
        | JumpToLatest -> followTail m, Cmd.none
        | Cancel ->
            if m.Streams.IsEmpty then
                m, Cmd.none
            else
                let m = stopStreams m |> addNotice AppTheme.Warning "cancelled"

                cfg.OnCancel m
        | ApprovalClick accepted -> resolveApproval cfg accepted m
        | Edit e ->
            match m.Approval with
            | Some _ -> m, Cmd.none // the modal swallows editing
            | None -> editPrompt e m, Cmd.none
        | InsertNewline ->
            match m.Approval with
            | Some _ -> m, Cmd.none
            | None ->
                { m with
                    Prompt = PromptBox.applyAction EditAction.Newline m.Prompt }
                |> resetCompletion,
                Cmd.none
        | HistoryPrev ->
            match m.Approval with
            | Some _ -> m, Cmd.none
            | None ->
                { m with
                    Prompt = PromptBox.historyPrev m.Prompt }
                |> resetCompletion,
                Cmd.none
        | HistoryNext ->
            match m.Approval with
            | Some _ -> m, Cmd.none
            | None ->
                { m with
                    Prompt = PromptBox.historyNext m.Prompt }
                |> resetCompletion,
                Cmd.none
        | ToggleButton ->
            match m.Approval with
            | Some a ->
                { m with
                    Approval =
                        Some
                            { a with
                                AcceptFocused = not a.AcceptFocused } },
                Cmd.none
            | None -> m, Cmd.none
        | Submit ->
            match m.Approval with
            | Some a -> resolveApproval cfg a.AcceptFocused m
            | None ->
                let text = (PromptBox.value m.Prompt).Trim()

                if text = "" then
                    m, Cmd.none
                else
                    let _, prompt = PromptBox.submit m.Prompt
                    let m = { m with Prompt = prompt } |> resetCompletion |> addUser text
                    cfg.OnSubmit text m
        | EscKey ->
            match m.Approval with
            | Some _ -> resolveApproval cfg false m
            | None ->
                if not m.Streams.IsEmpty then updateCore cfg Cancel m
                elif not m.Following then followTail m, Cmd.none
                else m, Cmd.none
        | KeyPress ke ->
            let mods = ke.Modifiers
            let plain = not mods.Ctrl && not mods.Alt && not mods.Meta && not mods.Shift

            if ke.EventType = Release then
                m, Cmd.none
            else
                match m.Approval with
                | Some _ ->
                    // The modal owns the keyboard: focus, confirm, deny.
                    match ke.Key with
                    | ArrowLeft
                    | ArrowRight
                    | Tab -> updateCore cfg ToggleButton m
                    | Enter -> updateCore cfg Submit m
                    | Escape -> updateCore cfg EscKey m
                    | _ -> m, Cmd.none
                | None ->
                    match activeCompletion cfg m, ke.Key with
                    | Some(_, cands), ArrowUp ->
                        let n = List.length cands

                        { m with
                            Completion =
                                { m.Completion with
                                    Selected = (m.Completion.Selected - 1 + n) % n } },
                        Cmd.none
                    | Some(_, cands), ArrowDown ->
                        { m with
                            Completion =
                                { m.Completion with
                                    Selected = (m.Completion.Selected + 1) % List.length cands } },
                        Cmd.none
                    | Some(tok, cands), (Tab | Enter) when plain || ke.Key = Tab ->
                        let pick = List.item (min m.Completion.Selected (List.length cands - 1)) cands

                        { m with
                            Prompt = PromptBox.acceptCompletion tok pick m.Prompt }
                        |> resetCompletion,
                        Cmd.none
                    | Some _, Escape ->
                        { m with
                            Completion = { m.Completion with Dismissed = true } },
                        Cmd.none
                    | _ ->
                        match ke.Key with
                        | Enter when mods.Shift || mods.Alt -> updateCore cfg InsertNewline m
                        | Char "j" when mods.Ctrl -> updateCore cfg InsertNewline m
                        | Enter -> updateCore cfg Submit m
                        | Escape -> updateCore cfg EscKey m
                        | PageUp -> updateCore cfg (ScrollPage -1) m
                        | PageDown -> updateCore cfg (ScrollPage 1) m
                        | ArrowUp when plain && fst (TextBuffer.cursorRowCol m.Prompt.Buffer) = 0 ->
                            updateCore cfg HistoryPrev m
                        | ArrowDown when
                            plain
                            && fst (TextBuffer.cursorRowCol m.Prompt.Buffer) = m.Prompt.Buffer.Text.Split('\n').Length
                                                                               - 1
                            ->
                            updateCore cfg HistoryNext m
                        | Tab -> m, Cmd.none
                        | _ -> editPrompt (Key ke) m, Cmd.none

    let update (cfg: ShellConfig) (msg: ShellMsg) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
        let m, cmd = updateCore cfg msg m
        sync m, cmd

    let private sessionLabel (m: ShellModel) =
        match m.Session with
        | Idle -> ""
        | Streaming ->
            if m.Streams.Count > 1 then
                sprintf "streaming %d replies…" m.Streams.Count
            else
                "streaming…"
        | AwaitingApproval ->
            if m.ApprovalQueue.IsEmpty then
                "approval"
            else
                sprintf "approval (+%d queued)" m.ApprovalQueue.Length

    let view (cfg: ShellConfig) (m: ShellModel) : LayoutNode<ShellMsg> =
        let theme = m.Theme
        let rows = promptRows cfg.MaxPromptRows m
        let promptH = rows + 2

        let header =
            Box.box
                theme.border
                [ Stack.hstackOf
                      [ Stack.sized Length.Content (Text.text (" " + cfg.Title) theme.title)
                        Stack.flex
                        Stack.sized Length.Content (Text.text (sessionLabel m) theme.fgSubtle) ] ]

        let prompt =
            Box.box
                theme.border
                [ PromptBox.renderRows
                      (m.Size.Width - 2)
                      rows
                      theme.accent
                      theme.fg
                      theme.selection
                      theme.fgSubtle
                      cfg.Placeholder
                      m.Approval.IsNone
                      m.Prompt ]

        // Follow-tail is resolved here, once per frame (Int32.MaxValue clamps to
        // the bottom), rather than re-measured on every streamed token.
        let offset = if m.Following then System.Int32.MaxValue else m.Offset

        let transcript =
            Box.box
                theme.border
                [ ChatTranscript.viewKeyed
                      m.Cache
                      theme
                      (wrapWidth m)
                      m.Frame
                      (viewportRowsFor cfg.MaxPromptRows m)
                      offset
                      theme.border
                      theme.fgMuted
                      (keyed m) ]

        let baseTree =
            Dock.dock [ Dock.top headerH header; Dock.bottom promptH prompt; Dock.fill transcript ]

        let completionLayer =
            match activeCompletion cfg m with
            | Some(_, cands) ->
                let row, col = TextBuffer.cursorRowColumn m.Prompt.Buffer
                let promptTop = m.Size.Height - promptH
                let anchorY = promptTop + 1 + min row (rows - 1)
                let anchorX = min (m.Size.Width - 1) (3 + col)

                let width =
                    min (m.Size.Width - 2) (max 12 ((cands |> List.map String.length |> List.max) + 4))

                [ Completion.view
                      m.Size.Width
                      m.Size.Height
                      anchorX
                      anchorY
                      width
                      8
                      theme.border
                      theme.selection
                      theme.fg
                      (min m.Completion.Selected (List.length cands - 1))
                      cands ]
            | None -> []

        let approvalLayer =
            match m.Approval with
            | Some a ->
                let title =
                    if m.ApprovalQueue.IsEmpty then
                        "Permission required"
                    else
                        sprintf "Permission required (%d more queued)" m.ApprovalQueue.Length

                [ ApprovalModal.view
                      theme
                      title
                      "A tool wants to run:"
                      a.Command
                      (Some "writes files")
                      "Accept"
                      "Deny"
                      a.AcceptFocused ]
            | None -> []

        match completionLayer @ approvalLayer with
        | [] -> baseTree
        | layers -> LayoutNode.Overlay(rect0, baseTree :: layers)

    /// Map input to shell messages. Keys are passed through as `KeyPress` and
    /// routed by context in `update` (the modal, the completion popup, or the
    /// prompt editor); the mouse wheel scrolls the transcript.
    let mapInput (e: InputEvent) : ShellMsg option =
        match e with
        | Key ke -> Some(KeyPress ke)
        | Paste _ -> Some(Edit e)
        | Resize s -> Some(Resized s)
        | Mouse me ->
            match me.Button with
            | ScrollUp -> Some(ScrollBy -3)
            | ScrollDown -> Some(ScrollBy 3)
            | _ -> None
        | _ -> None

    /// Route clicks on the approval modal's buttons (hit-tested by the runtime).
    let onMouseRegion (region: RegionId option) (me: MouseEvent) : ShellMsg option =
        if me.Pressed && not me.Moved && me.Button = MouseButton.Left then
            if region = Some ApprovalModal.acceptRegion then
                Some(ApprovalClick true)
            elif region = Some ApprovalModal.denyRegion then
                Some(ApprovalClick false)
            else
                None
        else
            None

    let subscriptions (_: ShellModel) : Sub<ShellMsg> list =
        [ Sub.TerminalResize Resized
          Sub.Every(TimeSpan.FromMilliseconds 120.0, fun () -> Tick) ]

    /// Build the complete shell `Program` — `AgentShell.program config |> Runtime.run`.
    let program (cfg: ShellConfig) : Program<ShellModel, ShellMsg> =
        Program.create (init cfg) (update cfg) (view cfg)
        |> Program.withMapInput mapInput
        |> Program.withMouseRegion onMouseRegion
        |> Program.withSubscriptions subscriptions
