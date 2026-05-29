namespace Mire.Protocol

open System
open System.Text

module ANSI =
    let ESC = "\x1b"
    let CSI = ESC + "["
    
    // Cursor
    let cursorHome = CSI + "H"
    let cursorTo(x, y) = $"{CSI}{y + 1};{x + 1}H"
    let cursorUp(n) = $"{CSI}{n}A"
    let cursorDown(n) = $"{CSI}{n}B"
    let cursorRight(n) = $"{CSI}{n}C"
    let cursorLeft(n) = $"{CSI}{n}D"
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
    
    // Synchronized output (iTerm2/Ghostty/Kitty)
    let beginSync = ESC + "[?2026h"
    let endSync = ESC + "[?2026l"
    
    // Kitty keyboard protocol
    let enableKittyKeyboard = CSI + ">1u"
    let disableKittyKeyboard = CSI + "<1u"
    
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
    let foregroundRgb(r, g, b) = $"{CSI}38;2;{r};{g};{b}m"
    let backgroundRgb(r, g, b) = $"{CSI}48;2;{r};{g};{b}m"
    let defaultFg = CSI + "39m"
    let defaultBg = CSI + "49m"
    
    // OSC 8 hyperlink
    let hyperlink(url, text) = $"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\"
    
    // OSC 52 clipboard
    let setClipboard(text: string) =
        let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
        $"\x1b]52;c;{b64}\x1b\\"

module TerminalMode =
    open System.Runtime.InteropServices
    
    [<DllImport("libc", SetLastError = true)>]
    extern int tcgetattr(int fd, IntPtr termios)
    
    [<DllImport("libc", SetLastError = true)>]
    extern int tcsetattr(int fd, int actions, IntPtr termios)
    
    [<DllImport("libc", SetLastError = true)>]
    extern int ioctl(int fd, uint64 request, IntPtr arg)
    
    // Platform-specific raw mode setup
    let setupRawMode() =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            // Windows: use Console APIs
            Console.OutputEncoding <- Encoding.UTF8
            Console.InputEncoding <- Encoding.UTF8
            Console.CursorVisible <- false
        else
            // Unix: use stty or termios
            // For now, use Console APIs which work on Unix too with .NET 6+
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
            with _ -> ()
    
    let restoreMode() =
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
            with _ -> ()
    
    let getTerminalSize() =
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

    let stdinAvailable() : bool =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            try Console.KeyAvailable with _ -> false
        else
            let mutable pfd = PollFd()
            pfd.Fd <- 0
            pfd.Events <- int16 1 // POLLIN
            pfd.Revents <- int16 0
            let result = poll(&pfd, 1u, 0)
            result > 0 && (int pfd.Revents &&& 1) <> 0

    let readStdinByte() : byte option =
        let buf = Array.zeroCreate<byte> 1
        let n = read(0, buf, 1u)
        if n > 0 then Some buf.[0] else None

    let rec readStdinBytes() : byte[] =
        if stdinAvailable() then
            match readStdinByte() with
            | Some b ->
                let rest = readStdinBytes()
                Array.append [|b|] rest
            | None -> [||]
        else
            [||]
