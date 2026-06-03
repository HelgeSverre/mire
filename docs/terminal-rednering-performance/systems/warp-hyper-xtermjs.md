# Warp.dev vs. Hyper.js / xterm.js — Terminal Rendering Performance

Research notes for the Mire F# TUI runtime. Focus: how two terminals built on radically different stacks render text fast, and the non-obvious gotchas they hit. Every claim traces to a URL in §5.

Confidence markers: 🟢 directly stated by primary source (vendor blog / PR) · 🟡 stated by secondary source or inferred from primary · 🔴 weak / dated / my extrapolation.

---

## 1. Overview — three very different stacks

|                  | **Warp**                                     | **Hyper.js**                                      | **xterm.js (VS Code, Hyper)**                        |
| ---------------- | -------------------------------------------- | ------------------------------------------------- | ---------------------------------------------------- |
| Stack            | Rust + Metal/`wgpu`, custom GPU UI framework | Electron (Chromium + Node) shell hosting xterm.js | TypeScript lib; DOM / Canvas / WebGL renderers       |
| Renders by       | Drawing triangles on the GPU directly        | Whatever xterm.js does, inside a web page         | DOM nodes → 2D canvas → WebGL (evolution)            |
| Owns the pixels? | Yes, every pixel via Metal/wgpu shaders      | No — delegates to xterm.js + browser              | Partially — canvas/WebGL bypass layout, DOM does not |

The key framing: **Warp and xterm.js are terminal _emulators_** — they own a VT100/PTY byte stream and a cell grid, and the hard problem is rasterizing glyphs onto a GPU fast. **Mire is a retained-mode TUI _runtime_** that _emits_ ANSI into someone else's emulator. So the rendering layer these projects sweat over (glyph atlas, GPU upload) is _below_ Mire — it is the emulator Mire talks to. What transfers to Mire is the architectural reasoning (diffing, dirty regions, batching, cache keys), not the pixel pipeline. (See §4.) 🟢 [warp how-it-works], 🟢 [vscode-renderer-blog]

Note: "Hyper.js" itself is just an Electron app that embeds xterm.js; its rendering performance story _is_ the xterm.js story plus Electron overhead. So §2–3 treat xterm.js as the substance and Hyper/Electron as a tax on top. 🟢 [xterm-webgl-pr] (Imms: WebGL renderer "powers the terminals in VS Code and Hyper")

---

## 2. Rendering architecture

### 2.1 Warp — Rust + Metal, a homegrown browser-shaped UI framework

- Warp started on **Electron, then pivoted to Rust + Metal** rendering directly on the GPU. Rust was chosen for speed, ecosystem, and the ability to compile one codebase to Mac/Linux/Windows and eventually WASM. 🟢 [warp how-it-works]
- Rust's GUI ecosystem was inadequate (Azul, Druid both experimental, no Metal backend), so Warp **built its own UI framework** with **Nathan Sobo** (Atom co-founder), "loosely inspired by Flutter" — effectively "building the architecture of a browser": an element tree rendered through pluggable GPU backends. This is the lineage that relates to Zed's **GPUI**. 🟢 [warp how-it-works]
- **The whole renderer is three primitives: rectangles, images, and glyphs** (glyphs via a texture atlas). Metal shaders for all three fit in **~200 lines**. New UI components (snackbar, context menu, block) are _compositions_ of these primitives and never touch the shaders. 🟢 [warp how-it-works]
- Metal is deliberately low-level — "the API essentially only allows rendering triangles … or sampling from a texture." Warp leans into that minimal surface rather than fighting it. 🟢 [warp how-it-works]
- **Performance**: average screen redraw **1.9 ms**, sustaining **>144 FPS** even at 4K with heavy UI, matching Alacritty-class speed while being far more UI-heavy. 🟢 [warp how-it-works]
- **Linux/cross-platform** moved off Metal-specific APIs to **`wgpu` + `winit` + `cosmic-text`**; ~98% of code is shared with the Mac app. 🟢 [warp-linux]

### 2.2 Warp's "blocks" — a different _data model_, not just a UI skin

- Traditional terminals keep **one grid** matching the VT100 spec; the shell thinks it's talking to a teletype and can move the cursor anywhere, so output bleeds across the screen. 🟢 [warp how-it-works]
- Warp instead keeps a **separate grid per command/output block**, which prevents one command's output from clobbering another's region and makes per-block search/copy/scroll possible. Blocks are populated via **shell hooks** (zsh/fish `precmd`/`preexec`, `bash-preexec`) that emit Device Control Strings carrying JSON metadata. 🟢 [warp how-it-works]
- Rendering implication: scrolling/relayout operate over a _list of block grids_ rather than a single ring buffer — closer to a document/list model than a classic scrollback buffer. 🟡 (inferred from the per-block-grid description) [warp how-it-works]

