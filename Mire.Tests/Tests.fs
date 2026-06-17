module Mire.Tests.Tests

open Expecto
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.Demo.Feed

// Helpers ------------------------------------------------------------------

/// Extract the KeyEvent from a parsed InputEvent, if any.
let private asKey (e: InputEvent option) : KeyEvent option =
    match e with
    | Some(Key k) -> Some k
    | _ -> None

/// Read a surface row as a string (concatenated graphemes).
let private rowText (s: Surface) (y: int) : string =
    String.concat "" [ for x in 0 .. s.Size.Width - 1 -> s.[x, y].Grapheme ]

// Grapheme width -----------------------------------------------------------

let graphemeTests =
    testList
        "Grapheme"
        [ test "ASCII is one cell" { Expect.equal (Grapheme.charWidth 'A') 1 "ASCII char is width 1" }
          test "CJK is two cells" { Expect.equal (Grapheme.charWidth '世') 2 "CJK ideograph is width 2" }
          test "combining mark is zero cells" { Expect.equal (Grapheme.charWidth '́') 0 "combining acute is width 0" }
          test "stringWidth sums mixed widths" { Expect.equal (Grapheme.stringWidth "a世b") 4 "1 + 2 + 1 = 4" }
          test "empty string is width 0" { Expect.equal (Grapheme.stringWidth "") 0 "empty string is width 0" } ]

// Input parsing ------------------------------------------------------------

