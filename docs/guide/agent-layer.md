# The agent layer (`Mire.Agent`)

`Mire.Agent` is an optional widget layer above the framework for building coding-agent
and chat UIs. It references `Mire` only — the base framework never knows what an LLM is
— and every widget is parameterized by an `AppTheme`, so it carries no app-specific
styling. Reference the package and `open Mire.Agent`.

The headline is the **`AgentShell.program`** builder — a ready-made shell `Program` that
composes `ChatTranscript` + `PromptBox` + `ApprovalModal` + the `Conversation` model and
owns scroll/follow-tail, prompt history, key routing, a spinner tick, and an
`Idle | Streaming | AwaitingApproval` session; you supply only `OnSubmit`/`OnApprove`.
The **`samples/AgentShell`** MVP (`just shell`) is built on it with **zero theme code**.

```fsharp
AgentShell.program
    { Theme = AppTheme.defaultTheme; Title = "agent"; Placeholder = "type…"
      OnSubmit = (fun text m -> …)      // what a submitted line does
      OnApprove = (fun ok cmd m -> …) } // what an approval decision does
|> Runtime.run
```

The `Conversation` model (a typed list over `TranscriptBlock` with stable ids, streaming
helpers, and the `Queued → Running → Succeeded/Failed` tool lifecycle) is what the shell
holds; `AgentShell.startReply`/`stream`/`finishReply`/`requestApproval`/`addTool`/`setTool`
are the helpers your callbacks use. See the agent-layer reference for the full list.

## ChatTranscript and TranscriptBlock

A transcript is a list of `TranscriptBlock`s; `ChatTranscript` renders and scrolls them.

```fsharp
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

type ToolStatus = Queued | Running | Succeeded | Failed
```

Render the whole transcript as a scrolling, **virtualized** view — only the blocks
intersecting the viewport are built into the scroll node, while the scrollbar reflects
the true content height:

```fsharp
ChatTranscript.view theme wrapWidth frame viewportH offset trackStyle thumbStyle blocks
```

The app owns `offset` (MVU state); the scroll-math helpers compute the bounds, so
following the tail is a one-liner in `update`:

```fsharp
ChatTranscript.contentHeight theme wrapWidth frame blocks
ChatTranscript.toBottom      theme wrapWidth frame viewportH blocks   // the follow-tail offset
ChatTranscript.clampOffset   theme wrapWidth frame viewportH offset blocks
ChatTranscript.atBottom      theme wrapWidth frame viewportH offset blocks
```

`frame` is a spinner tick (drive it from a `Sub.Every`) so running tool calls animate.
`ChatTranscript.renderBlock` renders a single block if you want to compose your own
layout; `render` returns a bare (non-scrolling) vstack.

## PromptBox

`PromptBox` is the agent prompt: a `TextBuffer`/`TextEdit` editor plus a submit-history
ring and slash/@-mention completion-token detection. The candidate *source* (your
commands, your files) stays app-owned.

```fsharp
PromptBox.empty
PromptBox.applyInput e p          // feed an InputEvent through the editing keymap
PromptBox.value p                 // current text

// history (Up/Down recall, draft-preserving):
let text, p' = PromptBox.submit p          // push to history, clear, return the text
PromptBox.historyPrev p / historyNext p

// completion: locate the token under the caret, then splice a pick
match PromptBox.completionToken [ '/'; '@' ] p with
| Some tok ->                              // tok.Trigger, tok.Query, tok.Start
    // … rank your own candidates against tok.Query, let the user pick `value` …
    PromptBox.acceptCompletion tok value p // replaces the token with `trigger+value+space`
| None -> p

// render (an accent glyph + the editable text, or a placeholder when empty):
PromptBox.render width glyphStyle textStyle cursorStyle placeholderStyle placeholder focused p
```

A typical flow: on each edit, recompute `completionToken` to open/refresh a popup
(render it with `Widgets.Completion.view`); on Enter, either accept the highlighted
completion or `submit`.

## ApprovalModal

A command/risk approval prompt — a centered modal with Accept/Deny buttons:

```fsharp
ApprovalModal.view theme "Permission required" "A tool wants to run:" command (Some "writes files") "Accept" "Deny" acceptFocused
```

The buttons are tagged with `Focusable` regions so clicks route through the runtime's
region table — match `ApprovalModal.acceptRegion` / `ApprovalModal.denyRegion` in your
`withMouseRegion` handler ([Input → mouse hit-testing](input.md#mouse-hit-testing)).
`ApprovalModal.buttonHit` is also available if you'd rather hand-test coordinates. The
accept/deny *behavior* stays in your `update`.

## DiffView

A reviewable diff — hunks with per-hunk accept/reject status, in unified or
side-by-side split mode:

```fsharp
type HunkStatus = Pending | Accepted | Rejected
type DiffMode   = Unified | Split
type DiffLine   = { Sign: char; Text: string }          // '+', '-', or ' '
type DiffHunk   = { Header: string; Lines: DiffLine list; Status: HunkStatus }

DiffView.render theme mode width selectedHunk hunks
DiffView.splitColumns lines      // (before, after) columns for split mode (pure, tested)
DiffView.statusMark status       // "✓" / "✗" / "·"
```

The app owns the hunk list, the selection, and the statuses (mutate `Status` on
accept/reject) — pure MVU. The `agentShell` sample's `diff` command drives it
interactively (j/k select, a/r accept/reject, s toggle mode).

## Putting it together

```fsharp
open Mire.Agent
let theme = AppTheme.defaultTheme   // no app theme code needed

type Model = { Transcript: TranscriptBlock list; Prompt: PromptBox; Offset: int; Size: Size }

let view m =
    Dock.dock
        [ Dock.bottom 3 (Box.box theme.border
            [ PromptBox.render (m.Size.Width - 2) theme.accent theme.fg theme.selection theme.fgSubtle "type a message" true m.Prompt ])
          Dock.fill (Box.box theme.border
            [ ChatTranscript.view theme (m.Size.Width - 4) 0 (m.Size.Height - 5) m.Offset theme.border theme.fgMuted m.Transcript ]) ]
```

See `samples/AgentShell/Program.fs` for the complete, runnable version (submit, the
approval modal, and the diff reviewer wired up), and `Mire.Demo.Agent` for the
comprehensive showcase (command palette, streaming, mouse, overlays).
