namespace Mire.Protocol

open System
open System.Text

module ANSI =
    let ESC = "\x1b"
    let CSI = ESC + "["

    // Cursor
    let cursorHome = CSI + "H"
    let cursorTo (x, y) = $"{CSI}{y + 1};{x + 1}H"
    let cursorUp (n) = $"{CSI}{n}A"
    let cursorDown (n) = $"{CSI}{n}B"
    let cursorRight (n) = $"{CSI}{n}C"
    let cursorLeft (n) = $"{CSI}{n}D"
    let cursorHide = CSI + "?25l"
    let cursorShow = CSI + "?25h"
    let cursorSave = ESC + "7"
    let cursorRestore = ESC + "8"

    // Screen
    let clearScreen = CSI + "2J"
    let clearLine = CSI + "2K"
    let clearLineRight = CSI + "0K"
    let clearLineLeft = CSI + "1K"
    let eraseDown = CSI + "0J"
    let eraseUp = CSI + "1J"

    // Modes
    let enterAltScreen = CSI + "?1049h"
    let exitAltScreen = CSI + "?1049l"
    let enableMouse = CSI + "?1002h" + CSI + "?1006h"
    let disableMouse = CSI + "?1006l" + CSI + "?1002l"
    let enableBracketedPaste = CSI + "?2004h"
    let disableBracketedPaste = CSI + "?2004l"
    let enableFocusEvents = CSI + "?1004h"
    let disableFocusEvents = CSI + "?1004l"

    // Light/dark theme change notifications (DEC private mode 2031 — Contour/Kitty/
    // Ghostty). While enabled, the terminal reports color-scheme changes in-band as
    // `CSI ? 997 ; 1 n` (dark) / `CSI ? 997 ; 2 n` (light). `queryColorScheme` (DSR
    // 996) asks for the current scheme once, answered with the same report form.
    let enableThemeNotifications = CSI + "?2031h"
    let disableThemeNotifications = CSI + "?2031l"
    let queryColorScheme = CSI + "?996n"

    // Synchronized output (iTerm2/Ghostty/Kitty)
    let beginSync = ESC + "[?2026h"
    let endSync = ESC + "[?2026l"

    // Kitty keyboard protocol. Push flags 1|2 = 3: 1 = disambiguate escape codes,
    // 2 = report event types (press/repeat/release). The disable form pops the
    // pushed level. (The runtime drops Release events unless the app opts in, so
    // reporting them here is transparent to apps that don't care — see Program.)
    let enableKittyKeyboard = CSI + ">3u"
    let disableKittyKeyboard = CSI + "<u"

    // Styles
    let resetStyle = CSI + "0m"
    let bold = CSI + "1m"
    let dim = CSI + "2m"
    let italic = CSI + "3m"
    let underline = CSI + "4m"
    let blink = CSI + "5m"
    let reverse = CSI + "7m"
    let strikethrough = CSI + "9m"

    // Colors
    let foregroundRgb (r, g, b) = $"{CSI}38;2;{r};{g};{b}m"
    let backgroundRgb (r, g, b) = $"{CSI}48;2;{r};{g};{b}m"
    let defaultFg = CSI + "39m"
    let defaultBg = CSI + "49m"

    // OSC 8 hyperlink
    /// Longest URI accepted into an OSC 8 sequence (terminals cap it too; Kitty
    /// and VTE use 2083-ish, so anything longer is almost certainly not a link).
    let maxOscUriLength = 2048

    /// Validate and encode a URI for an OSC 8 control string. The URI is data
    /// spliced *inside* a terminal instruction, so an embedded ESC/BEL/ST would
    /// terminate it early and let the rest run as commands. `None` if it holds
    /// any control scalar (C0, DEL, C1) or is empty/over `maxOscUriLength`;
    /// otherwise non-ASCII and space are percent-encoded (UTF-8), as the OSC 8
    /// spec requires URIs to be printable ASCII (0x21–0x7E).
    let oscUri (url: string) : string option =
        if String.IsNullOrEmpty url || url.Length > maxOscUriLength then
            None
        elif url |> Seq.exists (fun c -> int c < 0x20 || (int c >= 0x7F && int c <= 0x9F)) then
            None
        elif url |> Seq.forall (fun c -> int c > 0x20 && int c < 0x7F) then
            Some url
        else
            let sb = StringBuilder()

            for b in Encoding.UTF8.GetBytes url do
                if b > 0x20uy && b < 0x7Fuy then
                    sb.Append(char b) |> ignore
                else
                    sb.Append('%').Append(b.ToString("X2")) |> ignore

            let encoded = sb.ToString()

            if encoded.Length > maxOscUriLength then
                None
            else
                Some encoded

    /// A complete OSC 8 hyperlink around `text`. An unsafe URL (see `oscUri`)
    /// drops the link and yields just the text. `text` is emitted as-is — this is
    /// a raw-output helper; cell text goes through the renderer's sanitizer.
    let hyperlink (url, text) =
        match oscUri url with
        | Some u -> $"\x1b]8;;{u}\x1b\\{text}\x1b]8;;\x1b\\"
        | None -> text

    /// Open a hyperlink: cells written after this carry the link until `osc8Close`.
    /// (The Diff writer brackets each linked run with these so the link attribute
    /// is scoped to exactly that run, even across cursor moves.) An unsafe URL
    /// (see `oscUri`) yields `""` — the run renders unlinked.
    let osc8Open (url: string) =
        match oscUri url with
        | Some u -> $"\x1b]8;;{u}\x1b\\"
        | None -> ""

    let osc8Close = "\x1b]8;;\x1b\\"

    // OSC 52 clipboard
    let setClipboard (text: string) =
        let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
        $"\x1b]52;c;{b64}\x1b\\"

    // Kitty graphics protocol (APC `_G … ST`). `kittyImage` transmits a PNG —
    // already base64-encoded — and displays it at the current cursor, sized to a
    // `cols`×`rows` cell box (`f=100` PNG, `a=T` transmit-and-display). The payload
    // is split into ≤4096-byte chunks with `m=1` on all but the last, per the spec.
    // `deleteImages` clears all placed images (`a=d`).
    let kittyImage (cols: int) (rows: int) (pngBase64: string) : string =
        let isBase64 (c: char) =
            (c >= 'A' && c <= 'Z')
            || (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9')
            || c = '+'
            || c = '/'
            || c = '='

        // The payload sits inside an APC control string: anything but base64
        // (an ESC, say) could terminate it and run the remainder as commands.
        if pngBase64 = "" || not (Seq.forall isBase64 pngBase64) then
            ""
        else
            let sb = StringBuilder()
            let chunkSize = 4096
            let n = pngBase64.Length
            let mutable i = 0
            let mutable first = true

            while i < n do
                let len = min chunkSize (n - i)
                let chunk = pngBase64.Substring(i, len)
                let m = if i + len >= n then 0 else 1

                if first then
                    sb.Append($"\x1b_Gf=100,a=T,c={cols},r={rows},m={m};{chunk}\x1b\\") |> ignore
                    first <- false
                else
                    sb.Append($"\x1b_Gm={m};{chunk}\x1b\\") |> ignore

                i <- i + len

            sb.ToString()

    let deleteImages = "\x1b_Ga=d\x1b\\"

module TerminalMode =
    open System.Runtime.InteropServices

    /// Run `stty` against the controlling terminal (stdin is inherited, so it acts
    /// on our tty). Returns `Ok stdout` on exit code 0, else `Error` with stderr.
    let private stty (args: string) : Result<string, string> =
        try
            let psi = Diagnostics.ProcessStartInfo()
            psi.FileName <- "stty"
            psi.Arguments <- args
            psi.UseShellExecute <- false
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            use p = Diagnostics.Process.Start(psi)
            let out = p.StandardOutput.ReadToEnd()
            let err = p.StandardError.ReadToEnd()

            if not (p.WaitForExit(2000)) then
                (try
                    p.Kill()
                 with _ ->
                     ())

                Error "stty timed out"
            elif p.ExitCode <> 0 then
                Error(if err = "" then $"stty exited {p.ExitCode}" else err.Trim())
            else
                Ok(out.Trim())
        with ex ->
            Error ex.Message

    // The terminal's line-discipline state before we changed it (`stty -g`), so
    // teardown restores the user's exact settings instead of resetting to `sane`.
    let mutable private savedState: string option = None

    /// The raw-mode policy: no echo, no line buffering, no signal keys (Ctrl+C/Z/\
    /// arrive as bytes, so the app's quit policy decides), no XON/XOFF flow
    /// control (Ctrl+S/Q reach the app), no Ctrl+V literal-next, no CR→NL input
    /// translation; byte-at-a-time reads. Output post-processing is left on.
    let rawPolicy = "-echo -icanon -isig -ixon -iexten -icrnl min 1 time 0"

    /// Enter raw mode, first saving the terminal's current settings for
    /// `restoreMode`. `Error` (with the reason) if the settings couldn't be saved
    /// or applied — e.g. stdin isn't a terminal.
    let trySetupRawMode () : Result<unit, string> =
        Console.OutputEncoding <- Encoding.UTF8
        Console.InputEncoding <- Encoding.UTF8

        (try
            Console.CursorVisible <- false
         with _ ->
             ())

        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Ok()
        else
            match stty "-g" with
            | Error e -> Error $"could not save terminal settings: {e}"
            | Ok saved ->
                if savedState.IsNone then
                    savedState <- Some saved

                match stty rawPolicy with
                | Ok _ -> Ok()
                | Error e -> Error $"could not enter raw mode: {e}"

    /// Enter raw mode (see `trySetupRawMode`), ignoring failure.
    let setupRawMode () = trySetupRawMode () |> ignore

    /// Restore the terminal settings saved by `trySetupRawMode` (falling back to
    /// `stty sane` if none were saved). `Error` if restoring failed.
    let tryRestoreMode () : Result<unit, string> =
        (try
            Console.CursorVisible <- true
         with _ ->
             ())

        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Ok()
        else
            let result =
                match savedState with
                | Some saved -> stty saved
                | None -> stty "sane"

            match result with
            | Ok _ ->
                savedState <- None
                Ok()
            | Error e -> Error e

    /// Restore the terminal settings (see `tryRestoreMode`), ignoring failure.
    let restoreMode () = tryRestoreMode () |> ignore

    let getTerminalSize () =
        try
            let width = Console.WindowWidth
            let height = Console.WindowHeight
            let size = Mire.Core.Size.Create(width, height)
            if size.IsValid then Some size else None
        with _ ->
            None

    // Unix raw stdin reading
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type PollFd =
        val mutable Fd: int
        val mutable Events: int16
        val mutable Revents: int16

    // NOTE: `poll` takes the pollfd BY REFERENCE, not as an array. A managed
    // `PollFd[]` is marshaled in-only here, so the kernel's writes to `revents`
    // never make it back and `stdinAvailable` would always read 0 → no input is
    // ever detected. Passing the single struct byref marshals as `pollfd*` (which
    // is exactly what poll wants for nfds=1) and is in/out, so `revents` is
    // written back. (For nfds>1 this would need a pinned array + IntPtr instead.)
    [<DllImport("libc", SetLastError = true)>]
    extern int poll(PollFd& fds, uint32 nfds, int timeout)

    [<DllImport("libc", SetLastError = true)>]
    extern int read(int fd, byte[] buf, uint32 count)

    let stdinAvailable () : bool =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            try
                Console.KeyAvailable
            with _ ->
                false
        else
            let mutable pfd = PollFd()
            pfd.Fd <- 0
            pfd.Events <- int16 1 // POLLIN
            pfd.Revents <- int16 0
            let result = poll (&pfd, 1u, 0)
            result > 0 && (int pfd.Revents &&& 1) <> 0

    let readStdinByte () : byte option =
        let buf = Array.zeroCreate<byte> 1
        let n = read (0, buf, 1u)
        if n > 0 then Some buf.[0] else None

    /// Default per-call budget for `readStdinBytesUpTo` (256 KiB).
    let readBudget = 256 * 1024

    /// Read whatever stdin has waiting, iteratively in 4 KiB chunks, stopping when
    /// nothing more is immediately available or `maxBytes` have been read (the
    /// rest stays in the kernel buffer for the next call). Bounded in both memory
    /// and time per call, so a huge paste or input flood can't stall a frame or
    /// grow the stack.
    let readStdinBytesUpTo (maxBytes: int) : byte[] =
        let out = new IO.MemoryStream()
        let chunk = Array.zeroCreate<byte> 4096
        let mutable go = true

        while go && int out.Length < maxBytes && stdinAvailable () do
            let want = min chunk.Length (maxBytes - int out.Length)
            let n = read (0, chunk, uint32 want)

            if n > 0 then out.Write(chunk, 0, n) else go <- false

        out.ToArray()

    /// Read whatever stdin has waiting (up to `readBudget` bytes).
    let readStdinBytes () : byte[] = readStdinBytesUpTo readBudget
