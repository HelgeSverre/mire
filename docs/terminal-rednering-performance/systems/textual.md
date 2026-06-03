# Textual / Rich — Terminal Rendering Performance

Research notes on how Textualize's **Rich** (rendering primitives) and **Textual**
(retained-mode compositor + app runtime) achieve fast, flicker-free terminal
rendering. Aimed at informing Mire (a diff-based, retained-mode, ANSI-emitting F#
TUI targeting Kitty/Ghostty).

Confidence markers: 🟢 primary/official, 🟡 inferred, 🔴 guessed.

---

## 1. Overview (the rendering stack)

Two layers, one company:

- **Rich** — the low-level rendering library. Everything printed first becomes a
  list of **Segments** (a string + a `Style` + optional control code). Segments are
  the intermediate representation; ANSI escape codes are generated _last_, right
  before the write. 🟢
- **Textual** — a retained-mode TUI runtime built on Rich. Widgets each render to
  **Strips** (immutable horizontal lines of Segments). A **compositor** combines
  overlapping widgets' strips into the final screen, and only damaged regions are
  repainted. Targets modern terminals (Kitty, Ghostty) and runs the loop at ~30 fps. 🟢

The key conceptual move Textualize repeatedly cites: **"switch the primitive."**
Stop thinking in _characters_ (cells) and think in _segments_ (styled runs). This
makes wide-character (CJK / emoji) handling and overlap composition tractable. 🟢

---

## 2. Rendering architecture

### Segment (Rich)

- Atomic unit: `Segment(text, style, control)`. Immutable. 🟢
- `Segment.simplify()` — combines contiguous segments with the _same_ style into a
  single segment. Fewer segments → fewer SGR (style) changes emitted → smaller,
  faster output. This is the core "ANSI compression" trick. 🟢
- `Segment.split_cells()` / `_split_cells` — split a segment at a **cell** position,
  not a character position. If the cut lands in the middle of a 2-cell-wide glyph,
  that glyph is replaced by **two spaces** to preserve display width. Memoized with
  `@lru_cache(1024 * 16)`; fast path for single-cell strings. 🟢
- `Segment.split_and_crop_lines()` — splits a segment stream into lines and crops
  lines longer than a width. 🟢

### Strip (Textual)

- A `Strip` is "like an immutable list of Segments" — \*\*immutability is explicitly
  the thing that enables effective caching." 🟢
- Methods: `crop`, `crop_extend`, `crop_pad`, `divide` (cut into smaller strips at
  cell indices), `adjust_cell_length`, `extend_cell_length`, `apply_style`,
  `simplify` (join same-style segments). 🟢
- `render_ansi()` and `blank()` are explicitly **`cached`** — repeated ANSI
  conversions and blank lines are not recomputed. 🟢
- A Strip caches its own **cell length** so width queries are O(1). 🟡

### The compositor (Textual) — four steps

Combines N overlapping widgets into one screen by working on segment lists, not
pixels: 🟢

1. **Find cuts** — collect every x-offset where any widget's segment list starts or
   ends across all layers.
2. **Apply cuts** — divide every segment list at those offsets, yielding equal-width
   pieces called **"chops."**
3. **Discard chops** — for each cell column only the _topmost_ chop survives;
   occluded chops are thrown away. _This discard step is the performance key._
4. **Combine** — concatenate the surviving top chops into one segment list per line.

Complexity is **linear in number of segments**, not quadratic in widget count. 🟢

### Spatial map — visibility culling

- A coarse grid (tiles of **100 cols × 20 lines**) maps tile coordinates → list of
  widgets overlapping that tile: `{(0,0): [w1, w2], (1,0): [w1], ...}`. 🟢
- To render: find which tiles the viewport overlaps, look them up, dedupe → the set
  of _potentially visible_ widgets; everything else is culled. 🟢
- Result: visible-widget determination is **near-constant time regardless of total
  widget count** — scrolling with 8 widgets behaves like scrolling with 1000+. The
  spatial map is **not recomputed while scrolling** (still valid across panning). 🟢

### Diff pipeline

- Rich's `Live` historically rewrote the _whole_ screen each frame; the maintainer
  noted deltas "would be possible... easier done at the **Segment level**" — which is
  precisely the route Textual took (the compositor diffs at segment/strip level). 🟢
- Partial updates: changing one button's color repaints only that button's region,
  not the whole screen. 🟢

---

## 3. Performance tricks & non-obvious gotchas (the important section)

- **Switch the primitive: segments, not cells.** Treating styled string-runs as the
  atom (vs. one object per cell) cuts allocation and lets caching/dedup work on
  meaningful chunks. The single biggest idea. 🟢
