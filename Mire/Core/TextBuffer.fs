namespace Mire.Core

/// A single-line text buffer with a cursor — `Cursor` is a char index in
/// `0 .. Text.Length`. Every edit is pure and returns a new buffer. Char-indexed
/// (not grapheme-aware): fine for ASCII-ish input; combining/wide clusters are a
/// known approximation, matching the rest of the framework.
type TextBuffer =
    { Text: string
      Cursor: int }

    static member Empty = { Text = ""; Cursor = 0 }
    static member Of(s: string) = { Text = s; Cursor = s.Length }

module TextBuffer =

    let private clampCursor (b: TextBuffer) =
        { b with
            Cursor = max 0 (min b.Text.Length b.Cursor) }

    /// A word char = anything that isn't whitespace (so `\n`/`\t`/spaces are all
    /// word boundaries — word ops won't silently run across a line break).
    let private isWord (c: char) = not (System.Char.IsWhiteSpace c)

    /// Index of the start of the line containing `pos` (char after the previous
    /// `\n`, or 0).
    let private lineStartIndex (text: string) (pos: int) : int =
        let p = max 0 (min text.Length pos)

        if p = 0 then
            0
        else
            match text.LastIndexOf('\n', p - 1) with
            | -1 -> 0
            | i -> i + 1

    /// Index of the end of the line containing `pos` (the next `\n`, or the text
    /// length for the last line).
    let private lineEndIndex (text: string) (pos: int) : int =
        let p = max 0 (min text.Length pos)

        match text.IndexOf('\n', p) with
        | -1 -> text.Length
        | i -> i

    let ofString (s: string) : TextBuffer = TextBuffer.Of s
    let isEmpty (b: TextBuffer) = b.Text = ""

    /// Insert `s` at the cursor and advance past it.
    let insert (s: string) (b: TextBuffer) : TextBuffer =
        let b = clampCursor b

        { Text = b.Text.Substring(0, b.Cursor) + s + b.Text.Substring(b.Cursor)
          Cursor = b.Cursor + s.Length }

    /// Delete the char before the cursor (Backspace).
    let backspace (b: TextBuffer) : TextBuffer =
        let b = clampCursor b

        if b.Cursor = 0 then
            b
        else
            { Text = b.Text.Substring(0, b.Cursor - 1) + b.Text.Substring(b.Cursor)
              Cursor = b.Cursor - 1 }

    /// Delete the char at the cursor (forward Delete).
    let delete (b: TextBuffer) : TextBuffer =
        let b = clampCursor b

        if b.Cursor >= b.Text.Length then
            b
        else
            { b with
                Text = b.Text.Substring(0, b.Cursor) + b.Text.Substring(b.Cursor + 1) }

    let left (b: TextBuffer) : TextBuffer =
        { b with Cursor = max 0 (b.Cursor - 1) }

    let right (b: TextBuffer) : TextBuffer =
        { b with
            Cursor = min b.Text.Length (b.Cursor + 1) }

    let home (b: TextBuffer) : TextBuffer = { b with Cursor = 0 }
    let toEnd (b: TextBuffer) : TextBuffer = { b with Cursor = b.Text.Length }

    /// Move the cursor to the start of the previous word (skip whitespace, then
    /// the word). No-op at the start of the buffer.
    let wordLeft (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let mutable i = b.Cursor

        while i > 0 && not (isWord b.Text.[i - 1]) do
            i <- i - 1

        while i > 0 && isWord b.Text.[i - 1] do
            i <- i - 1

        { b with Cursor = i }

    /// Move the cursor to the end of the next word. No-op at the end of the buffer.
    let wordRight (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let n = b.Text.Length
        let mutable i = b.Cursor

        while i < n && not (isWord b.Text.[i]) do
            i <- i + 1

        while i < n && isWord b.Text.[i] do
            i <- i + 1

        { b with Cursor = i }

    /// Delete from the cursor back to the start of the previous word. The action
    /// is key-agnostic; the conventional binding (Ctrl/Cmd+Backspace) is wired in
    /// `Mire.Core.TextEdit`.
    let deleteWordBack (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let i = (wordLeft b).Cursor

        { Text = b.Text.Substring(0, i) + b.Text.Substring(b.Cursor)
          Cursor = i }

    /// Delete from the cursor forward to the end of the next word.
    let deleteWordForward (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let j = (wordRight b).Cursor

        { b with
            Text = b.Text.Substring(0, b.Cursor) + b.Text.Substring(j) }

    /// The cursor's `(row, col)` — both 0-based — over the `\n`-delimited text.
    let cursorRowCol (b: TextBuffer) : int * int =
        let p = max 0 (min b.Text.Length b.Cursor)
        let mutable row = 0

        for i in 0 .. p - 1 do
            if b.Text.[i] = '\n' then
                row <- row + 1

        (row, p - lineStartIndex b.Text p)

    /// Move the cursor to the start of the current line.
    let lineStart (b: TextBuffer) : TextBuffer =
        { b with
            Cursor = lineStartIndex b.Text b.Cursor }

    /// Move the cursor to the end of the current line (before its `\n`).
    let lineEnd (b: TextBuffer) : TextBuffer =
        { b with
            Cursor = lineEndIndex b.Text b.Cursor }

    /// Move to the same column on the previous line (clamped to its length). No
    /// sticky desired-column: the column is recomputed each move. No-op on line 0.
    let up (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let (row, col) = cursorRowCol b

        if row = 0 then
            b
        else
            let curLineStart = lineStartIndex b.Text b.Cursor
            let prevLineStart = lineStartIndex b.Text (curLineStart - 1)
            let prevLineLen = (curLineStart - 1) - prevLineStart

            { b with
                Cursor = prevLineStart + min col prevLineLen }

    /// Move to the same column on the next line (clamped). No-op on the last line.
    let down (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let (_, col) = cursorRowCol b
        let curLineEnd = lineEndIndex b.Text b.Cursor

        if curLineEnd >= b.Text.Length then
            b
        else
            let nextLineStart = curLineEnd + 1
            let nextLineLen = lineEndIndex b.Text nextLineStart - nextLineStart

            { b with
                Cursor = nextLineStart + min col nextLineLen }

    let clear (_: TextBuffer) : TextBuffer = TextBuffer.Empty
