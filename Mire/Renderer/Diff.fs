namespace Mire.Renderer

open System
open System.Text
open Mire.Core

type DiffRun =
    { X: int
      Y: int
      Text: string
      Style: Style }

module Diff =
    
    let compute (prev: Surface option) (next: Surface) : DiffRun list =
        match prev with
        | None ->
            // Full render - emit all non-empty cells grouped by style
            let runs = ResizeArray<DiffRun>()
            let mutable y = 0
            while y < next.Size.Height do
                let mutable x = 0
                while x < next.Size.Width do
                    let cell = next.[x, y]
                    if not cell.IsEmpty then
                        // Collect run of same-style cells
                        let mutable text = StringBuilder()
                        let style = cell.Style
                        let startX = x
                        text.Append(cell.Grapheme) |> ignore
                        x <- x + 1
                        while x < next.Size.Width && next.[x, y].Style = style && not next.[x, y].IsEmpty do
                            text.Append(next.[x, y].Grapheme) |> ignore
                            x <- x + 1
                        runs.Add
                            { X = startX
                              Y = y
                              Text = text.ToString()
                              Style = style }
                    else
                        x <- x + 1
                y <- y + 1
            runs |> Seq.toList
        | Some prev ->
            let runs = ResizeArray<DiffRun>()
            let minHeight = min prev.Size.Height next.Size.Height
            let minWidth = min prev.Size.Width next.Size.Width
            let mutable y = 0
            while y < minHeight do
                let mutable x = 0
                while x < minWidth do
                    let prevCell = prev.[x, y]
                    let nextCell = next.[x, y]
                    if prevCell <> nextCell then
                        let style = nextCell.Style
                        let startX = x
                        let text = StringBuilder()
                        
                        // Collect contiguous changed cells with same style
                        while x < minWidth &&
                              (prev.[x, y] <> next.[x, y]) &&
                              next.[x, y].Style = style do
                            text.Append(next.[x, y].Grapheme) |> ignore
                            x <- x + 1
                        
                        runs.Add
                            { X = startX
                              Y = y
                              Text = text.ToString()
                              Style = style }
                    else
                        x <- x + 1
                y <- y + 1
            runs |> Seq.toList
    
    let renderToTerminal (runs: DiffRun list) (output: System.IO.TextWriter) =
        let mutable currentStyle = Style.Default
        let mutable currentX = -1
        let mutable currentY = -1
        
        // Hide cursor during render
        output.Write(Mire.Protocol.ANSI.cursorHide)
        
        for run in runs do
            // Move cursor if needed
            if run.Y <> currentY || run.X <> currentX then
                output.Write(Mire.Protocol.ANSI.cursorTo(run.X, run.Y))
                currentX <- run.X
                currentY <- run.Y
            
            // Set style if changed
            if run.Style <> currentStyle then
                output.Write(run.Style.ToAnsi())
                currentStyle <- run.Style
            
            // Write text
            output.Write(run.Text)
            currentX <- currentX + run.Text.Length
        
        // Reset style
        if currentStyle <> Style.Default then
            output.Write(Mire.Protocol.ANSI.resetStyle)
        
        output.Flush()
    
    let clearScreen (output: System.IO.TextWriter) =
        output.Write(Mire.Protocol.ANSI.clearScreen)
        output.Write(Mire.Protocol.ANSI.cursorHome)
        output.Flush()
