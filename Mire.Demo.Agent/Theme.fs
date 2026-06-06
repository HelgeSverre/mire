namespace Mire.Demo.Agent

open Mire.Core
open Mire.Brand
open Mire.Widgets

/// Harness palette + semantic styles, built on the canonical Mire brand tokens
/// (brand/palette.fs) so the demo stays on-brand. Brand discipline: neutrals carry
/// hierarchy, emerald is the single accent moment (the prompt glyph), selection is
/// inverse video. Status colors (ok/warn/err/info) are a *functional* extension —
/// tool results, diffs, and toasts need them to be legible.
module Theme =

    let private ofPalette (c: Palette.Color) =
        let (r, g, b) = c.Rgb
        Color.Rgb(r, g, b)

    // brand neutrals + accent (dark mode)
    let fg = ofPalette Palette.Semantic.Dark.fg
    let fgMuted = ofPalette Palette.Semantic.Dark.fgMuted
    let fgSubtle = ofPalette Palette.Semantic.Dark.fgSubtle
    let bgElevated = ofPalette Palette.Semantic.Dark.bgElevated
    let borderColor = ofPalette Palette.Semantic.Dark.borderStrong
    let accent = ofPalette Palette.Semantic.Dark.accent
    let accentFg = ofPalette Palette.Semantic.Dark.accentFg
    let headingColor = ofPalette Palette.Accent.a300 // light emerald — on-brand markdown headings

    // functional status colors (mirror Mire.Widgets.Style)
    let okColor = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
    let warnColor = Color.Rgb(0xFFuy, 0xA0uy, 0x00uy)
    let errColor = Color.Rgb(0xFFuy, 0x57uy, 0x22uy)
    let infoColor = Color.Rgb(0x4Auy, 0x90uy, 0xD9uy)

    // base text styles
    let text = Style.Default.WithForeground(fg)
    let muted = Style.Default.WithForeground(fgMuted)
    let subtle = Style.Default.WithForeground(fgSubtle)
    let title = Style.Default.WithForeground(fg).WithBold(true)

    // markdown styles (lightly colorized)
    let heading1 = Style.Default.WithForeground(headingColor).WithBold(true)
    let heading2 = Style.Default.WithForeground(headingColor).WithBold(true)
    let heading3 = Style.Default.WithForeground(fgMuted).WithBold(true)
    let bold = text.WithBold(true)
    let italic = text.WithItalic(true)
    let strike = subtle.WithStrikethrough(true)

    let link =
        Style.Default.WithForeground(infoColor).WithUnderline(UnderlineStyle.Single)

    let mention = Style.Default.WithForeground(accent)
    // code + a light syntax-highlight palette (all share the elevated background)
    let code = Style.Default.WithForeground(fg).WithBackground(bgElevated)
    let codeKw = Style.Default.WithForeground(infoColor).WithBackground(bgElevated)
    let codeStr = Style.Default.WithForeground(okColor).WithBackground(bgElevated)
    let codeCom = Style.Default.WithForeground(fgSubtle).WithBackground(bgElevated)
    let codeNum = Style.Default.WithForeground(Color.Cyan).WithBackground(bgElevated)

    // chrome
    let borderStyle = Style.Default.WithForeground(borderColor)
    let borderFocus = Style.Default.WithForeground(accent)
    let accentStyle = Style.Default.WithForeground(accent)

    // status styles
    let okStyle = Style.Default.WithForeground(okColor)
    let warnStyle = Style.Default.WithForeground(warnColor)
    let errStyle = Style.Default.WithForeground(errColor)
    let infoStyle = Style.Default.WithForeground(infoColor)

    // selection = inverse video (brand-approved); the strong variant is the accent moment
    let selection =
        Style.Default.WithForeground(ofPalette Palette.Semantic.Dark.bg).WithBackground(fg)

    let selAccent = Style.Default.WithForeground(accentFg).WithBackground(accent)

    /// Functional tone for toasts / notices / table cells.
    type Tone =
        | Success
        | Warning
        | Danger
        | Info
        | Neutral

    let toneStyle (t: Tone) : Style =
        match t with
        | Success -> okStyle
        | Warning -> warnStyle
        | Danger -> errStyle
        | Info -> infoStyle
        | Neutral -> muted

    /// On-brand style set for the framework's `Markdown` widget (the agent flavor —
    /// `@mentions` enabled).
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
