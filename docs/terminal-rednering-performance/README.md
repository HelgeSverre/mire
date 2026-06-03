# Terminal Rendering Performance — Research Synthesis

Research into how modern terminal emulators and TUI runtimes achieve fast,
smooth rendering, distilled for the **Mire** F# TUI runtime (diff-based,
retained-mode, ANSI-emitting, targeting Kitty/Ghostty).

> **Mire's position in the stack matters.** Mire is _not_ a terminal emulator —
> it emits ANSI/escape sequences that run _inside_ one (kitty, Ghostty, …). So
> the GPU/glyph-atlas/subpixel-kerning layer that dominates the emulators below
> is **the host's job, not Mire's**. What transfers to Mire is the _upstream_
> half: damage/diff tracking, output batching, the ANSI byte-cost model,
> Unicode width correctness, latency/frame pacing, and synchronized output.

## Contents

- [`00-research-brief.md`](00-research-brief.md) — objective & key questions
- [`systems/textual.md`](systems/textual.md) — Textual/Rich (TUI framework — closest analog to Mire)
- [`systems/kitty.md`](systems/kitty.md) — Kitty (GPU emulator)
- [`systems/alacritty-wezterm.md`](systems/alacritty-wezterm.md) — Alacritty & WezTerm (Rust GPU emulators)
- [`systems/warp-hyper-xtermjs.md`](systems/warp-hyper-xtermjs.md) — Warp, Hyper, xterm.js
- [`systems/windows-terminal.md`](systems/windows-terminal.md) — Windows Terminal (AtlasEngine, refterm, a11y)
- [`systems/cross-cutting.md`](systems/cross-cutting.md) — protocols & shared gotchas (start here for actionable items)

---

## Executive summary — the 7 findings that matter for Mire

1. **Wrap every frame's diff flush in synchronized output (DEC mode 2026).**
   Send `CSI ?2026h` (Begin Synchronized Update) before writing the diff and
   `CSI ?2026l` (End) after. A single `write()` is **not** painted atomically —
   the emulator may `read()` it in chunks and paint a half-frame, which is the
   root of TUI tearing/flicker. Kitty papers over this with a 3ms `input_delay`;
   2026 fixes it properly. Supported by kitty, Ghostty, iTerm2, WezTerm,
   Contour, foot; unsupported terminals silently drop the private mode, so it's
   safe to emit unconditionally. **This is the single highest-leverage change.**

2. **The cost surface is escape-sequence _bytes/count per frame_, not cells.**
   Across kitty, Windows Terminal, and Alacritty, the painter is nearly free;
   the bottleneck is the VT byte stream the app emits and the host parses
   (kitty went so far as to write a SIMD parser; ~85% of Windows Terminal's CPU
   is VT parsing + buffer management _after_ rendering was fixed). For Mire that
   means: relative cursor moves over absolute addressing where cheaper, **SGR
   pen-delta** (only emit style changes), **run-coalescing** of same-style cells,
   overwrite rather than clear, and never full-repaint. Fewer bytes = faster.

