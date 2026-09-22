/// The incremental input decoder: event decoding must not depend on where the
/// OS splits `read()`s.
module Mire.Tests.InputDecoderTests

open Expecto
open Mire.Core
open Mire.Protocol

let private utf8 (s: string) = System.Text.Encoding.UTF8.GetBytes s

/// Decode `bytes` fed as the given chunks, then flush (as the runtime would once
/// input goes idle).
let private decodeChunks (chunks: byte[] list) =
    use d = new InputDecoder()

    let evs =
        [ for c in chunks do
              yield! d.Feed c ]

    evs @ d.Flush()

let private decodeWhole (bytes: byte[]) = decodeChunks [ bytes ]

/// Every way to split `bytes` into two reads.
let private allTwoWaySplits (bytes: byte[]) =
    [ for i in 1 .. bytes.Length - 1 -> [ bytes.[.. i - 1]; bytes.[i..] ] ]

let private keyOf (e: InputEvent) =
    match e with
    | Key k -> Some k.Key
    | _ -> None

/// A mixed stream: typed keys, an arrow, a mouse press, UTF-8 (2-, 3-, 4-byte),
/// a Kitty CSI u chord, and a bracketed paste with an embedded newline.
let private fixture =
    Array.concat
        [ utf8 "ab"
          utf8 "\u001b[A"
          utf8 "\u001b[<0;6;4M"
          utf8 "é世😀"
          utf8 "\u001b[99;5u"
          utf8 "x\u001b[200~hello\nworld é\u001b[201~"
          utf8 "\u001bOB"
          utf8 "z" ]

