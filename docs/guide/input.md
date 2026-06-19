# Input

The runtime reads raw bytes from the terminal, decodes them into an `InputEvent`, and
hands each to your `MapInput` (and, for mouse, optionally to a region handler). You map
events to messages; the rest of MVU takes over.

## Input events

```fsharp
type InputEvent =
    | Key of KeyEvent
    | Mouse of MouseEvent
    | Paste of string            // bracketed paste, reassembled across reads
    | Resize of Size
    | FocusGained
    | FocusLost
    | ThemeChanged of ColorScheme   // requires withThemeNotifications
    | Tick of TimeSpan
```

A `KeyEvent` carries the decoded key, its modifiers, and (with the Kitty protocol) its
event type:

```fsharp
type Key =
    | Char of string             // a printable grapheme, e.g. Char "a"
    | Enter | Escape | Backspace | Tab | Space | Delete | Insert
    | ArrowUp | ArrowDown | ArrowLeft | ArrowRight
    | Home | End | PageUp | PageDown
    | Function of int            // F1–F35
    | Unknown of string

type KeyModifiers = { Shift: bool; Ctrl: bool; Alt: bool; Meta: bool }
type KeyEventType = Press | Repeat | Release

type KeyEvent =
    { Key: Key; Text: string option; Modifiers: KeyModifiers; Repeat: bool; EventType: KeyEventType }
```

## Mapping input to messages

`MapInput : InputEvent -> 'msg option` — return `Some msg` to act, `None` to ignore.
Match on the event and the key:

```fsharp
let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | Char c when ke.Modifiers.Ctrl && c = "p" -> Some OpenPalette
        | Char _ | Space | Backspace | Delete -> Some (Edit e)   // hand the raw event to the editor
        | Enter -> Some Submit
        | ArrowUp -> Some Up
        | Tab when ke.Modifiers.Shift -> Some PrevField
        | Tab -> Some NextField
        | _ -> None
    | Paste _ -> Some (Edit e)
    | Resize s -> Some (Resized s)
    | _ -> None
```

`MapInput` can't see your model, so route *which overlay/field* should handle a key in
`update` — carry the raw event (or a normalized key) in a message and decide there.
This is why editors take `Edit of InputEvent`: the modifiers survive for word chords.

## What's decoded

For modern Kitty-compatible terminals, input decoding is complete:

- **Keyboard** — the Kitty keyboard protocol (`CSI u`): modifier chords, the
  `:event` sub-param (press/repeat/release), and the private-use functional codepoints
  (numeric keypad, F13–F35, media keys). Legacy `CSI`/`SS3` arrows and function keys
  decode too.
- **Mouse** — SGR 1006 (`?1006`): position, button, wheel, modifiers, and the motion bit
  (a button-held drag sets `MouseEvent.Moved`, so a drag is distinguishable from a click).
- **Bracketed paste** (`?2004`) — reassembled across reads into one `Paste s` even when
  a large paste is split over multiple buffers.
- **Focus** (`?1004`) — `FocusGained` / `FocusLost`.

A single `read()` can carry several sequences back-to-back (a scroll/drag burst, fast
typing, queued escapes); the parser tokenizes the buffer into per-event spans and the
runtime processes them all, so nothing is dropped. Multi-byte UTF-8 keystrokes (accented
letters, CJK, emoji) decode to a `Char` key too.

## Key releases

The runtime asks the terminal to report event types (`CSI > 3 u`), so every keystroke
produces a **press *and* a release**. By default the runtime **drops releases**, so the
common case stays one message per keystroke. Opt in if you track key-down/up (games,
chords); `Repeat` always passes through:

```fsharp
program |> Program.withKeyReleases true
// then check ke.EventType in MapInput: Press | Repeat | Release
```

## Quit policy

`Ctrl+C` ends the loop by default (restoring the terminal). Replace the policy with
`withQuitOn` — it runs *before* `MapInput`, so a matched event is consumed as quit:

```fsharp
// Make Ctrl+C a normal key; exit only via Cmd.quit from update:
program |> Program.withQuitOn (fun _ -> false)
```

## Theme notifications

Opt in to light/dark color-scheme reporting (DEC mode 2031). The runtime enables the
mode and queries the current scheme at startup; changes arrive as `ThemeChanged` through
`MapInput`, so you can swap your `AppTheme` on the fly:

```fsharp
program |> Program.withThemeNotifications true
// MapInput:  | ThemeChanged Dark -> Some UseDark  | ThemeChanged Light -> Some UseLight
```

## Mouse hit-testing

Beyond raw `Mouse` events, the runtime can hit-test clicks against a **retained region
table** built from the last rendered frame, so you focus or activate UI by id instead
of recomputing rectangles by hand.

1. Tag the clickable subtrees in your `view` with `Focusable.region`:

```fsharp
Focusable.region (RegionId "accept") (Text.text " [ Accept ] " theme.selection)
Focusable.region (RegionId "deny")   (Text.text " ‹ Deny › "  theme.fgMuted)
```

2. Install a handler. It gets the **topmost** region under the cursor (or `None`) and
   the mouse event; returning `Some msg` consumes the event (it does *not* also reach
   `MapInput`). The default returns `None`, so apps that don't use it are unaffected and
   mouse events flow to `MapInput` as before.

```fsharp
let onMouseRegion (region: RegionId option) (me: MouseEvent) : Msg option =
    if me.Pressed && me.Button = MouseButton.Left then
        match region with
        | Some r when r = RegionId "accept" -> Some Accept
        | Some r when r = RegionId "deny"   -> Some Deny
        | _ -> None
    else None

program
|> Program.withMapInput mapInput          // wheel, keys, etc.
|> Program.withMouseRegion onMouseRegion  // clicks on tagged regions
```

Regions nested inside a `Scroll` are omitted from the table (their rects live in the
scroll's virtual content space, not screen space). For wheel scrolling, handle the
`Mouse` event's `ScrollUp`/`ScrollDown` button in `MapInput` as usual.

`Mire.Layout` also exposes the pure pieces if you need them directly:
`Layout.collectRegions tree` returns the `(RegionId * Rect)` table, and
`Layout.regionAt regions point` returns the topmost hit.

## Keyboard focus rings

Mouse hit-testing is the spatial half of focus; the keyboard half is `Mire.Layout.Focus`
— a pure tab-order ring with a modal-trap stack, held as one field in your model and
driven entirely in `update`. It pairs naturally with `Focusable.region` (use the same
`RegionId`s): `Focus.ofOrder`, `Focus.next`/`prev`, `Focus.focus`, `Focus.isFocused`,
`Focus.pushTrap`/`popTrap` (for modals). `Mire.Demo.Feed` is the reference adopter.