### 2.3 xterm.js — the DOM → Canvas → WebGL evolution

All three implement a common `IRenderer` interface; the consumer picks one. 🟡 [xterm-deepwiki]

1. **DOM (2017 and before, now the safe fallback).** Each visible row is HTML elements. Hit a **hard ceiling**: "composing elements and doing a layout could take longer than a frame (16.6 ms) all by itself." Cannot hold 60 FPS regardless of optimization. 🟢 [vscode-renderer-blog]
2. **Canvas (2017).** Rewrote onto `<canvas>` 2D. Layered into **4 canvases** (text, selection, link underline, cursor), a **dirty-region model** ("a slim internal model … the minimal amount of information about a cell's drawn state"), and a **texture atlas** pre-rendering ASCII into a GPU-resident `ImageBitmap` to replace slow `fillText`. Result: **~5–45× faster**, 60 FPS replacing sub-10 FPS during heavy streaming. 🟢 [vscode-renderer-blog]
3. **WebGL (2019, Daniel Imms / "Tyriar", now default GPU path).** Builds a **single `Float32Array` of all vertex data** and uploads it in one go with a vertex+fragment shader, instead of per-char draw calls. Caches every glyph (Unicode, emoji, combined chars) in a **texture atlas trimmed to minimal rectangles** for packing efficiency. Requires **WebGL2** (~68% desktop support at merge; no Safari then). 🟢 [xterm-webgl-pr]

The canvas renderer was later **moved to an addon, then deprecated/removed**; DOM stays as the no-GPU fallback. In VS Code this is the `terminal.integrated.gpuAcceleration: on|auto|off` setting (auto default since 1.55). 🟢 [xterm-release-notes], 🟡 [vscode-webgl-issue]

---

## 3. Performance tricks & non-obvious gotchas (most important)

### GPU text rendering — the subpixel / glyph-atlas problem (Warp)

- **G1 — The atlas cache key is the whole game.** The rasterizer's output depends on how the _vector_ glyph overlaps the _pixel grid_, so a key of `(font_id, glyph_id, font_size)` is **wrong** — it ignores subpixel position. Warp's correct key is **`(font_id, glyph_id, font_size, fract(glyph_position.x))`**. 🟢 [warp-text-rendering]
- **G2 — The crisp-vs-kerned trap.** The easy fix (Warp shipped it "for a long time") is to **snap each glyph's x to the nearest pixel** to avoid GPU blending → crisp text but **wrong kerning**. Allowing true fractional positions → correct kerning but **blurry text** (GPU blends the sampled texture). You cannot have both naively. 🟢 [warp-text-rendering]
- **G3 — Subpixel quantization buckets.** The resolution: split each pixel into **3 subpixel buckets** (offsets **0.0, 0.33, 0.66**) and round the position to the nearest bucket. Keys then quantize to a tiny set instead of the infinite range of float fractions — atlas stays useful, kerning stays good, blur stays imperceptible. 🟢 [warp-text-rendering]
- **G4 — Don't double-count the offset.** Because ~0.33 px of horizontal position is _baked into the rasterized glyph_, you must **subtract that baked offset** from the position handed to the GPU, or the glyph drifts. Easy off-by-a-third bug. 🟢 [warp-text-rendering]
- **G5 — Glyph cache, not line cache.** A line cache stores many redundant copies of the same monospace glyph and is invalidated constantly as stdout/stderr writes change line contents; a **per-glyph cache** is the better memory/perf tradeoff. 🟢 [warp-text-rendering]
- **G6 — Rejected fancier options.** Interpolating between the two nearest rasterized glyphs (true subpixel interp) and supersampling were both rejected: more complexity (potentially reading two glyphs on two atlas textures) for visually imperceptible gain. The shipped fix was **<200 lines** (mostly Rust + minor Metal edits) — "more learning than code." 🟢 [warp-text-rendering]
- **G7 — Minimize the primitive set.** Warp's biggest leverage: realize a terminal only needs **rect / image / glyph**, so shaders are ~200 lines and never change. Complexity lives in _composition_, not the GPU pipeline. 🟢 [warp how-it-works]

