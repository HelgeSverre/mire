---
title: The agent layer (Mire.Agent)
description: ChatTranscript, PromptBox, ApprovalModal, and DiffView — the optional widget layer for coding-agent and chat UIs.
category: reference
order: 5
---

`Mire.Agent` is an optional package above the framework for building coding-agent and
chat UIs. It references `Mire` only — the base framework never knows what an LLM is — and
every widget is parameterized by an `AppTheme`. The `samples/AgentShell` MVP (`just shell`)
composes the whole layer on `AppTheme.defaultTheme` with zero theme code; read it as the
canonical example.

The fastest path to a working shell is the `AgentShell` builder (below); follow
[Build an agent shell](/docs/how-to/build-an-agent-shell/) for the walkthrough. This page
is the per-component reference.

## AgentShell — the program builder

`AgentShell.program config` returns a ready-made `Program<ShellModel, ShellMsg>` that
composes the whole layer — transcript + prompt + approval modal + the `Conversation`
model — and owns scroll/follow-tail, prompt history, key routing, a spinner tick, and an
`Idle | Streaming | AwaitingApproval` session. You supply only *what a submission or an
approval does*:

```fsharp
let config: ShellConfig =
    { Theme = AppTheme.defaultTheme
      Title = "└ mire · agent"
      Placeholder = "type a message — or `run`"
      OnSubmit = fun text m -> …, Cmd<ShellMsg>      // a line was submitted (user msg already appended)
      OnApprove = fun accepted cmd m -> …, Cmd<ShellMsg> }   // an approval was resolved

AgentShell.program config |> Runtime.run
```

The callbacks use the model helpers to mutate the conversation:

```fsharp
AgentShell.addUser / addAssistant / addNotice / addBlock   // append a block + follow tail
AgentShell.startReply m            // (id, m') — begin a streaming assistant reply
AgentShell.stream id chunk         // ShellModel -> ShellModel — append a token
AgentShell.finishReply id          // end the stream
AgentShell.requestApproval cmd     // raise the approval modal
AgentShell.addTool name cmd status // (id, m') — append a tool call
AgentShell.setTool id status meta output
```

Stream a reply by returning a `Cmd.ofAsync` that dispatches `Apply` updaters as chunks
arrive — `Apply (AgentShell.stream id chunk)`, then `Apply (AgentShell.finishReply id)`.
`samples/AgentShell` is built on this and dogfoods streaming + approvals.

## Conversation — the message model

A typed model over `TranscriptBlock`: an ordered list of entries with stable
`MessageId`s, streaming state, and a tool-call lifecycle. Pure and testable; the app holds
one and renders `Conversation.blocks` through `ChatTranscript`.

```fsharp
let conv = Conversation.empty |> Conversation.addUser "build it"
let id, conv = Conversation.startAssistant conv     // an empty, streaming reply
let conv = conv |> Conversation.appendText id "Sure" |> Conversation.finishStreaming id

let id, conv = Conversation.addToolCall "shell" "dotnet build" Queued conv
let conv = Conversation.setTool id Succeeded "1.1s" "ok" conv   // Queued → Running → Succeeded/Failed

Conversation.blocks conv        // TranscriptBlock list — for ChatTranscript
Conversation.isStreaming conv   // true while any entry streams
```

## ChatTranscript and TranscriptBlock

A transcript is a list of `TranscriptBlock`s; `ChatTranscript` renders and scrolls them.

```fsharp
type ToolStatus = Queued | Running | Succeeded | Failed

type TranscriptBlock =
    | UserMsg of string
    | AssistantMd of string                 // rendered as markdown
    | Thinking of string
    | ToolCall of name: string * cmd: string * status: ToolStatus * meta: string * output: string
    | DiffBlock of file: string * lines: DiffLine list
    | TableBlock of headers: string list * rows: string list list
    | ErrorBlock of string
    | Notice of AppTheme.Tone * string
    | FileTree of string list
    | TaskTimeline of (string * ToolStatus) list
    | PlanBlock of (bool * string) list
```

Render the whole transcript as a **virtualized**, scrolling view — only the blocks
intersecting the viewport are built into the scroll node, while the scrollbar reflects
the true content height:

```fsharp
ChatTranscript.view theme wrapWidth frame viewportH offset trackStyle thumbStyle blocks
```

The app owns `offset`. The scroll-math helpers compute the bounds, so following the tail
is one line in `update`:

```fsharp
ChatTranscript.contentHeight theme wrapWidth frame blocks
ChatTranscript.toBottom      theme wrapWidth frame viewportH blocks   // follow-tail offset
ChatTranscript.clampOffset   theme wrapWidth frame viewportH offset blocks
ChatTranscript.atBottom      theme wrapWidth frame viewportH offset blocks
```

`frame` is a spinner tick (drive it from a `Sub.Every`) so running tool calls animate.
`renderBlock` renders one block if you compose your own layout, and the individual block
renderers are also exposed as first-class, standalone widgets you can drop anywhere (not
just in a transcript): `ChatTranscript.toolCallView`, `thinkingView`, `fileTreeView`, and
`taskTimelineView`.

## PromptBox

A `TextBuffer`/`TextEdit` editor plus submit-history and slash/@-mention completion. The
candidate source stays app-owned.

```fsharp
PromptBox.empty
PromptBox.applyInput e p          // feed an InputEvent through the editing keymap
PromptBox.value p

let text, p' = PromptBox.submit p          // push to history, clear, return the text
PromptBox.historyPrev p                    // Up: draft-preserving recall
PromptBox.historyNext p                    // Down

// Resolve the token under the caret AND its candidates in one call — the app
// supplies the candidate source; selection index + popup placement stay app state.
match PromptBox.completion [ '/'; '@' ] (fun tok -> myCandidates tok.Trigger tok.Query) p with
| Some (tok, candidates) -> // render `candidates` with Completion.view; accept a pick:
    PromptBox.acceptCompletion tok candidates.[selected] p
| None -> p

// Or locate the token yourself with the lower-level pair:
PromptBox.completionToken [ '/'; '@' ] p   // tok.Trigger, tok.Query, tok.Start

PromptBox.render width glyphStyle textStyle cursorStyle placeholderStyle placeholder focused p
```

## ApprovalModal

A command/risk approval prompt with Accept/Deny buttons.

```fsharp
ApprovalModal.view theme "Permission required" "A tool wants to run:" command (Some "writes files") "Accept" "Deny" acceptFocused
```

The buttons are tagged with `Focusable` regions — route clicks via `withMouseRegion` and
match `ApprovalModal.acceptRegion` / `ApprovalModal.denyRegion` (see
[Mouse and focus](/docs/how-to/mouse-and-focus/)). `ApprovalModal.buttonHit` hand-tests
coordinates if you prefer. The accept/deny behavior stays in your `update`.

## DiffView

A reviewable diff — hunks with per-hunk accept/reject status, unified or split.

```fsharp
type HunkStatus = Pending | Accepted | Rejected
type DiffMode   = Unified | Split
type DiffLine   = { Sign: char; Text: string }         // '+', '-', or ' '
type DiffHunk   = { Header: string; Lines: DiffLine list; Status: HunkStatus }

DiffView.render theme mode width selectedHunk hunks
DiffView.splitColumns lines      // (before, after) columns for split mode
DiffView.statusMark status       // "✓" / "✗" / "·"
```

The app owns the hunk list, the selection, and the statuses (mutate `Status` on
accept/reject). The `agentShell` sample drives it interactively (j/k select, a/r
accept-reject, s toggle mode).
