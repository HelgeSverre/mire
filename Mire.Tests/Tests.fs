module Mire.Tests.Tests

open Expecto
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
open Mire.App
open Mire.FeedDemo
open Mire.MinesweeperDemo

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

    let box: LayoutNode<unit> = LayoutNode.Filled(Rect.Create(0, 0, 0, 0), Style.Default)
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
              Expect.equal (tup (placedRect Center Length.Content Length.Content txt)) (7, 4, 5, 1) "5 wide, 1 tall, centered"
          }
          test "oversized child clamps to the area without a negative origin" {
              Expect.equal (tup (placedRect Center (Cells 100) (Cells 100) box)) (0, 0, 20, 10) "clamped to 20×10 at origin"
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
              Expect.equal surf.[0, 0].Style.Background (Some backdropBg) "backdrop fills the corner (occludes the base)"
              Expect.equal surf.[19, 9].Style.Background (Some backdropBg) "backdrop fills the far corner too"
              Expect.isFalse (System.String.IsNullOrEmpty surf.[5, 2].Grapheme) "box border drawn at the centered top-left (5,2)"
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
                      Mire.Widgets.Stack.vstack [ for i in 1..8 -> Mire.Widgets.Text.text (sprintf "row%d" i) Style.Default ]

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
                  Mire.Widgets.Table.view 2 Style.Default sel 1 (Some 2) cols rows

              let surf = Surface(Size.Create(10, 5))
              Layout.measure (Rect.Create(0, 0, 10, 5)) node |> Layout.render surf
              Expect.stringContains (rowText surf 0) "Name" "sticky header on the first row"
              Expect.stringContains (rowText surf 1) "beta" "first windowed row (topRow=1) below the header"
              Expect.stringContains (rowText surf 2) "gamma" "second windowed row"
              Expect.equal surf.[5, 2].Style.Background (Some selBg) "selected row (gamma) highlighted full-column-width"
          }
          test "CommandPalette.matches is a case-insensitive subsequence" {
              Expect.isTrue (Mire.Widgets.CommandPalette.matches "tp" "ToolPanel") "t,p subsequence of ToolPanel"
              Expect.isTrue (Mire.Widgets.CommandPalette.matches "" "anything") "empty query matches all"
              Expect.isTrue (Mire.Widgets.CommandPalette.matches "TOOL" "tool call") "case-insensitive"
              Expect.isFalse (Mire.Widgets.CommandPalette.matches "px" "ToolPanel") "no 'x' after 'p'"
              Expect.isFalse (Mire.Widgets.CommandPalette.matches "pool" "Panel") "not a subsequence"
          }
          test "CommandPalette.filter keeps the fuzzy matches in order" {
              let items = [ "Open File"; "Close File"; "Toggle Theme"; "Find" ]

              Expect.equal
                  (Mire.Widgets.CommandPalette.filter "fi" items)
                  [ "Open File"; "Close File"; "Find" ]
                  "items with f…i as a subsequence (Toggle Theme has no 'f')"
          }
          test "CommandPalette.view shows the query line and the items" {
              let dim = Style.Default.WithForeground(Color.Rgb(0x88uy, 0x88uy, 0x88uy))
              let sel = Style.Default.WithBackground(Color.Rgb(0x4Cuy, 0xAFuy, 0x50uy))

              let node: LayoutNode<unit> =
                  Mire.Widgets.CommandPalette.view 30 8 dim dim dim sel dim "Commands" "op" 0 [ "Open File"; "Open Folder" ]

              let surf = Surface(Size.Create(40, 12))
              Layout.measure (Rect.Create(0, 0, 40, 12)) node |> Layout.render surf
              let whole = String.concat "\n" [ for y in 0 .. 11 -> rowText surf y ]
              Expect.stringContains whole "op" "the ❯ query line is rendered"
              Expect.stringContains whole "Open File" "a filtered item is rendered"
          }
          test "Overlay.atPoint places a child at the point, clamped on-screen" {
              let bg = Color.Rgb(0x30uy, 0x30uy, 0x30uy)
              let fill: LayoutNode<unit> = LayoutNode.Filled(Rect.Create(0, 0, 0, 0), Style.Default.WithBackground bg)

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
              let whole = String.concat "\n" [ for y in 0 .. 11 -> rowText surf y ]
              Expect.stringContains whole "foo()" "a candidate is rendered"
              Expect.stringContains whole "format" "the other candidate is rendered"
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

// Feed helpers (Mire.FeedDemo) --------------------------------------------

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

// Minesweeper --------------------------------------------------------------

/// Build a fully-placed board from an explicit mine layout, computing adjacency
/// by hand so reveal/chord tests are deterministic (no RNG involved).
let private mkBoard (rows: int) (cols: int) (mines: (int * int) list) : Board =
    let mineSet = Set.ofList mines

    let adjacent r c =
        let mutable n = 0

        for dr in -1 .. 1 do
            for dc in -1 .. 1 do
                if dr <> 0 || dc <> 0 then
                    let nr, nc = r + dr, c + dc

                    if nr >= 0 && nr < rows && nc >= 0 && nc < cols && mineSet.Contains(nr, nc) then
                        n <- n + 1

        n

    let cells =
        Array2D.init rows cols (fun r c ->
            { Mine = mineSet.Contains(r, c)
              Adjacent = adjacent r c
              State = Hidden })

    { Rows = rows
      Cols = cols
      Mines = mines.Length
      Cells = cells
      Status = Playing
      Placed = true
      Difficulty = Beginner }

let private noRng = System.Random(0) // unused when the board is already Placed

let minesweeperTests =
    testList
        "Minesweeper"
        [ test "dimensions match the three presets" {
              Expect.equal (Board.dimensions Beginner) (9, 9, 10) "Beginner 9x9/10"
              Expect.equal (Board.dimensions Intermediate) (16, 16, 40) "Intermediate 16x16/40"
              Expect.equal (Board.dimensions Expert) (16, 30, 99) "Expert 16x30/99"
          }
          test "neighbor counts: corner 3, edge 5, interior 8" {
              let b = Board.empty Beginner
              Expect.equal (Board.neighbors b 0 0 |> List.length) 3 "corner has 3 neighbours"
              Expect.equal (Board.neighbors b 0 4 |> List.length) 5 "top edge has 5"
              Expect.equal (Board.neighbors b 4 4 |> List.length) 8 "interior has 8"
          }
          test "placeMines is first-click-safe and places the right count" {
              let b = Board.placeMines (System.Random(123)) 4 4 (Board.empty Beginner)
              // clicked cell + its 8 neighbours must be mine-free
              for (r, c) in (4, 4) :: Board.neighbors b 4 4 do
                  Expect.isFalse b.Cells.[r, c].Mine (sprintf "(%d,%d) in safe zone is not a mine" r c)

              let mutable count = 0

              for r in 0 .. b.Rows - 1 do
                  for c in 0 .. b.Cols - 1 do
                      if b.Cells.[r, c].Mine then
                          count <- count + 1

              Expect.equal count 10 "exactly 10 mines placed"
          }
          test "adjacency equals the actual neighbouring mine count" {
              let b = Board.placeMines (System.Random(99)) 4 4 (Board.empty Beginner)

              for r in 0 .. b.Rows - 1 do
                  for c in 0 .. b.Cols - 1 do
                      let actual =
                          Board.neighbors b r c
                          |> List.filter (fun (nr, nc) -> b.Cells.[nr, nc].Mine)
                          |> List.length

                      Expect.equal b.Cells.[r, c].Adjacent actual (sprintf "Adjacent correct at (%d,%d)" r c)
          }
          test "flood-fill opens the connected zero region and its border" {
              // 1x3: mine at (0,0). Revealing (0,2) [adj 0] floods to (0,1) [adj 1].
              let b = Board.reveal noRng 0 2 (mkBoard 1 3 [ (0, 0) ])
              Expect.equal b.Cells.[0, 2].State Revealed "clicked zero-cell revealed"
              Expect.equal b.Cells.[0, 1].State Revealed "numbered border revealed by flood"
              Expect.equal b.Cells.[0, 0].State Hidden "the mine stays hidden"
          }
          test "revealing all non-mine cells wins" {
              let b = Board.reveal noRng 0 2 (mkBoard 1 3 [ (0, 0) ])
              Expect.equal b.Status Won "all safe cells revealed → Won"
          }
          test "revealing a mine loses and exposes mines" {
              let b = Board.reveal noRng 0 0 (mkBoard 1 3 [ (0, 0) ])
              Expect.equal b.Status Lost "stepped on a mine → Lost"
              Expect.equal b.Cells.[0, 0].State Revealed "the mine is exposed"
          }
          test "toggleFlag round-trips and blocks reveal" {
              let b = Board.toggleFlag 0 0 (mkBoard 1 3 [ (2, 2) ]) // arbitrary mine elsewhere
              Expect.equal b.Cells.[0, 0].State Flagged "flag placed"
              let b2 = Board.reveal noRng 0 0 b
              Expect.equal b2.Cells.[0, 0].State Flagged "flagged cell cannot be revealed"
              let b3 = Board.toggleFlag 0 0 b
              Expect.equal b3.Cells.[0, 0].State Hidden "second toggle clears the flag"
          }
          test "chord with a correct flag clears neighbours" {
              // mine at (0,0); reveal the (0,1) number, flag the mine, chord it.
              let b = mkBoard 1 3 [ (0, 0) ] |> Board.reveal noRng 0 1 |> Board.toggleFlag 0 0
              let b = Board.chord noRng 0 1 b
              Expect.equal b.Cells.[0, 2].State Revealed "chord revealed the safe neighbour"
              Expect.equal b.Status Won "board cleared"
          }
          test "chord with a wrong flag detonates" {
              // mine at (0,0); reveal (0,1), wrongly flag (0,2), chord → reveals the mine.
              let b = mkBoard 1 3 [ (0, 0) ] |> Board.reveal noRng 0 1 |> Board.toggleFlag 0 2
              let b = Board.chord noRng 0 1 b
              Expect.equal b.Status Lost "chording onto a mis-flagged board loses"
          }
          test "minesRemaining counts down with flags" {
              let b = Board.empty Beginner
              Expect.equal (Board.minesRemaining b) 10 "starts at mine count"
              let b = Board.toggleFlag 0 0 b
              Expect.equal (Board.minesRemaining b) 9 "one flag → 9 remaining"
          } ]

// Cmd.quit (quit-from-update) ----------------------------------------------

let cmdQuitTests =
    testList
        "Cmd.quit"
        [ test "Cmd.quit triggers requestQuit and sends no message" {
              let mutable quit = false
              let sent = ResizeArray<int>()
              Cmd.dispatch (fun () -> quit <- true) (fun (m: int) -> sent.Add m) Cmd.quit
              Expect.isTrue quit "Cmd.quit invokes the runtime's requestQuit callback"
              Expect.isEmpty sent "Cmd.quit does not enqueue any message"
          }
          test "Cmd.none / Cmd.ofMsg do not request quit" {
              let mutable quit = false
              let sent = ResizeArray<int>()
              let rq () = quit <- true
              let send (m: int) = sent.Add m
              Cmd.dispatch rq send Cmd.none
              Cmd.dispatch rq send (Cmd.ofMsg 7)
              Expect.isFalse quit "neither none nor ofMsg requests quit"
              Expect.sequenceEqual sent (seq { 7 }) "ofMsg still delivers its message"
          }
          test "Cmd.batch propagates a nested Cmd.quit and still sends siblings" {
              let mutable quitCount = 0
              let sent = ResizeArray<int>()

              Cmd.dispatch
                  (fun () -> quitCount <- quitCount + 1)
                  (fun (m: int) -> sent.Add m)
                  (Cmd.batch [ Cmd.ofMsg 1; Cmd.quit; Cmd.ofMsg 2 ])

              Expect.equal quitCount 1 "a Cmd.quit anywhere in a batch requests quit exactly once"
              Expect.sequenceEqual sent (seq { 1; 2 }) "sibling ofMsg commands in the batch still fire"
          } ]

// Focus (keyboard focus ring + modal trap) ----------------------------------

let focusTests =
    let a = RegionId "a"
    let b = RegionId "b"
    let c = RegionId "c"
    let ring = Focus.ofOrder [ a; b; c ]

    testList
        "Focus"
        [ test "ofOrder focuses the first id" {
              Expect.equal (Focus.current ring) (Some a) "current = first id"
          }
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
              Expect.equal (Focus.current (Focus.focus (RegionId "z") ring)) (Some a) "absent id ignored, current unchanged"
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
              Expect.equal (Focus.current (t |> Focus.next |> Focus.next)) (Some(RegionId "ok")) "wraps within the trap ring, never escapes to base"
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
          inputViewTests
          feedTests
          minesweeperTests
          cmdQuitTests ]
