# Terminal Rendering Performance: Alacritty & WezTerm

Research notes for the Mire F# TUI runtime. Focus: two Rust GPU terminals and what
their rendering pipelines teach a diff-based, ANSI-emitting TUI. Confidence markers:
🟢 primary source / direct claim, 🟡 secondary or partial, 🔴 weak / disputed / inferred.

> Note: Alacritty and WezTerm are **terminal emulators** (they sit below the PTY,
> turning a byte stream into pixels on a GPU). Mire is a **TUI runtime** (it sits
> above the PTY, turning a UI tree into a byte stream). The lessons that transfer are
> about _minimizing work per frame via damage/dirty tracking_ and _parser cost_, not
> about GPU specifics. See section 5.

---

## 1. Overview

### Alacritty

- Cross-platform OpenGL terminal emulator, written in Rust. Stated design goals, in
  order: **Correctness, Performance ("the fastest terminal emulator available
  anywhere"), Appearance, Simplicity, Portability**. 🟢
- Philosophy of **integration over reimplementation**: deliberately omits tabs, splits,
  and a GUI config editor, deferring those to the window manager / a multiplexer like
  tmux. Simplicity is partly a performance stance — less state, fewer code paths per
  frame. 🟢
- Originated from frustration that "using vim inside tmux in many terminals was a
  particularly bad experience… none of them were ever quite fast enough." 🟢
- Rendering stack: pulls bytes off the PTY, parses with the `vte` crate into an
  `alacritty_terminal` grid model, then rasterizes glyphs and draws them via OpenGL with
  a glyph atlas. Reading/parsing the PTY runs on a **separate thread** from repaint —
  a structural advantage in throughput benchmarks. 🟢
- Built its own library infrastructure because it didn't exist: a non-GPL clipboard lib,
  the `vte` parser, cross-platform font rasterization, and fontconfig bindings. 🟢

### WezTerm

- GPU-accelerated cross-platform terminal emulator, also Rust, by Wez Furlong. Far more
  feature-rich than Alacritty (tabs, splits, multiplexing, Lua config, ligatures). 🟢
- Rendering stack: a cross-platform `RenderState` abstraction over multiple GPU
  backends — **OpenGL** and **WebGpu** (the latter maps more directly to Metal/Vulkan/DX
  than the OpenGL→Metal translation layer). 🟢 The default backend has flip-flopped
  between WebGpu and OpenGL across releases. 🟡
- Text path: shaping with **HarfBuzz + FreeType**, then a layered set of caches feeding
  quad generation and a GPU draw. (Note: WezTerm also vendors the `allsorts` crate in its
  font stack, but the active shaper in the hot path is HarfBuzz; I could not confirm
  allsorts is on the render hot path from these sources. 🔴)

---

## 2. Rendering architecture (parse → grid → GPU; damage)

Both follow the same broad pipeline; the differences are in caching depth and damage.

```
PTY bytes ──▶ VT parser ──▶ grid/cell model ──▶ shape+rasterize ──▶ glyph atlas ──▶ GPU quads ──▶ swap
             (state machine) (rows × cols cells)  (HarfBuzz/FT or     (texture cache)  (instanced)
                                                   freetype)
```

### Alacritty

- **Parser:** the `vte` crate, an implementation of **Paul Williams' DEC ANSI parser
  state machine**. Table-/state-driven: the `Parser` does pure state bookkeeping and
  emits actions through a user-supplied `Perform` trait; semantic interpretation
  (what a CSI sequence _means_) lives in the consumer, not the parser. UTF-8 decoding is
  a separate `utf8parse` crate. This separation keeps the hot loop branch-predictable. 🟢
- **Grid:** `alacritty_terminal` holds the cell grid + scrollback as a model.
- **GPU:** glyph atlas (rasterized glyphs cached in a texture), instanced rendering of
  cell quads. A 2020 renderer rewrite (PR #4373) claimed **1.5–2× faster on average,
  up to ~20× in extreme cases**, and lowered the GL requirement toward OpenGL(ES) 2.0. 🟡
- **Damage:** historically Alacritty did **no** damage tracking — it re-rendered the
  whole grid every frame. It compensated by being _very_ fast at drawing empty cells
  (cheaper than glyph cells), so blank-screen frames were nearly free. 🟢 Damage
  tracking was added later (see section 4).

### WezTerm

- **Multi-level caching** is the defining feature. Observed cache layers:
  `shape_cache`, `line_to_ele_shape_cache`, `line_state_cache`, `line_quad_cache`,
  `glyph_cache`. The `line_quad_cache` shows very high hit rates in practice (~3471 hits
  vs ~64 misses in one user's stats) — once a line is drawn, subsequent frames reuse the
  computed quads. 🟢
- **Selective glyph storage:** a glyph's `texture` field is `Some(Sprite)` (atlas coords)
  for visible glyphs, `None` for whitespace — whitespace never consumes atlas space. 🟢
- **Lazy shaping, cached per line:** Wez: "when rendering a line, BIDI and font shaping
  are computed and cached and associated with the line, so the initial draw of a
  screenful of text may take a few milliseconds the first time." Cost is paid once per
  unique line content, then amortized. 🟢

---

## 3. Performance tricks & non-obvious gotchas (most important)

### Shared / cross-cutting

- **🟢 Throughput ≠ latency ≠ frame consistency.** Alacritty's own docs warn that
  vtebench measures _only_ PTY read+parse throughput, not latency or frame pacing. Some
  terminals deliberately _slow down_ to save power. A terminal can win throughput and
  lose felt responsiveness. Measure the dimension you actually care about.
- **🟢 The "render in a separate thread from PTY read/parse" architecture matters.**
  Ivan Molodetskikh's GNOME-46 analysis notes Alacritty is consistently among the
  fastest precisely because it doesn't block PTY reading on repaint, unlike single-thread
  designs (old GTK VTE). Repaint duration leaks into throughput numbers when they share
  a thread.
- **🟡 Empty cells are cheap, glyph cells are expensive.** A GPU terminal's per-frame cost
  scales with _non-blank_ cells, not grid size. Drawing a mostly-empty screen is nearly
  free even without damage tracking (this is _why_ Alacritty got away with full redraws
  for so long).

### Alacritty-specific

- **🟢 Latency came from VBLANK serialization, not draw speed.** Per the author
  (jwilm/alacritty#673): the old synchronous loop cost up to **`3 × VBI − draw_time`** in
  the worst case — input arrives just after a vblank, the program's response waits for the
  next vblank, and the render waits for another. Fix: move rendering to a separate thread
  so keypresses reach the terminal immediately and the child's output is drawable on the
  _very next_ frame, cutting worst case to ~**2 × VBI**. The lesson: a terminal can have a
  blazing-fast `draw()` and still feel laggy purely from frame-synchronization topology.
- **🟢 Independent latency studies contradicted the "fastest" marketing.** Typometer-based
  measurements (Pavel Fatin's tool) by LWN/anarcat, Dan Luu, and the Zutty author all
  found Alacritty had _higher and more variable_ typing latency than old terminals
  (xterm, rxvt, st). Dan Luu measured ~31 ms median idle / up to ~56 ms tail. Throughput
  crown, latency middle-of-pack — and the devs acknowledged it stems from the design.
- **🟢 Performance can be a _governance_ fight, not just engineering.** In the damage-PR
  thread a maintainer drew a hard line: "If the performance gets worse, it's worse…
  everything after that is mostly an excuse." The counter: a +5% `draw()` cost paired
  with a −99% _compositor_ cost yields fewer cycles-to-screen and lower latency. Whose
  CPU budget are you optimizing? (See section 4.)
- **🟢 Skip rendering entirely when not visible.** 0.11.0: Alacritty no longer renders
  fully-occluded windows on macOS/X11, and fixed CPU spikes from mouse movement over
  unfocused windows. The cheapest frame is the one you never draw.
- **🟢 `dense_cells` and `unicode` are the killer benchmarks** in vtebench — dense glyph
  coverage and wide/combining Unicode are the worst cases that stress the whole pipeline.

### WezTerm-specific

- **🟢 Font shaping is the dominant hidden cost, and ligature-capable fonts make it
  worse — even with ligatures disabled.** Issue #5280: profiling showed HarfBuzz
  `do_shape` at **84%+** of CPU for ligature-supporting Nerd Fonts vs **24.1%** for Hack
  Nerd Font (no ligatures). Setting `calt=0/clig=0/liga=0` did **not** fix it; the cost is
  in `FT_Load_Glyph → TT_Hint_Glyph → TT_RunIns` (TrueType bytecode hinting), not the
  ligature feature itself. Certain non-ligature Unicode symbols (e.g. `⋅` U+22C5) made it
  _worse_. Gotcha: "disable ligatures" is not a reliable shaping-cost escape hatch — the
  font's hinting program and glyph complexity dominate.
- **🟢 Shaping is cached per unique line, so the _first_ paint of a screen is the slow
  one.** Steady-state scrolling/typing reuses `line_quad_cache` / `shape_cache` entries.
  Cache misses (new content, resize invalidating caches) are where jank appears.
- **🟡 Backend choice is a real lever on Apple Silicon:** `front_end = "WebGpu"` talks to
  Metal directly and can add FPS vs the OpenGL→Metal translation path on M1. A software
  fallback (`webgpu_force_fallback_adapter`) exists but is CPU-rendered and slow.

---

## 4. The damage-tracking story (Alacritty)

This is the centerpiece for Mire because it's exactly the dirty-region problem Mire's
`Diff.compute` solves, fought out in public.

1. **Starting point:** Alacritty re-rendered the **entire grid every frame**. No damage
   tracking. It stayed competitive because empty cells are cheap to draw on a GPU. 🟢
2. **First damage work — PR #2724 (kennylevinsen), "Track and report damage to compatible
   compositors":** the idea was to compute which regions changed and tell the **Wayland
   compositor** via `swap_buffers_with_damage`, so the _compositor_ does less work even if
   Alacritty itself doesn't. Design notes:
   - Damage was threaded as state into `RenderableCellsIter` and extracted after iteration. 🟢
   - Insight: treat **damage like a cell flag** (similar to underline/strikeout) — it's
     just a per-cell bit; shape it into per-line damage rects. 🟢
   - Tradeoff called out: once you commit to _partial_ rendering you lose the simple
     "bail out entirely if zero cells are dirty" early-exit you'd otherwise get. 🟢
   - The governance clash described in §3 happened here: is +draw-time / −compositor-time
     a win? The pro-damage argument was holistic (**cycles-to-screen and latency**, not
     synthetic `draw()` time). 🟢
3. **Shipped in 0.11.0:** "track and report surface damage information to Wayland
   compositors." So the _first_ shipped benefit was compositor-side, Wayland-only. 🟢
4. **Going further — Issue #5843 / PR #5863 (kchibisov), buffer age + partial rendering:**
   use `EGL_EXT_buffer_age` / `GLX_EXT_buffer_age` so Alacritty itself can **partially
   redraw** (skip unchanged regions) on X11 + Wayland, not just inform the compositor.
   - **Buffer age** tells you how many frames old the back buffer's contents are, so you
     know which regions are still valid and only need to repaint the accumulated damage
     since that buffer was last current. 🟢
   - `alacritty_terminal` damage was enhanced to report damage **only up to `occ`** (the
     scrollback display offset) so partial redraws work _during scrolling_. 🟢
   - **Non-obvious gotchas the author hit** (why partial rendering is genuinely hard):
     glyphs that **extend beyond their cell boundary** weren't invalidated correctly;
     search/hints and the message bar rendered wrong; clearing logic was suboptimal;
     debug damage-highlight broke. Most of these are "a cell changed but the _pixels_ that
     changed spill outside that cell's rect" problems. 🟢

**Takeaway:** damage tracking at the _cell_ level is easy to state and hard to get right
because the mapping from "logical cell changed" to "screen pixels that must be repainted"
is not 1:1 — overhanging glyphs, ligatures, cursors, decorations all bleed past cell
edges.

---

## 5. Relevance to Mire

Mire is a retained-mode TUI runtime that builds a full `Surface` and emits ANSI via
`Diff.compute`. It does **not** own a GPU or a glyph atlas — the _terminal_ (which may be
Alacritty/WezTerm/Ghostty) does. So filter the lessons:

**Maps directly — Mire's `Diff.compute` _is_ damage tracking, one layer up.**

- Mire already does what Alacritty had to retrofit: compute the dirty runs and emit only
  those, rather than repainting the whole screen. The Alacritty story validates the
  architecture: **diff-based emission is the correct default**, and the early-exit
  "nothing changed → emit nothing" is a real win (Alacritty _lost_ that simplicity when it
  went partial; Mire should _keep_ it — a frame with zero `DiffRun`s should produce zero
  bytes and ideally no cursor moves). 🟢→Mire
- The cell-overhang gotcha has a Mire analogue: when a cell's _content_ is the same but a
  neighbor's wide glyph or a style run changes, the visible result can shift. Mire's diff
  works on the logical cell grid (it emits ANSI; the terminal owns pixels), so it's mostly
  immune to the _pixel-overhang_ class of bugs — but **wide graphemes / combining marks**
  (the `Grapheme` width logic, and vtebench's `unicode` being a top stressor) are exactly
  where a cell-granular diff can desync. Worth a targeted test: changing a char adjacent to
  a double-width grapheme must invalidate the right run. 🟡→Mire

**Parser lesson (Mire's `InputParser`).**

- `vte` succeeds by being a **pure table-driven state machine with semantics pushed out to
  a `Perform` trait** — the parser stays branch-predictable; meaning lives in the consumer.
  Mire's `InputParser` (bytes → `InputEvent`) is the same shape of problem in reverse.
  Keep decoding as a flat state machine; don't interleave it with policy. The known gaps
  (mouse/paste/focus, Kitty `CSI u`) are _new states/actions_, not a reason to restructure. 🟡→Mire

**Latency / frame-scheduling lesson — the most transferable non-obvious one.**

- Alacritty's headline latency problem had **nothing to do with how fast it drew** — it
  was VBLANK serialization (`3 × VBI`). For Mire's hand-rolled ~30 FPS loop the analogue
  is: **don't let input-to-output wait for two full frame ticks.** If a keypress arrives
  just after a tick, the model update + diff + write should be flushable promptly, not
  parked until tick N+2. Consider rendering immediately after an input-driven model change
  rather than only on the fixed cadence (input-coalesced render), so Mire doesn't
  reproduce Alacritty's original lag on a fixed timer. 🟡→Mire
- Corollary from 0.11.0: **the cheapest frame is the one never drawn.** If nothing in the
  model changed and no command produced output, Mire should skip the diff+write entirely
  (occlusion/idle skip). 🟢→Mire

**What does NOT transfer.**

- Glyph atlas, atlas eviction, instanced GPU quads, WebGpu vs OpenGL, HarfBuzz shaping
  cost — all live in the _terminal_, below Mire. Mire never rasterizes a glyph. The one
  indirect implication: Mire emitting fewer, contiguous styled runs reduces how much the
  terminal must shape/draw, so **coalescing adjacent same-style cells into single ANSI
  runs in `Diff.compute` is a real downstream win** (fewer SGR switches, fewer cursor
  moves → less work for the GPU terminal). 🟡→Mire

---

## 6. Sources

**Alacritty — damage / rendering**

- 🟢 PR #2724 "Track and report damage to compatible compositors" — https://github.com/alacritty/alacritty/pull/2724
- 🟢 Issue #5843 "Perform partial rendering when possible" — https://github.com/alacritty/alacritty/issues/5843
- 🟢 PR #5863 "Use buffer age to perform partial rendering" — https://github.com/alacritty/alacritty/pull/5863
- 🟡 PR #4373 "[wip] New faster renderer" — https://github.com/alacritty/alacritty/pull/4373
- 🟢 Alacritty 0.11.0 changelog (surface damage to Wayland, occlusion skip) — https://alacritty.org/changelog_0_11_0.html
- 🟡 `alacritty/src/display/damage.rs` — https://github.com/alacritty/alacritty/blob/master/alacritty/src/display/damage.rs

**Alacritty — design / parser / benchmark**

- 🟢 Alacritty repo README (design goals, integration philosophy) — https://github.com/alacritty/alacritty
- 🟢 `vte` crate (Paul Williams state machine, Perform trait) — https://github.com/alacritty/vte
- 🟢 vtebench (throughput tool, dense_cells/unicode stressors, caveats) — https://github.com/alacritty/vtebench
- 🟢 alacritty_terminal crate — https://crates.io/crates/alacritty_terminal

**Latency**

- 🟢 jwilm/alacritty #673 (author on VBLANK latency, render-thread fix) — https://github.com/jwilm/alacritty/issues/673
- 🟢 Pavel Fatin, "Typing with Pleasure" (Typometer methodology) — https://pavelfatin.com/typing-with-pleasure/
- 🟢 Dan Luu, "Terminal latency" — https://danluu.com/term-latency/
- 🟢 LWN "A look at terminal emulators, part 2" / anarcat — https://lwn.net/Articles/751763/ , https://anarc.at/blog/2018-05-04-terminal-emulators-2/
- 🟢 "Typing latency of Zutty" — https://tomscii.sig7.se/2021/01/Typing-latency-of-Zutty
- 🟢 Ivan Molodetskikh, "Just How Much Faster Are the GNOME 46 Terminals?" (render-thread vs PTY-thread) — https://bxt.rs/blog/just-how-much-faster-are-the-gnome-46-terminals/

**Cross-comparison**

- 🟢 foot Performance wiki (damage tracking, empty-vs-glyph cells, scroll memmove trick, PGO) — https://codeberg.org/dnkl/foot/wiki/Performance

**WezTerm**

- 🟢 WezTerm rendering pipeline (DeepWiki) — https://deepwiki.com/wezterm/wezterm
- 🟢 Issue #5280 "Unresponsiveness… related to Font shaping" (HarfBuzz do_shape 84% vs 24%) — https://github.com/wezterm/wezterm/issues/5280
- 🟢 Discussion #6288 "How to debug slowness" (cache-layer hit/miss stats) — https://github.com/wezterm/wezterm/discussions/6288
- 🟢 `front_end` config (OpenGL vs WebGpu, default history) — https://wezterm.org/config/lua/config/front_end.html
- 🟡 `webgpu_force_fallback_adapter` (software backend) — https://wezterm.org/config/lua/config/webgpu_force_fallback_adapter.html
- 🟡 Discussion #3664 "Framerate Concerns" — https://github.com/wezterm/wezterm/discussions/3664
- 🔴 johal.in WezTerm post — flagged as likely AI-generated SEO; numbers not trusted — https://johal.in/wezterm-gpu-ligatures-nerd-fonts-lua-config-2026/
