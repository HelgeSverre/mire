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

    member _.Size = safeSize
    member _.Cells = cells

    member _.Item
        with get (x, y) =
            if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                cells.[y * safeSize.Width + x]
            else
                Cell.Empty
        and set (x, y) (value: Cell) =
            if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                cells.[y * safeSize.Width + x] <- value

    member _.Clear() = Array.Fill(cells, Cell.Empty)

    member _.FillRect(rect: Rect, cell: Cell) =
        for y in rect.Top .. rect.Bottom do
            for x in rect.Left .. rect.Right do
                if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                    cells.[y * safeSize.Width + x] <- cell

    member _.Write(x, y, text: string, style: Style) =
        let mutable cx = x

        for c in text do
            let w = Grapheme.charWidth c

            if w = 0 then
                // Combining mark - append to previous cell if possible
                if cx > x && cx > 0 && cx <= safeSize.Width && y >= 0 && y < safeSize.Height then
                    let idx = y * safeSize.Width + cx - 1
                    let cell = cells.[idx]

                    if not cell.IsEmpty then
                        cells.[idx] <-
                            { cell with
                                Grapheme = cell.Grapheme + string c }
            elif cx >= 0 && cx < safeSize.Width && y >= 0 && y < safeSize.Height then
                cells.[y * safeSize.Width + cx] <- Cell.FromChar(c, style)
                cx <- cx + w

    member _.WriteClipped(rect: Rect, text: string, style: Style) =
        let mutable cx = rect.Left
        let mutable cy = rect.Top

        for c in text do
            if c = '\n' then
                cx <- rect.Left
                cy <- cy + 1
            else
                let w = Grapheme.charWidth c

                if w = 0 then
                    if
                        cx > rect.Left
                        && cx > 0
                        && cx <= rect.Right
                        && cy >= rect.Top
                        && cy <= rect.Bottom
                        && cx <= safeSize.Width
                        && cy < safeSize.Height
                    then
                        let idx = cy * safeSize.Width + cx - 1
                        let cell = cells.[idx]

                        if not cell.IsEmpty then
                            cells.[idx] <-
                                { cell with
                                    Grapheme = cell.Grapheme + string c }
                elif
                    cx >= 0
                    && cx <= rect.Right
                    && cy >= rect.Top
                    && cy <= rect.Bottom
                    && cx < safeSize.Width
                    && cy < safeSize.Height
                then
                    cells.[cy * safeSize.Width + cx] <- Cell.FromChar(c, style)
                    cx <- cx + w

    member _.DrawBox(rect: Rect, style: Style) =
        if rect.Width > 0 && rect.Height > 0 then
            // Corners
            if
                rect.Left >= 0
                && rect.Left < safeSize.Width
                && rect.Top >= 0
                && rect.Top < safeSize.Height
            then
                cells.[rect.Top * safeSize.Width + rect.Left] <- Cell.FromChar('┌', style)

            if
                rect.Right >= 0
                && rect.Right < safeSize.Width
                && rect.Top >= 0
                && rect.Top < safeSize.Height
            then
                cells.[rect.Top * safeSize.Width + rect.Right] <- Cell.FromChar('┐', style)

            if
                rect.Left >= 0
                && rect.Left < safeSize.Width
                && rect.Bottom >= 0
                && rect.Bottom < safeSize.Height
            then
                cells.[rect.Bottom * safeSize.Width + rect.Left] <- Cell.FromChar('└', style)

            if
                rect.Right >= 0
                && rect.Right < safeSize.Width
                && rect.Bottom >= 0
                && rect.Bottom < safeSize.Height
            then
                cells.[rect.Bottom * safeSize.Width + rect.Right] <- Cell.FromChar('┘', style)
            // Horizontal lines
            for x in rect.Left + 1 .. rect.Right - 1 do
                if x >= 0 && x < safeSize.Width && rect.Top >= 0 && rect.Top < safeSize.Height then
                    cells.[rect.Top * safeSize.Width + x] <- Cell.FromChar('─', style)

                if
                    x >= 0
                    && x < safeSize.Width
                    && rect.Bottom >= 0
                    && rect.Bottom < safeSize.Height
                then
                    cells.[rect.Bottom * safeSize.Width + x] <- Cell.FromChar('─', style)
            // Vertical lines
            for y in rect.Top + 1 .. rect.Bottom - 1 do
                if rect.Left >= 0 && rect.Left < safeSize.Width && y >= 0 && y < safeSize.Height then
                    cells.[y * safeSize.Width + rect.Left] <- Cell.FromChar('│', style)

                if rect.Right >= 0 && rect.Right < safeSize.Width && y >= 0 && y < safeSize.Height then
                    cells.[y * safeSize.Width + rect.Right] <- Cell.FromChar('│', style)

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

                        cells.[y * safeSize.Width + x] <- cell

    member _.DrawHorizontalLine(y, x1, x2, style: Style) =
        for x in x1..x2 do
            if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                cells.[y * safeSize.Width + x] <- Cell.FromChar('─', style)

    member _.DrawVerticalLine(x, y1, y2, style: Style) =
        for y in y1..y2 do
            if x >= 0 && x < safeSize.Width && y >= 0 && y < safeSize.Height then
                cells.[y * safeSize.Width + x] <- Cell.FromChar('│', style)
