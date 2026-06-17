namespace Mire.Core

[<Struct>]
type Cell =
    { Grapheme: string
      Width: int
      Style: Style }

    static member Empty =
        { Grapheme = " "
          Width = 1
          Style = Style.Default }

    static member FromChar(c: char, style: Style) =
        { Grapheme = string c
          Width = 1
          Style = style }

    static member FromString(s: string, style: Style) =
        { Grapheme = s
          Width = s.Length
          Style = style }

    /// The trailing half of a wide (width-2) glyph: an empty-grapheme placeholder
    /// the renderer skips (the wide glyph already covers this column). It is
    /// deliberately distinct from `Empty` so the diff repaints this column when a
    /// later frame replaces the wide glyph with narrower content (clearing the
    /// glyph's stale right half). Carries the glyph's style so it groups into the
    /// same diff run.
    static member Continuation(style: Style) =
        { Grapheme = ""
          Width = 0
          Style = style }

    member this.IsEmpty =
        this.Grapheme = " " && this.Width = 1 && this.Style = Style.Default

    /// The trailing half of a wide glyph (see `Continuation`): no grapheme of its
    /// own, zero width. Used to step back to the base glyph when attaching a
    /// combining mark, and skipped when emitting.
    member this.IsContinuation = this.Grapheme = "" && this.Width = 0

    member this.WithStyle(style: Style) =
        { Grapheme = this.Grapheme
          Width = this.Width
          Style = style }
