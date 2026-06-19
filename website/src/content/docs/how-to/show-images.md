---
title: Show an image
description: Render a portable placeholder with ImagePreview, and overlay real pixels on Kitty/Ghostty with the graphics protocol.
category: how-to
order: 6
---

Terminals split into those that speak the Kitty graphics protocol (Ghostty, Kitty —
real pixels) and those that don't. Mire handles both: a portable cell-grid placeholder
that renders everywhere, plus a command to overlay the real image where it's supported.
The framework never decodes images — you supply encoded bytes.

## The portable placeholder

`ImagePreview` draws a bordered, captioned box sized to a cell footprint. It renders on
every terminal and is what you put in your layout:

```fsharp
ImagePreview.render width height theme.border theme.fgMuted "logo.png" (Some (640, 480))
```

The last argument is the optional pixel size, shown as `640×480` when known.

## Overlay real pixels

On a graphics-capable terminal, issue a command to draw the actual image at a cell
position, sized to a `cols × rows` box. Supply the PNG already base64-encoded:

```fsharp
let png = Convert.ToBase64String(File.ReadAllBytes "logo.png")

let update msg m =
    match msg with
    | Show -> m, Cmd.kittyImage col row cols rows png   // transmit + display
    | Hide -> m, Cmd.clearImages
```

The payload is chunked at 4096 base64 bytes per the protocol. The image is an _overlay_
on top of the cell grid, so re-issue it after a frame that repaints its region.

## The pattern

Render `ImagePreview` as the reserved region (the fallback that shows on unsupported
terminals), and on supported terminals issue `Cmd.kittyImage` positioned at that
region's screen coordinates. The fallback shows through whenever the image can't be drawn.

`Cmd.writeRaw s` is the general escape hatch these commands are built on — use it for any
out-of-band sequence (window-title sets, notifications) that paints no cells. See
[the terminal protocol explanation](/docs/explanation/the-region-model/) and the
[Program/Cmd reference](/docs/reference/program-cmd-sub/#commands).
