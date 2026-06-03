# Research Brief — Terminal Rendering Performance

## Objective

Understand how modern terminal emulators and TUI runtimes achieve fast, smooth
rendering — the non-obvious gotchas, performance tricks, and hard-won lessons —
so the **Mire** F# TUI runtime (diff-based, retained-mode, Kitty-targeting) can
adopt proven techniques and avoid known traps.

## Key Design Questions

1. **Diffing & damage tracking** — How do systems decide what to redraw? Cell-level
   diff, dirty regions, damage rectangles, or full repaint? When does each win?
2. **Output batching & flush strategy** — How do they minimize syscalls / escape
   sequence bytes? Synchronized output (DECSET 2026)? Coalescing writes?
3. **GPU vs CPU rendering** — Where does GPU help (glyph atlas, quad batching) and
   where is it overkill for a TUI runtime that emits ANSI to a host terminal?
4. **Scrolling** — Why is smooth scrolling hard? What tricks (scroll regions,
   line shifting, pixel-level scroll, fractional scroll) make it smooth?
5. **Unicode / grapheme width** — What correctness+perf traps exist (wcwidth,
   emoji, ZWJ, combining marks, ambiguous width, grapheme clustering)?
6. **Escape sequence parsing** — Fast VTE/state-machine parsing, costs of cursor
   addressing vs relative moves, SGR coalescing.
7. **Latency & frame pacing** — Input-to-photon latency, vsync, frame scheduling,
   why 30/60/120fps, typometer-style measurement.
8. **Accessibility** — How does rendering architecture interact with screen
   readers (UIA), and what perf/architecture constraints does a11y impose?

## Systems

| System               | Focus                                                          |
| -------------------- | -------------------------------------------------------------- |
| Textual / Textualize | TUI framework; smoother-scrolling article + related blog posts |
| Kitty                | GPU terminal; rendering architecture, perf claims, protocols   |
| Alacritty            | GPU terminal; "fastest terminal" design, damage tracking       |
| WezTerm              | GPU terminal (adjacent); glyph cache, shaping                  |
| Hyper.js             | Electron/web terminal (adjacent); xterm.js perf                |
| Warp.dev             | Rust GPU terminal/Metal; custom UI framework, blocks           |
| Microsoft Terminal   | AtlasEngine/DxEngine docs + terminal-a11y-2023.md              |

## Per-System Report Template

1. Overview (what it is, rendering stack)
2. Rendering architecture (pipeline: model → diff → output/GPU)
3. Performance tricks & non-obvious gotchas (the meat — bullet list w/ detail)
4. Relevance to Mire (a diff-based ANSI-emitting retained-mode TUI)
5. Sources (URLs) + confidence markers 🟢🟡🔴
