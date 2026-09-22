namespace Mire.Renderer

open System
open Mire.Core

type Surface(size: Size) =
    let safeSize =
        if size.IsValid then
            size
        else
            Size.Create(max 1 size.Width, max 1 size.Height)

    let cells = Array.init (safeSize.Width * safeSize.Height) (fun _ -> Cell.Empty)

    /// A narrow blank carrying `style` — what a wide glyph's orphaned half
    /// degrades to when its partner column is overwritten or clipped away.
    let blank (style: Style) =
        { Grapheme = " "
          Width = 1
          Style = style }

    let inBounds x y =
        x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height

    /// Store `cell` at (x, y), preserving the wide-glyph pairing invariant: a
    /// wide (width-2) lead is always immediately followed by its continuation
    /// cell, and a continuation always follows a wide lead. Overwriting either
    /// half of an existing pair degrades the *other* half to a blank, so the
    /// grid never holds a lead whose right column shows unrelated content (the
    /// terminal would erase or shift one of them and the diff would desync).
    let put x y (cell: Cell) =
        if inBounds x y then
            let row = y * safeSize.Width
            let old = cells.[row + x]

            // Cutting a pair's right half: the lead on the left loses its column.
            if old.IsContinuation && not cell.IsContinuation && x > 0 then
                let left = cells.[row + x - 1]

                if left.Width = 2 then
                    cells.[row + x - 1] <- blank left.Style

            // Cutting a pair's left half: the continuation on the right is orphaned.
            if old.Width = 2 && cell.Width <> 2 && x + 1 < safeSize.Width then
                let right = cells.[row + x + 1]

                if right.IsContinuation then
                    cells.[row + x + 1] <- blank right.Style

            cells.[row + x] <- cell

    /// Place one grapheme cluster of display width `w` (1 or 2) at (x, y),
    /// clipped to columns `clipLeft..clipRight`. A wide glyph is clipped
    /// atomically: if its second column falls outside the clip, a blank is
    /// written in its place rather than half a glyph. A wide glyph written over
    /// the lead of another wide glyph also repairs that glyph's continuation.
    let putGlyph x y (cluster: string) (w: int) (style: Style) (clipLeft: int) (clipRight: int) =
        if x >= clipLeft && x <= clipRight && inBounds x y then
            if w = 2 then
                if x + 1 <= clipRight && x + 1 < safeSize.Width then
                    // The continuation column may itself hold a wide lead whose
                    // continuation (x + 2) would be orphaned.
                    put (x + 1) y (blank style)

                    put
                        x
                        y
                        { Grapheme = cluster
                          Width = 2
                          Style = style }

                    cells.[y * safeSize.Width + x + 1] <- Cell.Continuation style
                else
                    put x y (blank style)
            else
                put
                    x
                    y
                    { Grapheme = cluster
                      Width = 1
                      Style = style }

    /// Tab stops every 8 columns, measured from the write's origin column.
    let tabWidth = 8

    /// Attach a zero-width (combining) cluster to the base glyph left of `cx`,
    /// stepping back over a wide glyph's continuation column (which carries no
    /// grapheme of its own) so it lands on the glyph, not the gap.
    let attachCombining (cx: int) (cy: int) (cluster: string) (clipLeft: int) (clipRight: int) =
        let mutable bx = cx - 1

        while bx > clipLeft
              && bx >= 0
              && bx < safeSize.Width
              && cy >= 0
              && cy < safeSize.Height
              && cells.[cy * safeSize.Width + bx].IsContinuation do
            bx <- bx - 1

        if bx >= clipLeft && bx <= clipRight && inBounds bx cy then
            let idx = cy * safeSize.Width + bx
            let cell = cells.[idx]

            if not cell.IsEmpty && not cell.IsContinuation then
                cells.[idx] <-
                    { cell with
                        Grapheme = cell.Grapheme + cluster }

    /// Lay `text` out along row `cy` from column `cx`, clipped to
    /// `clipLeft..clipRight`, with tab stops measured from `origin`. Control
    /// scalars are rendered as inert visible stand-ins (`Grapheme.sanitize`), so
    /// ordinary text can never smuggle terminal commands into the output stream.
    /// Returns the column after the last cluster. Newlines are the caller's
    /// business (`WriteClipped` splits on them; `Write` shows them as `␊`).
    let writeRun
        (cx0: int)
        (cy: int)
        (clusters: string seq)
        (style: Style)
        (origin: int)
        (clipLeft: int)
        (clipRight: int)
        =
        let mutable cx = cx0

        for raw in clusters do
            if raw = "\t" then
                let next = origin + ((cx - origin) / tabWidth + 1) * tabWidth

                while cx < next do
                    putGlyph cx cy " " 1 style clipLeft clipRight
                    cx <- cx + 1
            else
                let cluster = Grapheme.sanitize raw
                let w = Grapheme.clusterWidth cluster

                if w = 0 then
                    attachCombining cx cy cluster clipLeft clipRight
                else
                    putGlyph cx cy cluster w style clipLeft clipRight
                    cx <- cx + w

        cx

    member _.Size = safeSize
    member _.Cells = cells

    /// Read or write one cell. Writes go through the wide-glyph repair (see
    /// `put`): overwriting half of a wide pair blanks the other half. To place a
    /// wide glyph, write its lead then a `Cell.Continuation` at x + 1.
    member _.Item
        with get (x, y) =
            if inBounds x y then
                cells.[y * safeSize.Width + x]
            else
                Cell.Empty
        and set (x, y) (value: Cell) =
            if inBounds x y then
                if value.IsContinuation then
                    // A continuation completes the lead just written at x - 1;
                    // store it without the "cut a pair" repair.
                    let old = cells.[y * safeSize.Width + x]

                    if old.Width = 2 && x + 1 < safeSize.Width then
                        let right = cells.[y * safeSize.Width + x + 1]

                        if right.IsContinuation then
                            cells.[y * safeSize.Width + x + 1] <- blank right.Style

                    cells.[y * safeSize.Width + x] <- value
                else
                    put x y value

    member _.Clear() = Array.Fill(cells, Cell.Empty)

    member _.FillRect(rect: Rect, cell: Cell) =
        for y in rect.Top .. rect.Bottom do
            for x in rect.Left .. rect.Right do
                put x y cell

    /// Translucent scrim: blend the colours of every cell already painted under
    /// `rect` toward `tint` by `strength` (0.0 = untouched, 1.0 = solid `tint`),
    /// keeping each glyph. Unlike `FillRect`, it doesn't replace the content with
    /// blanks — it fades it, the way a modal dims the screen behind it. Cells whose
    /// fg/bg are `Default` (terminal default) are resolved to `fgFallback`/
    /// `bgFallback` before blending so they dim too rather than staying full-bright.
    member _.Scrim(rect: Rect, tint: Color, strength: float, fgFallback: Color, bgFallback: Color) =
        for y in rect.Top .. rect.Bottom do
            for x in rect.Left .. rect.Right do
                if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                    let idx = y * safeSize.Width + x
                    let cell = cells.[idx]
                    let s = cell.Style
                    let fg0 = s.Foreground |> Option.defaultValue fgFallback
                    let bg0 = s.Background |> Option.defaultValue bgFallback

                    let style =
                        { s with
                            Foreground = Some(Color.Blend(fg0, tint, strength))
                            Background = Some(Color.Blend(bg0, tint, strength)) }

                    cells.[idx] <- { cell with Style = style }

    /// Write `text` on row `y` starting at column `x`, clipped to the surface.
    /// Single-line: a newline shows as `␊` (use `WriteClipped` for multi-line
    /// text); tabs expand to 8-column stops from `x`; other control characters
    /// render as visible stand-ins rather than reaching the terminal.
    member _.Write(x, y, text: string, style: Style) =
        writeRun x y (Grapheme.clusters text) style x 0 (safeSize.Width - 1) |> ignore

    /// Write `text` into `rect`, starting at its top-left, clipped to it. `\n`,
    /// `\r`, and `\r\n` start the next row; tabs expand to 8-column stops from
    /// `rect.Left`; other control characters render as visible stand-ins. A wide
    /// glyph that doesn't fit before `rect.Right` is replaced by a blank.
    member _.WriteClipped(rect: Rect, text: string, style: Style) =
        let mutable cy = rect.Top
        let mutable line = ResizeArray<string>()

        let flush () =
            if cy >= rect.Top && cy <= rect.Bottom then
                writeRun rect.Left cy line style rect.Left rect.Left rect.Right |> ignore

            line.Clear()

        for cluster in Grapheme.clusters text do
            if cluster = "\n" || cluster = "\r" || cluster = "\r\n" then
                flush ()
                cy <- cy + 1
            else
                line.Add cluster

        flush ()

    member _.DrawBox(rect: Rect, style: Style) =
        if rect.Width > 0 && rect.Height > 0 then
            // Corners
            if
                rect.Left >= 0
                && rect.Left < safeSize.Width
                && rect.Top >= 0
                && rect.Top < safeSize.Height
            then
                put rect.Left rect.Top (Cell.FromChar('┌', style))

            if
                rect.Right >= 0
                && rect.Right < safeSize.Width
                && rect.Top >= 0
                && rect.Top < safeSize.Height
            then
                put rect.Right rect.Top (Cell.FromChar('┐', style))

            if
                rect.Left >= 0
                && rect.Left < safeSize.Width
                && rect.Bottom >= 0
                && rect.Bottom < safeSize.Height
            then
                put rect.Left rect.Bottom (Cell.FromChar('└', style))

            if
                rect.Right >= 0
                && rect.Right < safeSize.Width
                && rect.Bottom >= 0
                && rect.Bottom < safeSize.Height
            then
                put rect.Right rect.Bottom (Cell.FromChar('┘', style))
            // Horizontal lines
            for x in rect.Left + 1 .. rect.Right - 1 do
                if x >= 0 && x < safeSize.Width && rect.Top >= 0 && rect.Top < safeSize.Height then
                    put x rect.Top (Cell.FromChar('─', style))

                if
                    x >= 0
                    && x < safeSize.Width
                    && rect.Bottom >= 0
                    && rect.Bottom < safeSize.Height
                then
                    put x rect.Bottom (Cell.FromChar('─', style))
            // Vertical lines
            for y in rect.Top + 1 .. rect.Bottom - 1 do
                if rect.Left >= 0 && rect.Left < safeSize.Width && y >= 0 && y < safeSize.Height then
                    put rect.Left y (Cell.FromChar('│', style))

                if rect.Right >= 0 && rect.Right < safeSize.Width && y >= 0 && y < safeSize.Height then
                    put rect.Right y (Cell.FromChar('│', style))

    member _.DrawFilledBox(rect: Rect, borderStyle: Style, fillStyle: Style) =
        if rect.Width > 0 && rect.Height > 0 then
            for y in rect.Top .. rect.Bottom do
                for x in rect.Left .. rect.Right do
                    if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                        let cell =
                            if x = rect.Left || x = rect.Right || y = rect.Top || y = rect.Bottom then
                                if x = rect.Left && y = rect.Top then
                                    Cell.FromChar('┌', borderStyle)
                                elif x = rect.Right && y = rect.Top then
                                    Cell.FromChar('┐', borderStyle)
                                elif x = rect.Left && y = rect.Bottom then
                                    Cell.FromChar('└', borderStyle)
                                elif x = rect.Right && y = rect.Bottom then
                                    Cell.FromChar('┘', borderStyle)
                                elif y = rect.Top || y = rect.Bottom then
                                    Cell.FromChar('─', borderStyle)
                                else
                                    Cell.FromChar('│', borderStyle)
                            else
                                Cell.FromChar(' ', fillStyle)

                        put x y cell

    member _.DrawHorizontalLine(y, x1, x2, style: Style) =
        for x in x1..x2 do
            if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                put x y (Cell.FromChar('─', style))

    member _.DrawVerticalLine(x, y1, y2, style: Style) =
        for y in y1..y2 do
            if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                put x y (Cell.FromChar('│', style))
