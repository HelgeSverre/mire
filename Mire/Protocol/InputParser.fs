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

    /// Map a Kitty `CSI u` Unicode key code to a `Key`. Letters/printables come
    /// through as their base codepoint (e.g. Ctrl+P → 112 → `Char "p"`).
    let private keyOfCodepoint (cp: int) : Key =
        match cp with
        | 13 -> Enter
        | 27 -> Escape
        | 9 -> Tab
        | 127 -> Backspace
        | 32 -> Space
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

                    match final with
                    // Kitty key encoding: ESC [ <codepoint> ; <modifiers> u
                    | 'u' ->
                        match parms with
                        | cp :: _ -> Some(mkKey (keyOfCodepoint cp) None mods)
                        | [] -> None
                    // Cursor / navigation keys; modifiers arrive as `ESC [ 1 ; <mod> <final>`.
                    | 'A' -> Some(mkKey ArrowUp None mods)
                    | 'B' -> Some(mkKey ArrowDown None mods)
                    | 'C' -> Some(mkKey ArrowRight None mods)
                    | 'D' -> Some(mkKey ArrowLeft None mods)
                    | 'H' -> Some(mkKey Home None mods)
                    | 'F' -> Some(mkKey End None mods)
                    | 'Z' -> Some(mkKey Tab None { mods with Shift = true }) // backtab (Shift+Tab)
                    | 'P' -> Some(mkKey (Function 1) None mods)
                    | 'Q' -> Some(mkKey (Function 2) None mods)
                    | 'S' -> Some(mkKey (Function 4) None mods) // ('R' omitted: collides with cursor-position report)
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

                        key |> Option.map (fun k -> mkKey k None mods)
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

    /// Decode the non-Key CSI sequences; `None` falls through to the Key decoder.
    let private parseSpecial (bytes: byte[]) : InputEvent option =
        if bytes.Length >= 3 && bytes.[1] = 0x5Buy then
            match parsePaste bytes with
            | Some ev -> Some ev
            | None ->
                match parseMouseSgr bytes with
                | Some ev -> Some ev
                | None -> parseFocus bytes
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
            | _ -> None

    let readEvent () : InputEvent option =
        if TerminalMode.stdinAvailable () then
            System.Threading.Thread.Sleep(1) // Let multi-byte sequences arrive
            let bytes = TerminalMode.readStdinBytes ()
            parseBytes bytes
        else
            None

    let readEventBlocking (timeoutMs: int) : InputEvent option =
        let sw = Diagnostics.Stopwatch.StartNew()

        while not (TerminalMode.stdinAvailable ()) && sw.ElapsedMilliseconds < int64 timeoutMs do
            System.Threading.Thread.Sleep(1)

        if TerminalMode.stdinAvailable () then
            readEvent ()
        else
            None
