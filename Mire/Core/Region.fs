namespace Mire.Core

type RegionId = RegionId of string

type ScrollState =
    { OffsetX: int
      OffsetY: int
      ContentWidth: int
      ContentHeight: int }

    static member Empty =
        { OffsetX = 0
          OffsetY = 0
          ContentWidth = 0
          ContentHeight = 0 }

type RenderMode =
    | Normal
    | Overlay
    | Floating
    | Portal
    | Offscreen

type Region =
    { Id: RegionId
      Rect: Rect
      ZIndex: int
      Clip: bool
      Scroll: ScrollState option
      Focusable: bool
      RenderMode: RenderMode }

    static member Create(id: string, rect: Rect) =
        { Id = RegionId id
          Rect = rect
          ZIndex = 0
          Clip = true
          Scroll = None
          Focusable = false
          RenderMode = Normal }
