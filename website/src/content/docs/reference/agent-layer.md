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

## ChatTranscript and TranscriptBlock

A transcript is a list of `TranscriptBlock`s; `ChatTranscript` renders and scrolls them.

```fsharp
type ToolStatus = Running | Succeeded | Failed

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
`renderBlock` renders one block if you compose your own layout.

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

match PromptBox.completionToken [ '/'; '@' ] p with
| Some tok ->                              // tok.Trigger, tok.Query, tok.Start
    PromptBox.acceptCompletion tok value p // replace the token with trigger+value+space
| None -> p

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
type DiffHunk   = { Header: string; Status: HunkStatus; Lines: DiffLine list }

DiffView.render theme mode width selectedHunk hunks
DiffView.splitColumns lines      // (before, after) columns for split mode
DiffView.statusMark status       // "✓" / "✗" / "·"
```

The app owns the hunk list, the selection, and the statuses (mutate `Status` on
accept/reject). The `agentShell` sample drives it interactively (j/k select, a/r
accept-reject, s toggle mode).
