namespace Mire.Core

type UnderlineStyle =
    | Single
    | Double
    | Curly
    | Dotted
    | Dashed

[<Struct>]
type Style =
    { Foreground: Color option
      Background: Color option
      Bold: bool
      Italic: bool
      Underline: UnderlineStyle option
      Dim: bool
      Strikethrough: bool
      /// OSC 8 hyperlink target. `None` = plain text. The Diff writer brackets a
      /// run carrying `Some url` in OSC 8 open/close sequences; `ToAnsi` ignores
      /// it (a link is not an SGR attribute). Because it's part of the record,
      /// structural equality splits diff runs at link boundaries automatically.
      Link: string option }

    static member Default =
        { Foreground = None
          Background = None
          Bold = false
          Italic = false
          Underline = None
          Dim = false
          Strikethrough = false
          Link = None }

    member this.WithForeground(color: Color) =
        { Foreground = Some color
          Background = this.Background
          Bold = this.Bold
          Italic = this.Italic
          Underline = this.Underline
          Dim = this.Dim
          Strikethrough = this.Strikethrough
          Link = this.Link }

    member this.WithBackground(color: Color) =
        { Foreground = this.Foreground
          Background = Some color
          Bold = this.Bold
          Italic = this.Italic
          Underline = this.Underline
          Dim = this.Dim
          Strikethrough = this.Strikethrough
          Link = this.Link }

    member this.WithBold(value: bool) =
        { Foreground = this.Foreground
          Background = this.Background
          Bold = value
          Italic = this.Italic
          Underline = this.Underline
          Dim = this.Dim
          Strikethrough = this.Strikethrough
          Link = this.Link }

    member this.WithItalic(value: bool) =
        { Foreground = this.Foreground
          Background = this.Background
          Bold = this.Bold
          Italic = value
          Underline = this.Underline
          Dim = this.Dim
          Strikethrough = this.Strikethrough
          Link = this.Link }

    member this.WithUnderline(style: UnderlineStyle) =
        { Foreground = this.Foreground
          Background = this.Background
          Bold = this.Bold
          Italic = this.Italic
          Underline = Some style
          Dim = this.Dim
          Strikethrough = this.Strikethrough
          Link = this.Link }

    member this.WithDim(value: bool) =
        { Foreground = this.Foreground
          Background = this.Background
          Bold = this.Bold
          Italic = this.Italic
          Underline = this.Underline
          Dim = value
          Strikethrough = this.Strikethrough
          Link = this.Link }

    member this.WithStrikethrough(value: bool) =
        { Foreground = this.Foreground
          Background = this.Background
          Bold = this.Bold
          Italic = this.Italic
          Underline = this.Underline
          Dim = this.Dim
          Strikethrough = value
          Link = this.Link }

    /// Attach an OSC 8 hyperlink target. The text renders normally; terminals
    /// that support OSC 8 make the run clickable.
    member this.WithLink(url: string) =
        { Foreground = this.Foreground
          Background = this.Background
          Bold = this.Bold
          Italic = this.Italic
          Underline = this.Underline
          Dim = this.Dim
          Strikethrough = this.Strikethrough
          Link = Some url }

    member this.ToAnsi() =
        let parts = System.Collections.Generic.List<string>()
        parts.Add("\x1b[0m") // reset first

        match this.Foreground with
        | Some color -> parts.Add(color.ToAnsiFg())
        | None -> ()

        match this.Background with
        | Some color -> parts.Add(color.ToAnsiBg())
        | None -> ()

        if this.Bold then
            parts.Add("\x1b[1m")

        if this.Dim then
            parts.Add("\x1b[2m")

        if this.Italic then
            parts.Add("\x1b[3m")

        match this.Underline with
        | Some Single -> parts.Add("\x1b[4m")
        | Some Double -> parts.Add("\x1b[21m")
        | Some Curly -> parts.Add("\x1b[4:3m")
        | Some Dotted -> parts.Add("\x1b[4:4m")
        | Some Dashed -> parts.Add("\x1b[4:5m")
        | None -> ()

        if this.Strikethrough then
            parts.Add("\x1b[9m")

        System.String.Concat(parts)

    override this.ToString() = this.ToAnsi()
