namespace Mire.Core

/// A named editing **action**, decoupled from any keystroke. `apply` runs it
/// against a `TextBuffer`; `defaultKeymap` maps a `KeyEvent` to one with
/// conventional, *overridable* bindings — the framework ships conventions, it
/// never hardcodes a binding into the buffer or the runtime. Apps wire keys →
/// actions however they like: use `defaultKeymap`/`applyInput` as-is, wrap them
/// to pre-empt a few keys, or build a keymap from scratch.
///
/// Modifier note: word-chords (Ctrl/Cmd+Backspace, Ctrl/Alt+←/→) need a
/// CSI-u-capable terminal (Kitty/Ghostty — the runtime enables it). Basic editing
/// (typing, Backspace, Delete, arrows, Home/End, paste) works everywhere.
type EditAction =
    | InsertText of string
    | Newline
    | DeleteBack
    | DeleteForward
    | DeleteWordBack
    | DeleteWordForward
    | CursorLeft
    | CursorRight
    | CursorUp
    | CursorDown
    | WordLeft
    | WordRight
    | LineStart
    | LineEnd
    | DocStart
    | DocEnd
    | SelectAll
    /// Apply a *motion* action as a selection extension (a shift+motion): anchors
    /// at the caret if needed, then moves. Wrap a cursor/word/line/doc move.
    | Select of EditAction

module TextEdit =

    /// A pure cursor motion (preserves any selection anchor). Non-motions are no-ops.
    let private motion (action: EditAction) (b: TextBuffer) : TextBuffer =
        match action with
        | CursorLeft -> TextBuffer.left b
        | CursorRight -> TextBuffer.right b
        | CursorUp -> TextBuffer.up b
        | CursorDown -> TextBuffer.down b
        | WordLeft -> TextBuffer.wordLeft b
        | WordRight -> TextBuffer.wordRight b
        | LineStart -> TextBuffer.lineStart b
        | LineEnd -> TextBuffer.lineEnd b
        | DocStart -> TextBuffer.home b
        | DocEnd -> TextBuffer.toEnd b
        | _ -> b

    /// Run an action against a buffer (pure). Edits replace any selection; plain
    /// moves clear it; `Select`/`SelectAll` set it.
    let apply (action: EditAction) (b: TextBuffer) : TextBuffer =
        match action with
        | InsertText s -> TextBuffer.insert s b
        | Newline -> TextBuffer.insert "\n" b
        | DeleteBack -> TextBuffer.backspace b
        | DeleteForward -> TextBuffer.delete b
        | DeleteWordBack -> TextBuffer.deleteWordBack b
        | DeleteWordForward -> TextBuffer.deleteWordForward b
        | SelectAll -> TextBuffer.selectAll b
        | Select mv -> TextBuffer.extend (motion mv) b
        | CursorLeft
        | CursorRight
        | CursorUp
        | CursorDown
        | WordLeft
        | WordRight
        | LineStart
        | LineEnd
        | DocStart
        | DocEnd -> motion action b |> TextBuffer.clearSelection

    /// Conventional default bindings. Returns `None` for keys it doesn't own (e.g.
    /// `Ctrl+C`, `Tab`, `Enter`+modifiers) so the app/runtime can claim them.
    /// Override by wrapping: `fun ke -> myCases ke |> Option.orElseWith (fun () -> defaultKeymap ke)`.
    let defaultKeymap (ke: KeyEvent) : EditAction option =
        let m = ke.Modifiers
        let plain = not m.Ctrl && not m.Alt && not m.Meta
        let word = m.Ctrl || m.Alt || m.Meta // Ctrl/Cmd/Alt treated alike (cross-platform)
        // Shift turns a motion into a selection extension.
        let sel (mv: EditAction) = if m.Shift then Select mv else mv

        match ke.Key with
        | Char c when (m.Ctrl || m.Meta) && c = "a" -> Some SelectAll // Ctrl/Cmd+A select all
        | Char c when plain -> Some(InsertText c)
        | Space when plain -> Some(InsertText " ")
        | Enter when plain -> Some Newline
        | Backspace -> Some(if word then DeleteWordBack else DeleteBack)
        | Delete -> Some(if word then DeleteWordForward else DeleteForward)
        | ArrowLeft -> Some(sel (if word then WordLeft else CursorLeft))
        | ArrowRight -> Some(sel (if word then WordRight else CursorRight))
        | ArrowUp when plain -> Some(sel CursorUp)
        | ArrowDown when plain -> Some(sel CursorDown)
        | Home -> Some(sel (if m.Ctrl || m.Meta then DocStart else LineStart))
        | End -> Some(sel (if m.Ctrl || m.Meta then DocEnd else LineEnd))
        | _ -> None

    /// Apply `keymap` to an `InputEvent`: keys via the keymap; `Paste s` inserts
    /// the text (multi-line paste works since `insert` handles `\n`); other events
    /// leave the buffer unchanged.
    let applyInputWith (keymap: KeyEvent -> EditAction option) (e: InputEvent) (b: TextBuffer) : TextBuffer =
        match e with
        | Key ke ->
            match keymap ke with
            | Some a -> apply a b
            | None -> b
        | Paste s -> apply (InsertText s) b
        | _ -> b

    /// `applyInputWith` using the conventional `defaultKeymap`.
    let applyInput (e: InputEvent) (b: TextBuffer) : TextBuffer = applyInputWith defaultKeymap e b
