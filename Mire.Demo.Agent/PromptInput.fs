namespace Mire.Demo.Agent

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// The agent prompt's editable text. Backed by the framework's real editing stack
/// now that it exists (ROADMAP v0.3): a `TextBuffer` for the text + cursor, the
/// `TextEdit` keymap for turning input events into edits, and the `TextArea` widget
/// for rendering. (Enter submits in this app, so the buffer stays single-line in
/// practice — but cursor movement, word-delete, and paste all go through the real
/// machinery.)
type PromptInput = { Buffer: TextBuffer }

module PromptInput =

    let empty = { Buffer = TextBuffer.Empty }

    let ofString (s: string) = { Buffer = TextBuffer.Of s }

    /// The current text (cursor-independent).
    let value (p: PromptInput) = p.Buffer.Text

    /// Feed one decoded input event (key or paste) through the conventional editing
    /// keymap — typing, Backspace/Delete, word-delete chords, cursor moves, paste.
    let applyInput (e: InputEvent) (p: PromptInput) =
        { Buffer = TextEdit.applyInput e p.Buffer }

    /// Apply a named edit action directly (used where the app knows the action but
    /// not a raw event — e.g. the neutralized Left/Right arrows).
    let applyAction (a: EditAction) (p: PromptInput) = { Buffer = TextEdit.apply a p.Buffer }

    /// Thin convenience shims (used where the app builds an edit directly rather
    /// than from an input event). Insert at the cursor / delete before it.
    let append (s: string) (p: PromptInput) =
        { Buffer = TextEdit.apply (InsertText s) p.Buffer }

    let backspace (p: PromptInput) =
        { Buffer = TextEdit.apply DeleteBack p.Buffer }

    /// Render the prompt line: an accent glyph, then the editable text via the
    /// `TextArea` widget (a block cursor at the caret when focused), or the
    /// placeholder when empty. `width` is the available content width (the glyph
    /// is subtracted for the text region).
    let render
        (width: int)
        (glyphStyle: Style)
        (textStyle: Style)
        (cursorStyle: Style)
        (placeholderStyle: Style)
        (placeholder: string)
        (focused: bool)
        (p: PromptInput)
        : LayoutNode<'msg> =
        let glyphW = 2

        let body =
            if TextBuffer.isEmpty p.Buffer then
                Text.text placeholder placeholderStyle
            else
                TextArea.render (max 0 (width - glyphW)) 1 textStyle cursorStyle focused p.Buffer

        Stack.hstackOf
            [ Stack.sized (Length.Cells glyphW) (Text.text "❯ " glyphStyle)
              Stack.sized Length.Fill body ]