let inputTests =
    testList
        "InputParser"
        [ test "printable ASCII" {
              let k = asKey (InputParser.parseBytes [| 0x41uy |])
              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some(Char "A")) "0x41 → Char \"A\""
          }
          test "spacebar decodes to Space with text" {
              let k = asKey (InputParser.parseBytes [| 0x20uy |])
              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some Space) "0x20 → Space (not Char \" \")"
              Expect.equal (k |> Option.bind (fun k -> k.Text)) (Some " ") "Space still carries its text \" \""
          }
          test "Enter" {
              let k = asKey (InputParser.parseBytes [| 0x0Duy |])
              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some Enter) "CR → Enter"
          }
          test "arrow up escape sequence" {
              let k = asKey (InputParser.parseBytes [| 0x1Buy; 0x5Buy; 0x41uy |])
              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some ArrowUp) "ESC [ A → ArrowUp"
          }
          test "Shift+Tab backtab" {
              let k = asKey (InputParser.parseBytes [| 0x1Buy; 0x5Buy; 0x5Auy |])
              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some Tab) "ESC [ Z → Tab"
              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Shift)) "…with Shift modifier"
          }
          test "Ctrl+C" {
              let k = asKey (InputParser.parseBytes [| 0x03uy |])
              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Ctrl)) "0x03 carries Ctrl"
          }
          test "empty input is None" { Expect.isNone (InputParser.parseBytes [||]) "no bytes → no event" }
          // Kitty keyboard protocol (CSI u) — what Ghostty/Kitty actually send for
          // modified keys when the runtime enables `CSI > 1 u`.
          test "Kitty CSI u: Ctrl+P → Char p + Ctrl" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[112;5u"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some(Char "p")) "ESC[112;5u → Char \"p\""
              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Ctrl)) "carries Ctrl"
              Expect.isFalse (k |> Option.exists (fun k -> k.Modifiers.Shift || k.Modifiers.Alt)) "Ctrl only"
          }
          test "Kitty CSI u: Ctrl+O → Char o + Ctrl" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[111;5u"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some(Char "o")) "ESC[111;5u → Char \"o\""
              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Ctrl)) "carries Ctrl"
          }
          test "Kitty CSI u: super/Cmd (mod 9) → Meta" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[112;9u"))

              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Meta)) "modifier 9 → Meta"
              Expect.isFalse (k |> Option.exists (fun k -> k.Modifiers.Ctrl)) "not Ctrl"
          }
          test "Kitty CSI u: bare Escape (ESC[27u)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[27u"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some Escape) "ESC[27u → Escape"
          }
          test "Kitty CSI u: Shift+Tab (ESC[9;2u)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[9;2u"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some Tab) "ESC[9;2u → Tab"
              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Shift)) "carries Shift"
          }
          test "modified arrow: Ctrl+Up (ESC[1;5A)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[1;5A"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some ArrowUp) "ESC[1;5A → ArrowUp"
              Expect.isTrue (k |> Option.exists (fun k -> k.Modifiers.Ctrl)) "carries Ctrl"
          }
          test "plain arrow still decodes (ESC[A, no mods)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[A"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some ArrowUp) "ESC[A → ArrowUp"
              Expect.isFalse (k |> Option.exists (fun k -> k.Modifiers.Ctrl || k.Modifiers.Shift)) "no modifiers"
          }
          // Application-cursor-key mode (DECCKM) — what JediTerm sends for arrows.
          test "SS3 arrow: ESC O A → ArrowUp" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1bOA"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some ArrowUp) "ESC O A → ArrowUp"
          }
          test "SS3 arrow: ESC O B → ArrowDown" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1bOB"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some ArrowDown) "ESC O B → ArrowDown"
          }
          test "SS3 F1 still decodes (ESC O P)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1bOP"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some(Function 1)) "ESC O P → F1"
          }
          // SGR 1006 mouse — ESC [ < b ; x ; y M|m (1-based coords → 0-based).
          test "SGR mouse: left press reports 0-based coords" {
              match InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[<0;6;4M") with
              | Some(Mouse m) ->
                  Expect.equal (m.X, m.Y) (5, 3) "col6/row4 (1-based) → (5,3) 0-based"
                  Expect.equal m.Button MouseButton.Left "button 0 → Left"
                  Expect.isTrue m.Pressed "M → press"
              | other -> failtestf "expected Mouse, got %A" other
          }
          test "SGR mouse: 'm' final is a release" {
              Expect.isTrue
                  (match InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[<0;6;4m") with
                   | Some(Mouse m) -> not m.Pressed
                   | _ -> false)
                  "lowercase m → release"
          }
          test "SGR mouse: wheel up (b=64) → ScrollUp" {
              Expect.equal
                  (match InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[<64;1;1M") with
                   | Some(Mouse m) -> Some m.Button
                   | _ -> None)
                  (Some ScrollUp)
                  "0x40 bit + low bit 0 → ScrollUp"
          }
          test "SGR mouse: ctrl modifier (b=16)" {
              Expect.isTrue
                  (match InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[<16;2;2M") with
                   | Some(Mouse m) -> m.Modifiers.Ctrl
                   | _ -> false)
                  "0x10 bit → Ctrl"
          }
          test "bracketed paste extracts the text between the markers" {
              Expect.equal
                  (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[200~hello world\x1b[201~"))
                  (Some(Paste "hello world"))
                  "ESC[200~ … ESC[201~ → Paste of the inner text"
          }
          test "focus in / focus out" {
              Expect.equal
                  (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[I"))
                  (Some FocusGained)
                  "ESC[I → FocusGained"

              Expect.equal
                  (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[O"))
                  (Some FocusLost)
                  "ESC[O → FocusLost"
          }
          // Kitty event types (CSI u `mod:event` subparam) — enabled by `CSI > 3 u`.
          test "Kitty CSI u: release event (ESC[97;5:3u)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[97;5:3u"))

              Expect.equal (k |> Option.map (fun k -> k.Key)) (Some(Char "a")) "key is 'a'"
              Expect.equal (k |> Option.map (fun k -> k.EventType)) (Some Release) "subparam :3 → Release"
          }
          test "Kitty CSI u: repeat event (ESC[97;1:2u)" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[97;1:2u"))

              Expect.equal (k |> Option.map (fun k -> k.EventType)) (Some Repeat) "subparam :2 → Repeat"
              Expect.isTrue (k |> Option.exists (fun k -> k.Repeat)) "Repeat event sets the Repeat flag"
          }
          test "Kitty CSI u: absent event subparam defaults to Press" {
              let k =
                  asKey (InputParser.parseBytes (System.Text.Encoding.ASCII.GetBytes "\x1b[112;5u"))

              Expect.equal (k |> Option.map (fun k -> k.EventType)) (Some Press) "no `:event` → Press"
          }
          // Bracketed-paste reassembly across reads (runtime carry buffer).
          test "stepPasteBuffer carries an unfinished paste, then flushes on completion" {
              let part1 = System.Text.Encoding.ASCII.GetBytes "\x1b[200~hello "
              let part2 = System.Text.Encoding.ASCII.GetBytes "world\x1b[201~"
              let toParse1, carry1 = InputParser.stepPasteBuffer (1 <<< 20) [||] part1
              Expect.isEmpty (List.ofArray toParse1) "incomplete paste buffers; nothing to parse yet"
              let toParse2, carry2 = InputParser.stepPasteBuffer (1 <<< 20) carry1 part2
              Expect.isEmpty (List.ofArray carry2) "carry cleared once the end marker arrives"

              Expect.equal
                  (InputParser.parseBytes toParse2)
                  (Some(Paste "hello world"))
                  "the two reads reassemble into one Paste"
          }
          test "stepPasteBuffer flushes ordinary input immediately" {
              let bytes = System.Text.Encoding.ASCII.GetBytes "abc"
              let toParse, carry = InputParser.stepPasteBuffer (1 <<< 20) [||] bytes
              Expect.equal (List.ofArray toParse) (List.ofArray bytes) "non-paste bytes pass straight through"
              Expect.isEmpty (List.ofArray carry) "nothing carried"
          }
          test "stepPasteBuffer flushes at the cap (lost end marker can't grow unbounded)" {
              let big =
                  Array.append (System.Text.Encoding.ASCII.GetBytes "\x1b[200~") (Array.zeroCreate 50)

              let toParse, carry = InputParser.stepPasteBuffer 8 [||] big
              Expect.isNonEmpty (List.ofArray toParse) "an over-cap incomplete paste is flushed, not buffered"
              Expect.isEmpty (List.ofArray carry) "carry cleared at the cap"
          } ]

// Frame diffing ------------------------------------------------------------

let diffTests =
    testList
        "Diff"
        [ test "identical surfaces produce no runs" {
              let a = Surface(Size.Create(4, 1))
              let b = Surface(Size.Create(4, 1))
              Expect.isEmpty (Diff.compute (Some a) b) "no changes → empty diff"
          }
          test "single changed cell yields one run" {
              let prev = Surface(Size.Create(4, 1))
              let next = Surface(Size.Create(4, 1))
              next.Write(0, 0, "A", Style.Default)
              let runs = Diff.compute (Some prev) next
              Expect.equal (List.length runs) 1 "one changed run"
              let r = List.head runs
              Expect.equal (r.X, r.Y, r.Text) (0, 0, "A") "run is 'A' at (0,0)"
          }
          test "full render (no previous) emits the written run" {
              let next = Surface(Size.Create(4, 1))
              next.Write(1, 0, "Hi", Style.Default)
              let runs = Diff.compute None next
              Expect.equal (List.length runs) 1 "one run on first paint"
              Expect.equal (List.head runs).Text "Hi" "run text is 'Hi'"
          }
          test "renderToTerminal brackets the frame in synchronized output" {
              use sw = new System.IO.StringWriter()

              Diff.renderToTerminal
                  [ { X = 0
                      Y = 0
                      Text = "A"
                      Style = Style.Default } ]
                  sw

              let out = sw.ToString()
              Expect.stringContains out ANSI.beginSync "frame opens with BSU (mode 2026)"
              Expect.stringContains out ANSI.endSync "frame closes with ESU (mode 2026)"
              Expect.isLessThan (out.IndexOf ANSI.beginSync) (out.IndexOf ANSI.endSync) "BSU precedes ESU"
          }
          test "renderToTerminal brackets a linked run in OSC 8 (open before text, close after)" {
              use sw = new System.IO.StringWriter()
              let url = "https://example.com"

              Diff.renderToTerminal
                  [ { X = 0
                      Y = 0
                      Text = "link"
                      Style = Style.Default.WithLink url } ]
                  sw

              let out = sw.ToString()
              Expect.stringContains out (ANSI.osc8Open url) "opens OSC 8 with the URL"
              Expect.stringContains out ANSI.osc8Close "closes OSC 8 before the frame ends"
              Expect.isLessThan (out.IndexOf(ANSI.osc8Open url)) (out.IndexOf "link") "link opens before its text"
              Expect.isLessThan (out.IndexOf "link") (out.IndexOf ANSI.osc8Close) "link closes after its text"
          }
          test "renderToTerminal emits no OSC 8 for an unlinked run" {
              use sw = new System.IO.StringWriter()

              Diff.renderToTerminal
                  [ { X = 0
                      Y = 0
                      Text = "plain"
                      Style = Style.Default } ]
                  sw

              Expect.isFalse ((sw.ToString()).Contains ANSI.osc8Close) "no link → no OSC 8 sequence"
          }
          test "cursor advances by display width across a wide-grapheme run" {
              // After a width-2 run at x=0, a contiguous run starting at x=2 is
              // already where the cursor sits, so no cursorTo should be emitted
              // for it. If currentX advanced by char count (1) instead of width
              // (2), a redundant cursorTo would appear before the second run.
              use sw = new System.IO.StringWriter()

              Diff.renderToTerminal
                  [ { X = 0
                      Y = 0
                      Text = "世"
                      Style = Style.Default }
                    { X = 2
                      Y = 0
                      Text = "x"
                      Style = Style.Default } ]
                  sw

              let out = sw.ToString()
              // Exactly one cursor-positioning sequence (the initial move to 0,0).
              let moves =
                  out.Split([| ANSI.cursorTo (0, 0) |], System.StringSplitOptions.None).Length - 1

              Expect.equal moves 1 "only the initial cursorTo is emitted; the wide run advances X by 2"
              Expect.isFalse (out.Contains(ANSI.cursorTo (2, 0))) "no redundant move before the second run"
          }
          // Characterization of the resize-repaint premise: diffing against a
          // smaller previous surface misses the newly-exposed region, whereas the
          // no-previous path (which the runtime now selects on resize by clearing
          // PreviousSurface) emits all of it. The runtime branch itself needs a tty
          // to exercise; these pin the Diff.compute behaviour that motivates it.
          test "growing: overlap-diff leaves the newly-exposed column unpainted" {
              let prev = Surface(Size.Create(1, 1))
              let next = Surface(Size.Create(2, 1))
              next.Write(0, 0, "A", Style.Default)
              next.Write(1, 0, "B", Style.Default)

              let overlapText =
                  Diff.compute (Some prev) next |> List.map (fun r -> r.Text) |> String.concat ""

              Expect.isFalse (overlapText.Contains "B") "overlap-diff misses the newly-exposed column (the bug)"

              let fullText =
                  Diff.compute None next |> List.map (fun r -> r.Text) |> String.concat ""

              Expect.stringContains fullText "A" "full repaint emits the original column"
              Expect.stringContains fullText "B" "full repaint emits the newly-exposed column"
          }
          test "growing: overlap-diff leaves the newly-exposed row unpainted" {
              let prev = Surface(Size.Create(1, 1))
              let next = Surface(Size.Create(1, 2))
              next.Write(0, 0, "A", Style.Default)
              next.Write(0, 1, "B", Style.Default)

              Expect.isFalse
                  (Diff.compute (Some prev) next |> List.exists (fun r -> r.Y = 1))
                  "overlap-diff produces no run for the newly-exposed row (the bug)"

              Expect.isTrue
                  (Diff.compute None next |> List.exists (fun r -> r.Y = 1 && r.Text = "B"))
                  "full repaint emits a run for the newly-exposed row"
          }
          // Wide-glyph trailing cell. A wide glyph occupies two columns; the
          // second is a *continuation* cell (empty grapheme, width 0) distinct
          // from a blank — so when a later frame replaces the wide glyph with
          // narrow content, the diff sees the continuation→blank change and
          // repaints the second column, clearing the wide glyph's right half.
          test "Surface.Write: a wide glyph sets width 2 and a continuation follows" {
              let s = Surface(Size.Create(4, 1))
              s.Write(0, 0, "世", Style.Default)
              Expect.equal s.[0, 0].Width 2 "the wide glyph cell records display width 2"
              Expect.equal s.[0, 0].Grapheme "世" "the glyph lands at its column"
              Expect.equal s.[1, 0].Grapheme "" "the trailing column is a continuation (empty grapheme)"
              Expect.isFalse s.[1, 0].IsEmpty "the continuation is distinct from a blank cell"
          }
          test "narrow content over a previous wide glyph clears the trailing ghost" {
              let prev = Surface(Size.Create(4, 1))
              prev.Write(0, 0, "世", Style.Default) // frame 1: wide glyph spans cols 0-1
              let next = Surface(Size.Create(4, 1)) // frame 2: a fresh surface (the runtime rebuilds each frame)
              next.Write(0, 0, "a", Style.Default) // narrow glyph at col 0 only

              let text =
                  Diff.compute (Some prev) next
                  |> List.filter (fun r -> r.Y = 0)
                  |> List.sortBy (fun r -> r.X)
                  |> List.map (fun r -> r.Text)
                  |> String.concat ""

              Expect.stringContains text "a " "col 0 repaints 'a' and col 1 repaints a blank, clearing the ghost"
          }
          test "full render of a wide glyph emits one run (continuation absorbed, no stray)" {
              let s = Surface(Size.Create(4, 1))
              s.Write(0, 0, "世", Style.Default)
              let runs = Diff.compute None s
              Expect.equal (List.length runs) 1 "the wide glyph + its continuation form a single run"
              Expect.equal (List.head runs).Text "世" "the run carries just the glyph (continuation appends nothing)"
          } ]

// Layout -------------------------------------------------------------------

let private stackChild len child : StackChild<unit> = { Length = len; Child = child }

let layoutTests =
    testList
        "Layout"
        [ test "vertical stack flows children sequentially" {
              let node: LayoutNode<unit> =
                  LayoutNode.Stack(
                      Rect.Create(0, 0, 0, 0),
                      Vertical,
                      [ stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), "a", Style.Default))
                        stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), "b", Style.Default)) ]
                  )

              let rects =
                  match Layout.measure (Rect.Create(0, 0, 10, 4)) node with
                  | LayoutNode.Stack(_, _, kids) ->
                      kids
                      |> List.choose (fun c ->
                          match c.Child with
                          | LayoutNode.Text(r, _, _) -> Some r
                          | _ -> None)
                  | _ -> []

              Expect.equal (List.length rects) 2 "two child rects"
              Expect.equal (rects.[0].Y, rects.[0].Height) (0, 1) "first child at y=0 height 1"
              Expect.equal (rects.[1].Y, rects.[1].Height) (1, 1) "second child at y=1 height 1"
              Expect.equal rects.[0].Width 10 "child spans the stack width"
          }
          test "dock Top(Content) sizes to the child's line count" {
              let node: LayoutNode<unit> =
                  LayoutNode.Dock(
                      Rect.Create(0, 0, 0, 0),
                      [ { Position = Top Content
                          Child = LayoutNode.Text(Rect.Create(0, 0, 0, 0), "x\ny", Style.Default) }
                        { Position = DockPosition.Fill
                          Child = LayoutNode.Empty } ]
                  )

              let h =
                  match Layout.measure (Rect.Create(0, 0, 10, 5)) node with
                  | LayoutNode.Dock(_, kids) ->
                      match (List.head kids).Child with
                      | LayoutNode.Text(r, _, _) -> r.Height
                      | _ -> -1
                  | _ -> -1

              Expect.equal h 2 "two-line text → 2 rows"
          }
          test "two Fill children split the remaining rows" {
              let node: LayoutNode<unit> =
                  LayoutNode.Stack(
                      Rect.Create(0, 0, 0, 0),
                      Vertical,
                      [ stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), "top", Style.Default))
                        stackChild Length.Fill LayoutNode.Empty
                        stackChild Length.Fill LayoutNode.Empty ]
                  )
              // Measure indirectly by checking the y-positions the children land at.
              let ys =
                  match Layout.measure (Rect.Create(0, 0, 10, 9)) node with
                  | LayoutNode.Stack(_, _, kids) ->
                      kids
                      |> List.map (fun c ->
                          match c.Child with
                          | LayoutNode.Text(r, _, _) -> r.Y
                          | LayoutNode.Empty -> -1
                          | _ -> -2)
                  | _ -> []
              // 9 rows: 1 fixed + (8 split → 4 + 4). First child at y=0.
              Expect.equal ys.Head 0 "fixed child at top"
          }
          test "scroll offset windows the content (offset 1 hides first row)" {
              let content: LayoutNode<unit> =
                  LayoutNode.Stack(
                      Rect.Create(0, 0, 0, 0),
                      Vertical,
                      [ for i in 1..5 ->
                            stackChild
                                (Cells 1)
                                (LayoutNode.Text(Rect.Create(0, 0, 0, 0), sprintf "r%d" i, Style.Default)) ]
                  )

              let node: LayoutNode<unit> =
                  LayoutNode.Scroll(Rect.Create(0, 0, 0, 0), { ScrollState.Empty with OffsetY = 1 }, content)

              let laid = Layout.measure (Rect.Create(0, 0, 6, 2)) node
              let surf = Surface(Size.Create(6, 2))
              Layout.render surf laid
              Expect.isTrue ((rowText surf 0).StartsWith "r2") "offset 1 → first visible row is r2"
              Expect.isTrue ((rowText surf 1).StartsWith "r3") "second visible row is r3"
          } ]

