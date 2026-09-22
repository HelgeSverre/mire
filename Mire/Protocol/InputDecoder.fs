namespace Mire.Protocol

open System
open System.IO
open System.Text
open Mire.Core

/// A persistent, incremental input decoder: feed it raw bytes as they arrive
/// and it yields every *complete* event, retaining any incomplete suffix — a
/// split CSI/SS3 sequence, a partial UTF-8 scalar, or an unfinished bracketed
/// paste — until the rest arrives. Decoding is therefore independent of how the
/// OS happens to chunk `read()`s: splitting a valid input stream at any byte
/// boundary produces the same events as feeding it whole.
///
/// Two things can't be resolved from bytes alone, and are left to the caller's
/// clock via `Flush`:
///   • a lone `ESC` — the Escape key, or the first byte of a sequence whose tail
///     hasn't arrived? Call `Flush` once input has been idle for an escape
///     timeout (the runtime uses ~30 ms) to settle it as Escape.
///   • a bracketed paste whose end marker never arrives. Once a paste starts the
///     decoder stays in paste mode until `ESC [ 201 ~`: pasted bytes — newlines
///     included — never leak into the key channel, where they could trigger an
///     app's Enter/Submit binding. Content past `pasteCap` bytes is dropped (the
///     paste is truncated, not split). `Flush` ends a stalled paste, emitting what
///     was collected as a `Paste`, never as keys.
type InputDecoder(pasteCap: int) =
    static let pasteStart = [| 0x1Buy; 0x5Buy; 0x32uy; 0x30uy; 0x30uy; 0x7Euy |] // ESC [ 2 0 0 ~
    static let pasteEnd = [| 0x1Buy; 0x5Buy; 0x32uy; 0x30uy; 0x31uy; 0x7Euy |] // ESC [ 2 0 1 ~

    /// Longest CSI we'll wait on. A sequence that runs longer without a final
    /// byte is garbage (or hostile) and is discarded rather than buffered forever.
    static let maxCsiLength = 256

    // Undecoded bytes (an incomplete suffix from earlier feeds + the new input).
    let mutable buf: byte[] = Array.zeroCreate 256
    let mutable len = 0

    // Bracketed-paste state.
    let mutable inPaste = false
    let paste = new MemoryStream()

    let append (bytes: ReadOnlySpan<byte>) =
        if len + bytes.Length > buf.Length then
            let grown = Array.zeroCreate (max (buf.Length * 2) (len + bytes.Length))
            Buffer.BlockCopy(buf, 0, grown, 0, len)
            buf <- grown

        bytes.CopyTo(Span<byte>(buf, len, bytes.Length))
        len <- len + bytes.Length

    let addPasteBytes (offset: int) (count: int) =
        let room = int64 pasteCap - paste.Length

        if room > 0L && count > 0 then
            paste.Write(buf, offset, int (min (int64 count) room))

    let finishPaste (events: ResizeArray<InputEvent>) =
        let text = Encoding.UTF8.GetString(paste.GetBuffer(), 0, int paste.Length)
        paste.SetLength 0L
        inPaste <- false
        events.Add(Paste text)

    let isContinuation (b: byte) = b &&& 0xC0uy = 0x80uy

    /// Classify the token at `pos`: `n > 0` = a complete token of `n` bytes;
    /// `0` = incomplete (wait for more); `-n` = `n` bytes of garbage to drop.
    let tokenAt (pos: int) : int =
        let b = buf.[pos]
        let avail = len - pos

        if b = 0x1Buy then
            if avail < 2 then
                0 // lone ESC: Escape key, or the start of a sequence? Wait (Flush settles it).
            else
                match buf.[pos + 1] with
                | 0x5Buy -> // CSI: params/intermediates 0x20–0x3F, then a final 0x40–0x7E
                    let mutable i = pos + 2
                    let mutable result = Int32.MinValue

                    while result = Int32.MinValue && i < len do
                        let c = buf.[i]

                        if c >= 0x40uy && c <= 0x7Euy then
                            result <- i - pos + 1
                        elif c >= 0x20uy && c <= 0x3Fuy then
                            i <- i + 1
                        else
                            // A byte that can't occur in a CSI (another ESC, a control,
                            // a UTF-8 byte): the sequence was cut. Drop what we have.
                            result <- -(i - pos)

                    if result <> Int32.MinValue then result
                    elif avail > maxCsiLength then -avail
                    else 0
                | 0x4Fuy -> if avail < 3 then 0 else 3 // SS3: ESC O x
                | _ -> 1 // ESC + something else: the ESC stands alone (Escape key)
        elif b < 0x80uy then
            1
        elif isContinuation b then
            -1 // stray continuation byte
        else
            let need =
                if b >= 0xF8uy then 0 // invalid lead
                elif b >= 0xF0uy then 4
                elif b >= 0xE0uy then 3
                elif b >= 0xC0uy then 2
                else 0

            if need = 0 then
                -1
            else
                // Validate the continuation bytes we have; a non-continuation means
                // the scalar was malformed — drop just the lead byte.
                let mutable ok = true
                let mutable i = 1

                while ok && i < need && i < avail do
                    if not (isContinuation buf.[pos + i]) then
                        ok <- false

                    i <- i + 1

                if not ok then -1
                elif avail < need then 0
                else need

    /// Scan `buf` from the start, emitting events and consuming bytes. Stops at an
    /// incomplete token (retained) or the end of input.
    let drain (events: ResizeArray<InputEvent>) =
        let mutable pos = 0
        let mutable stop = false

        while not stop && pos < len do
            if inPaste then
                let span = ReadOnlySpan<byte>(buf, pos, len - pos)
                let endAt = span.IndexOf(ReadOnlySpan<byte>(pasteEnd))

                if endAt >= 0 then
                    addPasteBytes pos endAt
                    pos <- pos + endAt + pasteEnd.Length
                    finishPaste events
                else
                    // Keep the last few bytes: they may be the start of a split end
                    // marker. Everything before them is paste content.
                    let keep = min (len - pos) (pasteEnd.Length - 1)
                    let take = len - pos - keep
                    addPasteBytes pos take
                    pos <- pos + take
                    stop <- true
            else
                let n = tokenAt pos

                if n = 0 then
                    stop <- true
                elif n < 0 then
                    pos <- pos - n
                elif
                    n = pasteStart.Length
                    && ReadOnlySpan<byte>(buf, pos, n).SequenceEqual(ReadOnlySpan<byte>(pasteStart))
                then
                    inPaste <- true
                    paste.SetLength 0L
                    pos <- pos + n
                else
                    match InputParser.parseBytes (Array.sub buf pos n) with
                    | Some ev -> events.Add ev
                    | None -> ()

                    pos <- pos + n

        // Compact the retained suffix to the front.
        if pos > 0 then
            Buffer.BlockCopy(buf, pos, buf, 0, len - pos)
            len <- len - pos

    /// A decoder with the runtime's default 1 MiB paste cap.
    new() = InputDecoder(1 <<< 20)

    /// Feed newly-read bytes; returns the events they complete, in order.
    member _.Feed(bytes: byte[]) : InputEvent list =
        let events = ResizeArray<InputEvent>()

        if not (isNull bytes) && bytes.Length > 0 then
            append (ReadOnlySpan<byte>(bytes))
            drain events

        List.ofSeq events

    /// Settle whatever is still pending as if no more input will follow: a lone
    /// `ESC` (or an `ESC`-prefixed fragment) decodes as the keys it spells, an
    /// unfinished paste is emitted as a (possibly truncated) `Paste`, and a partial
    /// UTF-8 scalar is dropped. Call when input has been idle past the escape (or
    /// paste) timeout.
    member _.Flush() : InputEvent list =
        let events = ResizeArray<InputEvent>()

        if inPaste then
            addPasteBytes 0 len
            len <- 0
            finishPaste events
        elif len > 0 then
            // Only an incomplete token can be pending: an ESC-led fragment or a
            // partial UTF-8 scalar.
            let rest = Array.sub buf 0 len
            len <- 0

            if rest.[0] = 0x1Buy then
                // The ESC stands alone (the Escape key); whatever followed it
                // before the timeout (`[`, `O`, …) was typed after it.
                match InputParser.parseBytes [| 0x1Buy |] with
                | Some ev -> events.Add ev
                | None -> ()

                events.AddRange(InputParser.parseAll (Array.sub rest 1 (rest.Length - 1)))
        // else: a partial UTF-8 scalar that never completed — dropped.

        List.ofSeq events

    /// True if bytes are buffered awaiting completion (an unsettled `ESC`, a
    /// split sequence, or a paste in progress) — the caller should `Flush` once
    /// input has been idle long enough.
    member _.HasPending = inPaste || len > 0

    /// True while inside a bracketed paste (between `ESC[200~` and `ESC[201~`).
    member _.InPaste = inPaste

    /// Bytes collected for the paste in progress (bounded by `pasteCap`).
    member _.PasteLength = if inPaste then int paste.Length else 0

    interface IDisposable with
        member _.Dispose() = paste.Dispose()
