namespace Mire.Demo.List

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
    let warnColor = Color.Rgb(0xFFuy, 0xA0uy, 0x00uy)
    let errColor = Color.Rgb(0xFFuy, 0x57uy, 0x22uy)
    let infoColor = Color.Rgb(0x4Auy, 0x90uy, 0xD9uy)

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
          info = Style.Default.WithForeground(infoColor)
          selection =
              Style.Default
                  .WithForeground(ofPalette Palette.Semantic.Dark.bg)
                  .WithBackground(fg)
          selectionAccent = Style.Default.WithForeground(accentFg).WithBackground(accent)
          key = Style.Default.WithForeground(fgMuted).WithBold(true)
          markdown = Markdown.defaultStyle }