// Positioned (overlay placement) -------------------------------------------

let positionedTests =
    let avail = Rect.Create(0, 0, 20, 10)

    // Measure a Positioned node and return the rect its child was assigned.
    let placedRect (p: Placement) (w: Length) (h: Length) (child: LayoutNode<unit>) : Rect =
        match Layout.measure avail (LayoutNode.Positioned(Rect.Create(0, 0, 0, 0), p, w, h, child)) with
        | LayoutNode.Positioned(_, _, _, _, c) ->
            match c with
            | LayoutNode.Filled(r, _) -> r
            | LayoutNode.Text(r, _, _) -> r
            | _ -> Rect.Create(0, 0, 0, 0)
        | _ -> Rect.Create(0, 0, 0, 0)

    let box: LayoutNode<unit> =
        LayoutNode.Filled(Rect.Create(0, 0, 0, 0), Style.Default)

    let tup (r: Rect) = (r.X, r.Y, r.Width, r.Height)

    testList
        "Positioned"
        [ test "Center centers a fixed-size child" {
              Expect.equal (tup (placedRect Center (Cells 6) (Cells 4) box)) (7, 3, 6, 4) "centered in 20×10"
          }
          test "TopLeft sits at the origin" {
              Expect.equal (tup (placedRect TopLeft (Cells 6) (Cells 4) box)) (0, 0, 6, 4) "origin"
          }
          test "TopRight pins to the top-right corner" {
              Expect.equal (tup (placedRect TopRight (Cells 6) (Cells 4) box)) (14, 0, 6, 4) "x=20-6, y=0"
          }
          test "BottomRight pins to the bottom-right corner" {
              Expect.equal (tup (placedRect BottomRight (Cells 6) (Cells 4) box)) (14, 6, 6, 4) "x=14, y=10-4"
          }
          test "BottomCenter pins to the bottom edge, centered horizontally" {
              Expect.equal (tup (placedRect BottomCenter (Cells 6) (Cells 4) box)) (7, 6, 6, 4) "x=7, y=6"
          }
          test "CenterLeft pins to the left edge, centered vertically" {
              Expect.equal (tup (placedRect CenterLeft (Cells 6) (Cells 4) box)) (0, 3, 6, 4) "x=0, y=3"
          }
          test "Content sizes the child to its intrinsic extent, then places it" {
              let txt = LayoutNode.Text(Rect.Create(0, 0, 0, 0), "hello", Style.Default) // 5×1

              Expect.equal
                  (tup (placedRect Center Length.Content Length.Content txt))
                  (7, 4, 5, 1)
                  "5 wide, 1 tall, centered"
          }
          test "oversized child clamps to the area without a negative origin" {
              Expect.equal
                  (tup (placedRect Center (Cells 100) (Cells 100) box))
                  (0, 0, 20, 10)
                  "clamped to 20×10 at origin"
          }
          test "render draws a centered Filled in the middle, not the corner" {
              let bg = Color.Rgb(0x40uy, 0x40uy, 0x40uy)
              let style = Style.Default.WithBackground bg

              let node: LayoutNode<unit> =
                  LayoutNode.Positioned(
                      Rect.Create(0, 0, 0, 0),
                      Center,
                      Cells 2,
                      Cells 2,
                      LayoutNode.Filled(Rect.Create(0, 0, 0, 0), style)
                  )

              let surf = Surface(Size.Create(6, 4))
              Layout.measure (Rect.Create(0, 0, 6, 4)) node |> Layout.render surf
              // child rect = (2,1,2,2)
              Expect.equal surf.[2, 1].Style.Background (Some bg) "centered cell carries the fill"
              Expect.isTrue surf.[0, 0].Style.Background.IsNone "corner cell left unfilled"
          } ]

// Widgets ------------------------------------------------------------------

