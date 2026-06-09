namespace Mire.Widgets

open Mire.Core

/// A complete, swappable style set for a TUI app — one record to pass around
/// (or store) instead of dozens of loose `Style` values. `AppTheme.default`
/// mirrors the existing `Style.*` module so existing code is unaffected; apps
/// build their own instances from a brand palette (see `brand/palette.fs`).
///
/// Named `AppTheme` (not `Theme`) to avoid an F# ambiguity: the Agent demo
/// already has `module Theme` in `Mire.Demo.Agent`, and both namespaces are
/// opened side-by-side in its view code. A `type Theme` in `Mire.Widgets`
/// would break 127+ call sites; `AppTheme` is unambiguous.
type AppTheme =
    { fg: Style
      fgMuted: Style
      fgSubtle: Style
      title: Style
      bg: Style
      bgElevated: Style
      border: Style
      borderFocus: Style
      divider: Style
      accent: Style
      accentFg: Style
      accentStrong: Style
      success: Style
      warning: Style
      danger: Style
      info: Style
      selection: Style
      selectionAccent: Style
      key: Style
      markdown: MarkdownStyle }

module AppTheme =

    let defaultTheme: AppTheme =
        let codeBg = Color.Rgb(0x2Auy, 0x2Auy, 0x2Auy)

        { fg = Style.text
          fgMuted = Style.dim
          fgSubtle = Style.Default.WithForeground(Color.Rgb(0x66uy, 0x66uy, 0x66uy))
          title = Style.title
          bg = Style.bg
          bgElevated = Style.Default.WithBackground(Color.Rgb(0x22uy, 0x22uy, 0x33uy))
          border = Style.border
          borderFocus = Style.highlight
          divider = Style.Default.WithBackground(Color.Rgb(0x44uy, 0x44uy, 0x44uy))
          accent = Style.Default.WithForeground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))
          accentFg = Style.Default.WithForeground(Color.White)
          accentStrong = Style.Default.WithForeground(Color.White).WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))
          success = Style.success
          warning = Style.warning
          danger = Style.danger
          info = Style.info
          selection =
              Style.Default
                  .WithForeground(Color.White)
                  .WithBackground(Color.Rgb(0x4Auy, 0x90uy, 0xD9uy))
          selectionAccent =
              Style.Default
                  .WithForeground(Color.White)
                  .WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))
          key = Style.key
          markdown = Markdown.defaultStyle }
