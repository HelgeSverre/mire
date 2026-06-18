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
    let hyperlink (url, text) =
        $"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\"

    /// Open a hyperlink: cells written after this carry the link until `osc8Close`.
    /// (The Diff writer brackets each linked run with these so the link attribute
    /// is scoped to exactly that run, even across cursor moves.)
    let osc8Open (url: string) = $"\x1b]8;;{url}\x1b\\"
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
        if pngBase64 = "" then
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

    // Platform-specific raw mode setup
    let setupRawMode () =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            // Windows: use Console APIs
            Console.OutputEncoding <- Encoding.UTF8
            Console.InputEncoding <- Encoding.UTF8
            Console.CursorVisible <- false
        else
            // Unix: UTF-8 console + raw mode via stty (below). `poll`/`read` on fd 0
            // handle input; `Console.WindowWidth/Height` handle size.
            Console.OutputEncoding <- Encoding.UTF8
            Console.InputEncoding <- Encoding.UTF8
            Console.CursorVisible <- false

            // Try to disable canonical mode and echo via stty
            try
                let psi = Diagnostics.ProcessStartInfo()
                psi.FileName <- "stty"
                psi.Arguments <- "-echo -icanon min 1 time 0"
                psi.UseShellExecute <- false
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                let p = Diagnostics.Process.Start(psi)
                p.WaitForExit(1000) |> ignore
            with _ ->
                ()

    let restoreMode () =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Console.CursorVisible <- true
        else
            Console.CursorVisible <- true

            try
                let psi = Diagnostics.ProcessStartInfo()
                psi.FileName <- "stty"
                psi.Arguments <- "sane"
                psi.UseShellExecute <- false
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                let p = Diagnostics.Process.Start(psi)
                p.WaitForExit(1000) |> ignore
            with _ ->
                ()

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

    let rec readStdinBytes () : byte[] =
        if stdinAvailable () then
            match readStdinByte () with
            | Some b ->
                let rest = readStdinBytes ()
                Array.append [| b |] rest
            | None -> [||]
        else
            [||]
