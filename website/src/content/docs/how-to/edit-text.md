---
title: Edit text
description: Hold a TextBuffer, feed input through the TextEdit keymap, and render it with Input or TextArea.
category: how-to
order: 5
---

Mire's editing stack is three pure layers: a `TextBuffer` (text + cursor + selection),
a `TextEdit` keymap (input → edit), and the `Input`/`TextArea` widgets (render).

## A single-line field

Hold a `TextBuffer` in your model, feed input events through `TextEdit.applyInput`, and
render with `Input.render`:

```fsharp
open Mire.Core
open Mire.Widgets

type Model = { Buffer: TextBuffer; Focused: bool }

let init () = { Buffer = TextBuffer.Empty; Focused = true }, Cmd.none

let update msg m =
    match msg with
    | Edit e   -> { m with Buffer = TextEdit.applyInput e m.Buffer }, Cmd.none
    | Submit   ->
        printfn "%s" m.Buffer.Text          // read the text
        { m with Buffer = TextBuffer.Empty }, Cmd.none   // then clear

let view m =
    Box.box theme.border
        [ Input.render 40 theme.fg theme.selection m.Focused m.Buffer ]

let mapInput e =
    match e with
    | Key { Key = Enter } -> Some Submit
    | Key _ | Paste _ -> Some (Edit e)
    | _ -> None
```

`TextEdit.applyInput` decodes the event with the conventional keymap: typing inserts;
Backspace/Delete (with Ctrl/Alt/Cmd = word-delete); arrows/Home/End move (with Shift =
extend selection); Ctrl/Cmd+A selects all; paste inserts (multi-line works). It returns
the buffer unchanged for events it doesn't own, so passing every `Key`/`Paste` through
is safe.

## A multi-line editor with soft-wrap

`TextArea` renders multiple lines. `render` scrolls both axes; `renderWrapped` word-wraps
each line to the width:

```fsharp
TextArea.renderWrapped 60 12 theme.fg theme.selection m.Focused m.Buffer
```

Both draw a block cursor when focused and highlight the selection.

## Custom key bindings

To pre-empt a few keys and fall back to the defaults, wrap the keymap:

```fsharp
let myKeymap ke =
    myCases ke |> Option.orElseWith (fun () -> TextEdit.defaultKeymap ke)

let buffer' = TextEdit.applyInputWith myKeymap e m.Buffer
```

Or apply named actions directly when you already know the intent:

```fsharp
TextEdit.apply (InsertText "👍") buffer
TextEdit.apply SelectAll buffer
TextEdit.apply (Select WordRight) buffer   // shift+word-right: extend the selection
```

## Read the selection

```fsharp
match TextBuffer.selection m.Buffer with
| Some (lo, hi) -> m.Buffer.Text.Substring(lo, hi - lo)   // the selected text
| None -> ""
```

<aside class="callout callout--note"><div class="callout__label">note</div><div>
Word chords (Ctrl/Cmd+Backspace, Ctrl/Alt+arrows) and shift-select need a CSI-u-capable
terminal — which the runtime enables. Basic editing works everywhere.
</div></aside>

## An agent prompt

For a chat or agent prompt you usually want submit-history and slash/@-mention completion
on top of a buffer. `Mire.Agent.PromptBox` wraps all of this — see
[the agent layer](/docs/reference/agent-layer/#promptbox).

See the [styling reference](/docs/reference/styling/) for the full `TextBuffer`/`TextEdit`
surface.