let widgetTests =
    testList
        "Widgets"
        [ test "Backdrop.behind fills the full row background, not just under glyphs" {
              let selBg = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
              let sel = Style.Default.WithForeground(Color.Black).WithBackground(selBg)

              let node: LayoutNode<unit> =
                  Mire.Widgets.Backdrop.behind sel (Mire.Widgets.Text.text "hi" sel)

              let laid = Layout.measure (Rect.Create(0, 0, 8, 1)) node
              let surf = Surface(Size.Create(8, 1))
              Layout.render surf laid
              Expect.equal surf.[0, 0].Style.Background (Some selBg) "glyph cell carries the selection bg"
              Expect.equal surf.[5, 0].Style.Background (Some selBg) "cell past the text is filled too (full-bleed)"
          }
          test "ListView highlights the selected row full-width, others not" {
              let selBg = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
              let sel = Style.Default.WithForeground(Color.Black).WithBackground(selBg)
              let row = Style.Default.WithForeground(Color.White)

              let node: LayoutNode<unit> =
                  Mire.Widgets.ListView.view 3 sel row 1 [ "alpha"; "beta"; "gamma" ]

              let surf = Surface(Size.Create(10, 3))
              Layout.measure (Rect.Create(0, 0, 10, 3)) node |> Layout.render surf
              Expect.equal surf.[8, 1].Style.Background (Some selBg) "selected row (beta) filled full width"
              Expect.notEqual surf.[8, 0].Style.Background (Some selBg) "unselected row (alpha) left unfilled"
          }
          test "Modal fills the backdrop and centers its box over the base tree" {
              let backdropBg = Color.Rgb(0x06uy, 0x08uy, 0x0Buy)
              let backdrop = Style.Default.WithBackground backdropBg
              let border = Style.Default.WithForeground Color.White
              let titleStyle = Style.Default.WithForeground Color.White

              let body: LayoutNode<unit> = Mire.Widgets.Text.text "body" Style.Default
              // 10×5 box centered in 20×10 → top-left border at (5,2)
              let m = Mire.Widgets.Modal.modal backdrop border titleStyle 10 5 "Hi" body

              let surf = Surface(Size.Create(20, 10))
              Layout.measure (Rect.Create(0, 0, 20, 10)) m |> Layout.render surf

              Expect.equal
                  surf.[0, 0].Style.Background
                  (Some backdropBg)
                  "backdrop fills the corner (occludes the base)"

              Expect.equal surf.[19, 9].Style.Background (Some backdropBg) "backdrop fills the far corner too"

              Expect.isFalse
                  (System.String.IsNullOrEmpty surf.[5, 2].Grapheme)
                  "box border drawn at the centered top-left (5,2)"

              Expect.notEqual surf.[5, 2].Grapheme " " "centered corner is a border glyph, not blank"
          }
          test "flexSpacer in a vstack pushes the last child to the bottom" {
              let a: LayoutNode<unit> = Mire.Widgets.Text.text "top" Style.Default
              let b: LayoutNode<unit> = Mire.Widgets.Text.text "bot" Style.Default

              let node: LayoutNode<unit> =
                  Mire.Widgets.Stack.vstackOf
                      [ Mire.Widgets.Stack.sized Length.Content a
                        Mire.Widgets.Spacer.flexSpacer
                        Mire.Widgets.Stack.sized Length.Content b ]

              let ys =
                  match Layout.measure (Rect.Create(0, 0, 6, 10)) node with
                  | LayoutNode.Stack(_, _, kids) ->
                      kids
                      |> List.choose (fun c ->
                          match c.Child with
                          | LayoutNode.Text(r, _, _) -> Some r.Y
                          | _ -> None)
                  | _ -> []

              Expect.equal ys [ 0; 9 ] "first child at the top (y=0), last child at the bottom row (y=9)"

              let surf = Surface(Size.Create(6, 10))
              Layout.measure (Rect.Create(0, 0, 6, 10)) node |> Layout.render surf
              Expect.isTrue ((rowText surf 0).StartsWith "top") "top text on the first row"
              Expect.isTrue ((rowText surf 9).StartsWith "bot") "bottom text pushed to the last row"
          }
          test "two flexSpacers center the middle child" {
              let mid: LayoutNode<unit> = Mire.Widgets.Text.text "m" Style.Default

              let node: LayoutNode<unit> =
                  Mire.Widgets.Stack.vstackOf
                      [ Mire.Widgets.Stack.flex
                        Mire.Widgets.Stack.sized (Length.Cells 1) mid
                        Mire.Widgets.Stack.flex ]

              let y =
                  match Layout.measure (Rect.Create(0, 0, 4, 10)) node with
                  | LayoutNode.Stack(_, _, kids) ->
                      kids
                      |> List.tryPick (fun c ->
                          match c.Child with
                          | LayoutNode.Text(r, _, _) -> Some r.Y
                          | _ -> None)
                  | _ -> None
              // total 10, fixed 1, remaining 9 over 2 Fill slots; the remainder row
              // goes to the first Fill slot, so the first flex is 5 and mid sits at y=5.
              Expect.equal y (Some 5) "middle child centered (remainder row to the first Fill slot)"
          }
          test "statusBar lays its items horizontally, not overlapping" {
              let bar: LayoutNode<unit> =
                  Mire.Widgets.StatusBar.statusBar
                      [ Mire.Widgets.Text.text "AA" Style.Default ]
                      []
                      [ Mire.Widgets.Text.text "BB" Style.Default ]

              let surf = Surface(Size.Create(20, 3))
              Layout.measure (Rect.Create(0, 0, 20, 3)) bar |> Layout.render surf
              let row = rowText surf 1
              Expect.stringContains row "AA" "left item rendered"
              Expect.stringContains row "BB" "right item rendered side-by-side (not overwritten by overlap)"
          }
          test "panel stacks its title above its body, not overlapping" {
              let p: LayoutNode<unit> =
                  Mire.Widgets.Box.panel "Ttl" Style.Default [ Mire.Widgets.Text.text "body" Style.Default ]

              let surf = Surface(Size.Create(12, 5))
              Layout.measure (Rect.Create(0, 0, 12, 5)) p |> Layout.render surf
              Expect.stringContains (rowText surf 1) "Ttl" "title on the first inner row"
              Expect.stringContains (rowText surf 2) "body" "body stacked on the next row (not overlapping the title)"
          }
          test "Toast.stack places cards top-right, stacked and not overlapping" {
              let mk (s: string) : LayoutNode<unit> = Mire.Widgets.Text.text s Style.Default

              let node: LayoutNode<unit> =
                  Mire.Widgets.Toast.stack TopRight 10 1 [ mk "AAAAAAAA"; mk "BBBBBBBB" ]

              let surf = Surface(Size.Create(30, 8))
              Layout.measure (Rect.Create(0, 0, 30, 8)) node |> Layout.render surf
              // width 10 placed TopRight in 30 wide → x 20..29; height 2*(1+1)=4 → rows 0..3
              Expect.stringContains (rowText surf 0) "AAAAAAAA" "first card on the top row"
              Expect.stringContains (rowText surf 2) "BBBBBBBB" "second card stacked below a blank row"
              Expect.equal ((rowText surf 0).Substring(0, 20).Trim()) "" "cards are right-aligned (left region empty)"
          }
          test "ScrollView offset helpers clamp and detect the bottom" {
              Expect.equal (Mire.Widgets.ScrollView.toBottom 4 8) 4 "bottom offset = contentH - viewportH"
              Expect.equal (Mire.Widgets.ScrollView.toBottom 4 2) 0 "content fits → bottom is 0"
              Expect.equal (Mire.Widgets.ScrollView.clampOffset 4 8 10) 4 "over-scroll clamps to the bottom"
              Expect.equal (Mire.Widgets.ScrollView.clampOffset 4 8 -3) 0 "under-scroll clamps to 0"
              Expect.isTrue (Mire.Widgets.ScrollView.atBottom 4 8 4) "offset 4 is at the bottom"
              Expect.isFalse (Mire.Widgets.ScrollView.atBottom 4 8 0) "offset 0 is not at the bottom"
          }
          test "ScrollView draws a proportional scrollbar thumb at the offset" {
              let track = Style.Default.WithForeground(Color.Rgb(0x33uy, 0x33uy, 0x33uy))
              let thumb = Style.Default.WithForeground Color.White

              let barColumn (offset: int) : string list =
                  let content: LayoutNode<unit> =
                      Mire.Widgets.Stack.vstack
                          [ for i in 1..8 -> Mire.Widgets.Text.text (sprintf "row%d" i) Style.Default ]

                  let node: LayoutNode<unit> =
                      Mire.Widgets.ScrollView.vertical 4 8 offset track thumb content

                  let surf = Surface(Size.Create(10, 4))
                  Layout.measure (Rect.Create(0, 0, 10, 4)) node |> Layout.render surf
                  [ for y in 0..3 -> surf.[9, y].Grapheme ] // rightmost column = the scrollbar

              // viewport 4 / content 8 → thumbHeight = 4*4/8 = 2
              Expect.equal (barColumn 0) [ "█"; "█"; "│"; "│" ] "offset 0 → thumb at the top"
              Expect.equal (barColumn 4) [ "│"; "│"; "█"; "█" ] "offset 4 (bottom) → thumb at the bottom"
          }
          test "Table: sticky header, windowed rows, full-width selection" {
              let rows = [ "alpha"; "beta"; "gamma"; "delta"; "epsilon" ]

              let cols: Mire.Widgets.Column<string, unit> list =
                  [ Mire.Widgets.Table.textColumn "Name" (Length.Cells 8) Style.Default id ]

              let selBg = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
              let sel = Style.Default.WithBackground selBg

              // 2-row window starting at topRow 1 (beta, gamma); select row 2 (gamma)
              let node: LayoutNode<unit> =
                  Mire.Widgets.Table.view 2 Style.Default sel 1 (fun i -> i = 2) cols rows

              let surf = Surface(Size.Create(10, 5))
              Layout.measure (Rect.Create(0, 0, 10, 5)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "Name" "sticky header on the first row"
              Expect.stringContains (rowText surf 1) "beta" "first windowed row (topRow=1) below the header"
              Expect.stringContains (rowText surf 2) "gamma" "second windowed row"

              Expect.equal
                  surf.[5, 2].Style.Background
                  (Some selBg)
                  "selected row (gamma) highlighted full-column-width"
          }
          test "CommandPalette.matches is a case-insensitive subsequence" {
              Expect.isTrue (Mire.Widgets.CommandPalette.matches "tp" "ToolPanel") "t,p subsequence of ToolPanel"
              Expect.isTrue (Mire.Widgets.CommandPalette.matches "" "anything") "empty query matches all"
              Expect.isTrue (Mire.Widgets.CommandPalette.matches "TOOL" "tool call") "case-insensitive"
              Expect.isFalse (Mire.Widgets.CommandPalette.matches "px" "ToolPanel") "no 'x' after 'p'"
              Expect.isFalse (Mire.Widgets.CommandPalette.matches "pool" "Panel") "not a subsequence"
          }
          test "CommandPalette.filter ranks fuzzy matches best-first" {
              let items = [ "Open File"; "Close File"; "Toggle Theme"; "Find" ]

              Expect.equal
                  (Mire.Widgets.CommandPalette.filter "fi" items)
                  [ "Find"; "Open File"; "Close File" ]
                  "Find (f…i at index 0) ranks before the File items; Toggle Theme has no 'f'"
          }
          test "CommandPalette.view shows the query line and the items" {
              let dim = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x88uy))
              let sel = Style.Default.WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))

              let node: LayoutNode<unit> =
                  Mire.Widgets.CommandPalette.view
                      30
                      8
                      dim
                      dim
                      dim
                      sel
                      dim
                      "Commands"
                      "op"
                      0
                      [ "Open File"; "Open Folder" ]

              let surf = Surface(Size.Create(40, 12))
              Layout.measure (Rect.Create(0, 0, 40, 12)) node |> Layout.render surf
              let whole = String.concat "\n" [ for y in 0..11 -> rowText surf y ]
              Expect.stringContains whole "op" "the ❯ query line is rendered"
              Expect.stringContains whole "Open File" "a filtered item is rendered"
          }
          test "Overlay.atPoint places a child at the point, clamped on-screen" {
              let bg = Color.Rgb(0x30uy, 0x30uy, 0x30uy)

              let fill: LayoutNode<unit> =
                  LayoutNode.Filled(Rect.Create(0, 0, 0, 0), Style.Default.WithBackground bg)

              // 4×2 child at (5,3) in a 20×10 area → occupies (5..8, 3..4)
              let node: LayoutNode<unit> = Mire.Widgets.Overlay.atPoint 5 3 4 2 20 10 fill
              let surf = Surface(Size.Create(20, 10))
              Layout.measure (Rect.Create(0, 0, 20, 10)) node |> Layout.render surf
              Expect.equal surf.[5, 3].Style.Background (Some bg) "top-left of the child at the anchor point"
              Expect.equal surf.[8, 4].Style.Background (Some bg) "bottom-right of the 4×2 child"
              Expect.isTrue surf.[4, 3].Style.Background.IsNone "nothing left of the anchor"
              Expect.isTrue surf.[5, 2].Style.Background.IsNone "nothing above the anchor"

              // anchored at the far corner → clamped so the whole child stays on-screen
              let clamped: LayoutNode<unit> = Mire.Widgets.Overlay.atPoint 19 9 4 2 20 10 fill
              let surf2 = Surface(Size.Create(20, 10))
              Layout.measure (Rect.Create(0, 0, 20, 10)) clamped |> Layout.render surf2
              Expect.equal surf2.[16, 8].Style.Background (Some bg) "clamped to (16,8) so the 4×2 child fits"
              Expect.equal surf2.[19, 9].Style.Background (Some bg) "child reaches the bottom-right corner"
          }
          test "Completion popup renders its items below the anchor" {
              let dim = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x88uy))
              let sel = Style.Default.WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))

              let node: LayoutNode<unit> =
                  Mire.Widgets.Completion.view 40 12 2 2 14 5 dim sel dim 0 [ "foo()"; "format" ]

              let surf = Surface(Size.Create(40, 12))
              Layout.measure (Rect.Create(0, 0, 40, 12)) node |> Layout.render surf
              let whole = String.concat "\n" [ for y in 0..11 -> rowText surf y ]
              Expect.stringContains whole "foo()" "a candidate is rendered"
              Expect.stringContains whole "format" "the other candidate is rendered"
          }
          test "ListView.viewWith virtualizes the window and supports multi-select" {
              let selBg = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)
              let sel = Style.Default.WithBackground selBg
              let row = Style.Default.WithForeground Color.White
              let labels = [ for i in 0..99 -> sprintf "row%d" i ]
              // height 3, scroll to row 50 → window centred near 50; rows 49 & 51 selected
              let selected = Set.ofList [ 49; 51 ]

              let node: LayoutNode<unit> =
                  Mire.Widgets.ListView.viewWith 3 sel row selected.Contains 50 labels

              // only the 3 visible rows are built (virtualized), not all 100
              let built =
                  match node with
                  | LayoutNode.Stack(_, _, kids) -> List.length kids
                  | _ -> -1

              Expect.equal built 3 "only the visible window of rows is materialised"

              let surf = Surface(Size.Create(12, 3))
              Layout.measure (Rect.Create(0, 0, 12, 3)) node |> Layout.render surf
              // window is rows 49..51 (off = 50 - 3/2 = 49)
              Expect.stringContains (rowText surf 0) "row49" "window starts at row 49"
              Expect.equal surf.[8, 0].Style.Background (Some selBg) "row 49 selected (multi)"
              Expect.notEqual surf.[8, 1].Style.Background (Some selBg) "row 50 not selected"
              Expect.equal surf.[8, 2].Style.Background (Some selBg) "row 51 selected (multi)"
          }
          test "Completion flips above the caret when there's no room below" {
              let dim = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x88uy))
              let sel = Style.Default.WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))
              // caret near the bottom (y=10 of 12); a 2-item popup (h=4) can't fit below
              let node: LayoutNode<unit> =
                  Mire.Widgets.Completion.view 40 12 2 10 14 5 dim sel dim 0 [ "foo()"; "format" ]

              let surf = Surface(Size.Create(40, 12))
              Layout.measure (Rect.Create(0, 0, 40, 12)) node |> Layout.render surf
              // flipped above → top at anchorY - h = 6; nothing rendered on the caret row (10) or below
              Expect.stringContains
                  (String.concat "\n" [ for y in 6..9 -> rowText surf y ])
                  "foo()"
                  "popup sits above the caret"

              Expect.isTrue
                  (System.String.IsNullOrWhiteSpace(rowText surf 11))
                  "nothing at the bottom (didn't overflow below)"
          }
          test "Separator.horizontal / vertical draw rules of the given length" {
              let h: LayoutNode<unit> = Mire.Widgets.Separator.horizontal 5 Style.Default
              let surfH = Surface(Size.Create(8, 1))
              Layout.measure (Rect.Create(0, 0, 8, 1)) h |> Layout.render surfH
              Expect.equal ((rowText surfH 0).Substring(0, 5)) "─────" "5-cell horizontal rule"

              let v: LayoutNode<unit> = Mire.Widgets.Separator.vertical 3 Style.Default
              let surfV = Surface(Size.Create(1, 4))
              Layout.measure (Rect.Create(0, 0, 1, 4)) v |> Layout.render surfV
              Expect.equal [ for y in 0..2 -> surfV.[0, y].Grapheme ] [ "│"; "│"; "│" ] "3-cell vertical rule"
          }
          test "Badge renders a padded label carrying the toned style" {
              let bg = Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy)

              let node: LayoutNode<unit> =
                  Mire.Widgets.Badge.badge (Style.Default.WithBackground bg) "done"

              let surf = Surface(Size.Create(10, 1))
              Layout.measure (Rect.Create(0, 0, 10, 1)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "done" "label shown"
              Expect.equal surf.[0, 0].Style.Background (Some bg) "the leading pad carries the toned bg"
          }
          test "KeyHint shows the key glyph and the label" {
              let key = Style.Default.WithForeground Color.White
              let lbl = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x88uy))
              let node: LayoutNode<unit> = Mire.Widgets.KeyHint.hint key lbl "Ctrl+P" "palette"
              let surf = Surface(Size.Create(20, 1))
              Layout.measure (Rect.Create(0, 0, 20, 1)) node |> Layout.render surf
              let r = rowText surf 0
              Expect.stringContains r "Ctrl+P" "key glyph shown"
              Expect.stringContains r "palette" "label shown"
          } ]

