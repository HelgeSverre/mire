---
title: Build an agent shell
description: Compose the Mire.Agent layer — a chat transcript, a prompt with history and completion, and an approval modal — into a working coding-agent UI.
category: how-to
order: 7
---

The optional `Mire.Agent` package gives you the pieces a coding-agent or chat UI needs:
a [`ChatTranscript`](/docs/reference/widgets/transcript/), a `PromptBox` (editing +
history + slash/@-mention completion), an `ApprovalModal` for tool permissions, and a
`DiffView`. This guide composes them into a shell. It is a normal Mire app — the agent
layer is UI only, so wiring it to a model or a tool runner is your `update`'s job.

The runnable version is `samples/AgentShell` (`just shell`); this walks the shape.

<aside class="callout callout--note"><div class="callout__label">note</div><div>
A higher-level <code>agentShell</code> program builder that wires all of this up for you
is planned (see the roadmap). Until then, you compose the pieces yourself — which is what
this guide shows, and it's only a couple of dozen lines.
</div></aside>

## The model

A transcript is a list of `TranscriptBlock`s. Hold that, a `PromptBox`, the scroll
offset, the terminal size, and whatever overlays your shell can show (here, a pending
tool approval):

```fsharp
open Mire.Core
open Mire.Widgets
open Mire.Agent
open Mire.App

let theme = AppTheme.defaultTheme

type Model =
    { Transcript: TranscriptBlock list
      Prompt: PromptBox
      Approval: (string * bool) option   // command awaiting accept/deny + which button is focused
      Offset: int
      Size: Size }

let init () =
    { Transcript = [ AssistantMd "How can I help?" ]
      Prompt = PromptBox.empty
      Approval = None
      Offset = 0
      Size = Mire.Protocol.TerminalMode.getTerminalSize () |> Option.defaultValue (Size.Create(80, 24)) },
    Cmd.none
```

## Messages and update

Typing flows through the `PromptBox`; Enter submits. On submit, append the user's
message (and, in a real shell, kick off the model via `Cmd.ofAsync`). Keep the transcript
scrolled to the tail with the `ChatTranscript` helpers.

```fsharp
type Msg =
    | Edit of InputEvent
    | Submit
    | ToggleButton   // ←/→/Tab in the approval modal
    | EscKey
    | Resized of Size

let private wrapWidth (m: Model) = max 10 (m.Size.Width - 4)
let private viewportRows (m: Model) = max 1 (m.Size.Height - 8)

// keep the newest output in view
let private followTail (m: Model) =
    { m with Offset = ChatTranscript.toBottom theme (wrapWidth m) 0 (viewportRows m) m.Transcript }

let private submit (m: Model) =
    let text = (PromptBox.value m.Prompt).Trim()
    if text = "" then m
    elif text = "run" then
        // a tool wants to run a command → raise an approval
        { m with
            Transcript = m.Transcript @ [ UserMsg "run" ]
            Prompt = PromptBox.empty
            Approval = Some("rm -rf build && cargo test", true) }
        |> followTail
    else
        { m with
            Transcript = m.Transcript @ [ UserMsg text; AssistantMd (sprintf "You said: *%s*." text) ]
            Prompt = PromptBox.empty }
        |> followTail

// turn an approval into a transcript block
let private resolve (accepted: bool) (m: Model) =
    let cmd = match m.Approval with Some (c, _) -> c | None -> ""
    let block =
        if accepted then ToolCall("shell", cmd, Succeeded, "0.4s", "ok")
        else Notice(AppTheme.Warning, sprintf "denied: %s" cmd)
    { m with Transcript = m.Transcript @ [ block ]; Approval = None } |> followTail

let update msg m =
    match msg with
    | Resized s -> { m with Size = s } |> followTail, Cmd.none
    | Edit e ->
        // while the modal is open it owns input; otherwise type into the prompt
        match m.Approval with
        | Some _ -> m, Cmd.none
        | None -> { m with Prompt = PromptBox.applyInput e m.Prompt }, Cmd.none
    | Submit ->
        match m.Approval with
        | Some (_, acceptFocused) -> resolve acceptFocused m, Cmd.none
        | None -> submit m, Cmd.none
    | ToggleButton ->
        match m.Approval with
        | Some (c, focused) -> { m with Approval = Some (c, not focused) }, Cmd.none
        | None -> m, Cmd.none
    | EscKey ->
        match m.Approval with
        | Some _ -> resolve false m, Cmd.none
        | None -> m, Cmd.none
```

In a real shell, replace the canned responses with `Cmd.ofAsync` calls that stream the
model's output back as messages — see [Program, Cmd, and Sub](/docs/reference/program-cmd-sub/#commands).

## The view

Dock a header and the prompt around the transcript. `ChatTranscript.view` is virtualized
and draws its own scrollbar; `PromptBox.render` draws the prompt glyph + editable text.
Layer the `ApprovalModal` over the base tree when one is pending.

```fsharp
let private transcript (m: Model) =
    Box.box theme.border
        [ ChatTranscript.view theme (wrapWidth m) 0 (viewportRows m) m.Offset theme.border theme.fgMuted m.Transcript ]

let private prompt (m: Model) =
    Box.box theme.border
        [ PromptBox.render (m.Size.Width - 2) theme.accent theme.fg theme.selection theme.fgSubtle
              "type a message — or `run`" m.Approval.IsNone m.Prompt ]

let view (m: Model) =
    let baseTree =
        Dock.dock
            [ Dock.top 3 (Box.box theme.border [ Text.text "└ mire · agent shell" theme.title ])
              Dock.bottom 3 (prompt m)
              Dock.fill (transcript m) ]

    match m.Approval with
    | None -> baseTree
    | Some (cmd, acceptFocused) ->
        LayoutNode.Overlay(Rect.Create(0, 0, 0, 0),
            [ baseTree
              ApprovalModal.view theme "Permission required" "A tool wants to run:" cmd
                  (Some "writes files") "Accept" "Deny" acceptFocused ])
```

## Routing input

Submit on Enter; the arrows/Tab move the modal's focus; everything else is editing.

```fsharp
let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | Enter -> Some Submit
        | Escape -> Some EscKey
        | Tab | ArrowLeft | ArrowRight -> Some ToggleButton
        | Char _ | Space | Backspace | Delete -> Some (Edit e)
        | _ -> None
    | Paste _ -> Some (Edit e)
    | Resize s -> Some (Resized s)
    | _ -> None

[<EntryPoint>]
let main _ =
    Program.create init update view
    |> Program.withMapInput mapInput
    |> Runtime.run
    0
```

That's a working shell: type to chat, `run` to raise a tool approval, ←/→ to choose,
Enter to confirm.

## Where to take it

- **Stream responses.** Append tokens to the active assistant block on a `Sub.Every` tick (or as `Cmd.ofAsync` results arrive) and re-`followTail`.
- **Slash commands and @-mentions.** `PromptBox.completionToken`/`acceptCompletion` locate and replace the token under the caret; render the candidates with [`Completion`](/docs/reference/widgets/completion/). History is `PromptBox.submit`/`historyPrev`/`historyNext`.
- **Review diffs.** Drop a `DiffView` into an overlay (or a transcript `DiffBlock`) for tool edits — accept/reject per hunk.
- **Click the modal buttons.** Tag them via `ApprovalModal.acceptRegion`/`denyRegion` and route clicks with [`withMouseRegion`](/docs/how-to/mouse-and-focus/).

See [the agent layer reference](/docs/reference/agent-layer/) for every component, and
`samples/AgentShell` for the complete, runnable source.
