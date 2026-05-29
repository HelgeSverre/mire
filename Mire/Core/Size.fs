namespace Mire.Core

[<Struct>]
type Size =
    { Width: int
      Height: int }
    static member Empty = { Width = 0; Height = 0 }
    static member Create(width, height) = { Width = width; Height = height }
    member this.Area = this.Width * this.Height
    member this.IsEmpty = this.Width <= 0 || this.Height <= 0
    member this.IsValid = this.Width > 0 && this.Height > 0
