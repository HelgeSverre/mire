/// Geometry contracts: scroll content extents honor explicit child sizes, nested
/// content is clipped to its ancestors, and hit regions use the same (scrolled,
/// clipped, occluded) geometry as painting.
module Mire.Tests.GeometryTests

open Expecto
open Mire.Core
open Mire.Renderer
open Mire.Layout
open Mire.App

let private rowText (s: Surface) (y: int) : string =
    String.concat "" [ for x in 0 .. s.Size.Width - 1 -> s.[x, y].Grapheme ]

let private z = Rect.Create(0, 0, 0, 0)
let private text s : LayoutNode<unit> = Text(z, s, Style.Default)

let private scrollAt (offY: int) child : LayoutNode<unit> =
    Scroll(
        z,
        { OffsetX = 0
          OffsetY = offY
          ContentWidth = 0
          ContentHeight = 0 },
        child
    )

let private draw (w: int) (h: int) (node: LayoutNode<unit>) =
    let s = Surface(Size.Create(w, h))
    let laid = Layout.measure (Rect.Create(0, 0, w, h)) node
    Layout.render s laid
    s, laid

[<Tests>]
let scrollExtentTests =
    testList
        "Scroll content extent"
        [ test "explicit Cells children size the scroll content (A at row 0, B at row 4)" {
              let content: LayoutNode<unit> =
                  Stack(
                      z,
                      Vertical,
                      [ { Length = Cells 4; Child = text "A" }
                        { Length = Cells 4; Child = text "B" } ]
                  )

              Expect.equal (Layout.contentExtent Vertical content) 8 "extent = 4 + 4, not 2 intrinsic rows"

              let s, _ = draw 1 2 (scrollAt 4 content)
              Expect.equal (rowText s 0) "B" "scrolling to row 4 reaches B"

              let s2, _ = draw 1 2 (scrollAt 99 content)
              Expect.equal (rowText s2 0) " " "over-scroll clamps to content height - viewport (row 6)"
          }
          test "a docked Top (Cells n) contributes n rows" {
              let content: LayoutNode<unit> =
                  Dock(
                      z,
                      [ { Position = Top(Cells 3)
                          Child = text "head" }
                        { Position = DockPosition.Fill
                          Child = text "body" } ]
                  )

              Expect.equal (Layout.contentExtent Vertical content) 4 "3 header rows + 1 body row"
          }
          test "Box children overlap, so a box's extent is its largest child plus the border" {
              let b: LayoutNode<unit> = Box(z, Style.Default, [ text "a\nb"; text "c" ])
              Expect.equal (Layout.contentExtent Vertical b) 4 "max(2, 1) + 2"
          }
          test "tab-bearing text is measured as rendered (8-column stops)" {
              Expect.equal (Layout.contentExtent Horizontal (text "a\tb")) 9 "a, tab to col 8, b"
          } ]

[<Tests>]
let clippingTests =
    testList
        "Ancestor clipping"
        [ test "an overflowing stack child can't overwrite its box's border" {
              // 4x4 box, 2x2 interior; the stack's Content child is 3 rows tall.
              let node: LayoutNode<unit> =
                  Box(
                      z,
                      Style.Default,
                      [ Stack(
                            z,
                            Vertical,
                            [ { Length = Content
                                Child = text "A\nB\nC" } ]
                        ) ]
                  )

              let s, _ = draw 4 4 node
              Expect.equal (rowText s 3) "└──┘" "bottom border intact"
              Expect.equal (rowText s 2) "│B │" "rows inside the interior still render"
          }
          test "an over-wide text in a narrow stack column is cut at the stack's edge" {
              let node: LayoutNode<unit> =
                  Stack(
                      z,
                      Horizontal,
                      [ { Length = Cells 3
                          Child =
                            Stack(
                                z,
                                Horizontal,
                                [ { Length = Cells 6
                                    Child = text "abcdef" } ]
                            ) }
                        { Length = Cells 2; Child = text "XY" } ]
                  )

              let s, _ = draw 5 1 node
              Expect.equal (rowText s 0) "abcXY" "the sibling isn't overwritten"
          }
          test "render restores the surface's clip afterwards" {
              let s, _ = draw 4 4 (Box(z, Style.Default, [ text "x" ]))
              Expect.equal s.Clip (Rect.Create(0, 0, 4, 4)) "full clip restored"
          } ]

