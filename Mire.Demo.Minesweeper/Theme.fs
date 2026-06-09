namespace Mire.Demo.Minesweeper

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
    let borderColor = ofPalette Palette.Semantic.Dark.borderStrong
    let accent = ofPalette Palette.Semantic.Dark.accent
    let accentFg = ofPalette Palette.Semantic.Dark.accentFg

    let okColor = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
    let warnColor = Color.Rgb(0xFFuy, 0xCAuy, 0x28uy)
    let errColor = Color.Rgb(0xFFuy, 0x57uy, 0x22uy)

    let theme: AppTheme =
        { fg = Style.Default.WithForeground(fg)
          fgMuted = Style.Default.WithForeground(fgMuted)
          fgSubtle = Style.Default.WithForeground(fgSubtle)
          title = Style.Default.WithForeground(fg).WithBold(true)
          bg = Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.bg)
          bgElevated = Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.bgElevated)
          border = Style.Default.WithForeground(borderColor)
          borderFocus = Style.Default.WithForeground(accent)
          divider = Style.Default.WithBackground(ofPalette Palette.Semantic.Dark.border)
          accent = Style.Default.WithForeground(accent)
          accentFg = Style.Default.WithForeground(accentFg)
          accentStrong =
              Style.Default.WithForeground(Color.White).WithBackground(ofPalette Palette.Accent.a700)
          success = Style.Default.WithForeground(okColor)
          warning = Style.Default.WithForeground(warnColor)
          danger = Style.Default.WithForeground(errColor)
          info = Style.Default.WithForeground(Color.Rgb(0x4Auy, 0x90uy, 0xD9uy))
          selection =
              Style.Default
                  .WithForeground(ofPalette Palette.Semantic.Dark.bg)
                  .WithBackground(fg)
          selectionAccent = Style.Default.WithForeground(accentFg).WithBackground(accent)
          key = Style.Default.WithForeground(fgMuted).WithBold(true)
          markdown = Markdown.defaultStyle }

    // Minesweeper-specific styles (number colors are classic, border/frame/status
    // are brand-token-derived)
    let frame =
        Style.Default.WithForeground(borderColor)

    let hidden =
        Style.Default.WithForeground(ofPalette Palette.Neutrals.n600)

    let flag =
        Style.Default.WithForeground(warnColor).WithBold(true)

    let mine =
        Style.Default.WithForeground(errColor).WithBold(true)

    let zero =
        Style.Default.WithForeground(ofPalette Palette.Neutrals.n700)

    let status =
        Style.Default.WithForeground(fg)

    let hint =
        Style.Default.WithForeground(fgMuted)

    let won =
        Style.Default.WithForeground(okColor).WithBold(true)

    let lost =
        Style.Default.WithForeground(errColor).WithBold(true)

    let cursor =
        Style.Default
            .WithForeground(ofPalette Palette.Semantic.Dark.bg)
            .WithBackground(ofPalette Palette.Semantic.Dark.fg)

    // Classic Minesweeper number colors
    let num1 =
        Style.Default.WithForeground(Color.Rgb(0x42uy, 0xA5uy, 0xF5uy))

    let num2 =
        Style.Default.WithForeground(Color.Rgb(0x66uy, 0xBBuy, 0x6Auy))

    let num3 =
        Style.Default.WithForeground(Color.Rgb(0xEFuy, 0x53uy, 0x50uy))

    let num4 =
        Style.Default.WithForeground(Color.Rgb(0x3Fuy, 0x51uy, 0xB5uy))

    let num5 =
        Style.Default.WithForeground(Color.Rgb(0xB7uy, 0x1Cuy, 0x1Cuy))

    let num6 =
        Style.Default.WithForeground(Color.Rgb(0x26uy, 0xC6uy, 0xDAuy))

    let num7 =
        Style.Default.WithForeground(Color.Rgb(0xECuy, 0xEFuy, 0xF1uy))

    let num8 =
        Style.Default.WithForeground(Color.Rgb(0x90uy, 0x90uy, 0x90uy))

    let numberStyle =
        function
        | 1 -> num1
        | 2 -> num2
        | 3 -> num3
        | 4 -> num4
        | 5 -> num5
        | 6 -> num6
        | 7 -> num7
        | 8 -> num8
        | _ -> zero
