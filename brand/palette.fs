// Mire — brand palette (F#)
// Generated from brand/palette.json. Do not edit by hand — regenerate.
//
// Standalone module — no dependency on Mire.Core, so it stays a pure brand
// asset. A TUI app picks the field its terminal supports:
//
//   open Mire.Brand.Palette
//   let fg =
//       if trueColor then Truecolor Semantic.Dark.fg.Hex
//       else Ansi256 Semantic.Dark.fg.Ansi256
//
// Color discipline (see voice.md / USAGE.md): the accent appears at most once
// per screen. Neutrals carry hierarchy; the accent is the single moment.

namespace Mire.Brand

module Palette =

    type Color =
        { Hex: string
          Rgb: byte * byte * byte
          Ansi256: byte }

    let private c hex (r: byte) (g: byte) (b: byte) (ansi: byte) =
        { Hex = hex
          Rgb = (r, g, b)
          Ansi256 = ansi }

    module Neutrals =
        let n50 = c "#FAFAFA" 250uy 250uy 250uy 231uy
        let n100 = c "#F3F3F3" 243uy 243uy 243uy 255uy
        let n200 = c "#E6E6E6" 230uy 230uy 230uy 254uy
        let n300 = c "#D4D4D4" 212uy 212uy 212uy 188uy
        let n400 = c "#AFAFAF" 175uy 175uy 175uy 145uy
        let n500 = c "#868686" 134uy 134uy 134uy 102uy
        let n600 = c "#606060" 96uy 96uy 96uy 59uy
        let n700 = c "#464646" 70uy 70uy 70uy 238uy
        let n800 = c "#292929" 41uy 41uy 41uy 235uy
        let n900 = c "#121212" 18uy 18uy 18uy 233uy
        let n950 = c "#050505" 5uy 5uy 5uy 232uy

    module Accent =
        // Emerald. a500 is the brand color; a700 is the filled-CTA shade.
        // (Matches the refreshed web palette in palette.css / palette.json.)
        let a100 = c "#D0F5E8" 208uy 245uy 232uy 158uy
        let a300 = c "#7DD4B8" 125uy 212uy 184uy 79uy
        let a500 = c "#1A9A7E" 26uy 154uy 126uy 29uy
        let a700 = c "#006F56" 0uy 111uy 86uy 23uy
        let a900 = c "#003D2E" 0uy 61uy 46uy 236uy

    module Semantic =
        module Light =
            let bg = Neutrals.n50
            let bgElevated = Neutrals.n100
            let fg = Neutrals.n900
            let fgMuted = Neutrals.n600
            let fgSubtle = Neutrals.n500
            let border = Neutrals.n200
            let borderStrong = Neutrals.n300
            let accent = Accent.a500
            let accentStrong = Accent.a700
            let accentFg = Neutrals.n950 // dark text on emerald (white fails AA)
            let accentBg = Accent.a100

        module Dark =
            let bg = Neutrals.n950
            let bgElevated = Neutrals.n900
            let fg = Neutrals.n50
            let fgMuted = Neutrals.n400
            let fgSubtle = Neutrals.n500
            let border = Neutrals.n800
            let borderStrong = Neutrals.n700
            let accent = Accent.a500
            let accentStrong = Accent.a700
            let accentFg = Neutrals.n950 // dark text on emerald (white fails AA)
            let accentBg = Accent.a900
