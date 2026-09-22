/// Output safety (control characters can't reach the terminal as commands) and
/// wide-glyph cell invariants at clip edges and under partial overwrites.
module Mire.Tests.RenderSafetyTests

open Expecto
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout

let private rowText (s: Surface) (y: int) : string =
    String.concat "" [ for x in 0 .. s.Size.Width - 1 -> s.[x, y].Grapheme ]

/// Every cell whose grapheme holds a control scalar (C0, DEL, C1).
let private controlCells (s: Surface) =
    [ for y in 0 .. s.Size.Height - 1 do
          for x in 0 .. s.Size.Width - 1 do
              if Grapheme.hasControl s.[x, y].Grapheme then
                  yield (x, y) ]

/// Render runs to a string, then strip every escape sequence the *writer itself*
/// emits (CSI …final, OSC … ST) so what's left is only the cell text.
let private emitted (runs: DiffRun list) =
    use sw = new System.IO.StringWriter()
    Diff.renderToTerminal runs sw
    sw.ToString()

/// Wide-glyph pairing invariant: a lead (Width 2) is followed by a
/// continuation, and every continuation follows a lead.
let private pairsValid (s: Surface) =
    [ for y in 0 .. s.Size.Height - 1 do
          for x in 0 .. s.Size.Width - 1 do
              let c = s.[x, y]

              if c.Width = 2 && not (x + 1 < s.Size.Width && s.[x + 1, y].IsContinuation) then
                  yield $"lead at {x},{y} without continuation"

              if c.IsContinuation && not (x > 0 && s.[x - 1, y].Width = 2) then
                  yield $"orphan continuation at {x},{y}" ]

[<Tests>]
let outputSafetyTests =
    testList
        "Output safety"
        [ test "Write turns ESC into a visible stand-in, not a cell holding ESC" {
              let s = Surface(Size.Create(20, 1))
              s.Write(0, 0, "hello\u001b[2Jworld", Style.Default)
              Expect.isEmpty (controlCells s) "no cell holds a control character"
              Expect.equal (rowText s 0) "hello␛[2Jworld      " "ESC shows as ␛"
          }
          test "WriteClipped sanitizes BEL, DEL, and C1 CSI (0x9B) but keeps newlines as row breaks" {
              let s = Surface(Size.Create(6, 2))
              s.WriteClipped(Rect.Create(0, 0, 6, 2), "a\u0007b\u007f\nc\u009b2J", Style.Default)
              Expect.isEmpty (controlCells s) "no control characters survive"
              Expect.equal (rowText s 0) "a␇b␡  " "BEL/DEL become control pictures"
              Expect.equal (rowText s 1) "c�2J  " "C1 becomes U+FFFD; LF broke the line"
          }
          test "tabs expand to 8-column stops from the write origin" {
              let s = Surface(Size.Create(12, 1))
              s.Write(1, 0, "a\tb", Style.Default)
              Expect.equal (rowText s 0) " a       b  " "tab advanced to origin + 8"
          }
          test "a Text node carrying escapes renders inert through Layout" {
              let s = Surface(Size.Create(12, 1))

              let node: LayoutNode<unit> =
                  Text(Rect.Create(0, 0, 0, 0), "x\u001b]52;c;AAAA\u0007", Style.Default)

              Layout.measure (Rect.Create(0, 0, 12, 1)) node |> Layout.render s
              let runs = Diff.compute None s
              Expect.isFalse (runs |> List.exists (fun r -> Grapheme.hasControl r.Text)) "runs carry no controls"
          }
          test "the diff writer sanitizes text assigned directly to a cell" {
              let s = Surface(Size.Create(4, 1))
              s.[0, 0] <- Cell.FromString("\u001b", Style.Default)
              let out = emitted (Diff.compute None s)
              // Everything the writer emits starts with a known sequence; the cell's
              // raw ESC would appear as ESC followed by something else.
              Expect.stringContains out "␛" "cell ESC emitted as its control picture"
          }
          test "oscUri rejects terminators and control characters" {
              Expect.isNone (ANSI.oscUri "https://x\u001b\\\u001b[2J") "ESC in URL rejected"
              Expect.isNone (ANSI.oscUri "https://x\u0007") "BEL in URL rejected"
              Expect.isNone (ANSI.oscUri "https://x\u009c") "C1 ST in URL rejected"
              Expect.isNone (ANSI.oscUri "") "empty rejected"
              Expect.isNone (ANSI.oscUri (String.replicate 3000 "a")) "over-long rejected"
              Expect.equal (ANSI.oscUri "https://example.com/a?b=1") (Some "https://example.com/a?b=1") "plain URL kept"

              Expect.equal
                  (ANSI.oscUri "https://ex.com/æ b")
                  (Some "https://ex.com/%C3%A6%20b")
                  "non-ASCII percent-encoded"
          }
          test "osc8Open yields nothing for an unsafe URL; the writer renders the run unlinked" {
              Expect.equal (ANSI.osc8Open "a\u001bb") "" "unsafe link dropped"

              let out =
                  emitted
                      [ { X = 0
                          Y = 0
                          Text = "hi"
                          Style = Style.Default.WithLink("http://x\u001b[2J") } ]

              Expect.isFalse (out.Contains "\u001b]8;;http") "no OSC 8 opened for the unsafe URL"
              Expect.isFalse (out.Contains "\u001b[2J") "the smuggled clear-screen never reaches the stream"
          }
          test "kittyImage refuses a non-base64 payload" {
              Expect.equal (ANSI.kittyImage 2 2 "AAAA\u001b\\\u001b[2J") "" "payload with ESC dropped"
              Expect.isTrue ((ANSI.kittyImage 2 2 "AAAA").StartsWith "\u001b_G") "valid payload still encodes"
          } ]

