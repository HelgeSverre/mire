namespace Mire.Demo.Feed

open Mire.Core
open Mire.Brand
open Mire.Widgets

module Theme =

    let private ofPalette (c: Palette.Color) =
        let (r, g, b) = c.Rgb
        Color.Rgb(r, g, b)

    let fg = ofPalette Palette.Semantic.Dark.fg
    let fgMuted = ofPalette Palette.Semantic.Dark.fgMuted
    let fgSubtle = ofPalette Palette.Semantic.Dark.fgSubtle
    let bgElevated = ofPalette Palette.Semantic.Dark.bgElevated
    let borderColor = ofPalette Palette.Semantic.Dark.borderStrong
    let accent = ofPalette Palette.Semantic.Dark.accent
    let accentFg = ofPalette Palette.Semantic.Dark.accentFg
    let headingColor = ofPalette Palette.Accent.a300

    let okColor = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
    let warnColor = Color.Rgb(0xFFuy, 0xA0uy, 0x00uy)
    let errColor = Color.Rgb(0xFFuy, 0x57uy, 0x22uy)
    let infoColor = Color.Rgb(0x4Auy, 0x90uy, 0xD9uy)

    let text = Style.Default.WithForeground(fg)
    let muted = Style.Default.WithForeground(fgMuted)
    let subtle = Style.Default.WithForeground(fgSubtle)
    let title = Style.Default.WithForeground(fg).WithBold(true)

    let borderStyle = Style.Default.WithForeground(borderColor)
    let accentStyle = Style.Default.WithForeground(accent)

    let okStyle = Style.Default.WithForeground(okColor)
    let warnStyle = Style.Default.WithForeground(warnColor)
    let errStyle = Style.Default.WithForeground(errColor)
    let infoStyle = Style.Default.WithForeground(infoColor)

    let bg = Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.bg)

    let selection =
        Style.Default.WithForeground(ofPalette Palette.Semantic.Dark.bg).WithBackground(fg)

    let selAccent = Style.Default.WithForeground(accentFg).WithBackground(accent)

    let bgElevatedStyle = Style.Default.WithBackground(bgElevated)

    let markdown: MarkdownStyle =
        let heading1 = Style.Default.WithForeground(headingColor).WithBold(true)
        let heading2 = Style.Default.WithForeground(headingColor).WithBold(true)
        let heading3 = Style.Default.WithForeground(fgMuted).WithBold(true)
        let code =
            Style.Default.WithForeground(fg).WithBackground(bgElevated)
        let codeKw =
            Style.Default.WithForeground(infoColor).WithBackground(bgElevated)
        let codeStr =
            Style.Default.WithForeground(okColor).WithBackground(bgElevated)
        let codeCom =
            Style.Default.WithForeground(fgSubtle).WithBackground(bgElevated)
        let codeNum =
            Style.Default.WithForeground(Color.Cyan).WithBackground(bgElevated)

        { Text = text
          Heading1 = heading1
          Heading2 = heading2
          Heading3 = heading3
          Quote = muted
          Link = Style.Default.WithForeground(infoColor).WithUnderline(UnderlineStyle.Single)
          Mention = None
          Rule = borderStyle
          Code = code
          CodeKeyword = codeKw
          CodeString = codeStr
          CodeComment = codeCom
          CodeNumber = codeNum }

    let theme: AppTheme =
        { fg = text
          fgMuted = muted
          fgSubtle = subtle
          title = title
          bg = bg
          bgElevated = bgElevatedStyle
          border = borderStyle
          borderFocus = Style.Default.WithForeground(accent)
          divider = Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.border)
          accent = accentStyle
          accentFg = Style.Default.WithForeground(accentFg)
          accentStrong =
              Style.Default.WithForeground(Color.White).WithBackground(ofPalette Palette.Accent.a700)
          success = okStyle
          warning = warnStyle
          danger = errStyle
          info = infoStyle
          selection = selection
          selectionAccent = selAccent
          key = Style.Default.WithForeground(fgMuted).WithBold(true)
          markdown = markdown }
