# Kitty — Terminal Rendering Performance

Research notes on how **kitty** (the GPU-based terminal emulator by Kovid Goyal) renders, why it claims low latency, and what an _application_ running inside it (like Mire) should emit to keep it fast.

Confidence legend: 🟢 stated directly in kitty docs/source or by Kovid Goyal · 🟡 well-supported by third-party/secondary sources or reasonable inference · 🔴 contested / weak evidence.

---

## 1. Overview — the rendering stack

Kitty is built on the premise that almost every other terminal renders glyphs on the **CPU** (rasterize each glyph to a bitmap, composite, blit the final image), which becomes the bottleneck under high-throughput workloads (`cat` of a large file, fast log tailing, full-screen TUIs). Kitty offloads the drawing pipeline to the **GPU via OpenGL**, treating the terminal as a fixed **character-cell grid** where every cell is the same size. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/), [faq](https://sw.kovidgoyal.net/kitty/faq/))

Key architectural facts stated by kitty itself:

- **Glyphs are cached as alpha masks in video RAM (a GPU texture atlas / "sprite map"), and rendered in parallel.** This is _why_ kitty is monospace-only: "every cell in the grid has to be the same size." 🟢 ([faq](https://sw.kovidgoyal.net/kitty/faq/))
- "Updates to the screen typically require **sending just a few bytes to the GPU**." 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
- "Parsing of the byte stream is done using **vector CPU instructions** (SIMD) for maximum performance." 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
- **Child-program I/O runs on a separate thread from rendering**, "to improve smoothness." 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))

Implementation is hybrid: **C** for the hot path (parser, screen model, GPU upload), **Python** for the UI/extensibility layer, **Go** for the "kittens" (CLI tools). No heavyweight UI toolkit — only OpenGL. 🟡 ([github readme](https://github.com/kovidgoyal/kitty), secondary interviews)

---

## 2. Rendering architecture (parse → cell grid → glyph atlas → GPU draw)

1. **VT stream parse (CPU, SIMD).** Incoming bytes are decoded UTF-8 → codepoints, scanning for escape-code boundaries using vector instructions. The SIMD parser (shipped ~early 2024) replaced the scalar one for a **~2× parser speedup in benchmarks, 10–50% real-world** depending on workload. Hot operations: `find_either_of_two_bytes` (locating ESC/control bytes), `utf8_decode_to_esc` (decode until an escape), SIMD base64 decode (images/clipboard), SIMD XOR (image disk-cache). Instruction set picked at runtime (`init_simd` in `kitty/simd-string.c`): AVX512/AVX2/AVX/SSSE3/SSE4.x on x86, NEON on ARM, scalar fallback. 🟢 ([RFC #7005](https://github.com/kovidgoyal/kitty/issues/7005), [performance](https://sw.kovidgoyal.net/kitty/performance/))
   - Tradeoff of the SIMD rewrite: **dropped non-UTF-8 input encodings and C1 control codes.** 🟢 ([RFC #7005](https://github.com/kovidgoyal/kitty/issues/7005))
2. **Screen model (cell grid).** Parsed output mutates an in-memory grid of cells. Each cell carries a glyph reference + colors + attrs. Lines track a **`has_dirty_text` flag in `LineAttrs`**; the renderer queries dirty lines and **only redraws those.** This is the damage/dirty-tracking layer (per-line, not user-facing). 🟢 ([DeepWiki: Screen and Terminal Display](https://deepwiki.com/kovidgoyal/kitty/2.3-screen-and-terminal-display))
3. **Glyph rasterization + atlas.** Each unique rendered glyph is rasterized once (HarfBuzz shaping for ligatures/complex text) and stored as an **alpha mask in a GPU texture atlas**. The per-cell GPU record (`GPUCell`) holds a **`sprite_idx` = index into the texture atlas**, plus packed 24-bit RGB / palette colors and attributes. 🟢/🟡 ([faq](https://sw.kovidgoyal.net/kitty/faq/), [DeepWiki](https://deepwiki.com/kovidgoyal/kitty/2.3-screen-and-terminal-display))
4. **GPU draw.** The cell grid is uploaded as compact per-cell data and drawn by OpenGL shaders in one batched/instanced pass — the GPU composites each cell by sampling its glyph's alpha mask from the atlas and applying fg/bg colors in parallel. Because glyphs already live in VRAM, a normal frame update only pushes the small changed cell data ("a few bytes"). 🟢/🟡 ([performance](https://sw.kovidgoyal.net/kitty/performance/), inference from sprite_idx + atlas)

---

## 3. Performance tricks & non-obvious gotchas (most important)

- **Glyph atlas in VRAM is the whole game.** Font rasterization happens once per glyph; subsequent frames just reference `sprite_idx`. Font rendering "is not a bottleneck" because it never re-rasterizes steady-state text. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
- **Steady-state frames are tiny GPU uploads** — "just a few bytes to the GPU" — so throughput is bounded by VT _parsing_, not by drawing. Kitty therefore optimized the parser (SIMD) rather than the painter. 🟢
- **`repaint_delay` is an _adaptive_ frame cap, not a fixed sleep.** Default ≈ 100 FPS. Crucially it is **ignored whenever there is pending input**, so the delay only ever "costs" time when nothing new needs showing — it caps idle/animation repaint without adding typing latency. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/), [search summary](https://sw.kovidgoyal.net/kitty/performance/))
- **`input_delay` (default 3 ms) deliberately _delays_ processing child output.** Non-obvious reason: kitty is so fast it would otherwise render a frame _mid-update_, catching a TUI's partial repaint and causing **flicker/torn frames**. The delay coalesces a burst of writes into one coherent frame. This is also why kitty's **out-of-the-box latency numbers look worse than Alacritty's** until you set `input_delay 0`. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/), [discussion #6493](https://github.com/kovidgoyal/kitty/discussions/6493))
  - **Gotcha for app authors:** the _correct_ fix for partial-frame flicker is not raising `input_delay` — it's the app emitting **synchronized-update brackets (mode 2026)**. Kitty maintainers explicitly frame `input_delay` as a crutch for apps that "don't use the atomic screen update facilities modern terminals provide." 🟢 ([discussion #6493](https://github.com/kovidgoyal/kitty/discussions/6493))
- **Per-line dirty flag** (`has_dirty_text`): only changed lines are re-uploaded/redrawn. An app that rewrites the entire screen every frame defeats this and forces full re-uploads. 🟢 ([DeepWiki](https://deepwiki.com/kovidgoyal/kitty/2.3-screen-and-terminal-display))
- **Render thread ≠ I/O thread.** Reading/parsing child output is decoupled from the GL render loop, so a flood of output can't stall frame pacing and vice-versa. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
- **vsync (`sync_to_monitor`) prevents tearing but caps FPS at refresh rate.** To actually hit 100 FPS you must turn it off or use a high-refresh monitor. Vsync mechanism is platform-specific: Wayland uses compositor render-frames (frame callbacks), macOS uses a constant-rate clock, X11 kitty self-paces ~100 FPS. 🟢/🟡 ([discussion #6590](https://github.com/kovidgoyal/kitty/discussions/6590), search summary)
- **Latency tuning recipe (kitty.conf), trades energy for latency:** `input_delay 0`, `repaint_delay 2`, `sync_to_monitor no`, `wayland_enable_ime no`. With these, kitty's own Typometer numbers beat the field; defaults do not. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
- **Cursor-blink interacts with the repaint floor:** any blink interval smaller than `repaint_delay` is clamped to `repaint_delay`. 🟡 ([search summary](https://sw.kovidgoyal.net/kitty/performance/))
- **SIMD base64 + SIMD XOR** matter specifically for the **graphics protocol and clipboard** paths (base64 is the transport) and the encrypted image disk cache. Image-heavy escape traffic is decoded fast rather than being a soft spot. 🟢 ([RFC #7005](https://github.com/kovidgoyal/kitty/issues/7005))
- **Benchmark methodology caveat (read before trusting numbers):**
  - Kitty's _throughput_ benchmark (`kitten __benchmark__`) **suppresses actual rendering to isolate parser speed** — so 134 MB/s is parse throughput, not paint throughput. 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
  - Reported numbers: kitty 0.33 ≈ **134.55 MB/s** avg (ASCII 121.8 / Unicode 105.0 / CSI 59.8 / Images 251.6 MB/s on a Ryzen 7 PRO 5850U), ~**2× the next terminal**. Scrolling `less` CPU: kitty **6–8%** vs Konsole 29–31% (xterm low but "extremely janky"). 🟢 ([performance](https://sw.kovidgoyal.net/kitty/performance/))
  - **Typometer latency benchmarks are software-only** (synthetic keystroke + software screen capture) — they do **not** measure true keyboard-to-screen latency; that needs a hardware camera rig. Treat cross-terminal latency rankings as configuration-dependent. 🟡/🔴 ([beuke.org](https://beuke.org/terminal-latency/), [HN 42526221](https://news.ycombinator.com/item?id=42526221))
- **Startup is bounded:** kitty starts within ~100 ms of comparable GPU terminals; occasional Linux slowness is **GPU power-management waking a discrete card**, fixable by pinning the GPU via env var — not a kitty cost. 🟢 ([faq](https://sw.kovidgoyal.net/kitty/faq/))

---

## 4. Protocols that matter for rendering perf

### Synchronized output — DEC private mode 2026 (the big one for TUIs) 🟢

- **BSU (Begin Synchronized Update):** `CSI ? 2026 h` · **ESU (End):** `CSI ? 2026 l`.
- Detect support via DECRQM: send `CSI ? 2026 $ p`; a supporting terminal replies `CSI ? 2026 ; 1 $ y` (or `;2`).
- Semantics: while enabled, the terminal **keeps presenting the last rendered frame** while still _processing_ incoming bytes into the off-screen grid. On ESU it presents the latest state **atomically** — no tearing, no partial frames. ([gist spec](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))
- This is the _protocol-level_ equivalent of double buffering, and the proper alternative to relying on kitty's `input_delay`. Wrapping a full frame in BSU…ESU lets you set `input_delay 0` for low latency without flicker.

### Graphics protocol (image rendering cost model) 🟢

Transports, fastest → slowest, with the perf rationale:

- **Shared memory** (POSIX/Windows named objects): zero-copy, terminal reads the memory object directly — bypasses serialization entirely. Local only.
- **File path**: terminal reads file directly, no data duplication in the escape stream. Local only.
- **Direct (embedded in escape codes)**: fine for small images / when local IPC unavailable.
- **Chunked base64**: payload split into ≤4096-byte base64 chunks (`m=1` continue, `m=0` final) for legacy-terminal interop; the SSH/remote fallback, highest overhead. ([graphics-protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/))
- Images get a server-side **ID and are cached on the GPU**; each on-screen draw is a **placement** (cell position + pixel offset + scale + z-index) referencing the cached image, so re-displaying costs no retransmission. Negative z-index renders below text; semi-transparent placements blend. `C=1` suppresses cursor movement after a placement to cut state churn. Optional ZLIB deflate before base64. ([graphics-protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/))

### Text-sizing & keyboard protocols 🟡

- **Text-sizing protocol** addresses cell-width ambiguity (wide chars / scaling) so the terminal doesn't mis-place glyphs — correctness that avoids reflow/repaint churn, more than a raw speed feature.
- **Kitty keyboard protocol** (`CSI u` form) gives unambiguous, low-overhead key events (full modifier/release reporting) — relevant to _input_ latency and disambiguation, not paint.
- Design ethos for all extensions: "as small and unobtrusive as possible … minimum possible amount of extra functionality into the terminal." ([protocol-extensions](https://sw.kovidgoyal.net/kitty/protocol-extensions/))

---

## 5. Relevance to Mire

Mire is a diff-based, ANSI-emitting F# TUI runtime running _inside_ kitty/ghostty. So the question is: **what should Mire emit, and what architectural ideas transfer?**

What Mire should _emit_ to keep kitty fast:

1. **Wrap every frame in synchronized-update brackets (mode 2026).** Emit `CSI ? 2026 h` before writing a frame's diff and `CSI ? 2026 l` after. This is the single highest-leverage change: it eliminates tearing/partial-frame flicker and makes Mire correct regardless of the user's `input_delay`. Add `CSI ? 2026 $ p` detection and only bracket when supported (kitty/ghostty both support it). → Add the two sequences to `Mire/Protocol/ANSI.fs` and have `Runtime.run` wrap the per-frame `Diff` write. 🟢
2. **Keep the diff minimal and line-coherent.** Kitty's per-line `has_dirty_text` flag means _fewer changed lines = fewer GPU uploads_. Mire's `Diff.compute` already emits only changed runs — good. Avoid full-screen repaints; never re-emit unchanged lines. Coalescing runs on the same line into one cursor-move + write helps both byte count and kitty's dirty-line accounting. 🟢/🟡
3. **Minimize escape-sequence count and length, not just cell count.** Kitty's cost model is dominated by **VT parsing throughput** (steady-state paint is "a few bytes to the GPU"). So Mire's wins come from emitting fewer/shorter escapes: don't re-emit SGR when style is unchanged between adjacent cells (track current pen, only emit `Style.ToAnsi` deltas); batch contiguous same-style runs; prefer one `cursorTo` + a run over per-cell moves. This is a parser-side saving, which is exactly kitty's bottleneck. 🟢
4. **For images, use placements + caching, not re-transmission.** If Mire ever does graphics, transmit once (file/shared-mem locally, chunked base64 over SSH), keep the image ID, and re-`placement` it rather than resending. 🟢

Architectural lessons that transfer to Mire's own internals:

- **Damage/dirty tracking pays off at every layer.** Kitty tracks dirty _lines_; Mire's `Surface` + `Diff` is the analogous mechanism one level up. Keep it strict — the framework's value is emitting _only_ what changed.
- **Decouple input/compute from frame pacing.** Kitty separates child-I/O from rendering and uses an _adaptive_ repaint cap that's bypassed when input is pending. Mire's ~30 FPS loop could similarly (a) skip rendering when nothing changed, and (b) render immediately when there's a pending message rather than waiting for the next tick — low latency without burning CPU idle. 🟡
- **"Atomic frame" thinking.** The mode-2026 lesson generalizes: build the _complete_ next state off-screen (Mire already builds a full `Surface`), then present it in one bracketed write. Never let a half-built frame reach the terminal.
- **Optimize the actual bottleneck.** Kitty proves the painter is cheap and the _byte stream_ is the cost. For Mire that means: the ANSI it emits is the perf surface — measure bytes/frame and escape-count/frame, not "cells drawn."

Non-goals confirmed: kitty is monospace-only and cell-grid based, which matches Mire's model exactly — no impedance mismatch.

---

## 6. Sources + confidence

- 🟢 [kitty Performance docs](https://sw.kovidgoyal.net/kitty/performance/) — glyph cache in VRAM, few bytes to GPU, SIMD parsing, separate render thread, repaint_delay/input_delay/sync_to_monitor, benchmark numbers + methodology, latency tuning recipe.
- 🟢 [kitty FAQ](https://sw.kovidgoyal.net/kitty/faq/) — alpha-mask glyph caching + parallel GPU render, monospace requirement, startup time, GPU power-management note.
- 🟢 [RFC #7005 — SIMD escape-code parser](https://github.com/kovidgoyal/kitty/issues/7005) — 2× parser speedup, runtime CPU detection, dropped non-UTF8/C1, SIMD base64/XOR, `kitten __benchmark__`.
- 🟢 [DeepWiki: Screen and Terminal Display](https://deepwiki.com/kovidgoyal/kitty/2.3-screen-and-terminal-display) — `has_dirty_text`/`LineAttrs`, dirty-line-only redraw, `GPUCell`/`sprite_idx` atlas index.
- 🟢 [Synchronized Update spec (mode 2026 gist)](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036) — BSU/ESU sequences, DECRQM detection, tearing semantics.
- 🟢 [kitty Graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/) — transports (shmem/file/direct/chunked), placements, GPU caching, z-index, compression.
- 🟢 [kitty Protocol extensions index](https://sw.kovidgoyal.net/kitty/protocol-extensions/) — text-sizing, keyboard, design ethos.
- 🟢 [Discussion #6493 — input_delay vs atomic updates](https://github.com/kovidgoyal/kitty/discussions/6493) — input_delay as crutch for non-synchronized apps.
- 🟡 [Discussion #6590 — Vsync via tty](https://github.com/kovidgoyal/kitty/discussions/6590) — platform-specific vsync (Wayland frames / macOS clock / X11 self-pace).
- 🟡/🔴 [beuke.org terminal latency benchmark](https://beuke.org/terminal-latency/) and [HN 42526221](https://news.ycombinator.com/item?id=42526221) — Typometer is software-only; latency rankings are config-dependent and contested.
- 🟡 [kitty GitHub README](https://github.com/kovidgoyal/kitty) + Console interview discussion ([HN 30144308](https://news.ycombinator.com/item?id=30144308)) — C/Python/Go hybrid, OpenGL-only, origin story.

_Note: `readmex` Performance page and a couple of fetches were thin/rate-limited (HN 30144308 returned HTTP 429); GPU upload/instancing specifics in §2 step 4 are partly inferred (🟡) from the `sprite_idx`+atlas model since kitty's user-facing docs don't detail the GL buffer-binding strategy._