[<Tests>]
let wideGlyphTests =
    testList
        "Wide-glyph invariants"
        [ test "a wide glyph whose second column is clipped becomes a blank, not half a glyph" {
              let s = Surface(Size.Create(4, 1))
              s.WriteClipped(Rect.Create(0, 0, 1, 1), "世", Style.Default)
              Expect.equal s.[0, 0].Width 1 "no wide lead inside a 1-column clip"
              Expect.equal s.[0, 0].Grapheme " " "replaced by a blank"
              Expect.isEmpty (pairsValid s) "pairs valid"
          }
          test "a wide glyph at the surface's right edge is clipped atomically" {
              let s = Surface(Size.Create(3, 1))
              s.Write(2, 0, "世", Style.Default)
              Expect.equal s.[2, 0].Width 1 "lead would spill past the edge → blank"
              Expect.isEmpty (pairsValid s) "pairs valid"
          }
          test "overwriting the right half of a wide glyph blanks its lead" {
              let s = Surface(Size.Create(4, 1))
              s.Write(0, 0, "世", Style.Default)
              s.Write(1, 0, "A", Style.Default)
              Expect.equal (rowText s 0) " A  " "lead degraded to blank; A at column 1"
              Expect.isEmpty (pairsValid s) "pairs valid"
          }
          test "overwriting the lead of a wide glyph blanks its continuation" {
              let s = Surface(Size.Create(4, 1))
              s.Write(0, 0, "世", Style.Default)
              s.Write(0, 0, "A", Style.Default)
              Expect.equal (rowText s 0) "A   " "continuation degraded to blank"
              Expect.isEmpty (pairsValid s) "pairs valid"
          }
          test "a wide glyph written over the lead of another at an odd offset repairs both" {
              let s = Surface(Size.Create(6, 1))
              s.Write(0, 0, "世界", Style.Default) // cols 0-1, 2-3
              s.Write(1, 0, "中", Style.Default) // cols 1-2: cuts both
              Expect.isEmpty (pairsValid s) "pairs valid"
              Expect.equal s.[1, 0].Grapheme "中" "new glyph placed"
              Expect.equal s.[0, 0].Grapheme " " "left pair's lead blanked"
              Expect.equal s.[3, 0].Grapheme " " "right pair's continuation blanked"
          }
          test "FillRect over half of a wide glyph repairs the other half" {
              let s = Surface(Size.Create(4, 1))
              s.Write(0, 0, "世", Style.Default)
              s.FillRect(Rect.Create(1, 0, 3, 1), Cell.FromChar('.', Style.Default))
              Expect.equal (rowText s 0) " ..." "lead blanked by the fill cutting its right half"
              Expect.isEmpty (pairsValid s) "pairs valid"
          }
          test "a wide glyph at either edge of a scroll viewport never leaves half a pair" {
              let content: LayoutNode<unit> = Text(Rect.Create(0, 0, 0, 0), "a世b", Style.Default)

              for offX in 0..3 do
                  let s = Surface(Size.Create(2, 1))

                  let node: LayoutNode<unit> =
                      Scroll(
                          Rect.Create(0, 0, 0, 0),
                          { OffsetX = offX
                            OffsetY = 0
                            ContentWidth = 0
                            ContentHeight = 0 },
                          content
                      )

                  Layout.measure (Rect.Create(0, 0, 2, 1)) node |> Layout.render s
                  Expect.isEmpty (pairsValid s) $"pairs valid at offset {offX}"
          } ]
