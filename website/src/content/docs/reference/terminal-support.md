---
title: Terminal support
description: The terminal protocols Mire relies on, which terminal emulators support them, and where Mire degrades gracefully.
category: reference
order: 6
---

Mire targets **modern, Kitty-compatible terminals** — truecolor, the Kitty keyboard
protocol, SGR mouse, synchronized output. Legacy terminals, 16-color palettes, and the
Windows console are explicitly out of scope. This page lists the protocols Mire actually
speaks and which emulators implement them, so you can tell whether your terminal will run
a Mire app well.

## Recommended

Mire is developed against **Ghostty** first, and works fully on **kitty**, **WezTerm**,
and **foot**. Any of these gives you the complete feature set — disambiguated keys, key
release/repeat, mouse, tear-free frames, hyperlinks, and (where the terminal supports it)
inline images.

## What Mire requires

These are non-negotiable — without them a Mire app won't render correctly:

- **Truecolor SGR** (`38;2;r;g;b` / `48;2;…`). Mire emits 24-bit color only; there is no
  256-color or 16-color fallback. Effectively every modern terminal supports this.
- **Alternate screen, cursor control, UTF-8.** Standard since xterm.

## What Mire uses when available

Each of these degrades gracefully — a terminal that ignores the private mode simply
doesn't get that feature; nothing breaks.

### Kitty keyboard protocol

Mire pushes progressive-enhancement flags **1 + 2** (`CSI > 3 u`): _disambiguate escape
codes_ (so e.g. `Esc`, `Ctrl+key`, and the arrows are unambiguous) and _report event
types_ (key press / repeat / release). Without it you still get keys via the legacy
encodings; with it, input is unambiguous and key-release events become available
(`Program.withKeyReleases`).

Supported by: **Alacritty, foot, Ghostty, iTerm2, kitty, Microsoft Terminal, Rio, TuiOS,
Warp, WezTerm, xterm.js** (per the
[protocol's own list](https://sw.kovidgoyal.net/kitty/keyboard-protocol/)).

### Mouse (button-event tracking + SGR coordinates)

Modes `1002` + `1006`: clicks, wheel, and drag, with unlimited coordinates. A held-button
drag is distinguishable from a click (`MouseEvent.Moved`). Supported by essentially every
modern terminal. (Hover / motion-without-button is not enabled.) A single read carrying
several events — a fast scroll or drag burst — decodes them all, so input never lags.

### Synchronized output

Mode `2026` — each frame is wrapped so the terminal paints it atomically, preventing tear.
Supported by Ghostty, kitty, iTerm2, WezTerm, Contour, and others; silently ignored
elsewhere.

### Bracketed paste & focus reporting

Modes `2004` and `1004` — pasted text arrives as one `Paste` event (reassembled even if
split across reads), and the app is told when it gains/loses focus. Widely supported.

### Light/dark theme notifications

DEC mode `2031` (with DSR `996` query) — the terminal reports its color scheme and tells
the app when it changes, so `Program.withThemeNotifications` can retheme live. Supported by
Contour, kitty, Ghostty. Opt-in; ignored elsewhere.

### Hyperlinks (OSC 8) & clipboard (OSC 52)

`OSC 8` makes runs clickable (`Style.WithLink`); `OSC 52` copies to the system clipboard
(`Cmd.setClipboard`). Both are broadly supported and harmless where they aren't.

### Inline images (Kitty graphics protocol)

`ImagePreview` always draws a portable text fallback; on terminals that implement the
[Kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/) an app can
overlay the real image with `Cmd.kittyImage`. Supported by **Ghostty, kitty, Konsole, st
(patched), Warp, wayst, WezTerm, iTerm2, xterm.js**.

## Internal inventory

The exact sequences, where they live in the code, and the current gaps (e.g. mouse drag
motion, multi-event reads) are tracked in
[`docs/PROTOCOLS.md`](https://github.com/HelgeSverre/mire/blob/main/docs/PROTOCOLS.md) in
the repo.
