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

    member this.IsEmpty =
        this.Grapheme = " " && this.Width = 1 && this.Style = Style.Default

    member this.WithStyle(style: Style) =
        { Grapheme = this.Grapheme
          Width = this.Width
          Style = style }
