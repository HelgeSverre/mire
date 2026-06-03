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

    /// Delete from the cursor back to the start of the previous word (Ctrl+W).
    let deleteWordBack (b: TextBuffer) : TextBuffer =
        let b = clampCursor b

        if b.Cursor = 0 then
            b
        else
            let mutable i = b.Cursor

            while i > 0 && b.Text.[i - 1] = ' ' do
                i <- i - 1

            while i > 0 && b.Text.[i - 1] <> ' ' do
                i <- i - 1

            { Text = b.Text.Substring(0, i) + b.Text.Substring(b.Cursor)
              Cursor = i }

    let clear (_: TextBuffer) : TextBuffer = TextBuffer.Empty
