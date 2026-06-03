namespace Mire.Core

[<Struct>]
type Color =
    | Rgb of r: byte * g: byte * b: byte
    | Default

    static member Black = Rgb(0uy, 0uy, 0uy)
    static member White = Rgb(255uy, 255uy, 255uy)
    static member Red = Rgb(239uy, 83uy, 80uy)
    static member Green = Rgb(102uy, 187uy, 106uy)
    static member Blue = Rgb(66uy, 165uy, 245uy)
    static member Yellow = Rgb(255uy, 238uy, 88uy)
    static member Magenta = Rgb(171uy, 71uy, 188uy)
    static member Cyan = Rgb(38uy, 198uy, 218uy)
    static member Gray = Rgb(120uy, 120uy, 120uy)
    static member DarkGray = Rgb(60uy, 60uy, 60uy)
    static member LightGray = Rgb(180uy, 180uy, 180uy)

    member this.ToHex() =
        match this with
        | Rgb(r, g, b) -> $"#{r:X2}{g:X2}{b:X2}"
        | Default -> ""

    member this.ToAnsiFg() =
        match this with
        | Rgb(r, g, b) -> $"\x1b[38;2;{r};{g};{b}m"
        | Default -> "\x1b[39m"

    member this.ToAnsiBg() =
        match this with
        | Rgb(r, g, b) -> $"\x1b[48;2;{r};{g};{b}m"
        | Default -> "\x1b[49m"
