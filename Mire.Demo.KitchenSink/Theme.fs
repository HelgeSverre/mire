namespace Mire.Demo.KitchenSink

open Mire.Core
open Mire.Brand
open Mire.Widgets

/// Brand-themed style set for the KitchenSink demo, built on the canonical Mire
/// brand tokens (brand/palette.fs). Brand discipline: neutrals carry hierarchy,
/// emerald is the single accent moment, selection is inverse video. Status colors
/// are a functional extension — badges, toasts, and table cells need them to be
/// legible.
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

    let heading1 = Style.Default.WithForeground(headingColor).WithBold(true)
    let heading2 = Style.Default.WithForeground(headingColor).WithBold(true)
    let heading3 = Style.Default.WithForeground(fgMuted).WithBold(true)
    let bold = text.WithBold(true)
    let italic = text.WithItalic(true)
    let strike = subtle.WithStrikethrough(true)
    let link = Style.Default.WithForeground(infoColor).WithUnderline(UnderlineStyle.Single)
    let mention = Style.Default.WithForeground(accent)

    let code = Style.Default.WithForeground(fg).WithBackground(bgElevated)
    let codeKw = Style.Default.WithForeground(infoColor).WithBackground(bgElevated)
    let codeStr = Style.Default.WithForeground(okColor).WithBackground(bgElevated)
    let codeCom = Style.Default.WithForeground(fgSubtle).WithBackground(bgElevated)
    let codeNum = Style.Default.WithForeground(Color.Cyan).WithBackground(bgElevated)

    let borderStyle = Style.Default.WithForeground(borderColor)
    let borderFocus = Style.Default.WithForeground(accent)
    let accentStyle = Style.Default.WithForeground(accent)
    let accentStrongStyle =
        Style.Default
            .WithForeground(Color.White)
            .WithBackground(ofPalette Palette.Accent.a700)

    let okStyle = Style.Default.WithForeground(okColor)
    let warnStyle = Style.Default.WithForeground(warnColor)
    let errStyle = Style.Default.WithForeground(errColor)
    let infoStyle = Style.Default.WithForeground(infoColor)

    let bg =
        Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.bg)

    let bgElevatedStyle =
        Style.Default.WithBackground(bgElevated)

    let divider =
        Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.border)

    let selection =
        Style.Default.WithForeground(ofPalette Palette.Semantic.Dark.bg).WithBackground(fg)

    let selAccent = Style.Default.WithForeground(accentFg).WithBackground(accent)

    let keyStyle = Style.Default.WithForeground(fgMuted).WithBold(true)

    let markdown: MarkdownStyle =
        { Text = text
          Heading1 = heading1
          Heading2 = heading2
          Heading3 = heading3
          Quote = muted
          Link = link
          Mention = Some mention
          Rule = borderStyle
          Code = code
          CodeKeyword = codeKw
          CodeString = codeStr
          CodeComment = codeCom
          CodeNumber = codeNum }

    /// The assembled `AppTheme` record for passing to widget helpers.
    let theme: AppTheme =
        { fg = text
          fgMuted = muted
          fgSubtle = subtle
          title = title
          bg = bg
          bgElevated = bgElevatedStyle
          border = borderStyle
          borderFocus = borderFocus
          divider = divider
          accent = accentStyle
          accentFg = Style.Default.WithForeground(accentFg)
          accentStrong = accentStrongStyle
          success = okStyle
          warning = warnStyle
          danger = errStyle
          info = infoStyle
          selection = selection
          selectionAccent = selAccent
          key = keyStyle
          markdown = markdown }