// TextBuffer (pure edit ops) -------------------------------------------------

let textBufferTests =
    testList
        "TextBuffer"
        [ test "insert at the cursor advances past it" {
              let b = TextBuffer.Empty |> TextBuffer.insert "ab" |> TextBuffer.insert "c"
              Expect.equal (b.Text, b.Cursor) ("abc", 3) "accumulated, cursor at end"
          }
          test "insert splices mid-string" {
              let b = { Text = "ace"; Cursor = 1 } |> TextBuffer.insert "b"
              Expect.equal (b.Text, b.Cursor) ("abce", 2) "inserted at index 1"
          }
          test "backspace deletes before the cursor; no-op at start" {
              Expect.equal (({ Text = "abc"; Cursor = 2 } |> TextBuffer.backspace).Text) "ac" "removed 'b'"
              let atStart = { Text = "abc"; Cursor = 0 } |> TextBuffer.backspace
              Expect.equal (atStart.Text, atStart.Cursor) ("abc", 0) "unchanged at start"
          }
          test "delete removes the char at the cursor" {
              let b = { Text = "abc"; Cursor = 1 } |> TextBuffer.delete
              Expect.equal (b.Text, b.Cursor) ("ac", 1) "removed 'b', cursor stays"
          }
          test "cursor moves clamp to bounds" {
              let b = { Text = "abc"; Cursor = 1 }
              Expect.equal (TextBuffer.left b).Cursor 0 "left"
              Expect.equal (TextBuffer.right b).Cursor 2 "right"
              Expect.equal (TextBuffer.toEnd b).Cursor 3 "end"
              Expect.equal (TextBuffer.left { Text = ""; Cursor = 0 }).Cursor 0 "left clamps at 0"
          }
          test "deleteWordBack removes the previous word" {
              let b = { Text = "foo bar"; Cursor = 7 } |> TextBuffer.deleteWordBack
              Expect.equal (b.Text, b.Cursor) ("foo ", 4) "removed 'bar'"

              let nl = { Text = "a\nbc"; Cursor = 4 } |> TextBuffer.deleteWordBack
              Expect.equal (nl.Text, nl.Cursor) ("a\n", 2) "stops at the newline boundary"
          }
          test "wordLeft / wordRight jump by word, clamping at the ends" {
              Expect.equal
                  ({ Text = "foo bar"; Cursor = 7 } |> TextBuffer.wordLeft).Cursor
                  4
                  "wordLeft → start of 'bar'"

              Expect.equal ({ Text = "foo bar"; Cursor = 0 } |> TextBuffer.wordLeft).Cursor 0 "wordLeft no-op at start"

              Expect.equal
                  ({ Text = "foo bar"; Cursor = 0 } |> TextBuffer.wordRight).Cursor
                  3
                  "wordRight → end of 'foo'"

              Expect.equal ({ Text = "foo bar"; Cursor = 7 } |> TextBuffer.wordRight).Cursor 7 "wordRight no-op at end"
          }
          test "deleteWordForward removes the next word" {
              let b = { Text = "foo bar"; Cursor = 0 } |> TextBuffer.deleteWordForward
              Expect.equal (b.Text, b.Cursor) (" bar", 0) "removed 'foo', cursor stays"
          }
          test "lineStart / lineEnd act within the current line" {
              Expect.equal ({ Text = "ab\ncd"; Cursor = 4 } |> TextBuffer.lineStart).Cursor 3 "→ start of 2nd line"
              Expect.equal ({ Text = "ab\ncd"; Cursor = 3 } |> TextBuffer.lineEnd).Cursor 5 "→ end of 2nd line"

              Expect.equal
                  ({ Text = "ab\ncd"; Cursor = 1 } |> TextBuffer.lineEnd).Cursor
                  2
                  "1st line ends before its \\n"
          }
          test "up / down move by row, clamping the column (no sticky col)" {
              let b = { Text = "abc\nde"; Cursor = 2 } // row 0, col 2
              let d = TextBuffer.down b
              Expect.equal d.Cursor 6 "down onto a shorter line clamps the column to its end"
              Expect.equal (TextBuffer.up d).Cursor 2 "up returns to col 2 on the longer line"
              Expect.equal (TextBuffer.up b) b "up is a no-op on the first line"

              Expect.equal
                  (TextBuffer.down { Text = "x"; Cursor = 1 })
                  { Text = "x"; Cursor = 1 }
                  "down is a no-op on the last line"
          }
          test "cursorRowCol maps the flat cursor to (row, col)" {
              Expect.equal (TextBuffer.cursorRowCol TextBuffer.Empty) (0, 0) "empty → (0,0)"
              Expect.equal (TextBuffer.cursorRowCol { Text = "a\nbb"; Cursor = 4 }) (1, 2) "end of 2nd line"
              Expect.equal (TextBuffer.cursorRowCol { Text = "a\nbb"; Cursor = 1 }) (0, 1) "end of 1st line"
          } ]