[<Tests>]
let inputDecoderTests =
    testList
        "InputDecoder"
        [ test "the whole fixture decodes to the expected event sequence" {
              let evs = decodeWhole fixture

              Expect.equal
                  evs
                  (InputParser.parseAll (utf8 "ab\u001b[A\u001b[<0;6;4M")
                   @ [ for s in [ "é"; "世"; "😀" ] ->
                           Key
                               { Key = Char s
                                 Text = Some s
                                 Modifiers = KeyModifiers.None
                                 Repeat = false
                                 EventType = Press } ]
                   @ InputParser.parseAll (utf8 "\u001b[99;5ux")
                   @ [ Paste "hello\nworld é" ]
                   @ InputParser.parseAll (utf8 "\u001bOBz"))
                  "one feed decodes every event"
          }
          test "splitting the fixture at every byte boundary yields identical events" {
              let expected = decodeWhole fixture

              for split in allTwoWaySplits fixture do
                  Expect.equal (decodeChunks split) expected $"split at {split.Head.Length}"
          }
          test "feeding the fixture one byte at a time yields identical events" {
              let expected = decodeWhole fixture
              Expect.equal (decodeChunks [ for b in fixture -> [| b |] ]) expected "byte-at-a-time"
          }
          test "ESC [ then A in a later read is ArrowUp, not Escape + literal A" {
              use d = new InputDecoder()
              let first = d.Feed(utf8 "\u001b[")
              Expect.isEmpty first "nothing emitted for a partial CSI"
              Expect.isTrue d.HasPending "partial CSI retained"
              Expect.equal (d.Feed(utf8 "A") |> List.map keyOf) [ Some ArrowUp ] "completed as ArrowUp"
          }
          test "UTF-8 é split as C3 | A9 decodes as one scalar" {
              use d = new InputDecoder()
              Expect.isEmpty (d.Feed [| 0xC3uy |]) "lead byte alone waits"
              Expect.equal (d.Feed [| 0xA9uy |] |> List.map keyOf) [ Some(Char "é") ] "one Char é"
          }
          test "a paste that starts after another key in the same read is still a paste" {
              let evs =
                  decodeChunks [ utf8 "x\u001b[200~hello"; utf8 " world\r\nsubmit?\u001b[201~" ]

              Expect.equal
                  evs
                  (InputParser.parseAll (utf8 "x") @ [ Paste "hello world\r\nsubmit?" ])
                  "x, then one Paste"

              Expect.isFalse
                  (evs |> List.exists (fun e -> keyOf e = Some Enter))
                  "the pasted newline never became an Enter key"
          }
          test "a split opening marker still starts a paste" {
              let evs = decodeChunks [ utf8 "\u001b[20"; utf8 "0~a\nb\u001b[201~" ]
              Expect.equal evs [ Paste "a\nb" ] "marker reassembled"
          }
          test "a split closing marker still ends the paste" {
              let evs = decodeChunks [ utf8 "\u001b[200~ab\u001b[2"; utf8 "01~c" ]
              Expect.equal evs [ Paste "ab"; List.head (InputParser.parseAll (utf8 "c")) ] "paste, then c"
          }
          test "an over-cap paste is truncated but stays a paste — no tail leaks as keys" {
              use d = new InputDecoder(8)
              let evs = d.Feed(utf8 "\u001b[200~0123456789\nabcdef\u001b[201~q")

              Expect.equal
                  evs
                  [ Paste "01234567"; List.head (InputParser.parseAll (utf8 "q")) ]
                  "truncated paste, then q"
          }
          test "a paste with no end marker stays a paste until Flush, which emits it as Paste" {
              use d = new InputDecoder()
              Expect.isEmpty (d.Feed(utf8 "\u001b[200~line1\nline2")) "nothing emitted while in paste"
              Expect.isTrue d.InPaste "in paste"
              Expect.equal (d.Flush()) [ Paste "line1\nline2" ] "flushed as a paste, never keys"
              Expect.isFalse d.InPaste "paste ended"
          }
          test "a lone ESC waits, then settles as Escape on Flush" {
              use d = new InputDecoder()
              Expect.isEmpty (d.Feed [| 0x1Buy |]) "lone ESC retained"
              Expect.equal (d.Flush() |> List.map keyOf) [ Some Escape ] "flush → Escape"
          }
          test "ESC followed by a non-sequence byte is Escape then that key" {
              Expect.equal (decodeWhole (utf8 "\u001bq") |> List.map keyOf) [ Some Escape; Some(Char "q") ] "Esc, q"
          }
          test "a timed-out partial CSI flushes as Escape plus the typed bytes" {
              Expect.equal (decodeChunks [ utf8 "\u001b[" ] |> List.map keyOf) [ Some Escape; Some(Char "[") ] "Esc, ["
          }
          test "stray continuation bytes and a malformed lead are dropped, not decoded" {
              Expect.equal
                  (decodeWhole [| 0xA9uy; 0xC3uy; 0x41uy |] |> List.map keyOf)
                  [ Some(Char "A") ]
                  "only A survives"
          }
          test "a runaway CSI is discarded rather than buffered forever" {
              use d = new InputDecoder()
              let junk = Array.append (utf8 "\u001b[") (Array.create 300 (byte '1'))
              d.Feed junk |> ignore
              Expect.isFalse d.HasPending "over-long CSI dropped"
              Expect.equal (d.Feed(utf8 "a") |> List.map keyOf) [ Some(Char "a") ] "decoding resumes"
          }
          test "decoding a large burst is linear, not quadratic (1 MiB of keys)" {
              let big = Array.create (1 <<< 20) (byte 'a')
              let sw = System.Diagnostics.Stopwatch.StartNew()
              use d = new InputDecoder()
              let n = d.Feed big |> List.length
              Expect.equal n (1 <<< 20) "one event per byte"
              Expect.isLessThan sw.Elapsed.TotalSeconds 10.0 "decodes in reasonable time"
          }
          test "parseAll on a long buffer no longer copies suffixes (1 MiB)" {
              let big = Array.create (1 <<< 20) (byte 'a')
              let sw = System.Diagnostics.Stopwatch.StartNew()
              let n = InputParser.parseAll big |> List.length
              Expect.equal n (1 <<< 20) "one event per byte"
              Expect.isLessThan sw.Elapsed.TotalSeconds 10.0 "parses in reasonable time"
          } ]
