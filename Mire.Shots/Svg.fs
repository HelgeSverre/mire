namespace Mire.Shots

open System.Text
open Mire.Core
open Mire.Renderer
open Mire.Layout

/// Serialize a rendered `Surface` to a self-contained SVG "screenshot": real glyphs,
/// real palette colors, a window chrome bar. Cells are a fixed grid, so runs are
/// stretched to an exact `textLength` and stay aligned regardless of the web font's
/// advance width. Inline the result into a page so the page's JetBrains Mono applies.
module Svg =

    // cell metrics (px)
    let private cw = 8.6
    let private ch = 18.0
    let private fontSize = 13.6
    let private padX = 16.0
    let private padY = 14.0
    let private barH = 30.0

    // brand dark surface — the default theme the gallery renders with
    let private defaultBg = "#0d0d0d"
    let private defaultFg = "#fafafa"

    let private hex (c: Color) =
        match c with
        | Rgb(r, g, b) -> sprintf "#%02x%02x%02x" r g b
        | Default -> defaultFg

    let private esc (s: string) =
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

    // a run's visual identity, so consecutive same-style cells group into one <text>
    let private styleKey (st: Style) =
        let fg =
            match st.Foreground with
            | Some c -> hex c
            | None -> defaultFg

        let bg =
            match st.Background with
            | Some c -> Some(hex c)
            | None -> None

        (fg, bg, st.Bold, st.Dim, st.Italic, st.Underline.IsSome, st.Strikethrough)

    /// Render `surf` to an SVG string. `title` shows in the window bar.
    let ofSurface (title: string) (surf: Surface) : string =
        let w = surf.Size.Width
        let h = surf.Size.Height
        let gridLeft = padX
        let gridTop = barH + padY
        let svgW = float w * cw + 2.0 * padX
        let svgH = float h * ch + 2.0 * padY + barH
        let sb = StringBuilder()

        let ap (s: string) = sb.Append(s) |> ignore

        ap (
            sprintf
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 %.0f %.0f\" width=\"%.0f\" height=\"%.0f\" role=\"img\" aria-label=\"%s\" font-family=\"'JetBrains Mono', ui-monospace, monospace\">"
                svgW
                svgH
                svgW
                svgH
                (esc title)
        )

        // window chrome
        ap (
            sprintf
                "<rect x=\"0\" y=\"0\" width=\"%.0f\" height=\"%.0f\" rx=\"8\" fill=\"%s\" stroke=\"#292929\"/>"
                svgW
                svgH
                defaultBg
        )

        ap (sprintf "<line x1=\"0\" y1=\"%.0f\" x2=\"%.0f\" y2=\"%.0f\" stroke=\"#292929\"/>" barH svgW barH)
        ap "<circle cx=\"18\" cy=\"15\" r=\"4\" fill=\"#3a3a3a\"/>"
        ap "<circle cx=\"34\" cy=\"15\" r=\"4\" fill=\"#3a3a3a\"/>"
        ap "<circle cx=\"50\" cy=\"15\" r=\"4\" fill=\"#3a3a3a\"/>"

        if title <> "" then
            ap (
                sprintf
                    "<text x=\"%.0f\" y=\"19\" fill=\"#868686\" font-size=\"11.5\">%s</text>"
                    (svgW / 2.0 - float title.Length * 3.2)
                    (esc title)
            )

        // cell grid, grouped into style runs per row
        for y in 0 .. h - 1 do
            let mutable x = 0

            while x < w do
                let start = x
                let key = styleKey surf.[x, y].Style
                let buf = StringBuilder()

                while x < w && styleKey surf.[x, y].Style = key do
                    let cell = surf.[x, y]

                    if not cell.IsContinuation then
                        buf.Append(if cell.Grapheme = "" then " " else cell.Grapheme) |> ignore

                    x <- x + 1

                let cols = x - start
                let (fg, bg, bold, dim, italic, underline, strike) = key
                let rx = gridLeft + float start * cw
                let textLen = float cols * cw

                match bg with
                | Some bgHex ->
                    ap (
                        sprintf
                            "<rect x=\"%.2f\" y=\"%.2f\" width=\"%.2f\" height=\"%.2f\" fill=\"%s\"/>"
                            rx
                            (gridTop + float y * ch)
                            textLen
                            ch
                            bgHex
                    )
                | None -> ()

                let text = buf.ToString()

                if text.Trim() <> "" then
                    let weight = if bold then " font-weight=\"700\"" else ""
                    let style = if italic then " font-style=\"italic\"" else ""
                    let opacity = if dim then " opacity=\"0.62\"" else ""

                    let deco =
                        if underline && strike then
                            " text-decoration=\"underline line-through\""
                        elif underline then
                            " text-decoration=\"underline\""
                        elif strike then
                            " text-decoration=\"line-through\""
                        else
                            ""

                    ap (
                        sprintf
                            "<text x=\"%.2f\" y=\"%.2f\" fill=\"%s\" font-size=\"%.1f\" textLength=\"%.2f\" lengthAdjust=\"spacingAndGlyphs\" xml:space=\"preserve\"%s%s%s%s>%s</text>"
                            rx
                            (gridTop + float y * ch + fontSize * 0.78)
                            fg
                            fontSize
                            textLen
                            weight
                            style
                            opacity
                            deco
                            (esc text)
                    )

        ap "</svg>"
        sb.ToString()

    /// Lay a node out at `(w, h)` through the real measure/render path, then export.
    let ofNode (title: string) (w: int) (h: int) (node: LayoutNode<'msg>) : string =
        let surf = Surface(Size.Create(w, h))
        Layout.render surf (Layout.measure (Rect.FromOrigin(Size.Create(w, h))) node)
        ofSurface title surf
