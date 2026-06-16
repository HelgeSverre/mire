namespace Mire.Widgets

open Mire.Core
open Mire.Brand

/// A complete, swappable style set for a TUI app — one record to pass around
/// (or store) instead of dozens of loose `Style` values. `AppTheme.defaultTheme`
/// is the Mire brand (emerald accent, neutral hierarchy, inverse-video selection),
/// built from `Mire.Brand.Palette`; apps swap individual fields or the whole set.
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

    /// Functional tone → style, for toasts / notices / status cells. The demos
    /// hand-rolled this; it lives here now so the mapping is shared.
    type Tone =
        | Success
        | Warning
        | Danger
        | Info
        | Neutral

    /// The Mire brand theme (dark): neutrals carry hierarchy, emerald is the one
    /// accent moment, selection is inverse video, status colors are functional.
    let defaultTheme: AppTheme =
        let rgb (c: Palette.Color) =
            let (r, g, b) = c.Rgb
            Color.Rgb(r, g, b)

        let accent = rgb Palette.Semantic.Dark.accent
        let accentFg = rgb Palette.Semantic.Dark.accentFg

        { fg = Style.text
          fgMuted = Style.dim
          fgSubtle = Style.Default.WithForeground(rgb Palette.Semantic.Dark.fgSubtle)
          title = Style.title
          bg = Style.bg
          bgElevated = Style.Default.WithBackground(rgb Palette.Semantic.Dark.bgElevated)
          border = Style.border
          borderFocus = Style.Default.WithForeground(accent)
          divider = Style.Default.WithForeground(rgb Palette.Semantic.Dark.border)
          accent = Style.Default.WithForeground(accent)
          accentFg = Style.Default.WithForeground(accentFg)
          accentStrong = Style.Default.WithForeground(accentFg).WithBackground(accent)
          success = Style.success
          warning = Style.warning
          danger = Style.danger
          info = Style.info
          // Selection is inverse video (brand-approved): swap fg/bg.
          selection =
            Style.Default.WithForeground(rgb Palette.Semantic.Dark.bg).WithBackground(rgb Palette.Semantic.Dark.fg)
          selectionAccent = Style.Default.WithForeground(accentFg).WithBackground(accent)
          key = Style.key
          markdown = Markdown.defaultStyle }

    /// Map a functional `Tone` to the theme's corresponding style.
    let toneStyle (theme: AppTheme) (tone: Tone) : Style =
        match tone with
        | Success -> theme.success
        | Warning -> theme.warning
        | Danger -> theme.danger
        | Info -> theme.info
        | Neutral -> theme.fgMuted
