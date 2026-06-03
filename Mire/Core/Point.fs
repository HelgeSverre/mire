namespace Mire.Core

[<Struct>]
type Point =
    { X: int
      Y: int }

    static member Origin = { X = 0; Y = 0 }
    static member Create(x, y) = { X = x; Y = y }
    member this.Add(dx, dy) = { X = this.X + dx; Y = this.Y + dy }

    member this.Add(other: Point) =
        { X = this.X + other.X
          Y = this.Y + other.Y }
