namespace Mire.Protocol

open System
open System.Text
open Mire.Core

module InputParser =

    let private mkKey key text mods : KeyEvent =
        { Key = key
          Text = text
          Modifiers = mods
          Repeat = false
          EventType = Press }

    /// xterm/Kitty modifier parameter is `(bitfield + 1)`: shift=1, alt=2,
    /// ctrl=4, super=8, … Decode to `KeyModifiers` (super → Meta, our closest).
    let private modifiersOf (param: int) : KeyModifiers =
        let bits = if param > 0 then param - 1 else 0

        { Shift = bits &&& 1 <> 0
          Alt = bits &&& 2 <> 0
          Ctrl = bits &&& 4 <> 0
          Meta = bits &&& 8 <> 0 }

    /// Kitty's private-use functional key codepoints (Unicode PUA, 57344–57454):
    /// the numeric keypad, F13–F35, and the media/lock/lone-modifier keys. Map the
    /// ones with a `Key` representation (keypad → its arrow/nav/digit/operator, F13+
    /// → `Function`); the rest (locks, media, menu, lone modifiers, KP_BEGIN) become
    /// `Unknown` so they never leak through as a meaningless PUA glyph insert.
    let private keyOfFunctional (cp: int) : Key =
        match cp with
        | 57414 -> Enter // KP_ENTER
        | 57417 -> ArrowLeft // KP_LEFT
        | 57418 -> ArrowRight // KP_RIGHT
        | 57419 -> ArrowUp // KP_UP
        | 57420 -> ArrowDown // KP_DOWN
        | 57421 -> PageUp // KP_PAGE_UP
        | 57422 -> PageDown // KP_PAGE_DOWN
        | 57423 -> Home // KP_HOME
        | 57424 -> End // KP_END
        | 57425 -> Insert // KP_INSERT
        | 57426 -> Delete // KP_DELETE
        | 57409 -> Char "." // KP_DECIMAL
        | 57410 -> Char "/" // KP_DIVIDE
        | 57411 -> Char "*" // KP_MULTIPLY
        | 57412 -> Char "-" // KP_SUBTRACT
        | 57413 -> Char "+" // KP_ADD
        | 57415 -> Char "=" // KP_EQUAL
        | 57416 -> Char "," // KP_SEPARATOR
        | _ when cp >= 57399 && cp <= 57408 -> Char(string (cp - 57399)) // KP_0..KP_9
        | _ when cp >= 57376 && cp <= 57398 -> Function(13 + (cp - 57376)) // F13..F35
        | _ -> Unknown(string cp) // locks, media, menu, lone modifiers, KP_BEGIN

    /// Map a Kitty `CSI u` Unicode key code to a `Key`. Letters/printables come
    /// through as their base codepoint (e.g. Ctrl+P → 112 → `Char "p"`); the
    /// private-use functional block (57344–57454) routes through `keyOfFunctional`.
    let private keyOfCodepoint (cp: int) : Key =
        match cp with
        | 13 -> Enter
        | 27 -> Escape
        | 9 -> Tab
        | 127 -> Backspace
        | 32 -> Space
        | _ when cp >= 57344 && cp <= 57454 -> keyOfFunctional cp
        | _ when cp >= 32 -> Char(Char.ConvertFromUtf32 cp)
        | _ -> Unknown(string cp)

    /// Parse a CSI sequence `ESC [ … <final>` into its numeric parameters and the
    /// final byte. Sub-parameters (after ':') are dropped. `None` if malformed.
    let private parseCsi (bytes: byte[]) : (int list * char) option =
        if bytes.Length < 3 then
            None
        else
            let final = char bytes.[bytes.Length - 1]

            if final < '\x40' || final > '\x7E' then
                None
            else
                let paramStr = Encoding.ASCII.GetString(bytes, 2, bytes.Length - 3)

                let parms =
                    if paramStr = "" then
                        []
                    else
                        paramStr.Split(';')
                        |> Array.map (fun p ->
                            match Int32.TryParse((p.Split(':')).[0]) with
                            | true, v -> v
                            | _ -> 0)
                        |> Array.toList

                Some(parms, final)

    /// Kitty `CSI u` event type lives in the modifier field's `:`-subparameter
    /// (`CSI code ; mod:event u`): 1/absent = press, 2 = repeat, 3 = release.
    /// `parseCsi` drops subparameters, so read it from the raw bytes here.
    let private kittyEventType (bytes: byte[]) : KeyEventType =
        if bytes.Length < 3 then
            Press
        else
            let paramStr = Encoding.ASCII.GetString(bytes, 2, bytes.Length - 3)
            let fields = paramStr.Split(';')

            if fields.Length < 2 then
                Press
            else
                let subs = fields.[1].Split(':')

                if subs.Length < 2 then
                    Press
                else
                    match Int32.TryParse subs.[1] with
                    | true, 2 -> Repeat
                    | true, 3 -> Release
                    | _ -> Press

    let private parseEscSequence (bytes: byte[]) : KeyEvent option =
        if bytes.Length < 2 then
            None
        else
            match bytes.[1] with
            | 0x5Buy -> // CSI: ESC [ …
                match parseCsi bytes with
                | None -> None
                | Some(parms, final) ->
                    let nth i =
                        if List.length parms > i then List.item i parms else 0

                    let mods = modifiersOf (nth 1)
                    // The Kitty `CSI > 3 u` mode reports event types (press/repeat/
                    // release) on the *legacy* forms too — `ESC [ 1 ; <mod>:<event> A`
                    // for arrows, `ESC [ <n> ; <mod>:<event> ~` for editing keys — not
                    // just on `CSI u`. Decode it for all of them, else release events
                    // masquerade as presses and every keystroke fires twice.
                    let evt = kittyEventType bytes
                    let mk m key = Some { mkKey key None m with EventType = evt; Repeat = (evt = Repeat) }

                    match final with
                    // Kitty key encoding: ESC [ <codepoint> ; <modifiers>[:<event>] u
                    | 'u' ->
                        match parms with
                        | cp :: _ -> mk mods (keyOfCodepoint cp)
                        | [] -> None
                    // Cursor / navigation keys; modifiers arrive as `ESC [ 1 ; <mod> <final>`.
                    | 'A' -> mk mods ArrowUp
                    | 'B' -> mk mods ArrowDown
                    | 'C' -> mk mods ArrowRight
                    | 'D' -> mk mods ArrowLeft
                    | 'H' -> mk mods Home
                    | 'F' -> mk mods End
                    | 'Z' -> mk { mods with Shift = true } Tab // backtab (Shift+Tab)
                    | 'P' -> mk mods (Function 1)
                    | 'Q' -> mk mods (Function 2)
                    | 'S' -> mk mods (Function 4) // ('R' omitted: collides with cursor-position report)
                    // `ESC [ <n> [; <mod>] ~` — editing/function keys.
                    | '~' ->
                        let key =
                            match nth 0 with
                            | 2 -> Some Insert
                            | 3 -> Some Delete
                            | 5 -> Some PageUp
                            | 6 -> Some PageDown
                            | 15 -> Some(Function 5)
                            | 17 -> Some(Function 6)
                            | 18 -> Some(Function 7)
                            | 19 -> Some(Function 8)
                            | 20 -> Some(Function 9)
                            | 21 -> Some(Function 10)
                            | 23 -> Some(Function 11)
                            | 24 -> Some(Function 12)
                            | _ -> None

                        key |> Option.bind (mk mods)
                    | _ -> None
            | 0x4Fuy -> // SS3: ESC O … — arrows/Home/End in *application cursor key*
                // mode (DECCKM), and unmodified F1–F4. Terminals like
                // JediTerm default to app-cursor mode, sending `ESC O A`
                // for Up where others send `ESC [ A`; decode both.
                if bytes.Length = 3 then
                    match bytes.[2] with
                    | 0x41uy -> Some(mkKey ArrowUp None KeyModifiers.None)
                    | 0x42uy -> Some(mkKey ArrowDown None KeyModifiers.None)
                    | 0x43uy -> Some(mkKey ArrowRight None KeyModifiers.None)
                    | 0x44uy -> Some(mkKey ArrowLeft None KeyModifiers.None)
                    | 0x50uy -> Some(mkKey (Function 1) None KeyModifiers.None)
                    | 0x51uy -> Some(mkKey (Function 2) None KeyModifiers.None)
                    | 0x52uy -> Some(mkKey (Function 3) None KeyModifiers.None)
                    | 0x53uy -> Some(mkKey (Function 4) None KeyModifiers.None)
                    | 0x48uy -> Some(mkKey Home None KeyModifiers.None)
                    | 0x46uy -> Some(mkKey End None KeyModifiers.None)
                    | _ -> None
                else
                    None
            | _ -> None

    // --- non-Key CSI sequences: mouse (SGR 1006), bracketed paste, focus ---

    let private pasteStart = [| 0x1Buy; 0x5Buy; 0x32uy; 0x30uy; 0x30uy; 0x7Euy |] // ESC [ 2 0 0 ~
    let private pasteEnd = [| 0x1Buy; 0x5Buy; 0x32uy; 0x30uy; 0x31uy; 0x7Euy |] // ESC [ 2 0 1 ~

    let private startsWith (prefix: byte[]) (bytes: byte[]) : bool =
        bytes.Length >= prefix.Length
        && Array.forall2 (=) prefix bytes.[0 .. prefix.Length - 1]

    /// First index ≥ `start` where `needle` occurs in `bytes`, or -1.
    let private indexOf (needle: byte[]) (bytes: byte[]) (start: int) : int =
        let mutable i = start
        let mutable found = -1

        while found < 0 && i + needle.Length <= bytes.Length do
            if Array.forall2 (=) needle bytes.[i .. i + needle.Length - 1] then
                found <- i
            else
                i <- i + 1

        found

    /// Bracketed paste: `ESC [ 200 ~ <text> ESC [ 201 ~` → `Paste text`. If the end
    /// marker isn't in this buffer (a paste split across reads), takes the text
    /// through the buffer's end.
    let private parsePaste (bytes: byte[]) : InputEvent option =
        if startsWith pasteStart bytes then
            let cStart = pasteStart.Length
            let endIdx = indexOf pasteEnd bytes cStart
            let cEnd = if endIdx >= 0 then endIdx else bytes.Length
            Some(Paste(Encoding.UTF8.GetString(bytes, cStart, cEnd - cStart)))
        else
            None

    /// True if `bytes` begins a bracketed paste whose end marker hasn't arrived
    /// yet — the runtime should keep buffering before handing it to `parseBytes`.
    let isIncompletePaste (bytes: byte[]) : bool =
        startsWith pasteStart bytes && indexOf pasteEnd bytes pasteStart.Length < 0

    /// Paste-reassembly step for the runtime read loop. Given the bytes carried
    /// from prior reads and a fresh read, decide whether to keep buffering (an
    /// unfinished bracketed paste still under `cap` bytes) or to flush. Returns
    /// `(bytesToParseNow, newCarry)`; an empty first element means "keep waiting".
    /// The `cap` bounds a missing end-marker so the carry can't grow without limit.
    let stepPasteBuffer (cap: int) (carry: byte[]) (incoming: byte[]) : byte[] * byte[] =
        let combined =
            if carry.Length = 0 then
                incoming
            else
                Array.append carry incoming

        if combined.Length > 0 && combined.Length < cap && isIncompletePaste combined then
            [||], combined
        else
            combined, [||]

    /// SGR mouse (1006): `ESC [ < b ; x ; y` then `M` (press) or `m` (release).
    /// Coords are 1-based on the wire; reported 0-based. `b` bits: 0-1 = button,
    /// 0x40 = wheel (low bits pick up/down/left/right), 0x04 shift, 0x08 alt, 0x10 ctrl.
    let private parseMouseSgr (bytes: byte[]) : InputEvent option =
        if bytes.Length >= 6 && bytes.[2] = 0x3Cuy then
            let final = char bytes.[bytes.Length - 1]

            if final = 'M' || final = 'm' then
                let parts =
                    Encoding.ASCII.GetString(bytes, 3, bytes.Length - 4).Split(';')
                    |> Array.map (fun s ->
                        match Int32.TryParse s with
                        | true, v -> Some v
                        | _ -> None)

                match parts with
                | [| Some b; Some x; Some y |] ->
                    let mods =
                        { Shift = b &&& 0x04 <> 0
                          Alt = b &&& 0x08 <> 0
                          Ctrl = b &&& 0x10 <> 0
                          Meta = false }

                    let button =
                        if b &&& 0x40 <> 0 then
                            match b &&& 0x03 with
                            | 0 -> ScrollUp
                            | 1 -> ScrollDown
                            | 2 -> ScrollLeft
                            | _ -> ScrollRight
                        else
                            match b &&& 0x03 with
                            | 0 -> MouseButton.Left
                            | 1 -> Middle
                            | 2 -> Right
                            | n -> UnknownButton n

                    Some(
                        Mouse
                            { X = x - 1
                              Y = y - 1
                              Button = button
                              Modifiers = mods
                              Pressed = (final = 'M') }
                    )
                | _ -> None
            else
                None
        else
            None

    /// Focus events: `ESC [ I` (gained) / `ESC [ O` (lost).
    let private parseFocus (bytes: byte[]) : InputEvent option =
        if bytes.Length = 3 && bytes.[1] = 0x5Buy then
            match bytes.[2] with
            | 0x49uy -> Some FocusGained
            | 0x4Fuy -> Some FocusLost
            | _ -> None
        else
            None

    /// Color-scheme report (DEC mode 2031 / DSR 996): `ESC [ ? 997 ; 1 n` = dark,
    /// `ESC [ ? 997 ; 2 n` = light.
    let private parseThemeReport (bytes: byte[]) : InputEvent option =
        if
            bytes.Length >= 5
            && bytes.[1] = 0x5Buy
            && bytes.[2] = 0x3Fuy
            && bytes.[bytes.Length - 1] = 0x6Euy
        then
            // body is between the '?' and the trailing 'n'
            let body = Encoding.ASCII.GetString(bytes, 3, bytes.Length - 4)

            match body.Split(';') with
            | [| "997"; "1" |] -> Some(ThemeChanged Dark)
            | [| "997"; "2" |] -> Some(ThemeChanged Light)
            | _ -> None
        else
            None

    /// Decode the non-Key CSI sequences; `None` falls through to the Key decoder.
    let private parseSpecial (bytes: byte[]) : InputEvent option =
        if bytes.Length >= 3 && bytes.[1] = 0x5Buy then
            match parsePaste bytes with
            | Some ev -> Some ev
            | None ->
                match parseMouseSgr bytes with
                | Some ev -> Some ev
                | None ->
                    match parseFocus bytes with
                    | Some ev -> Some ev
                    | None -> parseThemeReport bytes
        else
            None

    let parseBytes (bytes: byte[]) : InputEvent option =
        if bytes.Length = 0 then
            None
        else
            match bytes.[0] with
            | 0x03uy ->
                Some(
                    Key
                        { Key = Char "c"
                          Text = Some "c"
                          Modifiers = { KeyModifiers.None with Ctrl = true }
                          Repeat = false
                          EventType = Press }
                )
            | 0x0Duy
            | 0x0Auy ->
                Some(
                    Key
                        { Key = Enter
                          Text = None
                          Modifiers = KeyModifiers.None
                          Repeat = false
                          EventType = Press }
                )
            | 0x09uy ->
                Some(
                    Key
                        { Key = Tab
                          Text = Some "\t"
                          Modifiers = KeyModifiers.None
                          Repeat = false
                          EventType = Press }
                )
            | 0x7Fuy ->
                Some(
                    Key
                        { Key = Backspace
                          Text = None
                          Modifiers = KeyModifiers.None
                          Repeat = false
                          EventType = Press }
                )
            | 0x1Buy ->
                if bytes.Length = 1 then
                    Some(
                        Key
                            { Key = Escape
                              Text = None
                              Modifiers = KeyModifiers.None
                              Repeat = false
                              EventType = Press }
                    )
                else
                    // mouse / paste / focus produce non-Key events; try them first
                    match parseSpecial bytes with
                    | Some ev -> Some ev
                    | None ->
                        match parseEscSequence bytes with
                        | Some keyEvent -> Some(Key keyEvent)
                        | None -> None
            | 0x20uy ->
                // Spacebar decodes to the semantic Space key (still carrying its
                // text " " so text-entry widgets reading KeyEvent.Text are unaffected).
                Some(
                    Key
                        { Key = Space
                          Text = Some " "
                          Modifiers = KeyModifiers.None
                          Repeat = false
                          EventType = Press }
                )
            | b when b >= 0x21uy && b <= 0x7Euy ->
                let s = Encoding.UTF8.GetString(bytes)

                Some(
                    Key
                        { Key = Char s
                          Text = Some s
                          Modifiers = KeyModifiers.None
                          Repeat = false
                          EventType = Press }
                )
            | b when b < 0x20uy ->
                // Control characters: map to ctrl+letter
                let letter = char (int b + int 'a' - 1)
                let s = string letter

                Some(
                    Key
                        { Key = Char s
                          Text = Some s
                          Modifiers = { KeyModifiers.None with Ctrl = true }
                          Repeat = false
                          EventType = Press }
                )
            | b when b >= 0x80uy ->
                // A multi-byte UTF-8 scalar (accented letters, CJK, emoji). The
                // tokenizer hands us exactly one scalar's bytes; decode it as a Char.
                let s = Encoding.UTF8.GetString(bytes)

                Some(
                    Key
                        { Key = Char s
                          Text = Some s
                          Modifiers = KeyModifiers.None
                          Repeat = false
                          EventType = Press }
                )
            | _ -> None

    /// How many bytes the event beginning at `offset` spans — the tokenizer's
    /// boundary rule. A single `read()` can return several events back-to-back
    /// (a scroll/drag burst, fast typing, queued sequences); `parseBytes` decodes
    /// only one, so we slice the buffer into per-event spans first. Rules:
    ///   • `ESC [ 200 ~ … 201 ~` (bracketed paste) — through the end marker (or to
    ///     the buffer end if it's missing), so the pasted text isn't split.
    ///   • `ESC [ …` (CSI) — through the first final byte (0x40–0x7E); params
    ///     (0x30–0x3F) and intermediates (0x20–0x2F) are interior.
    ///   • `ESC O x` (SS3) — 3 bytes.
    ///   • a lone `ESC`, or `ESC` + an unrecognized byte — just the `ESC` (1 byte),
    ///     so the following byte tokenizes on its own.
    ///   • a control byte (< 0x20) or `DEL` (0x7F) — 1 byte.
    ///   • printable lead byte — one UTF-8 scalar (length from the lead byte), so a
    ///     run of typed characters becomes one Key event each, not a single fused one.
    let private nextEventLength (bytes: byte[]) (offset: int) : int =
        let len = bytes.Length
        let b = bytes.[offset]
        let rest = bytes.[offset..]

        if b = 0x1Buy then
            if startsWith pasteStart rest then
                let endIdx = indexOf pasteEnd rest pasteStart.Length
                if endIdx >= 0 then endIdx + pasteEnd.Length else rest.Length
            elif offset + 1 < len && bytes.[offset + 1] = 0x5Buy then // CSI: ESC [ …
                let mutable i = offset + 2
                let mutable finalAt = -1

                while finalAt < 0 && i < len do
                    if bytes.[i] >= 0x40uy && bytes.[i] <= 0x7Euy then
                        finalAt <- i
                    else
                        i <- i + 1

                if finalAt >= 0 then finalAt - offset + 1 else len - offset
            elif offset + 1 < len && bytes.[offset + 1] = 0x4Fuy then // SS3: ESC O x
                min 3 (len - offset)
            else
                1 // lone ESC, or an ESC-prefixed byte we don't decode as a sequence
        elif b < 0x20uy || b = 0x7Fuy then
            1
        else
            // Printable: one UTF-8 scalar, length inferred from the lead byte.
            let scalar =
                if b < 0x80uy then 1
                elif b >= 0xF0uy then 4
                elif b >= 0xE0uy then 3
                elif b >= 0xC0uy then 2
                else 1 // stray continuation byte — consume one, defensively

            min scalar (len - offset)

    /// Decode every event in a raw input buffer, in order. Splits the buffer into
    /// per-event byte spans (see `nextEventLength`) and runs each through
    /// `parseBytes`, dropping spans that don't decode. The runtime uses this so a
    /// burst of input delivered in one `read()` isn't collapsed to a single event.
    let parseAll (bytes: byte[]) : InputEvent list =
        let acc = System.Collections.Generic.List<InputEvent>()
        let mutable i = 0

        while i < bytes.Length do
            let n = max 1 (nextEventLength bytes i)

            match parseBytes bytes.[i .. i + n - 1] with
            | Some ev -> acc.Add ev
            | None -> ()

            i <- i + n

        List.ofSeq acc

    let readEvent () : InputEvent option =
        if TerminalMode.stdinAvailable () then
            System.Threading.Thread.Sleep(1) // Let multi-byte sequences arrive
            let bytes = TerminalMode.readStdinBytes ()
            parseBytes bytes
        else
            None

    /// Read whatever raw input bytes are waiting (with a short settle delay so a
    /// multi-byte escape sequence arrives whole). `[||]` if nothing is available.
    /// The runtime uses this (rather than `readEvent`) so it can reassemble a
    /// bracketed paste split across reads via `stepPasteBuffer` before parsing.
    let readRawBytes () : byte[] =
        if TerminalMode.stdinAvailable () then
            System.Threading.Thread.Sleep(1)
            TerminalMode.readStdinBytes ()
        else
            [||]

    let readEventBlocking (timeoutMs: int) : InputEvent option =
        let sw = Diagnostics.Stopwatch.StartNew()

        while not (TerminalMode.stdinAvailable ()) && sw.ElapsedMilliseconds < int64 timeoutMs do
            System.Threading.Thread.Sleep(1)

        if TerminalMode.stdinAvailable () then
            readEvent ()
        else
            None