// TextEdit (editing actions + configurable keymap) --------------------------

let textEditTests =
    let key k mods : KeyEvent =
        { Key = k
          Text = None
          Modifiers = mods
          Repeat = false
          EventType = Press }

    let ctrl = { KeyModifiers.None with Ctrl = true }

    testList
        "TextEdit"
        [ test "apply runs the action against the buffer" {
              Expect.equal (TextEdit.apply (InsertText "hi") TextBuffer.Empty).Text "hi" "InsertText inserts"
              Expect.equal (TextEdit.apply Newline (TextBuffer.Of "a")).Text "a\n" "Newline inserts a line break"
              Expect.equal (TextEdit.apply DeleteWordBack { Text = "foo bar"; Cursor = 7 }).Text "foo " "DeleteWordBack"
          }
          test "defaultKeymap follows conventions" {
              Expect.equal
                  (TextEdit.defaultKeymap (key (Char "a") KeyModifiers.None))
                  (Some(InsertText "a"))
                  "plain char inserts"

              Expect.equal (TextEdit.defaultKeymap (key Enter KeyModifiers.None)) (Some Newline) "Enter → Newline"

              Expect.equal
                  (TextEdit.defaultKeymap (key Backspace ctrl))
                  (Some DeleteWordBack)
                  "Ctrl+Backspace → word delete"

              Expect.equal (TextEdit.defaultKeymap (key ArrowLeft ctrl)) (Some WordLeft) "Ctrl+Left → word jump"
              Expect.equal (TextEdit.defaultKeymap (key Home KeyModifiers.None)) (Some LineStart) "Home → line start"
              Expect.equal (TextEdit.defaultKeymap (key Home ctrl)) (Some DocStart) "Ctrl+Home → doc start"
              Expect.equal (TextEdit.defaultKeymap (key (Char "c") ctrl)) None "Ctrl+C is unmapped (app/quit owns it)"
          }
          test "applyInput inserts pasted text (multi-line)" {
              Expect.equal (TextEdit.applyInput (Paste "x\ny") TextBuffer.Empty).Text "x\ny" "Paste → insert"
          }
          test "a custom keymap overrides only the keys it claims" {
              // app convention: Shift+Enter = newline, plain Enter = submit (None); rest default
              let km (ke: KeyEvent) =
                  match ke.Key, ke.Modifiers.Shift with
                  | Enter, true -> Some Newline
                  | Enter, false -> None
                  | _ -> TextEdit.defaultKeymap ke

              Expect.equal (km (key Enter KeyModifiers.None)) None "plain Enter left to the app (submit)"

              Expect.equal
                  (km (key Enter { KeyModifiers.None with Shift = true }))
                  (Some Newline)
                  "Shift+Enter newlines"

              Expect.equal
                  (km (key (Char "z") KeyModifiers.None))
                  (Some(InsertText "z"))
                  "unclaimed keys fall through to the default"
          } ]

let inputViewTests =
    testList
        "Input.render"
        [ test "draws a block cursor at the cursor cell" {
              let txt = Style.Default.WithForeground(Color.White)
              let curBg = Color.Rgb(0x9Auy, 0xA2uy, 0xAEuy)
              let cur = Style.Default.WithForeground(Color.Black).WithBackground(curBg)

              let node: LayoutNode<unit> =
                  Mire.Widgets.Input.render 10 txt cur true { Text = "abc"; Cursor = 1 }

              let surf = Surface(Size.Create(10, 1))
              Layout.measure (Rect.Create(0, 0, 10, 1)) node |> Layout.render surf
              Expect.equal surf.[1, 0].Grapheme "b" "cursor cell shows the char under it"
              Expect.equal surf.[1, 0].Style.Background (Some curBg) "cursor cell highlighted"
              Expect.isTrue surf.[0, 0].Style.Background.IsNone "char left of cursor not highlighted"
          }
          test "scrolls horizontally to keep the cursor visible" {
              let cur = Style.Default.WithBackground(Color.White)

              let node: LayoutNode<unit> =
                  Mire.Widgets.Input.render 4 Style.Default cur true { Text = "0123456789"; Cursor = 10 }

              let surf = Surface(Size.Create(4, 1))
              Layout.measure (Rect.Create(0, 0, 4, 1)) node |> Layout.render surf
              Expect.isTrue ((rowText surf 0).StartsWith "789") "window scrolled to the tail near the cursor"
          } ]

// TextArea (multi-line editor render) ---------------------------------------

