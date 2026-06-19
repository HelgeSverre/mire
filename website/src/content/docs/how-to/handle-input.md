---
title: Handle keyboard, mouse, and paste
description: Route decoded input events to your messages — keys and chords, the mouse wheel, pasted text, and changing the quit key.
category: how-to
order: 1
---

The runtime decodes raw terminal bytes into an `InputEvent` and hands each to your
`MapInput`. You return `Some msg` to act or `None` to ignore.

## Match a key

```fsharp
let mapInput (e: InputEvent) : Msg option =
    match e with
    | Key ke ->
        match ke.Key with
        | Enter -> Some Submit
        | Escape -> Some Cancel
        | ArrowUp -> Some Up
        | Char "q" -> Some Quit
        | _ -> None
    | _ -> None
```

A printable key arrives as `Char "a"` (a string, since a key can be a multi-byte
grapheme). The other cases are `Enter`, `Escape`, `Backspace`, `Tab`, `Space`,
`Delete`, `Insert`, the arrows, `Home`/`End`/`PageUp`/`PageDown`, and `Function of int`
(F1–F35).

## Match a chord

Modifiers live on `ke.Modifiers` (`Shift`, `Ctrl`, `Alt`, `Meta`):

```fsharp
| Char c when ke.Modifiers.Ctrl && c = "p" -> Some OpenPalette
| Tab when ke.Modifiers.Shift -> Some PrevField
| Tab -> Some NextField
```

## Mouse wheel and clicks

A `Mouse` event carries position, button, and modifiers. The wheel comes through as
`ScrollUp`/`ScrollDown` buttons:

```fsharp
| Mouse me ->
    match me.Button with
    | ScrollUp -> Some (Scroll -3)
    | ScrollDown -> Some (Scroll 3)
    | _ -> None
```

For _clicking_ a specific piece of UI, don't compute rectangles by hand — tag the
clickable region and let the runtime hit-test it. See
[Mouse and focus](/docs/how-to/mouse-and-focus/).

## Accept pasted text

Bracketed paste is reassembled across reads into one event, even for large pastes:

```fsharp
| Paste s -> Some (Insert s)   // s is the full pasted text, newlines included
```

## Decide where a key goes

`MapInput` can't see your model, so route _which_ part of the UI handles a key inside
`update` — carry the raw event (or a normalized key) in a message and branch on your
state there:

```fsharp
// mapInput: hand editing keys through as a raw event
| Key ke ->
    match ke.Key with
    | Char _ | Space | Backspace | Delete -> Some (Edit e)
    | Escape -> Some CloseOverlay
    | _ -> None

// update: the open overlay (or the base view) decides what Edit means
let update msg m =
    match msg, m.Overlay with
    | Edit e, Some _ -> ...   // the modal's field handles it
    | Edit e, None   -> ...   // the base prompt handles it
```

## Change the quit key

`Ctrl+C` quits by default (and restores the terminal). The quit policy runs _before_
`MapInput`, so a matched event is consumed as quit:

```fsharp
// make Ctrl+C an ordinary key; quit only via Cmd.quit from update
program |> Program.withQuitOn (fun _ -> false)
```

## Get key releases

The runtime asks the terminal to report event types, so every keystroke is a press
_and_ a release. Releases are dropped by default (one message per keystroke). Opt in if
you track key-down/up; `ke.EventType` is then `Press | Repeat | Release`:

```fsharp
program |> Program.withKeyReleases true
```

## React to light/dark changes

Opt in to theme notifications and you'll get a `ThemeChanged` event when the terminal's
scheme changes — swap your `AppTheme` in response. See
[Theme your app](/docs/how-to/theme-your-app/#react-to-the-terminals-light-dark-setting).
