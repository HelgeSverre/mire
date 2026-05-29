namespace Mire.Protocol

open System
open System.Text
open Mire.Core

module InputParser =

    let private parseEscSequence (bytes: byte[]) : KeyEvent option =
        if bytes.Length < 2 then None
        else
            match bytes.[1] with
            | 0x5Buy -> // [
                if bytes.Length = 3 then
                    match bytes.[2] with
                    | 0x41uy -> Some { Key = ArrowUp; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x42uy -> Some { Key = ArrowDown; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x43uy -> Some { Key = ArrowRight; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x44uy -> Some { Key = ArrowLeft; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x48uy -> Some { Key = Home; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x46uy -> Some { Key = End; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    // Backtab (Shift+Tab): legacy `ESC [ Z`. Note: under the full Kitty
                    // keyboard protocol some terminals emit the `CSI u` form instead,
                    // which this parser does not yet decode (see ROADMAP v0.2/v0.5).
                    | 0x5Auy -> Some { Key = Tab; Text = None; Modifiers = { KeyModifiers.None with Shift = true }; Repeat = false; EventType = Press }
                    | _ -> None
                elif bytes.Length >= 4 && bytes.[bytes.Length - 1] = 0x7Euy then // ~ terminated
                    match bytes.[2] with
                    | 0x31uy when bytes.Length = 5 && bytes.[3] = 0x35uy -> Some { Key = Function 5; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x31uy when bytes.Length = 5 && bytes.[3] = 0x37uy -> Some { Key = Function 6; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x31uy when bytes.Length = 5 && bytes.[3] = 0x38uy -> Some { Key = Function 7; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x31uy when bytes.Length = 5 && bytes.[3] = 0x39uy -> Some { Key = Function 8; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.Length = 5 && bytes.[3] = 0x30uy -> Some { Key = Function 9; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.Length = 5 && bytes.[3] = 0x31uy -> Some { Key = Function 10; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.Length = 5 && bytes.[3] = 0x33uy -> Some { Key = Function 11; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.Length = 5 && bytes.[3] = 0x34uy -> Some { Key = Function 12; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy -> Some { Key = Insert; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x33uy -> Some { Key = Delete; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x35uy -> Some { Key = PageUp; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x36uy -> Some { Key = PageDown; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | _ -> None
                elif bytes.Length = 4 then
                    match bytes.[2] with
                    | 0x31uy when bytes.[3] = 0x35uy -> Some { Key = Function 5; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x31uy when bytes.[3] = 0x37uy -> Some { Key = Function 6; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x31uy when bytes.[3] = 0x38uy -> Some { Key = Function 7; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x31uy when bytes.[3] = 0x39uy -> Some { Key = Function 8; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.[3] = 0x30uy -> Some { Key = Function 9; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.[3] = 0x31uy -> Some { Key = Function 10; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.[3] = 0x33uy -> Some { Key = Function 11; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x32uy when bytes.[3] = 0x34uy -> Some { Key = Function 12; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | _ -> None
                else None
            | 0x4Fuy -> // O
                if bytes.Length = 3 then
                    match bytes.[2] with
                    | 0x50uy -> Some { Key = Function 1; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x51uy -> Some { Key = Function 2; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x52uy -> Some { Key = Function 3; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x53uy -> Some { Key = Function 4; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x48uy -> Some { Key = Home; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | 0x46uy -> Some { Key = End; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press }
                    | _ -> None
                else None
            | _ -> None

    let parseBytes (bytes: byte[]) : InputEvent option =
        if bytes.Length = 0 then None
        else
            match bytes.[0] with
            | 0x03uy -> Some (Key { Key = Char "c"; Text = Some "c"; Modifiers = { KeyModifiers.None with Ctrl = true }; Repeat = false; EventType = Press })
            | 0x0Duy | 0x0Auy -> Some (Key { Key = Enter; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press })
            | 0x09uy -> Some (Key { Key = Tab; Text = Some "\t"; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press })
            | 0x7Fuy -> Some (Key { Key = Backspace; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press })
            | 0x1Buy ->
                if bytes.Length = 1 then
                    Some (Key { Key = Escape; Text = None; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press })
                else
                    match parseEscSequence bytes with
                    | Some keyEvent -> Some (Key keyEvent)
                    | None -> None
            | b when b >= 0x20uy && b <= 0x7Euy ->
                let s = Encoding.UTF8.GetString(bytes)
                Some (Key { Key = Char s; Text = Some s; Modifiers = KeyModifiers.None; Repeat = false; EventType = Press })
            | b when b < 0x20uy ->
                // Control characters: map to ctrl+letter
                let letter = char (int b + int 'a' - 1)
                let s = string letter
                Some (Key { Key = Char s; Text = Some s; Modifiers = { KeyModifiers.None with Ctrl = true }; Repeat = false; EventType = Press })
            | _ -> None

    let readEvent() : InputEvent option =
        if TerminalMode.stdinAvailable() then
            System.Threading.Thread.Sleep(1) // Let multi-byte sequences arrive
            let bytes = TerminalMode.readStdinBytes()
            parseBytes bytes
        else
            None

    let readEventBlocking(timeoutMs: int) : InputEvent option =
        let sw = Diagnostics.Stopwatch.StartNew()
        while not (TerminalMode.stdinAvailable()) && sw.ElapsedMilliseconds < int64 timeoutMs do
            System.Threading.Thread.Sleep(1)
        if TerminalMode.stdinAvailable() then readEvent() else None
