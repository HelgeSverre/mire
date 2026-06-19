namespace Mire.Agent

open Mire.Widgets

/// A stable identifier for a conversation entry — handed back by the `add*`/`start*`
/// helpers so the app can update that entry later (stream tokens into it, flip a
/// tool call's status) without tracking list positions.
type MessageId = int

/// One conversation entry: a `TranscriptBlock` with a stable id and a streaming flag
/// (true while an assistant message is still being appended to). The block is what
/// `ChatTranscript` renders; the id/flag are the model's bookkeeping.
type Entry =
    { Id: MessageId
      Block: TranscriptBlock
      Streaming: bool }

/// The typed conversation model — an ordered list of identified entries layered over
/// `TranscriptBlock`, with a tool-call lifecycle and streaming helpers. Pure and
/// testable: the app holds one in its MVU model and renders `Conversation.blocks`
/// through `ChatTranscript`. The scroll offset / follow-tail stays app-owned (see
/// `ChatTranscript.toBottom`).
type Conversation =
    { Entries: Entry list
      NextId: MessageId }

module Conversation =

    let empty: Conversation = { Entries = []; NextId = 0 }

    /// The entries in order (oldest first).
    let entries (c: Conversation) = c.Entries

    /// The blocks in order — feed this to `ChatTranscript.view`/`render`.
    let blocks (c: Conversation) : TranscriptBlock list = c.Entries |> List.map (fun e -> e.Block)

    let isEmpty (c: Conversation) = List.isEmpty c.Entries

    /// Append a block, returning its new id and the updated conversation.
    let add (block: TranscriptBlock) (c: Conversation) : MessageId * Conversation =
        let id = c.NextId

        id,
        { Entries = c.Entries @ [ { Id = id; Block = block; Streaming = false } ]
          NextId = id + 1 }

    /// Append a block, discarding the id (when you won't update the entry later).
    let addBlock (block: TranscriptBlock) (c: Conversation) : Conversation = add block c |> snd

    let addUser (text: string) c = addBlock (UserMsg text) c
    let addAssistant (md: string) c = addBlock (AssistantMd md) c
    let addThinking (text: string) c = addBlock (Thinking text) c
    let addNotice (tone: AppTheme.Tone) (text: string) c = addBlock (Notice(tone, text)) c
    let addError (text: string) c = addBlock (ErrorBlock text) c

    // --- streaming assistant message ----------------------------------------

    let private mapEntry (id: MessageId) (f: Entry -> Entry) (c: Conversation) : Conversation =
        { c with
            Entries = c.Entries |> List.map (fun e -> if e.Id = id then f e else e) }

    /// Start an empty assistant message in the **streaming** state; returns its id so
    /// the app can `appendText` chunks into it and `finishStreaming` when done.
    let startAssistant (c: Conversation) : MessageId * Conversation =
        let id = c.NextId

        id,
        { Entries = c.Entries @ [ { Id = id; Block = AssistantMd ""; Streaming = true } ]
          NextId = id + 1 }

    /// Append a token/chunk to a text entry (assistant/user/thinking). No-op if the id
    /// isn't a text block. This is the streaming primitive — drive it from a
    /// `Cmd.ofAsync` result or a `Sub.Every` tick, then re-follow the tail.
    let appendText (id: MessageId) (chunk: string) (c: Conversation) : Conversation =
        c
        |> mapEntry id (fun e ->
            match e.Block with
            | AssistantMd s -> { e with Block = AssistantMd(s + chunk) }
            | UserMsg s -> { e with Block = UserMsg(s + chunk) }
            | Thinking s -> { e with Block = Thinking(s + chunk) }
            | _ -> e)

    /// Clear an entry's streaming flag (the stream finished).
    let finishStreaming (id: MessageId) (c: Conversation) : Conversation =
        c |> mapEntry id (fun e -> { e with Streaming = false })

    /// True while any entry is still streaming — drive a spinner / "thinking" state.
    let isStreaming (c: Conversation) : bool =
        c.Entries |> List.exists (fun e -> e.Streaming)

    // --- tool-call lifecycle -------------------------------------------------

    /// Add a tool call in `status` (typically `Pending` or `Running`); returns its id
    /// for later `setTool` transitions (`Pending → Running → Succeeded/Failed`).
    let addToolCall (name: string) (cmd: string) (status: ToolStatus) (c: Conversation) : MessageId * Conversation =
        add (ToolCall(name, cmd, status, "", "")) c

    /// Transition a tool call's status (and set its meta/output). No-op if the id isn't
    /// a `ToolCall`. Keeps the original name/command.
    let setTool
        (id: MessageId)
        (status: ToolStatus)
        (meta: string)
        (output: string)
        (c: Conversation)
        : Conversation =
        c
        |> mapEntry id (fun e ->
            match e.Block with
            | ToolCall(name, cmd, _, _, _) -> { e with Block = ToolCall(name, cmd, status, meta, output) }
            | _ -> e)
