# Terminal protocol

Mire targets modern, Kitty-compatible terminals and uses their protocol features
directly. Most of this is handled for you by the runtime and the `Diff` writer; this
guide covers the parts you touch as an app author. The escape sequences themselves live
in `Mire.Protocol.ANSI` — don't scatter raw `\x1b[…]` literals through your code;
everything flows through named bindings there.

## What the runtime opts into

On startup `Runtime.run` enters the alternate screen and enables, then restores on exit:

- **Alternate screen** (`?1049h`) and **synchronized output** (`?2026h`) — each frame's
  changed cells are bracketed so the frame appears atomically (no tearing).
- **Truecolor** foreground/background (`38;2` / `48;2`).
- **Kitty keyboard protocol** with event-type reporting (`>3u`).
- **Mouse tracking** (`?1002` / `?1006`) and **focus events** (`?1004`).
- **Bracketed paste** (`?2004`), reassembled across reads.
- **Theme notifications** (`?2031`) — only if you opt in with `withThemeNotifications`.

You don't manage any of this; it's setup/teardown around your loop.

## OSC 8 hyperlinks

A run of cells can carry a clickable URL via `Style.WithLink`:

```fsharp
let linked = Style.text.WithLink "https://example.com"
Text.text "docs" linked
```

The link isn't an SGR attribute — the `Diff` writer brackets a linked run in OSC 8
open/close sequences and closes any open link at frame end. Because `Link` is part of
the `Style` record, diff runs split at link boundaries automatically. `Markdown` link
spans carry their real URL the same way.

## OSC 52 clipboard

Copy to the system clipboard from `update` with a command:

```fsharp
| Copy -> model, Cmd.setClipboard model.SelectedText
```

It's written straight to the terminal outside the cell diff (it paints nothing). Works
on terminals with clipboard write enabled (Ghostty/Kitty/iTerm2); silently ignored
elsewhere.

## Raw escapes — `Cmd.writeRaw`

`Cmd.writeRaw s` is the escape hatch for out-of-band terminal effects that paint no
cells — window-title sets, notifications, or any sequence you build from `ANSI`.
`Cmd.setClipboard` and the Kitty-graphics commands are built on it.

```fsharp
| SetTitle t -> model, Cmd.writeRaw (sprintf "\x1b]0;%s\x1b\\" t)
```

## Kitty graphics

`ImagePreview` (a base widget) draws a portable, captioned placeholder box that renders
on every terminal:

```fsharp
ImagePreview.render width height borderStyle captionStyle "logo.png" (Some (640, 480))
```

On Kitty/Ghostty you overlay the *real* pixels with a command — Mire never decodes
images, so you supply already-base64-encoded PNG bytes:

```fsharp
// transmit-and-display a PNG at cell (col, row), sized to a cols×rows box:
| ShowImage -> model, Cmd.kittyImage col row cols rows pngBase64
| Hide      -> model, Cmd.clearImages
```

The payload is chunked at 4096 base64 bytes per the protocol. The image is an *overlay*
on top of the cell grid — re-issue it after a frame that repaints its region. Pattern:
render `ImagePreview` as the reserved cell region (the fallback that shows on
unsupported terminals), and on supported terminals issue `Cmd.kittyImage` positioned at
that region's screen coordinates.

`ANSI.kittyImage` / `ANSI.deleteImages` expose the raw sequence builders if you need
them.

## Grapheme widths

The cell grid is monospace, so display *width* matters. `Mire.Core.Grapheme` measures
it correctly:

- `Grapheme.stringWidth s` — the column count of a string, measured by **grapheme
  cluster** (UAX #29). Emoji ZWJ sequences, regional-indicator flags, base+combining
  runs, and VS15/VS16 presentation all resolve to the right width; astral scalars
  (surrogate pairs) count once. There's an ASCII fast path for the common case.
- `Grapheme.clusters s` / `Grapheme.clusterWidth cluster` — the pieces, if you need them.

You rarely call these directly — `Surface.Write` iterates clusters, so a wide glyph
occupies two columns with an empty *continuation* cell in its trailing column (so the
diff repaints it cleanly when narrower content replaces it), and an astral emoji lands
in a single cell instead of split surrogate halves. Layout uses `stringWidth` to size
`Text`. If you build custom drawing, measure with `Grapheme`, not `String.length`.

## Headless / non-terminal use

None of the above runs in the headless `--dump`/snapshot path — `Layout.measure` and
`Layout.render` are pure and write to a `Surface`, no escapes involved. That's what makes
layout testable without a tty (see [Architecture → headless rendering](architecture.md#headless-rendering)).
