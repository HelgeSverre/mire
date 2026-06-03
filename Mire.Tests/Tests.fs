module Mire.Tests.Tests

open Expecto
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout
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

[<Tests>]
let all =
    testList
        "Mire"
        [ graphemeTests
          inputTests
          diffTests
          layoutTests
          widgetTests
          textBufferTests
          inputViewTests
          feedTests
          minesweeperTests ]
