namespace Mire.Core

module Grapheme =
    
    let isWide (c: char) : bool =
        let code = int c
        // CJK Unified Ideographs Extension A
        (code >= 0x3400 && code <= 0x4DBF) ||
        // CJK Unified Ideographs
        (code >= 0x4E00 && code <= 0x9FFF) ||
        // Hangul Syllables
        (code >= 0xAC00 && code <= 0xD7AF) ||
        // CJK Unified Ideographs Extension B
        (code >= 0x20000 && code <= 0x2A6DF) ||
        // Fullwidth ASCII variants
        (code >= 0xFF01 && code <= 0xFF5E) ||
        // Fullwidth brackets and punctuation
        (code >= 0xFF5F && code <= 0xFF60) ||
        // Fullwidth symbol variants
        (code >= 0xFFE0 && code <= 0xFFE6) ||
        // Japanese kana
        (code >= 0x3040 && code <= 0x309F) ||
        (code >= 0x30A0 && code <= 0x30FF) ||
        // CJK Symbols and Punctuation
        (code >= 0x3000 && code <= 0x303F) ||
        // Enclosed CJK Letters and Months
        (code >= 0x3200 && code <= 0x32FF) ||
        // CJK Compatibility
        (code >= 0x3300 && code <= 0x33FF)
    
    let isZeroWidth (c: char) : bool =
        let code = int c
        // Combining Diacritical Marks
        (code >= 0x0300 && code <= 0x036F) ||
        // Combining Diacritical Marks Extended
        (code >= 0x1AB0 && code <= 0x1AFF) ||
        // Combining Diacritical Marks Supplement
        (code >= 0x1DC0 && code <= 0x1DFF) ||
        // Combining Marks for Symbols
        (code >= 0x20D0 && code <= 0x20FF) ||
        // Variation Selectors
        (code >= 0xFE00 && code <= 0xFE0F) ||
        // Zero Width Space / Joiner / Non-Joiner
        code = 0x200B || code = 0x200C || code = 0x200D
    
    let charWidth (c: char) : int =
        if isZeroWidth c then 0
        elif isWide c then 2
        else 1
    
    let stringWidth (s: string) : int =
        let mutable w = 0
        for c in s do
            w <- w + charWidth c
        w
