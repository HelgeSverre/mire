---
title: Build an agent shell
description: Compose the Mire.Agent layer into a working coding-agent UI — the AgentShell builder for the fast path, or the pieces by hand for full control.
category: how-to
order: 7
---

The optional `Mire.Agent` package gives you the pieces a coding-agent or chat UI needs:
a [`ChatTranscript`](/docs/reference/widgets/transcript/), a `PromptBox` (editing +
history + slash/@-mention completion), an `ApprovalModal` for tool permissions, a
`DiffView`, and the [`Conversation`](/docs/reference/agent-layer/#conversation-the-message-model)
message model. The fastest way to wire them together is the **`AgentShell` builder**; if
you need more control, the same pieces compose by hand.

The runnable version is `samples/AgentShell` (`just shell` / `-- --dump`).

## The fast path: `AgentShell.program`

`AgentShell.program config` is a ready-made `Program` — it owns the transcript scroll and
follow-tail, the prompt and its history, the approval modal, key routing, a spinner tick,
and an `Idle | Streaming | AwaitingApproval` session. You supply only **what a submitted
line does** and **what an approval decision does**:

```fsharp
open Mire.Core
open Mire.Widgets
open Mire.Agent
open Mire.App

// What a submitted line does: `run` raises an approval; anything else streams a reply.
let onSubmit (text: string) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
    if text = "run" then
        AgentShell.requestApproval "rm -rf build && cargo test" m, Cmd.none
    else
        let id, m = AgentShell.startReply m          // an empty streaming reply
        let words = (sprintf "You said: *%s*." text).Split(' ')
        // stream a token at a time; an LLM client would dispatch Apply the same way
        let cmd =
            Cmd.ofAsync (fun dispatch -> async {
                for w in words do
                    do! Async.Sleep 50
                    dispatch (Apply (AgentShell.stream id (w + " ")))
                dispatch (Apply (AgentShell.finishReply id)) })
        m, cmd

// What an approval does: accept → run the tool; deny → a warning notice.
let onApprove (accepted: bool) (cmd: string) (m: ShellModel) : ShellModel * Cmd<ShellMsg> =
    if accepted then
        let id, m = AgentShell.addTool "shell" cmd Running m
        m, Cmd.ofAsync (fun dispatch -> async {
            do! Async.Sleep 300
            dispatch (Apply (AgentShell.setTool id Succeeded "0.3s" "ok")) })
    else
        AgentShell.addNotice AppTheme.Warning (sprintf "denied: %s" cmd) m, Cmd.none

let config: ShellConfig =
    { Theme = AppTheme.defaultTheme
      Title = "└ mire · agent"
      Placeholder = "type a message — or `run`"
      OnSubmit = onSubmit
      OnApprove = onApprove }

[<EntryPoint>]
let main _ =
    AgentShell.program config |> Runtime.run
    0
```

That's a complete shell: type to chat (replies stream in), `run` to raise a tool approval
(←/→ choose, Enter confirm, Esc deny), ↑/↓ to recall history, Ctrl+C to quit.

### Streaming

The shell renders streaming for free. Start a reply with `AgentShell.startReply` (which
returns the new message's id and flips the session to `Streaming`), then feed chunks in
from a command via the `Apply` message — `Apply (AgentShell.stream id chunk)` — and end
with `Apply (AgentShell.finishReply id)`. The transcript follows the tail as it grows, and
because the runtime coalesces a burst of messages into one render per frame, a fast token
stream stays smooth.

### The model helpers

`OnSubmit`/`OnApprove` (and any `Apply` updater) mutate the conversation with:

| Helper | Effect |
| --- | --- |
| `addUser` / `addAssistant` / `addNotice` / `addBlock` | append a block, follow the tail |
| `startReply` → `(id, m)` | begin a streaming assistant reply |
| `stream id chunk` | append a token to a streaming reply |
| `finishReply id` | end the stream (→ `Idle`) |
| `requestApproval cmd` | raise the approval modal (→ `AwaitingApproval`) |
| `addTool name cmd status` → `(id, m)` | append a tool call |
| `setTool id status meta output` | transition a tool call (`Queued → Running → Succeeded/Failed`) |

## By hand: composing the pieces yourself

If you need a layout the builder doesn't cover (extra panes, a custom overlay), compose
the widgets directly. Hold a [`Conversation`](/docs/reference/agent-layer/#conversation-the-message-model)
(or a raw `TranscriptBlock list`), a `PromptBox`, the scroll offset, and whatever overlays
your shell shows, and render:

```fsharp
let view (m: Model) =
    let baseTree =
        Dock.dock
            [ Dock.top 3 (Box.box theme.border [ Text.text "└ mire · agent" theme.title ])
              Dock.bottom 3 (Box.box theme.border
                  [ PromptBox.render (m.Size.Width - 2) theme.accent theme.fg theme.selection theme.fgSubtle
                        "type a message" m.Approval.IsNone m.Prompt ])
              Dock.fill (Box.box theme.border
                  [ ChatTranscript.view theme (wrapWidth m) m.Frame (viewportRows m) m.Offset
                        theme.border theme.fgMuted (Conversation.blocks m.Conversation) ]) ]

    match m.Approval with
    | None -> baseTree
    | Some (cmd, acceptFocused) ->
        LayoutNode.Overlay(Rect.Create(0, 0, 0, 0),
            [ baseTree
              ApprovalModal.view theme "Permission required" "A tool wants to run:" cmd
                  (Some "writes files") "Accept" "Deny" acceptFocused ])
```

Route Enter→submit, Esc→cancel, Tab/←/→→move the modal's focus, everything else→editing
(`PromptBox.applyInput`); keep the transcript pinned with `ChatTranscript.toBottom`. This
is exactly what `AgentShell` does internally — read its source for the full shape.

## Where to take it

- **Wire it to a model.** Replace the canned `onSubmit` with a real client: `startReply`, then a `Cmd.ofAsync` that streams the model's tokens back as `Apply (AgentShell.stream id …)`.
- **Slash commands and @-mentions.** `PromptBox.completion triggers source` resolves the token under the caret and its candidates in one call; render them with [`Completion`](/docs/reference/widgets/completion/).
- **Review diffs.** Drop a `DiffView` into an overlay (or a transcript `DiffBlock`) for tool edits — accept/reject per hunk.

See [the agent layer reference](/docs/reference/agent-layer/) for every component, and
`samples/AgentShell` for the complete, runnable source.
