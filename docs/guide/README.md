# Mire user guide

Mire is a small, composable F# runtime for **modern terminal UIs** — Elmish/TEA
with the terminal as the platform: cell-diffed rendering, region-based layout, raw
input decoding, and direct control over the terminal protocol. It targets modern
Kitty-compatible terminals (Ghostty first) and **.NET 10**.

These guides are task-oriented and grounded in the shipping code — when a guide and
the source disagree, the source wins. For the _why_ behind the design, see
[`SPEC.md`](../../SPEC.md); for what's built and what's next, see
[`ROADMAP.md`](../../ROADMAP.md).

## Reading order

1. **[Getting started](getting-started.md)** — install, your first app, running it.
2. **[Architecture](architecture.md)** — the MVU loop: `Program`, `Cmd`, `Sub`, `Runtime`, and the render pipeline.
3. **[Layout](layout.md)** — the `LayoutNode` tree: `Stack`, `Dock`, `Box`, `Scroll`, `Overlay`, `Positioned`, `Focusable`, and `Length` sizing.
4. **[Widgets](widgets.md)** — the widget catalog: text, panels, lists, tables, inputs, overlays, status chrome, and more.
5. **[Styling & theming](styling-and-theming.md)** — `Color`, `Style`, and the swappable `AppTheme`.
6. **[Input](input.md)** — decoding keys, mouse, paste, focus, and theme changes; routing them to messages.
7. **[Text editing](text-editing.md)** — `TextBuffer`, the `TextEdit` keymap, selection, and the `Input`/`TextArea` widgets.
8. **[Terminal protocol](terminal-protocol.md)** — ANSI, terminal modes, OSC 8 links, OSC 52 clipboard, Kitty graphics, and grapheme widths.
9. **[The agent layer](agent-layer.md)** — `Mire.Agent`: `ChatTranscript`, `PromptBox`, `ApprovalModal`, `DiffView`, and the `agentShell` sample.

## Learn by example

The repo ships runnable apps you can read and run:

- **`samples/Gallery`** (`just gallery`) — every base widget in its states across seven tabbed pages. The fastest way to see what's available.
- **`samples/AgentShell`** (`just shell`) — a minimal coding-agent shell composing the agent layer on the default theme with zero theme code.
- **`Mire.Demo.Agent`** — the comprehensive, canonical showcase (palettes, overlays, streaming, diffs, mouse).
- **`Mire.Demo.Feed`** — a multi-feed RSS reader (the first adopter of the `Focus` manager).
- **`Mire.Demo.Spreadsheet`** — an A1 grid with in-cell editing and a formula engine.

Every demo and sample supports a headless `-- --dump` mode that renders representative
screens to plain text — the easiest way to see layout without taking over your terminal.
