namespace Mire.Core

[<Struct>]
type Rect =
    { X: int
      Y: int
      Width: int
      Height: int }

    static member Create(x, y, width, height) =
        { X = x
          Y = y
          Width = width
          Height = height }

    static member FromPoints(topLeft: Point, bottomRight: Point) =
        { X = topLeft.X
          Y = topLeft.Y
          Width = bottomRight.X - topLeft.X + 1
          Height = bottomRight.Y - topLeft.Y + 1 }

    static member FromOrigin(size: Size) =
        { X = 0
          Y = 0
          Width = size.Width
          Height = size.Height }

    member this.Left = this.X
    member this.Top = this.Y
    member this.Right = this.X + this.Width - 1
    member this.Bottom = this.Y + this.Height - 1
    member this.TopLeft = { X = this.X; Y = this.Y }
    member this.BottomRight = { X = this.Right; Y = this.Bottom }

    member this.Size =
        { Width = this.Width
          Height = this.Height }

    member this.IsEmpty = this.Width <= 0 || this.Height <= 0

    member this.Contains(point: Point) =
        point.X >= this.Left
        && point.X <= this.Right
        && point.Y >= this.Top
        && point.Y <= this.Bottom

    member this.Contains(other: Rect) =
        other.Left >= this.Left
        && other.Right <= this.Right
        && other.Top >= this.Top
        && other.Bottom <= this.Bottom

    member this.Intersects(other: Rect) =
        not (
            other.Right < this.Left
            || other.Left > this.Right
            || other.Bottom < this.Top
            || other.Top > this.Bottom
        )

    member this.Intersect(other: Rect) =
        let x1 = max this.Left other.Left
        let y1 = max this.Top other.Top
        let x2 = min this.Right other.Right
        let y2 = min this.Bottom other.Bottom

        if x1 <= x2 && y1 <= y2 then
            { X = x1
              Y = y1
              Width = x2 - x1 + 1
              Height = y2 - y1 + 1 }
        else
            { X = 0; Y = 0; Width = 0; Height = 0 }

    member this.Offset(dx, dy) =
        Rect.Create(this.X + dx, this.Y + dy, this.Width, this.Height)

    member this.ClipTo(clipRect: Rect) = this.Intersect(clipRect)

    member this.Inflate(dx, dy) =
        { X = this.X - dx
          Y = this.Y - dy
          Width = this.Width + dx * 2
          Height = this.Height + dy * 2 }