### Why DOM rendering is slow (xterm.js/VS Code)

- **G8 — Layout is an unbeatable hard cap.** Browser layout/compose of row elements can exceed the **16.6 ms** frame budget _by itself_ — no optimization beats it. This is _the_ reason DOM terminals top out. 🟢 [vscode-renderer-blog]
- **G9 — GC thrash.** Thousands of DOM nodes for cells generate enough garbage that **GC "pushes out rendering time by a noticeable amount."** 🟢 [vscode-renderer-blog]
- **G10 — Monospace isn't monospace.** Some Unicode chars render at variable width even in "monospace" fonts; the DOM fix (wrap chars in fixed-width spans) **adds more elements and slows layout further** — a vicious cycle. 🟢 [vscode-renderer-blog]
- **G11 — Native selection fights you.** Terminal selection spans pages and cells in ways the DOM selection model wasn't built for, forcing custom override logic. 🟢 [vscode-renderer-blog]

### Canvas vs WebGL tradeoffs (xterm.js)

- **G12 — Per-char draw calls are the canvas bottleneck.** Canvas issues many individual instructions and trusts the browser to batch/optimize; WebGL instead **packs one `Float32Array` and does a single upload** → up to **~900% faster** (901% on Windows 87×26; 839% at 300×80). 🟢 [xterm-webgl-pr], 🟢 [vscode-webgl-issue]
- **G13 — Canvas is _unreliable_, not just slow.** Electron/Chromium `drawImage` regressions and configs that "slow to a crawl"; and if GPU accel is off, canvas drops to **CPU rasterization** — worse than DOM. So the fallback order is deliberately **WebGL → DOM** (skip canvas). 🟡 [vscode-webgl-issue]
- **G14 — Measure FPS and fall back at runtime.** VS Code/xterm.js **monitors achieved FPS** and drops WebGL → DOM if it's far below expected, catching broken Chromium/GPU interactions that "supported" feature flags lied about. 🟡 [vscode-webgl-issue]
- **G15 — Trim glyphs to minimal rectangles.** The WebGL atlas stores each glyph's tight bounding box, not a fixed cell, for far better texture packing. 🟢 [xterm-webgl-pr]
- **G16 — Atlas eviction is crude on purpose.** At merge, atlas-full → **clear and restart** rather than juggle multiple textures. Later (v5.0) a **multi-row packing** strategy (place glyph in the best-fit row by pixel height, not just the active row) and (v5.1) **multi-page atlases** (512×512 tiles growing to 4096×4096) gave effectively unlimited glyph space and faster GPU uploads. 🟢 [xterm-webgl-pr], 🟡 [xterm-release-notes]
- **G17 — Upload only the changed subset.** Don't re-send the whole attribute buffer each frame; uploading only the changed slice markedly improved scrolling. 🟢 [xterm-webgl-pr]
- **G18 — WebGL2-only by choice.** Required for vertex array objects + instanced drawing; the cost was dropping Safari/old-browser support (the DOM fallback covers them). 🟢 [xterm-webgl-pr]

### Electron overhead (Hyper)

- **G19 — Electron was fast enough to _start_ but not to _win_.** Warp explicitly tried Electron and abandoned it for native Rust+GPU to hit Alacritty-class throughput. An Electron terminal pays Chromium + Node + IPC overhead on top of whatever renderer xterm.js uses; the renderer choice (DOM/canvas/WebGL) matters _more_ there because there's less headroom. 🟢 [warp how-it-works], 🟡 (inference)

### Accessibility gotcha (applies to anyone who owns their pixels)

- **G20 — Owning the pixels means owning a11y.** Custom GPU UI gets **no free screen-reader / keyboard-nav / selection** from the OS or DOM — all must be reimplemented. The same warning applies to any non-DOM renderer (xterm.js canvas/WebGL keep a hidden accessibility buffer for this reason). 🟢 [warp how-it-works]

---

## 4. Relevance to Mire (F# diff-based ANSI TUI)

Crucial distinction: **Mire is not an emulator.** Warp and xterm.js _consume_ a PTY byte stream and _rasterize glyphs_. Mire _produces_ ANSI and lets the host emulator (Ghostty, Kitty) do the rasterization. So the GPU/atlas pixel work in §3 happens **one layer below Mire** — Mire's job is to emit the _minimal correct ANSI_ and let the emulator's (likely WebGL-class) renderer be fast.

**Transfers directly:**