- **Immutability → caching.** Strips and Segments are immutable specifically so they
  can be cached/hashed and reused across frames. McGugan: Python is "a bit too slow,"
  but immutable objects are trivially cacheable ("if objects can't change, you can
  just stick them in a cache"). 🟢
- **`simplify()` to collapse style runs.** Merging adjacent same-style segments
  before emitting ANSI reduces the number of SGR escape sequences written — fewer
  bytes, fewer terminal state changes. Do this both at Segment and Strip level. 🟢
- **Cell-aware splitting, not char-aware.** Splitting on cell columns and substituting
  two spaces for a bisected wide glyph keeps every downstream width calculation
  correct. Width is computed in _cells_, never `len(str)`. 🟢
- **Memoize the hot split.** `_split_cells` uses `@lru_cache(16384)` with a single-cell
  fast path — splitting is the inner loop of cropping/compositing. 🟢
- **Cache ANSI conversion and blanks.** `Strip.render_ansi()` and `Strip.blank()` are
  cached; blank lines and repeated styled lines never re-serialize. 🟢
- **Discard occluded work early.** The compositor throws away covered "chops" _before_
  combining/serializing — never pay to render pixels nobody sees. 🟢
- **Spatial grid for culling.** Constant-time "what's on screen" via a coarse tile map
  beats scanning all widgets' rectangles every frame. Tile size (100×20) is a
  deliberate coarseness tradeoff. 🟢
- **Don't recompute structure while scrolling.** Scrolling pans the viewport but the
  spatial map and widget strips stay valid; only crop offsets change. 🟢
- **Synchronized output (DEC mode 2026 / BSU+ESU) to kill flicker.** Wrap a frame in
  `CSI ? 2026 h` … `CSI ? 2026 l`; a supporting terminal repaints atomically, so
  half-painted frames vanish. Query support with `CSI ? 2026 $ p` (reply
  `CSI ? 2026 ; 2 $ y` = supported-but-inactive). Implementations add a safety timeout
  (tmux ~1s, Windows Terminal ~100ms) in case ESU never arrives. Textual and crossterm
  support it; Anthropic/Claude Code pushed it across the ecosystem (xterm.js, tmux,
  Windows Terminal). 🟢
- **A single `write()` does NOT guarantee an atomic paint.** Even one write may be
  `read()` by the emulator in multiple chunks (esp. over a network / larger than one
  packet) → tearing. Kitty mitigates with an _input delay_ that batches client bytes;
  Alacritty processes packets immediately (so it can flicker). This is exactly _why_
  mode 2026 exists — you cannot rely on write atomicity. 🟢
- **Smuggle sync through tmux.** When `$TMUX` is set, wrap sync sequences in tmux
  passthrough (`\033Ptmux;...\033\\`) so they reach the real emulator. 🟢
- **Inline (non-fullscreen) rendering trick.** Render frame lines each terminated by
  newline _except the last_; then emit a cursor-move-up escape to return to the frame
  origin so the next frame overwrites in place. For a smaller next frame, emit a
  "clear from cursor downward" escape first to erase leftover lines. 🟢
- **Keep the hardware cursor where text input will land.** Inline apps must move the
  real terminal cursor to the insertion point so IME/CJK composition popups and emoji
  pickers anchor correctly. 🟢
- **Query cursor position to get the origin.** Ask the terminal where the cursor is,
  subtract it from physical mouse coordinates → mouse events relative to the app's
  on-screen origin (needed for inline apps that don't own the whole screen). 🟢

---

## 4. The smooth-scrolling story (what made it hard, the fix)

🟢 The blog "Smoother scrolling in the terminal — a feature decades in the making"
(2025-02-16):

- **The hard part — a missing data point.** Terminals historically report the mouse
  position in **cells**, and report their own size in **cells**, never pixels. Smooth
  (sub-cell) scrolling needs pixel-resolution mouse deltas mapped to content, i.e.
  `cell = pixel / pixels_per_cell`. But you can't compute `pixels_per_cell` if the
  terminal only tells you its size in cells. So scrolling jumped a whole cell at a
  time — "jerky."
- **The fix.** A newer terminal extension reports the terminal size in **both cells
  and pixels** (gist by rockorager — the in-band resize extension, **DEC mode 2048**:
  enable `CSI ? 2048 h`, query `CSI ? 2048 $ p`, report
  `CSI 48 ; rows ; cols ; height_px ; width_px t`). With pixel dimensions known, you
  derive `pixels_per_cell`, convert pixel-granular mouse coordinates to fractional
  cell offsets, and scroll smoothly. Combined with pixel-resolution mouse reporting
  (SGR-pixels mouse mode, terminals reporting mouse in pixels), Textual 2.0+ does
  fractional scrolling.
- **Where it works.** Kitty, Ghostty, "and a few others." Textualize believes Textual
  may be the first TUI to do this. (Note: the post is conceptual; the author
  deliberately glosses exact mode numbers — 2048 comes from the linked gist, the SGR
  pixel mouse mode number 1016 is the standard one but is _not_ stated in the post.) 🟡

---

## 5. Relevance to Mire

Mire is already diff-based, retained-mode, ANSI-emitting, struct-heavy F#, targeting
Kitty/Ghostty — a close architectural cousin of Textual. Concrete takeaways:

- **Adopt synchronized output (mode 2026) now.** Mire's diff writer emits change runs
  to a `TextWriter`; wrap each flushed frame in `CSI ? 2026 h` … `l` (add named
  bindings in `Mire/Protocol/ANSI.fs`). This is the single cheapest flicker win and
  directly relevant given Mire targets Kitty/Ghostty (both support it). Query support
  once at startup; skip the wrap if unsupported (don't spam no-op mode changes). 🟢/🟡
- **Don't trust write atomicity.** Mire shouldn't assume one `Diff.compute` flush
  paints atomically — same reason as above; mode 2026 is the real guarantee. 🟢
- **`simplify`-style SGR coalescing in the diff emitter.** When emitting a changed
  run, Mire should avoid re-emitting `Style.ToAnsi` when consecutive cells share a
  style — track "current pen" and only emit SGR on change. Equivalent to
  `Segment.simplify`; cuts escape-sequence bytes substantially. Mire's `Cell`/`Style`
  structs make a "last emitted style" comparison cheap. 🟢/🟡
- **Cache ANSI per style.** Textual caches `render_ansi`. Mire could memoize
  `Style.ToAnsi` results (small dictionary keyed by the struct) since the same handful
  of styles recur every frame. 🟡
- **Cell-based width is mandatory.** Mire already has `Core/Grapheme` width; ensure the
  diff/surface code splits and pads on _cell_ width and substitutes spaces when a wide
  glyph is bisected by a clip boundary — exactly Rich's `split_cells` rule. 🟢
- **Spatial-map / occlusion ideas are for later.** Mire's `Overlay` doesn't yet
  position/anchor layers; when it does, the compositor "find cuts → apply cuts →
  discard occluded → combine" recipe and a coarse spatial grid are the proven approach
  for keeping composition linear and scroll cost independent of widget count. Worth
  noting in ROADMAP, not urgent. 🟡
- **Smooth scrolling is reachable.** If Mire wants sub-cell scrolling on Kitty/Ghostty:
  enable in-band resize (mode 2048) to learn pixel size + SGR-pixel mouse reporting,
  derive `pixels_per_cell`, and scroll by fractional cells. This is a differentiator
  but depends on Mire first decoding mouse/pixel events (a known gap in
  `InputParser.fs`). 🟢/🟡
- **Inline (non-altscreen) mode trick.** If Mire ever wants an inline render mode
  (not taking the whole screen), use the "newline all lines except last, then
  cursor-up to origin; clear-below before a shorter frame" technique. 🟢

---

## 6. Sources

- 🟢 Smoother scrolling in the terminal — a feature decades in the making (2025-02-16) — https://textual.textualize.io/blog/2025/02/16/smoother-scrolling-in-the-terminal-mdash-a-feature-decades-in-the-making/
- 🟢 Algorithms for high performance terminal apps (2024-12-12) — https://textual.textualize.io/blog/2024/12/12/algorithms-for-high-performance-terminal-apps/
- 🟢 Behind the Curtain of Inline Terminal Applications (2024-04-20) — https://textual.textualize.io/blog/2024/04/20/behind-the-curtain-of-inline-terminal-applications/
- 🟢 Textual `Strip` API reference — https://textual.textualize.io/api/strip/
- 🟢 Rich `rich.segment` API reference — https://rich.readthedocs.io/en/stable/reference/segment.html
- 🟢 Rich discussion #1314 — "Rendering by frame diffs rather than complete redraws" — https://github.com/Textualize/rich/discussions/1314
- 🟢 In-band resize extension gist (rockorager, DEC mode 2048) — https://gist.github.com/rockorager/e695fb2924d36b2bcf1fff4a3704bd83
- 🟢 Synchronized Output spec (DEC mode 2026, BSU/ESU) — https://github.com/contour-terminal/vt-extensions/blob/master/synchronized-output.md
- 🟢 Synchronized Output (Contour docs) — https://contour-terminal.org/vt-extensions/synchronized-output/
- 🟢 Segments — Textualize/rich DeepWiki — https://deepwiki.com/Textualize/rich/2.3-styling-and-markup
- 🟡 Talk Python #498 — Algorithms for high performance terminal apps — https://talkpython.fm/episodes/show/498/algorithms-for-high-performance-terminal-apps
- 🟡 Talk Python #380 — 7 lessons from building a modern TUI framework — https://talkpython.fm/episodes/show/380/7-lessons-from-building-a-modern-tui-framework
- 🟡 tmux PR #4744 — synchronized output (mode 2026) support — https://github.com/tmux/tmux/pull/4744
- 🟡 microsoft/terminal PR #18826 — Implement DECSET 2026 — https://github.com/microsoft/terminal/pull/18826
- 🔴 SGR-pixels mouse mode number 1016 (standard, used for pixel-granular mouse; not explicitly named in the Textual post) — inferred from xterm/ANSI conventions
