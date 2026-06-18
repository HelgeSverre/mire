# Text editing

Mire's editing stack is three layers, all pure:

1. **`Mire.Core.TextBuffer`** — text + cursor + selection, with pure edit/move ops.
2. **`Mire.Core.TextEdit`** — a named-action layer and an overridable keymap that turns
   a `KeyEvent` (or `InputEvent`) into an edit.
3. **`Widgets.Input` / `Widgets.TextArea`** — render a buffer (block cursor, selection
   highlight, scroll / soft-wrap).

You hold a `TextBuffer` in your model, feed input through `TextEdit`, and render with
`Input`/`TextArea`.

## TextBuffer

```fsharp
type TextBuffer =
    { Text: string
      Cursor: int            // a char index in 0 .. Text.Length
      Anchor: int option }   // Some i ⇒ selection between i and Cursor; None ⇒ just the caret

TextBuffer.Empty
TextBuffer.Of "initial text"   // caret at the end
```

It's char-indexed (a fine approximation for editing; the *renderer* is grapheme-aware).
The ops are pure and return new buffers:

```fsharp
// edits (replace any selection):
TextBuffer.insert "x" b
TextBuffer.backspace b      // or deletes the selection
TextBuffer.delete b         // forward delete / selection
TextBuffer.deleteWordBack b
TextBuffer.deleteWordForward b

// movement (preserve the selection anchor):
TextBuffer.left b / right b / home b / toEnd b
TextBuffer.wordLeft b / wordRight b
TextBuffer.up b / down b           // over '\n'-delimited lines
TextBuffer.lineStart b / lineEnd b

// selection:
TextBuffer.selectAll b
TextBuffer.selectWord b
TextBuffer.extend move b           // shift+move: anchor then move
TextBuffer.selection b             // (lo, hi) option
TextBuffer.hasSelection b
TextBuffer.clearSelection b
```

## TextEdit — actions and the keymap

`TextEdit` decouples editing *actions* from keystrokes, so bindings are conventional and
overridable rather than baked in:

```fsharp
type EditAction =
    | InsertText of string | Newline
    | DeleteBack | DeleteForward | DeleteWordBack | DeleteWordForward
    | CursorLeft | CursorRight | CursorUp | CursorDown
    | WordLeft | WordRight | LineStart | LineEnd | DocStart | DocEnd
    | SelectAll
    | Select of EditAction   // a shift+motion: extend the selection

TextEdit.apply action buffer            // run an action (pure)
TextEdit.applyInput inputEvent buffer   // decode an InputEvent via the default keymap
```

`TextEdit.defaultKeymap` ships conventional bindings: typing inserts; Backspace/Delete
(with Ctrl/Alt/Cmd = word-delete); arrows/Home/End move (with Shift = extend selection);
Ctrl/Cmd+A selects all; paste inserts (multi-line works). It returns `None` for keys it
doesn't own (Ctrl+C, Tab, modified Enter) so your app/runtime can claim them.

Wire it straight through, or wrap it to pre-empt a few keys:

```fsharp
// straight through:
let buffer' = TextEdit.applyInput e buffer

// custom bindings first, default for the rest:
let myKeymap ke = myCases ke |> Option.orElseWith (fun () -> TextEdit.defaultKeymap ke)
let buffer' = TextEdit.applyInputWith myKeymap e buffer
```

Word chords (Ctrl/Cmd+Backspace, Ctrl/Alt+arrows) and shift-select need a CSI-u-capable
terminal — which the runtime enables. Basic editing works everywhere.

## Rendering — Input and TextArea

`Input` is single-line; `TextArea` is multi-line. Both render a block cursor when
focused and highlight the selection (reusing the cursor style as inverse video):

```fsharp
Input.render width textStyle cursorStyle focused buffer

TextArea.render        width height textStyle cursorStyle focused buffer  // scrolls both axes, no wrap
TextArea.renderWrapped width height textStyle cursorStyle focused buffer  // soft word-wrap
```

`Input` scrolls horizontally to keep the cursor visible; `TextArea.render` scrolls both
axes; `TextArea.renderWrapped` word-wraps each logical line to `width` and scrolls/selects
over the resulting *visual* rows. Pick wrap vs. scroll per call site. The pure wrapper
`TextArea.wrapLine width line` is also exposed (it partitions a line into visual segments,
breaking at spaces and hard-breaking over-long words) if you need it directly.

## Putting it together

```fsharp
type Model = { Buffer: TextBuffer; Focused: bool }

let update msg m =
    match msg with
    | Edit e -> { m with Buffer = TextEdit.applyInput e m.Buffer }, Cmd.none
    | Submit -> // read m.Buffer.Text, then clear:
        doSomething m.Buffer.Text
        { m with Buffer = TextBuffer.Empty }, Cmd.none

let view m =
    Box.box theme.border
        [ Input.render 40 theme.fg theme.selection m.Focused m.Buffer ]

let mapInput e =
    match e with
    | Key { Key = Enter } -> Some Submit
    | Key _ | Paste _ -> Some (Edit e)
    | _ -> None
```

## The agent prompt

For a chat/agent prompt you usually want submit-history and slash/@-mention completion
on top of a buffer. `Mire.Agent.PromptBox` wraps `TextBuffer`/`TextEdit` with exactly
that — see [The agent layer](agent-layer.md#promptbox).
