namespace Mire.Core

open System
open System.Globalization

/// Display-width measurement. The terminal cell grid is monospace, so what matters
/// is how many *columns* a piece of text occupies. We work at two levels:
///
/// * **scalar** (a Unicode code point) — zero-width (combining marks, joiners,
///   format controls), wide (East-Asian / emoji → 2 columns), or normal (1).
/// * **grapheme cluster** (a user-perceived character, UAX #29) — emoji ZWJ
///   sequences, regional-indicator flags, and base+combining-mark runs each occupy
///   a single cell. `clusters` segments a string into these; `clusterWidth` gives a
///   cluster's column count. This is what the renderer iterates so astral glyphs
///   (surrogate pairs) and emoji clusters land in one cell instead of being split.
///
/// The East-Asian wide ranges are an approximation of `EastAsianWidth.txt` (the BCL
/// has no width table); zero-width detection uses Unicode general categories, which
/// is exact.
module Grapheme =

    /// True for code points that take **no** columns: combining marks (Mn/Me),
    /// format controls (Cf — ZWJ/ZWNJ/ZWSP, bidi, soft hyphen, variation selectors).
    let isZeroWidthScalar (cp: int) : bool =
        if cp = 0x200B || cp = 0x200C || cp = 0x200D then
            true
        else
            match CharUnicodeInfo.GetUnicodeCategory(cp) with
            | UnicodeCategory.NonSpacingMark
            | UnicodeCategory.EnclosingMark
            | UnicodeCategory.Format -> true
            | _ -> false

    /// True for code points that take **two** columns: East-Asian wide/fullwidth
    /// blocks and the emoji/pictograph blocks (incl. the astral planes).
    let isWideScalar (cp: int) : bool =
        (cp >= 0x1100 && cp <= 0x115F) // Hangul Jamo
        || cp = 0x2329
        || cp = 0x232A // angle brackets
        || (cp >= 0x2E80 && cp <= 0x303E) // CJK radicals … CJK symbols & punctuation (incl. U+3000)
        || (cp >= 0x3041 && cp <= 0x33FF) // kana, enclosed CJK, CJK compatibility
        || (cp >= 0x3400 && cp <= 0x4DBF) // CJK Ext A
        || (cp >= 0x4E00 && cp <= 0x9FFF) // CJK Unified Ideographs
        || (cp >= 0xA000 && cp <= 0xA4CF) // Yi
        || (cp >= 0xAC00 && cp <= 0xD7A3) // Hangul syllables
        || (cp >= 0xF900 && cp <= 0xFAFF) // CJK compatibility ideographs
        || (cp >= 0xFE10 && cp <= 0xFE19) // vertical forms
        || (cp >= 0xFE30 && cp <= 0xFE6F) // CJK compatibility / small form variants
        || (cp >= 0xFF00 && cp <= 0xFF60) // fullwidth forms
        || (cp >= 0xFFE0 && cp <= 0xFFE6) // fullwidth signs
        || (cp >= 0x1F000 && cp <= 0x1F02F) // mahjong tiles
        || (cp >= 0x1F0A0 && cp <= 0x1F0FF) // playing cards
        || (cp >= 0x1F300 && cp <= 0x1F64F) // misc pictographs, emoticons
        || (cp >= 0x1F680 && cp <= 0x1F6FF) // transport & map symbols
        || (cp >= 0x1F900 && cp <= 0x1F9FF) // supplemental symbols & pictographs
        || (cp >= 0x1FA70 && cp <= 0x1FAFF) // symbols & pictographs extended-A
        || (cp >= 0x20000 && cp <= 0x3FFFD) // astral CJK (Ext B and beyond)

    /// Column count of a single scalar (0, 1, or 2).
    let scalarWidth (cp: int) : int =
        if isZeroWidthScalar cp then 0
        elif isWideScalar cp then 2
        else 1

    // --- per-char (BMP) helpers, kept for back-compat -----------------------
    // These see one UTF-16 code unit; a surrogate half is neither wide nor
    // zero-width here (width 1) — measure astral text through `clusterWidth`/
    // `stringWidth`, which decode surrogate pairs.

    let isZeroWidth (c: char) : bool = isZeroWidthScalar (int c)
    let isWide (c: char) : bool = isWideScalar (int c)
    let charWidth (c: char) : int = scalarWidth (int c)

    /// The code points of a string, decoding surrogate pairs into astral scalars.
    let private codepointsOf (s: string) : int list =
        let mutable i = 0

        [ while i < s.Length do
              if Char.IsHighSurrogate s.[i] && i + 1 < s.Length && Char.IsLowSurrogate s.[i + 1] then
                  yield Char.ConvertToUtf32(s.[i], s.[i + 1])
                  i <- i + 2
              else
                  yield int s.[i]
                  i <- i + 1 ]

    /// Segment a string into extended grapheme clusters (UAX #29). Emoji ZWJ
    /// sequences, regional-indicator flags, and base+combining-mark runs each come
    /// out as one element — so the renderer can place each in a single cell.
    let clusters (s: string) : string list =
        if String.IsNullOrEmpty s then
            []
        else
            let e = StringInfo.GetTextElementEnumerator s

            [ while e.MoveNext() do
                  yield e.GetTextElement() ]

    /// Column count of one grapheme cluster. A cluster carrying a ZWJ, an emoji
    /// variation selector (U+FE0F), a regional indicator, or any wide scalar is a
    /// single wide cell (2); a text-presentation selector (U+FE0E) forces 1;
    /// otherwise it's the width of the base scalar (combining marks add nothing).
    let clusterWidth (cluster: string) : int =
        if cluster = "" then
            0
        else
            let cps = codepointsOf cluster
            let first = List.head cps
            let has cp = List.contains cp cps
            let anyWide = cps |> List.exists isWideScalar
            let regional = cps |> List.exists (fun cp -> cp >= 0x1F1E6 && cp <= 0x1F1FF)

            // A wide scalar (CJK, astral emoji), a ZWJ sequence, an emoji variation
            // selector, or a regional indicator forces a wide cell — checked before
            // the VS15 downgrade so it can't narrow an inherently-wide glyph (e.g. a
            // CJK ideograph with a text-presentation selector stays width 2).
            if has 0x200D || has 0xFE0F || regional || anyWide then 2
            elif has 0xFE0E then 1
            else scalarWidth first

    // Bounded memo for the *non-ASCII* width path (UAX #29 segmentation is the
    // expensive part; ASCII keeps its allocation-free fast path and never caches).
    // Width is a pure function of the string, so the cache can't go stale. Bounded
    // so a transcript of unique non-ASCII lines can't grow it without limit.
    let private widthCache =
        System.Collections.Concurrent.ConcurrentDictionary<string, int>()

    let private widthCacheCap = 8192

    /// Test/diagnostic hook: how many distinct non-ASCII strings are memoized.
    let widthCacheSize () = widthCache.Count

    /// Test hook: is `s` in the width cache? (Race-free under parallel tests, unlike
    /// observing `widthCacheSize` deltas against a shared global cache.)
    let widthCacheContains (s: string) = widthCache.ContainsKey s

    /// Total display width of a string, measured by grapheme cluster. Falls back to
    /// the length for pure-ASCII text (the common case), which avoids segmentation;
    /// non-ASCII widths are memoized (bounded) since segmentation is the hot cost.
    let stringWidth (s: string) : int =
        let mutable ascii = true

        for c in s do
            if int c >= 0x80 then
                ascii <- false

        if ascii then
            s.Length
        else
            match widthCache.TryGetValue s with
            | true, w -> w
            | _ ->
                let w = clusters s |> List.sumBy clusterWidth
                // Stop inserting past the cap (a simple bound; correctness is
                // unaffected — uncached strings just recompute).
                if widthCache.Count < widthCacheCap then
                    widthCache.[s] <- w

                w

    // --- control-character safety -------------------------------------------
    // Cell text is content, not terminal instructions. A C0 control (ESC, BEL,
    // CR, …), DEL, or a C1 control (0x80–0x9F, e.g. CSI 0x9B / OSC 0x9D) reaching
    // the output stream would be *executed* by the terminal — `"a\x1b[2Jb"` would
    // clear the screen instead of showing inert text. The renderer maps every such
    // scalar to a visible, inert stand-in before it lands in a cell (and the diff
    // writer re-checks at emission). Raw instructions go through the explicit,
    // trusted `Cmd.writeRaw` path instead.

    /// True for a C0 control (< 0x20), DEL (0x7F), or C1 control (0x80–0x9F).
    let isControlScalar (cp: int) : bool = cp < 0x20 || (cp >= 0x7F && cp <= 0x9F)

    /// True if `s` contains any control scalar (see `isControlScalar`).
    let hasControl (s: string) : bool =
        let mutable found = false
        let mutable i = 0

        while not found && i < s.Length do
            if isControlScalar (int s.[i]) then
                found <- true

            i <- i + 1

        found

    /// The visible stand-in for a control scalar: C0 → its Unicode Control
    /// Picture (U+2400 + cp, e.g. ESC → `␛`), DEL → `␡`, C1 → U+FFFD. All are a
    /// single narrow column.
    let controlPicture (cp: int) : string =
        if cp < 0x20 then string (char (0x2400 + cp))
        elif cp = 0x7F then "␡"
        else "�"

    /// Replace every control scalar in `s` with its visible stand-in
    /// (`controlPicture`). Returns `s` unchanged (no allocation) when it has none.
    let sanitize (s: string) : string =
        if not (hasControl s) then
            s
        else
            let sb = System.Text.StringBuilder(s.Length)

            for c in s do
                if isControlScalar (int c) then
                    sb.Append(controlPicture (int c)) |> ignore
                else
                    sb.Append(c) |> ignore

            sb.ToString()

    // --- boundaries & columns (for editing) ---------------------------------
    // Text buffers index by UTF-16 offset; these helpers keep those offsets on
    // grapheme-cluster boundaries (never inside a surrogate pair, a base+mark
    // run, or a ZWJ sequence) and convert between offsets and display columns.

    /// The offset just past the cluster starting at `pos` (`s.Length` at the end).
    let nextBoundary (s: string) (pos: int) : int =
        if pos >= s.Length then
            s.Length
        elif pos < 0 then
            0
        else
            pos + max 1 (StringInfo.GetNextTextElementLength(s, pos))

    /// The largest cluster boundary ≤ `pos`: `pos` itself if it's a boundary,
    /// else the start of the cluster containing it. Scans from the start of
    /// `pos`'s line (a line feed always ends a cluster), so cost is per line.
    let floorBoundary (s: string) (pos: int) : int =
        let pos = max 0 (min s.Length pos)

        if pos = 0 || pos = s.Length then
            pos
        else
            let mutable i = s.LastIndexOf('\n', pos - 1) + 1

            while i < pos && nextBoundary s i <= pos do
                i <- nextBoundary s i

            i

    /// The cluster boundary before `pos` (0 at the start).
    let prevBoundary (s: string) (pos: int) : int =
        if pos <= 0 then 0 else floorBoundary s (pos - 1)

    /// True if `pos` is a cluster boundary of `s`.
    let isBoundary (s: string) (pos: int) : bool =
        pos >= 0 && pos <= s.Length && floorBoundary s pos = pos

    /// How an editor displays one cluster: the text to paint and its columns. A
    /// tab shows as a single blank column, a control character as its visible
    /// stand-in, and a zero-width cluster with no base (a lone combining mark) on
    /// a dotted circle — so every cluster occupies at least one column and a
    /// caret can always sit on it.
    let editDisplay (cluster: string) : string * int =
        if cluster = "\t" then
            " ", 1
        else
            let shown = sanitize cluster
            let w = clusterWidth shown

            if w = 0 then "◌" + shown, 1 else shown, w

    /// Display columns of `s.[lo .. hi - 1]` as an editor shows it (`editDisplay`).
    let editColumns (s: string) (lo: int) (hi: int) : int =
        let hi = min s.Length hi
        let mutable i = max 0 lo
        let mutable cols = 0

        while i < hi do
            let j = nextBoundary s i
            cols <- cols + snd (editDisplay (s.Substring(i, j - i)))
            i <- j

        cols

    /// The offset in `s.[lo .. hi - 1]` whose display column (from `lo`) is the
    /// largest ≤ `col` — where a caret moved to column `col` of that line lands.
    let offsetAtColumn (s: string) (lo: int) (hi: int) (col: int) : int =
        let hi = min s.Length hi
        let mutable i = max 0 lo
        let mutable cols = 0
        let mutable go = true

        while go && i < hi do
            let j = nextBoundary s i
            let w = snd (editDisplay (s.Substring(i, j - i)))

            if cols + w > col then
                go <- false
            else
                cols <- cols + w
                i <- j

        i
