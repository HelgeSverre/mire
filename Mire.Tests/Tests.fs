module Mire.Tests.Tests

open Expecto
open Mire.Core
open Mire.Protocol
open Mire.Renderer
open Mire.Layout

// Helpers ------------------------------------------------------------------

/// Extract the KeyEvent from a parsed InputEvent, if any.
let private asKey (e: InputEvent option) : KeyEvent option =
    match e with
    | Some (Key k) -> Some k
    | _ -> None

/// Read a surface row as a string (concatenated graphemes).
let private rowText (s: Surface) (y: int) : string =
    String.concat "" [ for x in 0 .. s.Size.Width - 1 -> s.[x, y].Grapheme ]

// Grapheme width -----------------------------------------------------------

let graphemeTests =
    testList "Grapheme" [
        test "ASCII is one cell" {
            Expect.equal (Grapheme.charWidth 'A') 1 "ASCII char is width 1"
        }
        test "CJK is two cells" {
            Expect.equal (Grapheme.charWidth '世') 2 "CJK ideograph is width 2"
        }
        test "combining mark is zero cells" {
            Expect.equal (Grapheme.charWidth '́') 0 "combining acute is width 0"
        }
        test "stringWidth sums mixed widths" {
            Expect.equal (Grapheme.stringWidth "a世b") 4 "1 + 2 + 1 = 4"
        }
        test "empty string is width 0" {
            Expect.equal (Grapheme.stringWidth "") 0 "empty string is width 0"
        }
    ]

// Input parsing ------------------------------------------------------------

let inputTests =
    testList "InputParser" [
        test "printable ASCII" {
            let k = asKey (InputParser.parseBytes [| 0x41uy |])
            Expect.equal (k |> Option.map (fun k -> k.Key)) (Some (Char "A")) "0x41 → Char \"A\""
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
        test "empty input is None" {
            Expect.isNone (InputParser.parseBytes [||]) "no bytes → no event"
        }
    ]

// Frame diffing ------------------------------------------------------------

let diffTests =
    testList "Diff" [
        test "identical surfaces produce no runs" {
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
    ]

// Layout -------------------------------------------------------------------

let private stackChild len child : StackChild<unit> = { Length = len; Child = child }

let layoutTests =
    testList "Layout" [
        test "vertical stack flows children sequentially" {
            let node : LayoutNode<unit> =
                LayoutNode.Stack(Rect.Create(0, 0, 0, 0), Vertical,
                    [ stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), "a", Style.Default))
                      stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), "b", Style.Default)) ])
            let rects =
                match Layout.measure (Rect.Create(0, 0, 10, 4)) node with
                | LayoutNode.Stack(_, _, kids) ->
                    kids |> List.choose (fun c ->
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
            let node : LayoutNode<unit> =
                LayoutNode.Dock(Rect.Create(0, 0, 0, 0),
                    [ { Position = Top Content; Child = LayoutNode.Text(Rect.Create(0, 0, 0, 0), "x\ny", Style.Default) }
                      { Position = DockPosition.Fill; Child = LayoutNode.Empty } ])
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
            let node : LayoutNode<unit> =
                LayoutNode.Stack(Rect.Create(0, 0, 0, 0), Vertical,
                    [ stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), "top", Style.Default))
                      stackChild Length.Fill LayoutNode.Empty
                      stackChild Length.Fill LayoutNode.Empty ])
            // Measure indirectly by checking the y-positions the children land at.
            let ys =
                match Layout.measure (Rect.Create(0, 0, 10, 9)) node with
                | LayoutNode.Stack(_, _, kids) ->
                    kids |> List.map (fun c ->
                        match c.Child with
                        | LayoutNode.Text(r, _, _) -> r.Y
                        | LayoutNode.Empty -> -1
                        | _ -> -2)
                | _ -> []
            // 9 rows: 1 fixed + (8 split → 4 + 4). First child at y=0.
            Expect.equal ys.Head 0 "fixed child at top"
        }
        test "scroll offset windows the content (offset 1 hides first row)" {
            let content : LayoutNode<unit> =
                LayoutNode.Stack(Rect.Create(0, 0, 0, 0), Vertical,
                    [ for i in 1 .. 5 ->
                        stackChild (Cells 1) (LayoutNode.Text(Rect.Create(0, 0, 0, 0), sprintf "r%d" i, Style.Default)) ])
            let node : LayoutNode<unit> =
                LayoutNode.Scroll(Rect.Create(0, 0, 0, 0), { ScrollState.Empty with OffsetY = 1 }, content)
            let laid = Layout.measure (Rect.Create(0, 0, 6, 2)) node
            let surf = Surface(Size.Create(6, 2))
            Layout.render surf laid
            Expect.isTrue ((rowText surf 0).StartsWith "r2") "offset 1 → first visible row is r2"
            Expect.isTrue ((rowText surf 1).StartsWith "r3") "second visible row is r3"
        }
    ]

[<Tests>]
let all =
    testList "Mire" [
        graphemeTests
        inputTests
        diffTests
        layoutTests
    ]
