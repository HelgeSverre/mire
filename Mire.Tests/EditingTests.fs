/// Editor invariants: grapheme-safe motion/deletion, selection precedence for
/// word deletes, anchor bounds after every edit, and column-accurate views.
module Mire.Tests.EditingTests

open Expecto
open Mire.Core
open Mire.Renderer
open Mire.Layout

let private rowText (s: Surface) (y: int) : string =
    String.concat "" [ for x in 0 .. s.Size.Width - 1 -> s.[x, y].Grapheme ]

let private buf text cursor anchor : TextBuffer =
    { Text = text
      Cursor = cursor
      Anchor = anchor }

/// Cursor and anchor within bounds and on grapheme boundaries.
let private valid (b: TextBuffer) =
    let ok p =
        p >= 0 && p <= b.Text.Length && Grapheme.isBoundary b.Text p

    ok b.Cursor && (b.Anchor |> Option.forall ok)

let private isValidUtf16 (s: string) =
    let mutable ok = true

    for i in 0 .. s.Length - 1 do
        if
            System.Char.IsHighSurrogate s.[i]
            && not (i + 1 < s.Length && System.Char.IsLowSurrogate s.[i + 1])
        then
            ok <- false

        if
            System.Char.IsLowSurrogate s.[i]
            && not (i > 0 && System.Char.IsHighSurrogate s.[i - 1])
        then
            ok <- false

    ok

let private draw (w: int) (h: int) (node: LayoutNode<unit>) =
    let s = Surface(Size.Create(w, h))
    Layout.measure (Rect.Create(0, 0, w, h)) node |> Layout.render s
    s

let private allActions =
    [ InsertText "x"
      InsertText "😀"
      Newline
      DeleteBack
      DeleteForward
      DeleteWordBack
      DeleteWordForward
      CursorLeft
      CursorRight
      CursorUp
      CursorDown
      WordLeft
      WordRight
      LineStart
      LineEnd
      DocStart
      DocEnd
      SelectAll
      Select CursorLeft
      Select CursorRight
      Select WordLeft
      Select WordRight
      Select CursorUp
      Select CursorDown ]

[<Tests>]
let graphemeEditingTests =
    testList
        "Grapheme-safe editing"
        [ test "Backspace after an emoji removes the whole surrogate pair" {
              let b = TextBuffer.backspace (TextBuffer.Of "😀")
              Expect.equal b.Text "" "no lone surrogate left"
              Expect.equal b.Cursor 0 "caret at start"
          }
          test "Delete before an emoji removes the whole pair" {
              let b = TextBuffer.delete (buf "a😀b" 1 None)
              Expect.equal b.Text "ab" "pair removed"
          }
          test "Left/Right step over a base + combining mark as one unit" {
              let t = "éx" // é (decomposed) then x
              let r = TextBuffer.right (buf t 0 None)
              Expect.equal r.Cursor 2 "stepped over e + U+0301"
              Expect.equal (TextBuffer.left r).Cursor 0 "and back"
          }
          test "Backspace removes a whole ZWJ family emoji" {
              let fam = "👨‍👩‍👧"
              let b = TextBuffer.backspace (TextBuffer.Of("a" + fam))
              Expect.equal b.Text "a" "whole cluster removed"
          }
          test "a cursor inside a surrogate pair is snapped to the boundary before any edit" {
              let b = TextBuffer.insert "x" (buf "😀" 1 None)
              Expect.isTrue (isValidUtf16 b.Text) "text stays valid UTF-16"
              Expect.equal b.Text "x😀" "inserted at the snapped boundary"
          }
          test "a selection ending inside a ZWJ sequence snaps to cluster boundaries" {
              let t = "a👨‍👩‍👧b"
              let b = TextBuffer.deleteSelection (buf t 3 (Some 0))
              Expect.isTrue (isValidUtf16 b.Text) "valid UTF-16"
              Expect.isTrue (b.Text.EndsWith "b") "the family emoji is either kept or removed whole"
          }
          test "Up/Down keep the display column across wide glyphs" {
              // line 0: "世界x" — x at column 4; line 1: "abcdef"
              let t = "世界x\nabcdef"
              let down = TextBuffer.down (buf t 2 None) // after 世界 → column 4
              Expect.equal down.Cursor (4 + 4) "column 4 on the next line (offset 4 + line start 4)"
              let up = TextBuffer.up (buf t (4 + 3) None) // column 3 on line 1
              Expect.equal up.Cursor 1 "column 3 is inside 界 → lands before it"
          }
          test "random action sequences never produce invalid UTF-16 or out-of-range anchors" {
              let rnd = System.Random 1234
              let seeds = [ "one two"; "😀 a👨‍👩‍👧 b"; "é 世界\nline2 x"; "" ]

              for seed in seeds do
                  let mutable b = TextBuffer.Of seed

                  for _ in 1..400 do
                      let a = allActions.[rnd.Next allActions.Length]
                      b <- TextEdit.apply a b
                      Expect.isTrue (valid b) $"invariants hold after {a} on {b}"
                      Expect.isTrue (isValidUtf16 b.Text) $"valid UTF-16 after {a}"
          } ]