let textAreaTests =
    testList
        "TextArea"
        [ test "renders multiple lines with the block cursor on the cursor's row" {
              let curBg = Color.Rgb(0x9Auy, 0xA2uy, 0xAEuy)
              let cur = Style.Default.WithBackground curBg
              // "ab\ncd", cursor 4 → (row 1, col 1)
              let node: LayoutNode<unit> =
                  Mire.Widgets.TextArea.render 10 3 Style.Default cur true { Text = "ab\ncd"; Cursor = 4 }

              let surf = Surface(Size.Create(10, 3))
              Layout.measure (Rect.Create(0, 0, 10, 3)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "ab" "first line on row 0"
              Expect.stringContains (rowText surf 1) "cd" "second line on row 1"
              Expect.equal surf.[1, 1].Style.Background (Some curBg) "block cursor at (col1,row1)"
              Expect.isTrue surf.[1, 0].Style.Background.IsNone "no cursor on the first row"
          }
          test "vertical-scrolls to keep the cursor row visible" {
              let cur = Style.Default.WithBackground(Color.Rgb(0x40uy, 0x40uy, 0x40uy))
              let text = "l0\nl1\nl2\nl3\nl4"
              let buf = { Text = text; Cursor = text.Length } // cursor on the last line

              let node: LayoutNode<unit> =
                  Mire.Widgets.TextArea.render 6 2 Style.Default cur true buf

              let surf = Surface(Size.Create(6, 2))
              Layout.measure (Rect.Create(0, 0, 6, 2)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "l3" "previous line above the window"
              Expect.stringContains (rowText surf 1) "l4" "last line at the bottom of the 2-row window"
          }
          test "empty buffer shows a cursor block at the origin" {
              let curBg = Color.Rgb(0x40uy, 0x40uy, 0x40uy)
              let cur = Style.Default.WithBackground curBg

              let node: LayoutNode<unit> =
                  Mire.Widgets.TextArea.render 8 2 Style.Default cur true TextBuffer.Empty

              let surf = Surface(Size.Create(8, 2))
              Layout.measure (Rect.Create(0, 0, 8, 2)) node |> Layout.render surf
              Expect.equal surf.[0, 0].Style.Background (Some curBg) "cursor block at (0,0)"
          } ]

// SplitView ----------------------------------------------------------------

let splitViewTests =
    testList
        "SplitView"
        [ test "horizontal: first | divider | second, divider carries its style" {
              let divBg = Color.Rgb(0x33uy, 0x33uy, 0x33uy)
              let div = Style.Default.WithBackground divBg

              let node: LayoutNode<unit> =
                  Mire.Widgets.SplitView.horizontal
                      (Cells 3)
                      div
                      (Mire.Widgets.Text.text "LLLL" Style.Default)
                      (Mire.Widgets.Text.text "RRRR" Style.Default)

              let surf = Surface(Size.Create(10, 1))
              Layout.measure (Rect.Create(0, 0, 10, 1)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "LLL" "left pane in the first 3 cells"
              Expect.equal surf.[3, 0].Style.Background (Some divBg) "divider gutter at x=3 carries the divider bg"
              Expect.equal surf.[4, 0].Grapheme "R" "right pane starts after the divider"
          }
          test "vertical: top / divider / bottom" {
              let divBg = Color.Rgb(0x22uy, 0x22uy, 0x22uy)
              let div = Style.Default.WithBackground divBg

              let node: LayoutNode<unit> =
                  Mire.Widgets.SplitView.vertical
                      (Cells 1)
                      div
                      (Mire.Widgets.Text.text "T" Style.Default)
                      (Mire.Widgets.Text.text "B" Style.Default)

              let surf = Surface(Size.Create(4, 4))
              Layout.measure (Rect.Create(0, 0, 4, 4)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "T" "top pane on row 0"
              Expect.equal surf.[0, 1].Style.Background (Some divBg) "divider row at y=1"
              Expect.stringContains (rowText surf 2) "B" "bottom pane below the divider"
          } ]

// Tooltip ------------------------------------------------------------------

let tooltipTests =
    testList
        "Tooltip"
        [ test "renders a bordered tip just below the anchor" {
              let node: LayoutNode<unit> =
                  Mire.Widgets.Tooltip.view 20 10 2 2 8 Style.Default Style.Default [ "hi"; "yo" ]

              let surf = Surface(Size.Create(20, 10))
              Layout.measure (Rect.Create(0, 0, 20, 10)) node |> Layout.render surf
              // anchor y=2 → below at y=3; box: top border row 3, " hi" row 4, " yo" row 5, bottom row 6
              Expect.stringContains (rowText surf 4) "hi" "first line one row below the top border"
              Expect.stringContains (rowText surf 5) "yo" "second line"
          }
          test "flips above the anchor when it would run off the bottom" {
              let node: LayoutNode<unit> =
                  Mire.Widgets.Tooltip.view 12 6 1 5 6 Style.Default Style.Default [ "x" ]

              let surf = Surface(Size.Create(12, 6))
              Layout.measure (Rect.Create(0, 0, 12, 6)) node |> Layout.render surf
              // h = 1+2 = 3; below (6) doesn't fit → flip to y = 5-3 = 2; box rows 2..4, " x" at row 3
              Expect.stringContains (rowText surf 3) "x" "tip flipped above the anchor"
              Expect.isFalse ((rowText surf 5).Contains "x") "nothing rendered below the anchor"
          } ]

// Spinner / ProgressBar / Tabs / Toggle ------------------------------------

let private render1 (w: int) (h: int) (n: LayoutNode<unit>) (y: int) : string =
    let s = Surface(Size.Create(w, h))
    Layout.measure (Rect.Create(0, 0, w, h)) n |> Layout.render s
    rowText s y

let spinnerTests =
    testList
        "Spinner"
        [ test "frameOf wraps and handles negative ticks" {
              Expect.equal (Mire.Widgets.Spinner.frameOf Mire.Widgets.Spinner.braille 0) "⠋" "tick 0"
              Expect.equal (Mire.Widgets.Spinner.frameOf Mire.Widgets.Spinner.braille 10) "⠋" "tick 10 wraps to frame 0"
              Expect.equal (Mire.Widgets.Spinner.frameOf Mire.Widgets.Spinner.braille -1) "⠏" "tick -1 → last frame"
          }
          test "frameOf on an empty frame set is empty" {
              Expect.equal (Mire.Widgets.Spinner.frameOf [||] 3) "" "no frames → empty string"
          } ]

let progressBarTests =
    testList
        "ProgressBar"
        [ test "half fill = half blocks, half track" {
              let node: LayoutNode<unit> =
                  Mire.Widgets.ProgressBar.view 10 Style.Default Style.Default 0.5

              Expect.equal (render1 10 1 node 0) "█████░░░░░" "5 filled + 5 track at 50%"
          }
          test "fraction clamps above 1.0" {
              let node: LayoutNode<unit> =
                  Mire.Widgets.ProgressBar.view 4 Style.Default Style.Default 2.0

              Expect.equal (render1 4 1 node 0) "████" "over-full clamps to all filled"
          }
          test "zero fraction is all track" {
              let node: LayoutNode<unit> =
                  Mire.Widgets.ProgressBar.view 4 Style.Default Style.Default 0.0

              Expect.equal (render1 4 1 node 0) "░░░░" "empty bar is all track"
          } ]

let tabsTests =
    testList
        "Tabs"
        [ test "active tab carries activeStyle, others don't" {
              let actBg = Color.Rgb(0x20uy, 0x80uy, 0x40uy)
              let act = Style.Default.WithBackground actBg

              let node: LayoutNode<unit> =
                  Mire.Widgets.Tabs.strip act Style.Default 1 [ "a"; "b" ]

              let surf = Surface(Size.Create(8, 1))
              Layout.measure (Rect.Create(0, 0, 8, 1)) node |> Layout.render surf
              // " a " at x0..2 (inactive), " b " at x3..5 (active) → 'b' is x4
              Expect.equal surf.[4, 0].Grapheme "b" "second tab label cell"
              Expect.equal surf.[4, 0].Style.Background (Some actBg) "selected tab carries activeStyle"
              Expect.isTrue surf.[1, 0].Style.Background.IsNone "unselected tab has no active bg"
          } ]

let toggleTests =
    testList
        "Toggle"
        [ test "checkbox reflects state" {
              Expect.stringContains
                  (render1 8 1 (Mire.Widgets.Toggle.checkbox Style.Default true "ok") 0)
                  "[x] ok"
                  "checked"

              Expect.stringContains
                  (render1 8 1 (Mire.Widgets.Toggle.checkbox Style.Default false "ok") 0)
                  "[ ] ok"
                  "unchecked"
          }
          test "radio + switch glyphs" {
              Expect.stringContains
                  (render1 8 1 (Mire.Widgets.Toggle.radio Style.Default true "x") 0)
                  "(•) x"
                  "radio selected"

              Expect.stringContains
                  (render1 8 1 (Mire.Widgets.Toggle.switch Style.Default Style.Default true) 0)
                  "ON"
                  "switch on"

              Expect.stringContains
                  (render1 8 1 (Mire.Widgets.Toggle.switch Style.Default Style.Default false) 0)
                  "OFF"
                  "switch off"
          } ]

// Markdown -----------------------------------------------------------------

let markdownTests =
    let h1bg = Color.Rgb(0x10uy, 0x20uy, 0x30uy)
    let codeBg = Color.Rgb(0x40uy, 0x40uy, 0x40uy)
    let menBg = Color.Rgb(0x50uy, 0x10uy, 0x50uy)

    let st =
        { Mire.Widgets.Markdown.defaultStyle with
            Heading1 = Style.Default.WithBackground h1bg
            Code = Style.Default.WithBackground codeBg
            Mention = Some(Style.Default.WithBackground menBg) }

    testList
        "Markdown"
        [ test "heading line uses the heading style (marker stripped)" {
              let node: LayoutNode<unit> = Mire.Widgets.Markdown.render st 20 "# Title"
              let surf = Surface(Size.Create(20, 3))
              Layout.measure (Rect.Create(0, 0, 20, 3)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "Title" "heading text"
              Expect.equal surf.[0, 0].Style.Background (Some h1bg) "heading carries Heading1 style"
          }
          test "dash bullets get a • prefix" {
              let node: LayoutNode<unit> = Mire.Widgets.Markdown.render st 20 "- item"
              let surf = Surface(Size.Create(20, 3))
              Layout.measure (Rect.Create(0, 0, 20, 3)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "• item" "dash becomes a bullet"
          }
          test "inline code span strips markers and carries the code style" {
              let node: LayoutNode<unit> =
                  Mire.Widgets.Markdown.wrap st 20 Style.Default "a `co` b"

              let surf = Surface(Size.Create(20, 2))
              Layout.measure (Rect.Create(0, 0, 20, 2)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "a co b" "backticks removed"
              Expect.equal surf.[2, 0].Style.Background (Some codeBg) "code span styled (c at x=2)"
          }
          test "@mention rule honors the optional style" {
              let on: LayoutNode<unit> = Mire.Widgets.Markdown.wrap st 20 Style.Default "hi @x"
              let surfOn = Surface(Size.Create(20, 2))
              Layout.measure (Rect.Create(0, 0, 20, 2)) on |> Layout.render surfOn
              Expect.equal surfOn.[3, 0].Style.Background (Some menBg) "@ token styled when Mention = Some"

              let off: LayoutNode<unit> =
                  Mire.Widgets.Markdown.wrap { st with Mention = None } 20 Style.Default "hi @x"

              let surfOff = Surface(Size.Create(20, 2))
              Layout.measure (Rect.Create(0, 0, 20, 2)) off |> Layout.render surfOff
              Expect.isTrue surfOff.[3, 0].Style.Background.IsNone "@ token plain when Mention = None"
          } ]

// Golden frames — full cell-grid snapshots of widget compositions ----------

/// Render a node onto a `w`×`h` surface and return the whole grid as text
/// (rows joined by '\n', empty cells as spaces) — the full-grid snapshot.
let private gridText (w: int) (h: int) (node: LayoutNode<unit>) : string =
    let s = Surface(Size.Create(w, h))
    Layout.measure (Rect.Create(0, 0, w, h)) node |> Layout.render s

    [ for y in 0 .. h - 1 ->
          String.concat
              ""
              [ for x in 0 .. w - 1 ->
                    let g = s.[x, y].Grapheme
                    if System.String.IsNullOrEmpty g then " " else g ] ]
    |> String.concat "\n"

let goldenFrameTests =
    // A small dashboard composition exercising several widgets at once.
    let dashboard: LayoutNode<unit> =
        Mire.Widgets.Stack.vstack
            [ Mire.Widgets.Tabs.strip Style.Default Style.Default 1 [ "a"; "b" ]
              Mire.Widgets.ProgressBar.view 10 Style.Default Style.Default 0.4
              Mire.Widgets.Toggle.checkbox Style.Default true "go" ]

    let boxScene: LayoutNode<unit> =
        Mire.Widgets.Box.box Style.Default [ Mire.Widgets.Text.text "hi" Style.Default ]

    testList
        "GoldenFrame"
        [ test "box composition snapshot" {
              let expected = String.concat "\n" [ "┌──────┐"; "│hi    │"; "└──────┘" ]
              Expect.equal (gridText 8 3 boxScene) expected "box grid"
          }
          test "dashboard composition snapshot" {
              let pad (s: string) =
                  s + String.replicate (12 - String.length s) " "

              let expected = String.concat "\n" [ pad " a  b "; pad "████░░░░░░"; pad "[x] go" ]
              Expect.equal (gridText 12 3 dashboard) expected "dashboard grid (tabs / bar / checkbox)"
          }
          test "rendering is deterministic" {
              Expect.equal (gridText 12 3 dashboard) (gridText 12 3 dashboard) "same node renders identically twice"
          }
          test "no row overflows or underflows the surface width" {
              let s = Surface(Size.Create(12, 3))
              Layout.measure (Rect.Create(0, 0, 12, 3)) dashboard |> Layout.render s

              for y in 0..2 do
                  Expect.equal (rowText s y).Length 12 (sprintf "row %d is exactly the surface width" y)
          } ]

// Feed helpers (Mire.Demo.Feed) --------------------------------------------

let feedTests =
    testList
        "Feed"
        [ test "parseDate reads an RFC-1123 pubDate" {
              let d = Feed.parseDate "Mon, 18 May 2026 10:00:00 GMT"
              Expect.equal (d.Year, d.Month, d.Day) (2026, 5, 18) "parses Y/M/D"
          }
          test "parseDate normalises to UTC" {
              let d = Feed.parseDate "Mon, 18 May 2026 10:00:00 +0200"
              Expect.equal d.Hour 8 "+0200 offset shifted back to UTC"
          }
          test "parseDate orders newer after older" {
              let older = Feed.parseDate "Mon, 18 May 2026 10:00:00 GMT"
              let newer = Feed.parseDate "Tue, 19 May 2026 09:00:00 GMT"
              Expect.isTrue (newer > older) "later pubDate sorts greater"
          }
          test "parseDate falls back to MinValue on garbage" {
              Expect.equal (Feed.parseDate "not a date") System.DateTime.MinValue "unparseable → MinValue"
          }
          test "parseDate handles empty string" {
              Expect.equal (Feed.parseDate "") System.DateTime.MinValue "empty → MinValue"
          }
          test "isValidUrl accepts http(s) with host" {
              Expect.isTrue (Feed.isValidUrl "https://example.com/feed.xml") "https ok"
              Expect.isTrue (Feed.isValidUrl "http://a.b/c") "http ok"
          }
          test "isValidUrl trims surrounding whitespace" {
              Expect.isTrue (Feed.isValidUrl "  https://example.com/feed.xml  ") "trimmed and accepted"
          }
          test "isValidUrl rejects missing scheme" {
              Expect.isFalse (Feed.isValidUrl "example.com/feed.xml") "no scheme → invalid"
          }
          test "isValidUrl rejects non-http schemes" {
              Expect.isFalse (Feed.isValidUrl "ftp://example.com/feed.xml") "ftp → invalid"
              Expect.isFalse (Feed.isValidUrl "file:///etc/hosts") "file → invalid"
          }
          test "isValidUrl rejects empty and plain text" {
              Expect.isFalse (Feed.isValidUrl "") "empty → invalid"
              Expect.isFalse (Feed.isValidUrl "notaurl") "plain text → invalid"
          } ]

// Cmd.quit (quit-from-update) ----------------------------------------------

let cmdQuitTests =
    testList
        "Cmd.quit"
        [ test "Cmd.quit triggers requestQuit and sends no message" {
              let mutable quit = false
              let sent = ResizeArray<int>()
              Cmd.dispatch (fun () -> quit <- true) ignore (fun (m: int) -> sent.Add m) Cmd.quit
              Expect.isTrue quit "Cmd.quit invokes the runtime's requestQuit callback"
              Expect.isEmpty sent "Cmd.quit does not enqueue any message"
          }
          test "Cmd.none / Cmd.ofMsg do not request quit" {
              let mutable quit = false
              let sent = ResizeArray<int>()
              let rq () = quit <- true
              let send (m: int) = sent.Add m
              Cmd.dispatch rq ignore send Cmd.none
              Cmd.dispatch rq ignore send (Cmd.ofMsg 7)
              Expect.isFalse quit "neither none nor ofMsg requests quit"
              Expect.sequenceEqual sent (seq { 7 }) "ofMsg still delivers its message"
          }
          test "Cmd.setClipboard invokes the clipboard hook with its text" {
              let copied = ResizeArray<string>()
              Cmd.dispatch ignore copied.Add (fun (_: int) -> ()) (Cmd.setClipboard "hello")
              Expect.sequenceEqual copied (seq { "hello" }) "setClipboard routes its text to the runtime hook"
          }
          test "Cmd.batch propagates a nested Cmd.quit and still sends siblings" {
              let mutable quitCount = 0
              let sent = ResizeArray<int>()

              Cmd.dispatch
                  (fun () -> quitCount <- quitCount + 1)
                  ignore
                  (fun (m: int) -> sent.Add m)
                  (Cmd.batch [ Cmd.ofMsg 1; Cmd.quit; Cmd.ofMsg 2 ])

              Expect.equal quitCount 1 "a Cmd.quit anywhere in a batch requests quit exactly once"

              Expect.sequenceEqual
                  sent
                  (seq {
                      1
                      2
                  })
                  "sibling ofMsg commands in the batch still fire"
          }
          test "QuitOn is a default-but-overridable policy (Ctrl+C by default)" {
              let prog =
                  Program.mkProgram (fun () -> 0, Cmd.none) (fun (_: int) (m: int) -> m, Cmd.none) (fun _ ->
                      LayoutNode.Empty)

              let key (c: string) (ctrl: bool) : InputEvent =
                  Key
                      { Key = Char c
                        Text = Some c
                        Modifiers = { KeyModifiers.None with Ctrl = ctrl }
                        Repeat = false
                        EventType = Press }

              Expect.isTrue (prog.QuitOn(key "c" true)) "default: Ctrl+C quits"
              Expect.isFalse (prog.QuitOn(key "x" true)) "default: Ctrl+X does not"
              Expect.isFalse (prog.QuitOn(key "c" false)) "default: a plain 'c' does not"

              let prog2 = prog |> Program.withQuitOn (fun _ -> false)
              Expect.isFalse (prog2.QuitOn(key "c" true)) "withQuitOn (fun _ -> false) disables the Ctrl+C quit"
          } ]

// Focus (keyboard focus ring + modal trap) ----------------------------------

let focusTests =
    let a = RegionId "a"
    let b = RegionId "b"
    let c = RegionId "c"
    let ring = Focus.ofOrder [ a; b; c ]

    testList
        "Focus"
        [ test "ofOrder focuses the first id" { Expect.equal (Focus.current ring) (Some a) "current = first id" }
          test "ofOrder of an empty list has no current" {
              Expect.equal (Focus.current (Focus.ofOrder [])) None "empty ring → no current"
          }
          test "next advances and wraps" {
              Expect.equal (Focus.current (Focus.next ring)) (Some b) "a → b"
              Expect.equal (Focus.current (ring |> Focus.next |> Focus.next)) (Some c) "→ c"
              Expect.equal (Focus.current (ring |> Focus.next |> Focus.next |> Focus.next)) (Some a) "c wraps to a"
          }
          test "prev retreats and wraps" {
              Expect.equal (Focus.current (Focus.prev ring)) (Some c) "a wraps back to c"
              Expect.equal (Focus.current (ring |> Focus.prev |> Focus.prev)) (Some b) "→ b"
          }
          test "next/prev are no-ops on a single-id ring" {
              let one = Focus.ofOrder [ a ]
              Expect.equal (Focus.current (Focus.next one)) (Some a) "next single = same"
              Expect.equal (Focus.current (Focus.prev one)) (Some a) "prev single = same"
          }
          test "next on an empty ring stays empty" {
              Expect.equal (Focus.current (Focus.next Focus.empty)) None "next empty = none"
          }
          test "focus sets current when the id is in the ring" {
              Expect.equal (Focus.current (Focus.focus c ring)) (Some c) "focus c → current c"
          }
          test "focus is a no-op when the id is absent" {
              Expect.equal
                  (Focus.current (Focus.focus (RegionId "z") ring))
                  (Some a)
                  "absent id ignored, current unchanged"
          }
          test "isFocused matches the current id only" {
              Expect.isTrue (Focus.isFocused a ring) "a is focused"
              Expect.isFalse (Focus.isFocused b ring) "b is not focused"
          }
          test "pushTrap confines focus to the trap ring" {
              let t = ring |> Focus.next |> Focus.pushTrap [ RegionId "ok"; RegionId "cancel" ]
              Expect.isTrue (Focus.isTrapped t) "trapped"
              Expect.equal (Focus.current t) (Some(RegionId "ok")) "current = first trap id"
              Expect.equal (Focus.current (Focus.next t)) (Some(RegionId "cancel")) "next stays within the trap ring"

              Expect.equal
                  (Focus.current (t |> Focus.next |> Focus.next))
                  (Some(RegionId "ok"))
                  "wraps within the trap ring, never escapes to base"
          }
          test "popTrap restores the base ring exactly where focus was" {
              let t = ring |> Focus.next |> Focus.pushTrap [ RegionId "ok" ] // base was on b
              let back = Focus.popTrap t
              Expect.isFalse (Focus.isTrapped back) "no longer trapped"
              Expect.equal (Focus.current back) (Some b) "base.Current preserved across the trap (was on b)"
          }
          test "popTrap is a no-op at depth 0" {
              Expect.equal (Focus.popTrap ring) ring "popping with no trap leaves focus untouched"
              Expect.isFalse (Focus.isTrapped (Focus.popTrap ring)) "still not trapped"
          }
          test "nested traps push and pop one level at a time" {
              let t =
                  ring
                  |> Focus.pushTrap [ RegionId "m1" ]
                  |> Focus.pushTrap [ RegionId "m2a"; RegionId "m2b" ]

              Expect.equal (Focus.current t) (Some(RegionId "m2a")) "innermost trap active"
              let popped = Focus.popTrap t
              Expect.equal (Focus.current popped) (Some(RegionId "m1")) "pop returns to the middle ring"
              Expect.isTrue (Focus.isTrapped popped) "still trapped (one level remains)"
          }
          test "empty focus has no current and isn't trapped" {
              Expect.equal (Focus.current Focus.empty) None "empty → none"
              Expect.isFalse (Focus.isTrapped Focus.empty) "empty → not trapped"
          } ]

[<Tests>]
let all =
    testList
        "Mire"
        [ graphemeTests
          inputTests
          diffTests
          layoutTests
          positionedTests
          focusTests
          widgetTests
          textBufferTests
          textEditTests
          inputViewTests
          textAreaTests
          splitViewTests
          tooltipTests
          spinnerTests
          progressBarTests
          tabsTests
          toggleTests
          markdownTests
          goldenFrameTests
          feedTests
          cmdQuitTests ]
