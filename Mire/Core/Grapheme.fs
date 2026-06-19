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
    let private widthCache = System.Collections.Concurrent.ConcurrentDictionary<string, int>()
    let private widthCacheCap = 8192

    /// Test/diagnostic hook: how many distinct non-ASCII strings are memoized.
    let widthCacheSize () = widthCache.Count

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