3. **Mire's `Surface` + `Diff.compute` is already the validated architecture.**
   It is exactly the back-buffer/front-buffer + cell-level damage model that
   Alacritty _added_ (PRs #2724 → #5863), that notcurses uses, and that
   Windows Terminal's AtlasEngine does at row granularity. Keep it. The one
   trap to guard: **never lose the "zero dirty cells → bail" early-exit** —
   Alacritty broke exactly this during its damage refactor.

4. **Unicode width disagreement is the most dangerous correctness bug.**
   If Mire's `Grapheme` width and the terminal's width disagree by one column,
   the cursor desyncs and _all_ subsequent output (and input echo) corrupts.
   Traps: per-codepoint `wcwidth` vs grapheme clusters; VS16 emoji presentation
   promoting width 1→2; ZWJ sequences; combining marks (width 0); ambiguous
   East-Asian width. Targeting kitty/Ghostty lets Mire assume **grapheme-cluster
   widths** (mode 2027); `CSI 6 n` cursor readback is the gold-standard check.

5. **Memoize the expensive per-cell work.** Textual caches immutable Segments/
   Strips (`@lru_cache(16384)`); Windows Terminal hashes glyphs (a third of its
   CPU). Mire's analog is **grapheme/width computation and styled-run building** —
   cache them, and make `Cell`/`Style` cheap to hash/compare (they're already
   structs). Immutability is what makes cross-frame caching safe.

6. **Latency, not throughput, is the metric users feel.** Independent Typometer
   studies (Dan Luu, Pavel Fatin, LWN) repeatedly showed the "fastest" terminals
   slower than xterm because of _frame scheduling_, not draw speed — Alacritty's
   problem was VBLANK serialization and parking on a timer. Lesson for Mire's
   ~30 FPS loop: **render promptly on input** rather than waiting for tick N+2;
   coalesce rapid model updates, but **never debounce input echo**. ~10–20ms is
   where users start to notice; aim for low single-digit ms input-to-emit.

7. **Smooth (sub-cell) scrolling needs pixel dimensions (mode 2048).**
   "Decades in the making" because terminals only reported size in _cells_.
   In-band resize (DEC mode 2048, `CSI 48;rows;cols;hpx;wpx t`) exposes pixel
   dims, letting an app derive pixels-per-cell and scroll fractionally. Optional
   for Mire (cell-granular scrolling via scroll regions is fine to start), but
   this is the mechanism if buttery scrolling becomes a goal.

---

## Key design questions — answered across systems

### Q1. Diffing & damage tracking — cell diff vs dirty regions vs full repaint?

| System           | Approach                                        | Note                                                                    |
| ---------------- | ----------------------------------------------- | ----------------------------------------------------------------------- |
| Textual          | Compositor + spatial grid → per-region damage   | 100×20 grid makes "what's visible" ~O(1); 1000 widgets ≈ 8              |
| Kitty            | Per-line dirty flag (`has_dirty_text`)          | only changed lines hit the GPU                                          |
| Alacritty        | Cell-level damage (added late, PRs #2724/#5863) | originally full-grid every frame — cheap on GPU but bad for compositors |
| WezTerm          | Multi-level caches + damage                     | shape/line-quad/glyph caches                                            |
| Windows Terminal | Row-granularity damage (AtlasEngine)            | instanced quads from cached glyph tiles                                 |
| **Mire**         | **Cell-level diff (`Diff.compute`)**            | **already correct; keep the zero-diff early-exit**                      |

**Pattern:** everyone converges on damage tracking; full repaint only survives
on a GPU where empty cells are free. Mire emits ANSI, so cell-level diff is
both correct and the cheapest in bytes. ✅ Mire is already here.

### Q2. Output batching & flush strategy?

Convergent answer: **coalesce the whole frame into one `write()`**, wrapped in
**synchronized output 2026**. Windows Terminal/conhost: big buffers get within
10% of a fast pipe; small writes are death by syscall. → Mire should build the
full diff byte string and flush once per frame inside BSU/ESU brackets.

### Q3. GPU vs CPU rendering?

GPU matters _only below Mire_ (the emulator rasterizes glyphs). The repeated
lesson (refterm, AtlasEngine, Warp) is that **even in emulators the GPU is never
the bottleneck — CPU-side text shaping/layout is**. The fix is always "rasterize
each unique glyph once, cache it, batch the rest." Mire's transferable version:
cache grapheme width + styled runs; don't recompute per frame. GPU itself: N/A
for Mire.

### Q4. Scrolling — why hard, what fixes it?

Hard because the cell grid quantizes motion; sub-cell smoothness needs pixel
reporting (**mode 2048**). Textual's smooth-scroll landed only once terminals
exposed pixels-per-cell. Cheaper wins available to Mire today: terminal **scroll
regions** (DECSTBM) and line-shifting instead of full redraw on scroll.

### Q5. Unicode / grapheme width?

The biggest correctness+perf trap (see finding #4). Use grapheme clustering, not
`wcwidth` per codepoint; handle VS16/ZWJ/combining; assume mode-2027 grapheme
widths on kitty/Ghostty; verify with `CSI 6 n`. Memoize width lookups.

### Q6. Escape-sequence parsing & cost?

Host side: table-driven Paul Williams VT500 DFA beats branching/regex; kitty
SIMD-prescans plain text. App side (Mire): minimize bytes — pen-delta SGR, run
coalescing, relative moves, no redundant style resets. Mire's own `InputParser`
should stay a flat table-driven state machine for the same reason.

### Q7. Latency & frame pacing?

Target latency over throughput (finding #6). Render on input, coalesce model
churn, never debounce echo, debounce _resize_. ~30 FPS coalescing is fine if the
loop reacts to input immediately rather than parking on the next tick.

### Q8. Accessibility & rendering architecture?

From Windows Terminal's a11y-2023 doc: UIA exposes the buffer via
`ITextRangeProvider` text units; a naive per-write `TextChanged` event storm
overwhelmed NVDA, pushing them toward **change-payload notifications** — which
dovetail naturally with a diff model. Selection should use **half-open ranges**
(their inclusive two-point model couldn't represent an empty caret). Mire is
pre-a11y, but a diff model is the right foundation and half-open selection ranges
are the cheap future-proofing.

---

## Feature / technique matrix

| Technique                      | Textual         | Kitty              | Alacritty     | WezTerm | Warp | xterm.js          | Win Terminal | **Mire today**   |
| ------------------------------ | --------------- | ------------------ | ------------- | ------- | ---- | ----------------- | ------------ | ---------------- |
| Cell/region damage tracking    | ✅              | ✅                 | ✅ (late)     | ✅      | ✅   | ✅ (canvas/webgl) | ✅           | ✅ `Diff`        |
| Single batched flush           | ✅              | ✅                 | ✅            | ✅      | ✅   | ✅                | ✅           | ⚠️ verify        |
| Synchronized output (2026)     | ✅ emits        | ✅ supports        | ✅ supports   | ✅      | —    | —                 | ✅           | ❌ **add**       |
| SGR pen-delta / run coalescing | ✅ `simplify()` | n/a                | n/a           | n/a     | n/a  | n/a               | n/a          | ⚠️ check `Diff`  |
| Glyph atlas (GPU)              | n/a             | ✅                 | ✅            | ✅      | ✅   | ✅ webgl          | ✅           | n/a (below Mire) |
| Grapheme-cluster width         | ✅              | ✅ (2027)          | ⚠️            | ✅      | ✅   | ⚠️                | ✅           | 🟡 `Grapheme`    |
| Cross-frame caching/memo       | ✅ lru_cache    | ✅                 | ✅            | ✅      | ✅   | ✅                | ✅ hash      | ⚠️ opportunity   |
| Pixel reporting / 2048 scroll  | ✅              | ✅                 | ⚠️            | ✅      | ✅   | —                 | ⚠️           | ❌ optional      |
| Render-on-input (low latency)  | ✅              | ✅ (`input_delay`) | ⚠️ fixed late | ✅      | ✅   | ✅                | ✅           | ⚠️ check loop    |

Legend: ✅ yes / ⚠️ partial-or-verify / ❌ missing / n/a not-applicable-to-stack.

---

## Recommendations for Mire (priority order)

### 1. Wire up synchronized output (mode 2026) around every frame flush — **do this first**

- **Already half-done:** `ANSI.beginSync` (`ESC[?2026h`) and `ANSI.endSync`
  (`ESC[?2026l`) **already exist** in `Mire/Protocol/ANSI.fs:41-42` — but a
  repo-wide grep shows they are **defined and never used**. The work is purely
  to wire them in: in `Diff.renderToTerminal` (or the runtime render step),
  write `beginSync` before the run loop and `endSync` after `output.Flush()` so
  the whole diff lands as one atomic update.
- **Why:** eliminates tearing/flicker at near-zero cost; supported by all Mire
  targets (kitty/Ghostty) and safely ignored elsewhere.
- **Open question:** emit unconditionally (field consensus) vs DECRQM-gate. Start
  unconditional.

### 2. Audit `Diff.renderToTerminal` output for byte minimality

- **Already good:** `Diff.renderToTerminal` (`Mire/Renderer/Diff.fs:86`) already
  coalesces — it only emits `cursorTo` when `run.X/Y` move and only emits style
  when `run.Style <> currentStyle`. That's run-level coalescing + pen tracking.
- **To improve:** `Style.ToAnsi()` likely emits a full SGR per change rather than
  a true _delta_ (only the changed attributes). Absolute `cursorTo` is used for
  every move; short same-row hops could use relative `CUF`/`CUB` to save bytes.
- **Why:** bytes/frame is the real cost surface on the host parser.
- **Avoid:** per-cell SGR resets, full-screen clears.

### 2b. ⚠️ Real width bug found: `Diff.renderToTerminal` advances cursor by `Text.Length`

- `currentX <- currentX + run.Text.Length` (`Diff.fs:108`) advances the tracked
  cursor by **UTF-16 char count**, not **display width**. Wide graphemes (CJK,
  emoji), combining marks (width 0), and ZWJ sequences will **desync `currentX`**,
  causing a wrong "cursor already here, skip the move" decision on the next run
  and corrupting output. This is finding #4 made concrete.
- **Fix:** advance by grapheme display width (use `Grapheme`'s width function),
  not `.Length`. Add a `Mire.Tests` case with a CJK/emoji run that exercises a
  subsequent same-row run.

### 3. Protect and memoize the per-cell hot path

- **Adopt:** keep the zero-diff early-exit; cache grapheme width and styled-run
  construction (structs already hash cheaply).
- **Why:** Textual's lru_cache and WT's glyph hashing show this is where TUI CPU
  goes once rendering is diff-based.

### 4. Harden Unicode width

- **Adopt:** grapheme-cluster widths in `Grapheme.fs` (not per-codepoint
  wcwidth); handle VS16/ZWJ/combining/ambiguous; assume mode-2027 on targets;
  add a `CSI 6 n` cursor-readback test in `Mire.Tests` for width-sensitive cases.
- **Why:** a one-column disagreement corrupts the whole display + input echo.

### 5. Make the loop render-on-input, not park-on-tick

- **Adopt:** ensure the ~30 FPS loop flushes promptly when input/messages arrive;
  coalesce model updates within a tick; debounce _resize_ (flag → ioctl at render,
  no per-frame clear); never debounce echo.
- **Why:** latency is the felt metric; Alacritty's reputation hit came from frame
  scheduling, not draw speed.

### 6. (Later / optional) Smooth scrolling & a11y

- Pixel reporting (mode 2048) for fractional scroll; scroll regions (DECSTBM) for
  cheap cell-granular scroll now.
- If selections/a11y arrive: half-open selection ranges + change-payload
  (diff-shaped) notifications.

---

## Notes & caveats

- **Latency rankings are weakly grounded.** Typometer/Dan-Luu numbers are
  software-only and config-dependent (e.g. kitty's `input_delay`). Treat
  cross-terminal latency comparisons as directional, not precise. 🟡
- **GPU/atlas/subpixel/shaping findings don't transfer to Mire** — they're the
  emulator's job. They're documented in the system reports for completeness and
  because they explain _why_ the host's cost model is what it is.
- **Directory name typo:** the folder is `terminal-rednering-performance` as
  requested. Say the word to rename it to `terminal-rendering-performance`.
