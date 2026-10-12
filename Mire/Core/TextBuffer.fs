namespace Mire.Core

/// A text buffer with a cursor — `Cursor` (and `Anchor`) are UTF-16 offsets in
/// `0 .. Text.Length`. Every edit is pure and returns a new buffer. Edits and
/// motions are grapheme-aware: the cursor and anchor are kept on grapheme-cluster
/// boundaries (see `TextBuffer.normalize`), so a motion or deletion never splits
/// a surrogate pair, a base + combining-mark run, or a ZWJ emoji sequence, and
/// vertical motion preserves the *display column*, not the char count.
type TextBuffer =
    {
        Text: string
        Cursor: int
        /// Selection anchor: `Some i` means text between `i` and `Cursor` is
        /// selected; `None` means no selection (just the caret). Preserved across
        /// `{ b with … }` copies, so plain move-ops keep it — `TextEdit` clears it on
        /// a non-extending move and sets/extends it on a shift-move.
        Anchor: int option
    }

    static member Empty = { Text = ""; Cursor = 0; Anchor = None }

    static member Of(s: string) =
        { Text = s
          Cursor = s.Length
          Anchor = None }

module TextBuffer =

    /// Clamp an offset into `0 .. text.Length` and snap it to a cluster boundary.
    let private snap (text: string) (pos: int) = Grapheme.floorBoundary text pos

    /// Restore the buffer invariants: `Cursor` and `Anchor` within
    /// `0 .. Text.Length` and on grapheme-cluster boundaries. Every operation
    /// normalizes its input, so a hand-built or stale buffer (an anchor past the
    /// end, a cursor inside a surrogate pair) can't make an edit throw or split a
    /// cluster.
    let normalize (b: TextBuffer) : TextBuffer =
        let t = if isNull b.Text then "" else b.Text

        { Text = t
          Cursor = snap t b.Cursor
          Anchor = b.Anchor |> Option.map (snap t) }

    let private clampCursor (b: TextBuffer) = normalize b

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

    /// The selected range as `(lo, hi)` offsets (`hi` exclusive, both clamped to
    /// the text and on cluster boundaries), or `None` when there's no selection.
    let selection (b: TextBuffer) : (int * int) option =
        let b = normalize b
        b.Anchor |> Option.map (fun a -> (min a b.Cursor, max a b.Cursor))

    /// True when a non-empty range is selected.
    let hasSelection (b: TextBuffer) : bool =
        match selection b with
        | Some(lo, hi) -> hi > lo
        | None -> false

    /// Drop any selection, keeping the caret where it is.
    let clearSelection (b: TextBuffer) : TextBuffer = { normalize b with Anchor = None }

    /// Delete the selected range (caret to its start); a no-op collapse otherwise.
    let deleteSelection (b: TextBuffer) : TextBuffer =
        let b = normalize b

        match selection b with
        | Some(lo, hi) when hi > lo ->
            { Text = b.Text.Substring(0, lo) + b.Text.Substring(hi)
              Cursor = lo
              Anchor = None }
            |> normalize
        | _ -> { b with Anchor = None }

    /// Select the whole buffer (anchor at 0, caret at the end).
    let selectAll (b: TextBuffer) : TextBuffer =
        { normalize b with
            Anchor = Some 0
            Cursor = b.Text.Length }

    /// Apply a motion as a selection extension (shift+move): anchor at the current
    /// caret if not already anchored, then move — the move preserves the anchor.
    let extend (move: TextBuffer -> TextBuffer) (b: TextBuffer) : TextBuffer =
        let b = normalize b

        move (
            match b.Anchor with
            | Some _ -> b
            | None -> { b with Anchor = Some b.Cursor }
        )

    /// Insert `s` at the cursor (replacing any selection) and advance past it.
    /// The caret lands on the next cluster boundary at or after the insertion
    /// (inserting a combining mark joins it to the preceding cluster).
    let insert (s: string) (b: TextBuffer) : TextBuffer =
        let b = deleteSelection b
        let s = if isNull s then "" else s
        let text = b.Text.Substring(0, b.Cursor) + s + b.Text.Substring(b.Cursor)
        let target = b.Cursor + s.Length
        let snapped = snap text target

        { Text = text
          Cursor =
            (if snapped < target then
                 Grapheme.nextBoundary text snapped
             else
                 snapped)
          Anchor = None }

    /// Delete the selection if any, else the cluster before the cursor (Backspace).
    let backspace (b: TextBuffer) : TextBuffer =
        if hasSelection b then
            deleteSelection b
        else
            let b = normalize b

            if b.Cursor = 0 then
                { b with Anchor = None }
            else
                let p = Grapheme.prevBoundary b.Text b.Cursor

                { Text = b.Text.Substring(0, p) + b.Text.Substring(b.Cursor)
                  Cursor = p
                  Anchor = None }
                |> normalize

    /// Delete the selection if any, else the cluster at the cursor (forward Delete).
    let delete (b: TextBuffer) : TextBuffer =
        if hasSelection b then
            deleteSelection b
        else
            let b = normalize b

            if b.Cursor >= b.Text.Length then
                { b with Anchor = None }
            else
                let n = Grapheme.nextBoundary b.Text b.Cursor

                { b with
                    Text = b.Text.Substring(0, b.Cursor) + b.Text.Substring(n)
                    Anchor = None }
                |> normalize

    /// Move one cluster left.
    let left (b: TextBuffer) : TextBuffer =
        let b = normalize b

        { b with
            Cursor = Grapheme.prevBoundary b.Text b.Cursor }

    /// Move one cluster right.
    let right (b: TextBuffer) : TextBuffer =
        let b = normalize b

        { b with
            Cursor = Grapheme.nextBoundary b.Text b.Cursor }

    let home (b: TextBuffer) : TextBuffer = { normalize b with Cursor = 0 }

    let toEnd (b: TextBuffer) : TextBuffer =
        { normalize b with
            Cursor = b.Text.Length }

    /// Move the cursor to the start of the previous word (skip whitespace, then
    /// the word). No-op at the start of the buffer.
    let wordLeft (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let mutable i = b.Cursor

        while i > 0 && not (isWord b.Text.[i - 1]) do
            i <- i - 1

        while i > 0 && isWord b.Text.[i - 1] do
            i <- i - 1

        { b with Cursor = snap b.Text i }

    /// Move the cursor to the end of the next word. No-op at the end of the buffer.
    let wordRight (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let n = b.Text.Length
        let mutable i = b.Cursor

        while i < n && not (isWord b.Text.[i]) do
            i <- i + 1

        while i < n && isWord b.Text.[i] do
            i <- i + 1

        // A whitespace char carrying a combining mark could leave `i` mid-cluster;
        // round forward so the motion always makes progress.
        let j = snap b.Text i

        { b with
            Cursor = (if j < i then Grapheme.nextBoundary b.Text j else j) }

    /// Select the word around/before the cursor (anchor at its start, caret at its end).
    let selectWord (b: TextBuffer) : TextBuffer =
        let b = normalize b

        { b with
            Anchor = Some (wordLeft b).Cursor
            Cursor = (wordRight b).Cursor }

    /// Delete the selection if any, else from the cursor back to the start of the
    /// previous word. The action is key-agnostic; the conventional binding
    /// (Ctrl/Cmd+Backspace) is wired in `Mire.Core.TextEdit`.
    let deleteWordBack (b: TextBuffer) : TextBuffer =
        if hasSelection b then
            deleteSelection b
        else
            let b = clampCursor b
            let i = (wordLeft b).Cursor

            { Text = b.Text.Substring(0, i) + b.Text.Substring(b.Cursor)
              Cursor = i
              Anchor = None }
            |> normalize

    /// Delete the selection if any, else from the cursor forward to the end of
    /// the next word.
    let deleteWordForward (b: TextBuffer) : TextBuffer =
        if hasSelection b then
            deleteSelection b
        else
            let b = clampCursor b
            let j = (wordRight b).Cursor

            { Text = b.Text.Substring(0, b.Cursor) + b.Text.Substring(j)
              Cursor = b.Cursor
              Anchor = None }
            |> normalize

    /// The cursor's `(row, col)` — both 0-based — over the `\n`-delimited text.
    /// `col` is the UTF-16 offset within the line (see `cursorRowColumn` for the
    /// display column).
    let cursorRowCol (b: TextBuffer) : int * int =
        let b = normalize b
        let p = b.Cursor
        let mutable row = 0

        for i in 0 .. p - 1 do
            if b.Text.[i] = '\n' then
                row <- row + 1

        (row, p - lineStartIndex b.Text p)

    /// The cursor's `(row, displayColumn)`: the row as in `cursorRowCol`, and the
    /// number of terminal columns the line's text before the cursor occupies (a
    /// wide glyph counts 2, a combining mark 0 — see `Grapheme.editDisplay`).
    let cursorRowColumn (b: TextBuffer) : int * int =
        let b = normalize b
        let (row, _) = cursorRowCol b
        let ls = lineStartIndex b.Text b.Cursor
        (row, Grapheme.editColumns b.Text ls b.Cursor)

    /// Move the cursor to the start of the current line.
    let lineStart (b: TextBuffer) : TextBuffer =
        let b = normalize b

        { b with
            Cursor = lineStartIndex b.Text b.Cursor }

    /// Move the cursor to the end of the current line (before its `\n`).
    let lineEnd (b: TextBuffer) : TextBuffer =
        let b = normalize b

        { b with
            Cursor = snap b.Text (lineEndIndex b.Text b.Cursor) }

    /// Move to the same display column on the previous line (clamped to its
    /// width). No sticky desired-column: the column is recomputed each move.
    /// No-op on line 0.
    let up (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let (row, col) = cursorRowColumn b

        if row = 0 then
            b
        else
            let curLineStart = lineStartIndex b.Text b.Cursor
            let prevLineStart = lineStartIndex b.Text (curLineStart - 1)
            let prevLineEnd = curLineStart - 1

            { b with
                Cursor = Grapheme.offsetAtColumn b.Text prevLineStart prevLineEnd col }

    /// Move to the same display column on the next line (clamped). No-op on the
    /// last line.
    let down (b: TextBuffer) : TextBuffer =
        let b = clampCursor b
        let (_, col) = cursorRowColumn b
        let curLineEnd = lineEndIndex b.Text b.Cursor

        if curLineEnd >= b.Text.Length then
            b
        else
            let nextLineStart = curLineEnd + 1
            let nextLineEnd = lineEndIndex b.Text nextLineStart

            { b with
                Cursor = Grapheme.offsetAtColumn b.Text nextLineStart nextLineEnd col }

    let clear (_: TextBuffer) : TextBuffer = TextBuffer.Empty
