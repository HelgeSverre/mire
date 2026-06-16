namespace Mire.Widgets

open System.Text
open Mire.Core
open Mire.Layout

/// The styles a `Markdown.render` uses. Inline bold/italic/strike are derived from
/// the base text style (`.WithBold`/…), so only the span/block colors that differ
/// are named here. `Mention` is optional — `Some` enables the `@token` rule (handy
/// for agent transcripts), `None` leaves `@` as plain text.
type MarkdownStyle =
    { Text: Style
      Heading1: Style
      Heading2: Style
      Heading3: Style
      Quote: Style
      Link: Style
      Mention: Style option
      Rule: Style
      Code: Style
      CodeKeyword: Style
      CodeString: Style
      CodeComment: Style
      CodeNumber: Style }

/// A deliberately-minimal, line-oriented markdown renderer (NOT CommonMark). It
/// produces a `vstack` of styled line nodes, word-wrapped to a caller-supplied
/// width (the layout engine doesn't word-wrap). Handles ATX headings, `> ` quotes,
/// `-`/`*`/ordered bullets, `---` rules, fenced code (light, generic syntax
/// highlighting), and inline `code`/`**bold**`/`*italic*`/`~~strike~~`/`[links]`.
module Markdown =

    /// A neutral default style set built on the framework's predefined `Style`s.
    let defaultStyle: MarkdownStyle =
        let codeBg = Color.Rgb(0x2Auy, 0x2Auy, 0x2Auy)

        { Text = Style.text
          Heading1 = Style.title
          Heading2 = Style.title
          Heading3 = Style.text.WithBold(true)
          Quote = Style.dim
          Link = Style.info.WithUnderline(UnderlineStyle.Single)
          Mention = None
          Rule = Style.border
          Code = Style.Default.WithForeground(Color.White).WithBackground(codeBg)
          CodeKeyword = Style.info.WithBackground(codeBg)
          CodeString = Style.success.WithBackground(codeBg)
          CodeComment = Style.dim.WithBackground(codeBg)
          CodeNumber = Style.Default.WithForeground(Color.Cyan).WithBackground(codeBg) }

    // ── inline emphasis ────────────────────────────────────────────────────────
    // Walk a line, emitting (char, style) pairs. Markers toggle a style until closed:
    //   `code`  **bold**  *italic*  ~~strike~~  [text](url) → linked text  @mention
    let private styledChars (s: MarkdownStyle) (baseStyle: Style) (text: string) : (char * Style) list =
        let res = ResizeArray<char * Style>()
        let n = text.Length

        let emit (a: int) (b: int) (st: Style) =
            for j in a .. b - 1 do
                res.Add(text.[j], st)

        let find (sub: string) (from: int) =
            let idx = text.IndexOf(sub, from)
            if idx < 0 then n else idx

        let mutable i = 0

        while i < n do
            let c = text.[i]
            let two = if i + 1 < n then text.Substring(i, 2) else ""

            if two = "**" then
                let close = find "**" (i + 2)
                emit (i + 2) close (baseStyle.WithBold(true))
                i <- (if close = n then n else close + 2)
            elif two = "~~" then
                let close = find "~~" (i + 2)
                emit (i + 2) close (baseStyle.WithStrikethrough(true))
                i <- (if close = n then n else close + 2)
            elif c = '`' then
                let close = find "`" (i + 1)
                emit (i + 1) close s.Code
                i <- (if close = n then n else close + 1)
            elif c = '*' then
                let close = find "*" (i + 1)
                emit (i + 1) close (baseStyle.WithItalic(true))
                i <- (if close = n then n else close + 1)
            elif c = '[' then
                let closeB = text.IndexOf(']', i)

                if closeB > 0 && closeB + 1 < n && text.[closeB + 1] = '(' then
                    let closeP = text.IndexOf(')', closeB)
                    let endP = if closeP < 0 then n else closeP
                    // Carry the real URL as an OSC 8 link on the styled link text
                    // (the `(url)` part itself is not emitted).
                    let urlStart = closeB + 2

                    let linkStyle =
                        if closeP > urlStart then
                            s.Link.WithLink(text.Substring(urlStart, closeP - urlStart))
                        else
                            s.Link

                    emit (i + 1) closeB linkStyle
                    i <- (if closeP < 0 then n else endP + 1)
                else
                    res.Add(c, baseStyle)
                    i <- i + 1
            elif c = '@' && s.Mention.IsSome && (i = 0 || text.[i - 1] = ' ') then
                // @file mention — style the token up to the next space
                let mutable j = i + 1

                while j < n && text.[j] <> ' ' do
                    j <- j + 1

                emit i j s.Mention.Value
                i <- j
            else
                res.Add(c, baseStyle)
                i <- i + 1

        List.ofSeq res

    // group consecutive (char,style) into pieces of (text, style, isSpace)
    let private toPieces (chars: (char * Style) list) : (string * Style * bool) list =
        let res = ResizeArray<string * Style * bool>()
        let sb = StringBuilder()
        let mutable curStyle = Style.Default
        let mutable curSpace = false
        let mutable has = false

        let flush () =
            if has then
                res.Add(sb.ToString(), curStyle, curSpace)
                sb.Clear() |> ignore
                has <- false

        for (c, st) in chars do
            let sp = (c = ' ')

            if has && st = curStyle && sp = curSpace then
                sb.Append(c) |> ignore
            else
                flush ()
                curStyle <- st
                curSpace <- sp
                sb.Append(c) |> ignore
                has <- true

        flush ()
        List.ofSeq res

    // greedily pack pieces into lines no wider than `width`
    let private packLines (width: int) (pieces: (string * Style * bool) list) : (string * Style) list list =
        let lines = ResizeArray<ResizeArray<string * Style>>()
        let cur = ResizeArray<string * Style>()
        let mutable curW = 0

        let push () =
            lines.Add(ResizeArray(cur))
            cur.Clear()
            curW <- 0

        for (t, st, isSpace) in pieces do
            let w = Grapheme.stringWidth t

            if isSpace then
                if cur.Count = 0 then
                    () // drop leading space
                elif curW + w <= width then
                    cur.Add(t, st)
                    curW <- curW + w
                else
                    push () // wrap swallows the space
            else if curW + w <= width || cur.Count = 0 then
                cur.Add(t, st)
                curW <- curW + w
            else
                push ()
                cur.Add(t, st)
                curW <- w

        if cur.Count > 0 then
            push ()

        if lines.Count = 0 then
            lines.Add(ResizeArray())

        lines |> Seq.map List.ofSeq |> List.ofSeq

    // merge same-style runs, then make one Text (or an hstack of segments) per line
    let private lineNode (emptyStyle: Style) (segs: (string * Style) list) : LayoutNode<'msg> =
        let merged = ResizeArray<string * Style>()

        for (t, st) in segs do
            if merged.Count > 0 then
                let (pt, ps) = merged.[merged.Count - 1]

                if ps = st then
                    merged.[merged.Count - 1] <- (pt + t, ps)
                else
                    merged.Add(t, st)
            else
                merged.Add(t, st)

        match List.ofSeq merged with
        | [] -> Text.text " " emptyStyle
        | [ (t, st) ] -> Text.text t st
        | many -> Stack.hstackOf (many |> List.map (fun (t, st) -> Stack.sized Length.Content (Text.text t st)))

    /// Wrap a single run of inline-markdown text to `width`, one node per line.
    let wrapLines (s: MarkdownStyle) (width: int) (baseStyle: Style) (text: string) : LayoutNode<'msg> list =
        styledChars s baseStyle text
        |> toPieces
        |> packLines (max 1 width)
        |> List.map (lineNode baseStyle)

    /// Wrap inline text into a single vstack node.
    let wrap (s: MarkdownStyle) (width: int) (baseStyle: Style) (text: string) : LayoutNode<'msg> =
        Stack.vstack (wrapLines s width baseStyle text)

    // ── light syntax highlighting for fenced code ───────────────────────────────
    let private keywords =
        set
            [ "let"
              "fun"
              "match"
              "with"
              "type"
              "module"
              "namespace"
              "open"
              "member"
              "if"
              "then"
              "else"
              "elif"
              "for"
              "in"
              "do"
              "while"
              "rec"
              "mutable"
              "function"
              "return"
              "const"
              "var"
              "new"
              "of"
              "and"
              "yield"
              "use"
              "true"
              "false"
              "null"
              "private"
              "public"
              "static"
              "import"
              "from"
              "def"
              "class" ]

    /// Tokenize a code line into colored segments (strings, // comments, numbers,
    /// keywords) and lay them horizontally. Deliberately generic — not per-language.
    let private highlightCodeLine (s: MarkdownStyle) (raw: string) : LayoutNode<'msg> =
        let segs = ResizeArray<string * Style>()
        segs.Add("  ", s.Code) // indent gutter
        let n = raw.Length

        let isIdent c =
            System.Char.IsLetterOrDigit c || c = '_' || c = '\''

        let mutable i = 0

        while i < n do
            let c = raw.[i]

            if c = '"' then
                let start = i
                i <- i + 1

                while i < n && raw.[i] <> '"' do
                    i <- i + 1

                if i < n then
                    i <- i + 1

                segs.Add(raw.Substring(start, i - start), s.CodeString)
            elif i + 1 < n && c = '/' && raw.[i + 1] = '/' then
                segs.Add(raw.Substring(i), s.CodeComment)
                i <- n
            elif System.Char.IsDigit c then
                let start = i

                while i < n && (System.Char.IsDigit raw.[i] || raw.[i] = '.') do
                    i <- i + 1

                segs.Add(raw.Substring(start, i - start), s.CodeNumber)
            elif System.Char.IsLetter c || c = '_' then
                let start = i

                while i < n && isIdent raw.[i] do
                    i <- i + 1

                let word = raw.Substring(start, i - start)
                segs.Add(word, (if keywords.Contains word then s.CodeKeyword else s.Code))
            else
                segs.Add(string c, s.Code)
                i <- i + 1
        // merge adjacent same-style runs to keep the hstack small
        let merged = ResizeArray<string * Style>()

        for (t, st) in segs do
            if merged.Count > 0 && snd merged.[merged.Count - 1] = st then
                let (pt, _) = merged.[merged.Count - 1]
                merged.[merged.Count - 1] <- (pt + t, st)
            else
                merged.Add(t, st)

        Stack.hstackOf (
            merged
            |> Seq.map (fun (t, st) -> Stack.sized Length.Content (Text.text t st))
            |> List.ofSeq
        )

    let private isOrdered (line: string) : bool =
        let t = line.TrimStart()
        let mutable i = 0

        while i < t.Length && System.Char.IsDigit t.[i] do
            i <- i + 1

        i > 0 && i + 1 < t.Length && t.[i] = '.' && t.[i + 1] = ' '

    /// Render a markdown document to a vstack of styled lines, wrapped to `width`.
    let render (s: MarkdownStyle) (width: int) (src: string) : LayoutNode<'msg> =
        let lines = src.Replace("\r\n", "\n").Split('\n')
        let nodes = ResizeArray<LayoutNode<'msg>>()

        let addWrapped (style: Style) (text: string) =
            for n in wrapLines s width style text do
                nodes.Add(n)

        let mutable i = 0

        while i < lines.Length do
            let line = lines.[i]

            if line.StartsWith("```") then
                i <- i + 1

                while i < lines.Length && not (lines.[i].StartsWith("```")) do
                    nodes.Add(highlightCodeLine s lines.[i])
                    i <- i + 1

                i <- i + 1 // skip closing fence (or past end)
            else
                if line.StartsWith("### ") then
                    nodes.Add(Text.text (line.Substring 4) s.Heading3)
                elif line.StartsWith("## ") then
                    nodes.Add(Text.text (line.Substring 3) s.Heading2)
                elif line.StartsWith("# ") then
                    nodes.Add(Text.text (line.Substring 2) s.Heading1)
                elif line.StartsWith("> ") then
                    addWrapped s.Quote ("│ " + line.Substring 2)
                elif line.StartsWith("- ") || line.StartsWith("* ") then
                    addWrapped s.Text ("• " + line.Substring 2)
                elif isOrdered line then
                    addWrapped s.Text line
                elif line.StartsWith("---") then
                    nodes.Add(Text.text (System.String('─', max 1 width)) s.Rule)
                elif line.Trim() = "" then
                    nodes.Add(Text.text " " s.Text)
                else
                    addWrapped s.Text line

                i <- i + 1

        Stack.vstack (List.ofSeq nodes)