[<Tests>]
let hitRegionTests =
    testList
        "Hit regions"
        [ test "a Focusable scrolled into view is hit at its screen position" {
              let content: LayoutNode<unit> =
                  Stack(
                      z,
                      Vertical,
                      [ { Length = Cells 3
                          Child = Focusable(z, RegionId "top", text "t") }
                        { Length = Cells 3
                          Child = Focusable(z, RegionId "bottom", text "b") } ]
                  )

              let _, laid = draw 10 2 (scrollAt 3 content)
              let regions = Layout.collectRegions laid
              Expect.equal (Layout.regionAt regions (Point.Create(0, 0))) (Some(RegionId "bottom")) "row 0 shows bottom"

              Expect.isFalse
                  (regions |> List.exists (fun (id, _) -> id = RegionId "top"))
                  "top scrolled out → no region"
          }
          test "a region partly scrolled out is clipped to the viewport" {
              let content: LayoutNode<unit> =
                  Stack(
                      z,
                      Vertical,
                      [ { Length = Cells 1; Child = text "-" }
                        { Length = Cells 4
                          Child = Focusable(z, RegionId "tall", text "x") } ]
                  )

              let _, laid = draw 5 3 (scrollAt 2 content)

              Expect.equal
                  (Layout.collectRegions laid)
                  [ (RegionId "tall", Rect.Create(0, 0, 5, 3)) ]
                  "rows 1-4 of content, offset 2 → screen rows 0..2 (clipped at 3)"
          }
          test "an opaque overlay layer blocks the region beneath it" {
              let node: LayoutNode<unit> =
                  Overlay(
                      z,
                      [ Focusable(z, RegionId "under", text "button")
                        Positioned(z, Center, Cells 4, Cells 2, Filled(z, Style.Default)) ]
                  )

              let _, laid = draw 10 4 node
              let regions = Layout.collectRegions laid
              Expect.isNone (Layout.regionAt regions (Point.Create(4, 1))) "click on the panel doesn't fall through"

              Expect.equal
                  (Layout.regionAt regions (Point.Create(0, 0)))
                  (Some(RegionId "under"))
                  "uncovered part still hits"
          }
          test "a modal scrim blocks everything beneath while the dialog on top stays clickable" {
              let node: LayoutNode<unit> =
                  Overlay(
                      z,
                      [ Focusable(z, RegionId "app", text "app")
                        Scrim(z, Color.Rgb(0uy, 0uy, 0uy), 0.5)
                        Positioned(z, Center, Cells 4, Cells 1, Focusable(z, RegionId "ok", text "OK")) ]
                  )

              let _, laid = draw 10 3 node
              let regions = Layout.collectRegions laid
              Expect.isNone (Layout.regionAt regions (Point.Create(0, 0))) "app blocked by the scrim"
              Expect.equal (Layout.regionAt regions (Point.Create(4, 1))) (Some(RegionId "ok")) "dialog button hit"
          }
          test "a Focusable's own background doesn't block the Focusable itself" {
              let node: LayoutNode<unit> =
                  Focusable(z, RegionId "btn", Overlay(z, [ Filled(z, Style.Default); text "ok" ]))

              let _, laid = draw 4 1 node

              Expect.equal
                  (Layout.regionAt (Layout.collectRegions laid) (Point.Create(1, 0)))
                  (Some(RegionId "btn"))
                  "hit"
          } ]

let private mouse button pressed moved : MouseEvent =
    { X = 0
      Y = 0
      Button = button
      Modifiers = KeyModifiers.None
      Pressed = pressed
      Moved = moved }

[<Tests>]
let pointerCaptureTests =
    testList
        "Pointer capture"
        [ test "a drag stays with the pressed region and the release ends capture" {
              let a, b = Some(RegionId "a"), Some(RegionId "b")
              let t1, c1 = PointerCapture.route None a (mouse MouseButton.Left true false)
              Expect.equal (t1, c1) (a, a) "press captures a"
              let t2, c2 = PointerCapture.route c1 b (mouse MouseButton.Left true true)
              Expect.equal (t2, c2) (a, a) "drag over b still goes to a"
              let t3, c3 = PointerCapture.route c2 None (mouse MouseButton.Left false false)
              Expect.equal (t3, c3) (a, None) "release delivered to a, capture ends"
              let t4, _ = PointerCapture.route c3 b (mouse MouseButton.Left true true)
              Expect.equal t4 b "motion after release hit-tests normally"
          }
          test "wheel events hit-test under the pointer and don't disturb a capture" {
              let a, b = Some(RegionId "a"), Some(RegionId "b")
              let t, c = PointerCapture.route a b (mouse ScrollUp true false)
              Expect.equal (t, c) (b, a) "wheel → region under the pointer"
          } ]
