namespace Mire.Agent

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// An agent prompt's editable text, backed by the framework's editing stack: a
/// `TextBuffer` for the text + cursor, the `TextEdit` keymap for turning input
/// events into edits, and the `TextArea` widget for rendering. (An agent shell
/// typically submits on Enter, so the buffer stays single-line in practice — but
/// cursor movement, word-delete, and paste all go through the real machinery.)
/// Carries a submit-`History` ring and tracks the active completion token, so
/// slash/@-mention popups and up/down recall are PromptBox behavior, not re-derived
/// by every app — the *candidate source* (commands, files) stays app-owned.
type PromptBox =
    {
        Buffer: TextBuffer
        /// Submitted entries, oldest first. Pushed by `submit`, browsed by
        /// `historyPrev`/`historyNext`.
        History: string list
        /// Position while browsing history: `None` = editing live; `Some i` = showing
        /// `History.[i]`. Any edit resets it to `None`.
        HistoryPos: int option
        /// The live text stashed when history browsing started, restored by paging
        /// `historyNext` past the newest entry.
        Draft: string
    }

/// The completion token under the cursor: the trigger char that opened it
/// (e.g. `/` or `@`), the `Query` typed after it, and the `Start` index of the
/// trigger in the buffer (where `acceptCompletion` splices the chosen value).
type CompletionToken =
    { Trigger: char
      Query: string
      Start: int }

module PromptBox =

    let empty =
        { Buffer = TextBuffer.Empty
          History = []
          HistoryPos = None
          Draft = "" }

    let ofString (s: string) = { empty with Buffer = TextBuffer.Of s }

    /// The current text (cursor-independent).
    let value (p: PromptBox) = p.Buffer.Text

    // Any live edit exits history browsing (the recalled text becomes the draft).
    let private edited (buf: TextBuffer) (p: PromptBox) =
        { p with
            Buffer = buf
            HistoryPos = None }

    /// Feed one decoded input event (key or paste) through the conventional editing
    /// keymap — typing, Backspace/Delete, word-delete chords, cursor moves, paste.
    let applyInput (e: InputEvent) (p: PromptBox) =
        edited (TextEdit.applyInput e p.Buffer) p

    /// Apply a named edit action directly (used where the app knows the action but
    /// not a raw event — e.g. the neutralized Left/Right arrows).
    let applyAction (a: EditAction) (p: PromptBox) = edited (TextEdit.apply a p.Buffer) p

    /// Thin convenience shims (used where the app builds an edit directly rather
    /// than from an input event). Insert at the cursor / delete before it.
    let append (s: string) (p: PromptBox) =
        edited (TextEdit.apply (InsertText s) p.Buffer) p

    let backspace (p: PromptBox) =
        edited (TextEdit.apply DeleteBack p.Buffer) p

    // --- history ------------------------------------------------------------

    /// Take the current text and reset to an empty prompt, pushing the text onto the
    /// history ring (skipping blanks and an immediate duplicate of the last entry).
    /// Returns the submitted text and the cleared prompt.
    let submit (p: PromptBox) : string * PromptBox =
        let v = p.Buffer.Text

        let history =
            if v.Trim() = "" then
                p.History
            else
                match List.tryLast p.History with
                | Some last when last = v -> p.History
                | _ -> p.History @ [ v ]

        v,
        { Buffer = TextBuffer.Empty
          History = history
          HistoryPos = None
          Draft = "" }

    let private recall (s: string) (p: PromptBox) = { p with Buffer = TextBuffer.Of s }

    /// Step to an older history entry (the conventional Up binding). Stashes the live
    /// draft on the first step; a no-op at the oldest entry or with no history.
    let historyPrev (p: PromptBox) : PromptBox =
        let n = List.length p.History

        if n = 0 then
            p
        else
            match p.HistoryPos with
            | None ->
                let idx = n - 1

                { (recall p.History.[idx] p) with
                    HistoryPos = Some idx
                    Draft = p.Buffer.Text }
            | Some i when i > 0 ->
                { (recall p.History.[i - 1] p) with
                    HistoryPos = Some(i - 1) }
            | Some _ -> p

    /// Step to a newer history entry (the conventional Down binding). Paging past the
    /// newest entry restores the stashed draft and stops browsing; a no-op when not
    /// browsing.
    let historyNext (p: PromptBox) : PromptBox =
        match p.HistoryPos with
        | None -> p
        | Some i ->
            let n = List.length p.History

            if i + 1 < n then
                { (recall p.History.[i + 1] p) with
                    HistoryPos = Some(i + 1) }
            else
                { (recall p.Draft p) with
                    HistoryPos = None
                    Draft = "" }

    // --- completion (slash / @mention) --------------------------------------

    /// The completion token under the cursor: the maximal non-whitespace run ending
    /// at the caret, if it begins with one of `triggers`. The candidate list and
    /// ranking are the app's (it knows its commands/files); this only locates the
    /// token. Slash-only-at-start is an app policy — filter on `tok.Start = 0`.
    let completionToken (triggers: char list) (p: PromptBox) : CompletionToken option =
        let text = p.Buffer.Text
        let cur = min p.Buffer.Cursor text.Length
        let mutable s = cur

        while s > 0 && not (System.Char.IsWhiteSpace text.[s - 1]) do
            s <- s - 1

        if s < cur && List.contains text.[s] triggers then
            Some
                { Trigger = text.[s]
                  Query = text.Substring(s + 1, cur - (s + 1))
                  Start = s }
        else
            None

    /// The active completion, fully resolved: the token under the caret paired with
    /// the candidate list the app's `source` returns for it — or `None` when there's
    /// no trigger token or the source yields nothing. This folds the demo's
    /// "find the token, then ask for candidates" glue into one call; the app supplies
    /// the candidate `source` (its commands/files) and owns the popup's selected
    /// index + placement (like `ListView`, key handling stays MVU-side). Accept a pick
    /// with `acceptCompletion tok value`.
    let completion
        (triggers: char list)
        (source: CompletionToken -> string list)
        (p: PromptBox)
        : (CompletionToken * string list) option =
        match completionToken triggers p with
        | Some tok ->
            match source tok with
            | [] -> None
            | candidates -> Some(tok, candidates)
        | None -> None

    /// Replace the token (from `tok.Start` to the caret) with `trigger + replacement`
    /// and a trailing space, leaving the caret after it — accepting a completion pick.
    let acceptCompletion (tok: CompletionToken) (replacement: string) (p: PromptBox) : PromptBox =
        let text = p.Buffer.Text
        let cur = min p.Buffer.Cursor text.Length
        let before = text.Substring(0, tok.Start)
        let after = text.Substring(cur)
        let inserted = string tok.Trigger + replacement + " "

        { p with
            Buffer =
                { Text = before + inserted + after
                  Cursor = before.Length + inserted.Length
                  Anchor = None }
            HistoryPos = None }

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
        (p: PromptBox)
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