- **Dirty-region / diff thinking (G8–G9 → Mire's `Diff.compute`).** xterm.js's canvas renderer keeps "a slim internal model … the minimal amount of information about a cell's drawn state" and only redraws changed cells — this is _exactly_ Mire's diff-based `Surface` → `DiffRun` model. The DOM disaster (G8/G9) is empirical proof that **rebuilding the whole view every frame and trusting a downstream layout/optimizer is the slow path** — validating Mire's "build full Surface, diff, emit only changed runs" architecture. 🟢 [vscode-renderer-blog]
- **Batch the output, minimize syscalls (G12/G17).** The WebGL win is "pack everything, one upload." Mire's analog: **coalesce diff runs into as few `cursorTo` + write sequences as possible and flush once per frame** to one `TextWriter`, rather than many small writes. "Upload only the changed subset" = emit ANSI only for changed cells. This is the single most transferable trick. 🟢 [xterm-webgl-pr]
- **Quantize a cache key (G1/G3).** The lesson generalizes: when caching, the key must include _everything the output depends on_, then **quantize continuous inputs into a few buckets** so the cache actually hits. If Mire ever caches styled-run → ANSI strings, the key is `(text, Style)` and `Style` is already a small struct DU — good, that bucketing is free. 🟢 [warp-text-rendering]
- **Minimal primitive set (G7).** Warp's "only rect/image/glyph" mirrors Mire keeping all drawing behind a small bounds-checked `Surface` primitive set. Resist growing it.

**Does NOT transfer (emulator-only concerns):**

- Glyph atlases, subpixel quantization, kerning, Metal/WebGL shaders, texture packing (G2–G6, G15–G16, G18) — these are the _host emulator's_ problem. Mire should just emit clusters and trust the emulator. The relevant adjacent concern for Mire is **grapheme/East-Asian width correctness** (already in `Core/Grapheme.fs`), which is the TUI-side analog of xterm.js's G10 "monospace isn't monospace" pain.
- Blocks-as-separate-grids (§2.2) is a _terminal_ feature; Mire's layout tree is already a richer document model, so it doesn't need Warp's escape-from-the-single-grid hack.
- Electron/a11y (G19/G20) — N/A; Mire inherits the host terminal's a11y.

**One caution:** don't over-index on "GPU = fast." For Mire the bottleneck is **bytes written to the PTY and the emulator's parse cost**, not pixel fill. The xterm.js parse-cost note (lower CPU lets program output parse faster) is the relevant axis: **smaller, well-batched ANSI diffs make the _downstream_ emulator faster too.** 🟡 [vscode-webgl-issue]

---

## 5. Sources

- `[warp how-it-works]` 🟢 How Warp Works — https://www.warp.dev/blog/how-warp-works
- `[warp-text-rendering]` 🟢 Adventures in Text Rendering: Kerning and Glyph Atlases — https://www.warp.dev/blog/adventures-text-rendering-kerning-glyph-atlases
- `[warp-linux]` 🟢 Warp for Linux (wgpu/winit/cosmic-text, 98% shared) — https://www.warp.dev/blog/warp-for-linux
- `[xterm-webgl-pr]` 🟢 xterm.js WebGL Renderer PR #1790 (Tyriar/Daniel Imms) — https://github.com/xtermjs/xterm.js/pull/1790
- `[vscode-renderer-blog]` 🟢 VS Code Integrated Terminal Performance Improvements (DOM→canvas, 2017) — https://code.visualstudio.com/blogs/2017/10/03/terminal-renderer
- `[vscode-webgl-issue]` 🟡 VS Code #106202 Transition default terminal renderer to WebGL (FPS fallback, 900%, reliability) — https://github.com/microsoft/vscode/issues/106202
- `[xterm-release-notes]` 🟡 xterm.js 5.0.0 / 5.1.0 release notes (canvas→addon→deprecated; multi-row & multi-page atlas) — https://github.com/xtermjs/xterm.js/releases/tag/5.1.0
- `[xterm-deepwiki]` 🟡 xterm.js architecture overview (IRenderer, three renderers) — https://deepwiki.com/xtermjs/xterm.js/1-overview
- 🟡 Experimental WebGL terminal renderer PR #84440 — https://github.com/microsoft/vscode/pull/84440
- 🟢 Daniel Imms confirms authorship (CSS-Tricks, WebGL text rendering background) — https://css-tricks.com/techniques-for-rendering-text-with-webgl/

_Researched May 2026 for Mire. Code is source of truth for what Mire actually does; this doc is external prior art._
