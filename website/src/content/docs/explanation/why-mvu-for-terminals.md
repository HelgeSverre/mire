---
title: Why MVU for the terminal
description: The reasoning behind an Elm-style architecture for a TUI, and what changes when the platform is a cell grid.
category: explanation
order: 2
---

Terminal UIs have historically been written imperatively: move the cursor here, write
these bytes, remember what you drew so you can erase it later. That style scales badly —
the bug surface is all the places where your idea of the screen and the actual screen
drift apart. Mire takes the opposite stance: you describe the screen you want, and the
runtime reconciles it. Here is why that fits the terminal especially well.

## A terminal is already a grid

The Model-View-Update pattern needs a cheap way to turn "what I want" into "what to
change." In the browser that means diffing a tree against the DOM. A terminal is simpler:
the screen is a fixed grid of character cells. Diffing two grids is a linear scan, and
the output is a handful of escape sequences for the runs that changed. The data structure
the platform imposes is the one diffing wants. MVU and the terminal meet in the middle.

## Pure description beats manual erasure

The hardest bugs in imperative TUIs are redraw bugs: a panel that doesn't clear its old
content, a cursor left in the wrong place, an artifact after a resize. They exist because
the code holds two models of the screen — the one in your head and the one on the glass —
and keeps them in sync by hand.

A pure `view` removes the second model. You return a complete description of the screen
for the current state; the runtime figures out the difference from the last frame. There
is nothing to erase, because you never mutated anything. Resize, overlay, scroll — all of
them are just a different description, diffed the same way.

## State in one place

Because `update` is the only thing that changes the model, the entire behavior of the app
is one function you can read top to bottom. There is no hidden state in widgets — widgets
are pure functions of the values you pass them, so a list doesn't secretly remember its
own selection; your model does. When something is wrong on screen, it is wrong in the
model, and the model is one record.

## The cost, honestly

MVU is not free. You write more types up front — a message for every interaction, an
explicit model. For a three-line script that prints colored text, that is overkill; reach
for `printf` and ANSI codes. MVU earns its keep when an app has state that evolves:
selection, scrolling, overlays, async work, focus. That is the kind of app Mire is for —
coding agents, chat interfaces, log and diff viewers, dashboards.

## What's different from Elm or Elmish

The shape is the same — `init`/`update`/`view`, commands, subscriptions — but the runtime
owns things a browser would otherwise provide:

- **Input is raw.** There are no DOM events; `MapInput` turns decoded terminal bytes into
  messages. You decide what every key means.
- **Layout is yours.** No CSS, no flexbox from the platform. The `LayoutNode` tree and
  `Length` sizing are Mire's layout engine, and they run every frame.
- **Output is bytes.** The "render" target is escape sequences, gated by a cell diff and
  wrapped in synchronized output.

The payoff is the same as Elm's: a UI you can reason about, test without the platform, and
extend without fear of redraw bugs. The difference is that here, Mire *is* the platform.

## Related

- [The loop and the render pipeline](/docs/explanation/the-loop/) — the mechanics.
- [Your first app](/docs/tutorials/your-first-app/) — see the pattern in 40 lines.