[<Tests>]
let selectionInvariantTests =
    testList
        "Selection invariants"
        [ test "forward word delete with a reverse selection deletes the selection and clears the anchor" {
              let b = TextBuffer.deleteWordForward (buf "one two" 0 (Some 7))
              Expect.equal b.Text "" "the whole selected buffer was deleted"
              Expect.equal b.Anchor None "anchor cleared"
              // Previously this left Anchor = Some 7 over a 4-char string; the next
              // insert then threw in Substring.
              Expect.equal (TextBuffer.insert "x" b).Text "x" "a following insert works"
          }
          test "word-backspace with everything selected deletes everything" {
              let b =
                  TextBuffer.deleteWordBack (TextBuffer.selectAll (TextBuffer.Of "one two three"))

              Expect.equal b.Text "" "selection wins over word deletion"
          }
          test "forward word delete without a selection still deletes to the word end" {
              let b = TextBuffer.deleteWordForward (buf "one two" 3 None)
              Expect.equal b.Text "one" "deleted ' two'"
              Expect.equal b.Anchor None "no anchor"
          }
          test "a stale anchor past the end is clamped instead of throwing" {
              let b = TextBuffer.insert "x" (buf "ab" 1 (Some 99))
              Expect.equal b.Text "ax" "selection [1,2) replaced"
          } ]

[<Tests>]
let columnViewTests =
    testList
        "Column-accurate editing views"
        [ test "wrapLine measures columns: 世界 at width 2 wraps into two rows" {
              Expect.equal (Mire.Widgets.TextArea.wrapLine 2 "世界") [ (0, "世"); (1, "界") ] "one wide glyph per row"
          }
          test "wrapLine never splits a cluster and still partitions the line" {
              let line = "a😀b👨‍👩‍👧c éf"

              for w in 1..6 do
                  let segs = Mire.Widgets.TextArea.wrapLine w line
                  Expect.equal (segs |> List.map snd |> String.concat "") line $"partition at width {w}"

                  for (start, _) in segs do
                      Expect.isTrue (Grapheme.isBoundary line start) $"segment starts on a boundary (w={w})"

                  for (_, t) in segs do
                      Expect.isTrue
                          (Grapheme.stringWidth t <= w || Grapheme.clusters t |> List.length = 1)
                          $"segment '{t}' fits width {w}"
          }
          test "Input draws the caret on the right cell after a wide glyph" {
              let cur = Style.Default.WithBackground(Color.White)

              let s =
                  draw 6 1 (Mire.Widgets.Input.render 6 Style.Default cur true (buf "世ab" 1 None))

              Expect.equal s.[0, 0].Grapheme "世" "wide glyph at column 0"
              Expect.equal s.[2, 0].Grapheme "a" "a at column 2"
              Expect.equal s.[2, 0].Style.Background (Some Color.White) "caret on a"
          }
          test "Input keeps the caret visible when scrolled past wide glyphs" {
              let cur = Style.Default.WithBackground(Color.White)
              let text = "世界世界世界"

              let s =
                  draw 4 1 (Mire.Widgets.Input.render 4 Style.Default cur true (TextBuffer.Of text))
              // caret after the text → a one-column block at the window's end
              let caretCells =
                  [ for x in 0..3 do
                        if s.[x, 0].Style.Background = Some Color.White then
                            yield x ]

              Expect.equal caretCells [ 3 ] "caret block on the last column"
              Expect.isTrue ((rowText s 0).Contains "界") "tail of the text visible"
          }
          test "TextArea.renderWrapped places a CJK line's caret on the wrapped row" {
              let cur = Style.Default.WithBackground(Color.White)
              let b = buf "世界世界" 2 None // before the second 世 (after 世界)
              let s = draw 4 2 (Mire.Widgets.TextArea.renderWrapped 4 2 Style.Default cur true b)
              Expect.equal (rowText s 0) "世界" "first row holds two wide glyphs (4 cols)"
              Expect.equal s.[0, 1].Style.Background (Some Color.White) "caret at the start of row 2"
          } ]
