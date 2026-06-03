# Windows Terminal — Rendering Performance & Architecture

Research notes for the Mire F# TUI runtime. Focus: the rendering engines (DxEngine → AtlasEngine), the refterm controversy and its non-obvious perf lessons, and how accessibility (UIA) constrains the text buffer.

Confidence markers: 🟢 well-sourced / corroborated · 🟡 single source or paraphrase · 🔴 inferred / uncertain.

---

## 1. Overview — DxEngine vs AtlasEngine

Windows Terminal historically rendered through **DxEngine** (a.k.a. DxRenderer), a renderer that leaned on **DirectWrite + Direct2D** for _both_ glyph rasterization _and_ composition/blending onto the swap-chain. 🟢

In late 2021, Leonard Hecker (`lhecker`) introduced **AtlasEngine** in PR [#11623](https://github.com/microsoft/terminal/pull/11623) ("Introduce AtlasEngine — A new text rendering prototype"). It shipped behind the `experimental.useAtlasEngine` profile flag in **Windows Terminal Preview 1.13** (Feb 2022), became default in the **1.16** Preview line (Sep 2022), and is now the production renderer. 🟢

The 1.13 release is famous for the team's public **"We were wrong"** acknowledgement about text-rendering performance — a direct response to the community pressure that the refterm episode (§3) created. 🟢 ([Visual Studio Magazine](https://visualstudiomagazine.com/articles/2022/02/07/windows-terminal-1-13.aspx))

**The core design pivot:** AtlasEngine does _not_ let DirectWrite/Direct2D draw to the screen. Instead:

> "DirectWrite and Direct2D are only used to rasterize glyphs. Blending and placing these glyphs into the target view is being done using Direct3D and a simple HLSL shader." 🟢 (PR #11623)

That single decision — demote DirectWrite from "the renderer" to "a glyph bitmap factory" — is the whole game.

---

## 2. Rendering architecture

### Layered renderer abstraction

The console/terminal renderer is split into an engine-agnostic base and pluggable backends (from `doc/ORGANIZATION.md`): 🟢

- `src/renderer/base` — "non-engine-specific rendering things like choosing the data from the console buffer, deciding how to lay out or transform that data, then dispatching commands to a specific final display engine."
- `src/renderer/inc` — interface definitions (the `IRenderEngine` contract).
- `src/renderer/gdi` — the legacy GDI engine (conhost).
- AtlasEngine implements `IRenderEngine`, replacing the older DxRenderer. 🟢 ([DeepWiki: Atlas Engine](https://deepwiki.com/microsoft/terminal/3.2-atlas-engine))

The text buffer itself lives in the host: `screenInfo.cpp` "holds a text buffer instance and a cursor instance and a selection instance"; output flows through `Output.cpp`, `Stream.cpp`, `Dbcs.cpp` (CJK). VT parsing/adapter lives under `src/terminal`. 🟢

### The cell matrix (monospace assumption)

AtlasEngine aggressively assumes monospace text, which collapses layout to pointer arithmetic:

> "The viewport is divided into cells, and its data is stored as a simple matrix. Modifications to this matrix involve only simple pointer arithmetic and is easy to understand." 🟢 (PR #11623)

This is the key structural difference from a general text layout engine: there is no per-line proportional layout pass; the grid _is_ the model.

### Backend split

AtlasEngine exposes an `IBackend` interface with two implementations: 🟢 (DeepWiki)

- **BackendD3D** (primary): requires "Direct3D 11.0+ with compute shader support." GPU-resident glyph atlas + **instanced rendering** — "render thousands of glyphs in single draw calls." Each renderable is a `QuadInstance` (glyph/cursor/background rect) carrying position, size, atlas texture coords, and color. `AtlasGlyphEntry` tracks a glyph's slot in the `_glyphAtlas` texture.
- **BackendD2D** (fallback): for old hardware / WARP (software) adapters; uses Direct2D's native text drawing.

### Glyph atlas

Glyphs are rasterized once (via DirectWrite) into a **grow-only texture atlas**, keyed by Unicode/HarfBuzz-style _clusters_ (indivisible shaping units, e.g. base char + combining marks). 🟢 (PR #11623)

> "The backing texture atlas for glyphs is grow-only and will not shrink. After 256MB of memory is used up (~20k glyphs) text output will be broken." 🟡 (PR #11623 — early-prototype limitation)

### Text shaping pipeline

Shaping still goes through DirectWrite: `_mapCharacters` uses `IDWriteFontFallback` for font selection; `_mapComplex` calls `IDWriteTextAnalyzer` to generate glyph indices/placements, stored in `ShapedRow` objects. 🟢 (DeepWiki)

### Threading & damage tracking

- Dual state: `_api` fields touched by render-thread API methods under the console lock; `_p` fields touched by `Present()` on a background thread; synced in `StartPaint()`. 🟢 (DeepWiki)
- **Row-granularity invalidation**: `invalidatedRows` (top/bottom) + `invalidatedCursorArea`. On scroll, `InvalidateScroll()` adjusts offsets and `StartPaint()` rotates the `_p.rows` pointer array + `colorBitmap` to match buffer scroll — i.e. scrolling moves pointers, not pixels. 🟢 (DeepWiki)

---

## 3. Performance tricks & non-obvious gotchas (most important)

### 3a. The numbers

From PR #11623's benchmarks: 🟢

- **~2×** raw text throughput in OpenConsole vs DxEngine (both at 144 FPS).
- **≥10×** colored VT output in WT/OpenConsole vs DxEngine.
- CPU consumption "up to halved."

### 3b. The non-obvious bottleneck: it's NOT the GPU

The single most important lesson, repeated across all sources: **the bottleneck in a terminal renderer is text _layout/shaping/glyph generation on the CPU_, not GPU fill rate.** Modern GPUs draw textured quads essentially for free; terminals were slow because they were doing expensive per-cell/per-frame text work that should have been cached or batched. 🟢

Tellingly, even _after_ AtlasEngine fixed rendering, Windows Terminal as a whole barely sped up, because the cost had moved (back) onto the CPU side:

> "VT parsing and related buffer management takes up most of the CPU time (~85%), due to which the AtlasEngine can't show any further improvements." 🟢 (PR #11623)

And within rendering prep itself:

> "Glyph hashing consumes up to a third of the current CPU time." DirectWrite **font fallback** is expensive, "particularly with complex scripts like Hindi." 🟢 (PR #11623)

**Gotcha for any renderer author:** once you cache glyph bitmaps and batch the draw, your hot path becomes (1) parsing the input stream and (2) _hashing the cluster to look it up in the cache_. The cache lookup key computation can itself dominate. 🟢

### 3c. refterm — the lessons that forced the rewrite

Casey Muratori's [refterm](https://github.com/cmuratori/refterm) ("reference monospace terminal renderer") was written to refute the Microsoft engineers' claim that a fast, feature-rich Windows terminal was "not realistic, economical, or humanly possible." It runs ~100× faster than the then-current Windows Terminal in ~3k lines. 🟢 ([Nibble Stew](https://nibblestew.blogspot.com/2021/07/looking-at-performance-of-refterm.html), [Lobsters](https://lobste.rs/s/odxvsl/it_takes_phd_develop))

Key refterm lessons (from the [refterm README](https://github.com/cmuratori/refterm/blob/main/README.md)):

1. **It's a _minimum_, not a maximum.** "refterm should be thought of as establishing a modern _minimum_ speed at which a reasonable terminal should be expected to perform. It should not be thought of as a maximum to aspire to." The point isn't that refterm is optimal — it explicitly isn't — it's that _even a naive sensible design_ beats the shipped product by orders of magnitude. 🟢

2. **Worst-case primitives are fine if you cache.** refterm deliberately uses the _slowest_ available Windows primitives — "extremely slow Unicode parsing with Uniscribe and extremely slow glyph generation with DirectWrite" — and _still_ hits good throughput. The trick: a **glyph cache** so "glyph generation only gets called when new glyphs are seen." Generate each unique glyph once, ever; thereafter it's a textured tile blit. 🟢 The cache is a hash/LRU keyed on the glyph cluster. 🟡

3. **Tile rendering.** The renderer is "a straightforward implementation of a tile renderer" — every detected glyph is drawn once into a glyph-map texture, then a vertex+pixel shader places the cached tile at the right cell. No per-frame shaping of unchanged content. 🟢

4. **The misdiagnosis.** Microsoft initially claimed _rendering_ was the root cause; Casey (from a games background) argued it could not be and was proven right — the real costs were architectural: doing layout/shaping work that should have been cached, and throughput plumbing. His TermBench showed colored text dropped throughput ~40×. 🟢

5. **conhost throughput — and a reversal.** refterm originally bypassed conhost because "Windows has very serious problems with conhost throughput" (fast pipes ~10× faster). **In v2 he reversed this:** "so long as conhost receives large writes, it is actually within 10% of the fast pipe alternative." The lesson is about **write batching / large buffers**, not the pipe mechanism per se. 🟢

6. **SIMD pre-scan parsing (v2).** "VT code and line break parsing now checks each 16-byte block for control codes before actually running the parser" — fast-path bulk plain text before paying for the per-byte state machine. 🟢

7. **Threads + ring buffer.** refterm is multithreaded with a "magic ring buffer" feeding the renderer; UI on GPU, parsing on CPU with SIMD. 🟡 ([min.news](https://min.news/en/tech/ca5ab7c81f934a1a70b126d54420e361.html))

8. **Fair-fight features.** To preempt "you cut corners" objections, refterm supports full Unicode incl. combining chars, RTL/Arabic, multi-cell glyphs, line wrapping + reflow on resize, scrollback, and VT color/cursor/underline/strikethrough/blink/reverse. The "it's just a toy" defense doesn't hold. 🟢

9. **Honest caveats.** Critics noted refterm idles at ~351 MB (vs 10–20 MB expected), was built with `/GS- /Gs999999` (disabled security), and froze on certain malformed binary input — it's a proof of concept, not a hardened product. The _architecture_ lessons stand regardless. 🟡 (Nibble Stew)

### 3d. Other AtlasEngine perf tricks

- **Shaders that don't read time don't force per-frame redraws** — added when retro/pixel-shader effects landed; avoids burning a frame every tick when nothing animates. 🟡 (release notes)
- **Scroll = pointer rotation**, not pixel copy (rotate `_p.rows` + `colorBitmap`). 🟢
- **Row-level damage tracking** keeps GPU work proportional to changed lines, not the whole viewport. 🟢
- **Instanced quads** mean N glyphs ≈ one draw call; per-glyph CPU→GPU overhead is amortized away. 🟢

---

## 4. Accessibility & rendering (UIA, from `doc/terminal-a11y-2023.md`)

Source: [terminal-a11y-2023.md](https://github.com/microsoft/terminal/blob/main/doc/terminal-a11y-2023.md). All 🟢 unless noted.

- **Shared UIA provider.** Conhost and Windows Terminal share one UI Automation provider so screen readers can navigate/read the terminal text area.

- **Text-range model.** Accessibility exposes the buffer via `ITextRangeProvider`, supporting text units: _Character, Format, Word, Line, Paragraph, Page, Document_. WT does **not** implement them all — e.g. movement by _Page_ is unimplemented (the viewport could stand in for a "page"). 🟡

- **Text attributes must be exposed explicitly.** Underline/bold/italic etc. are surfaced through `ITextRangeProvider` (the _Format_ unit) — a11y of text decorations is _not_ automatic; the renderer's attribute model has to be queryable.

- **Event-storm problem (the key perf constraint).** Both conhost and WT historically "dispatch an event when text is written to the terminal output," and "NVDA is unable to handle too many text changed events." This forces a design where the terminal must _not_ fire a UIA TextChanged event per write.

- **Shift to notifications with payloads.** Newer approach: "dispatch UIA notifications with a payload of what text was written to the screen." This means screen readers "may no longer be required to diff the text buffer and figure out what has changed." Trade-off: the buffer/renderer must be able to _describe what changed_ (a damage/diff concept) rather than just hold final state — and password input needs special handling so it isn't read aloud.

- **Selection model gotcha.** "Selections are stored as two inclusive terminal coordinates, which makes it impossible to represent an empty selection." This breaks mark-mode cursor reporting: screen readers say "x selected, y unselected" instead of just announcing the character under the cursor. **Architectural lesson:** an inclusive two-point selection model can't express a zero-width caret — a subtle data-model bug that only surfaces through a11y. 🟢

- **Threading.** UIA events for cursor/text/selection changes must be synchronized against terminal output operations — accessibility imposes cross-thread notification discipline on the buffer.

**Net a11y → architecture pressure:** the text buffer must (1) retain enough structure to answer range/word/line queries after the fact, (2) describe _what changed_ per write (so it dovetails with a damage/diff renderer rather than fighting it), and (3) represent a true empty/caret selection. These are buffer-model decisions, not rendering ones.

---

## 5. Relevance to Mire (F#, diff-based ANSI TUI runtime)

Mire is a _producer_ of ANSI to a host terminal, not a GPU renderer — but most lessons transpose to its CPU pipeline (`Surface` → `Diff.compute` → ANSI to stdout):

1. **The bottleneck is CPU text work + I/O, never "fill rate."** Mire has no GPU; its analog of refterm's lesson is: don't redo per-cell work for unchanged content. The diff-based `Surface`/`Diff.compute` model is already the correct shape — it's Mire's "row-level damage tracking." Keep it. 🟢→🟡 (analogy)

2. **Batch writes.** refterm's biggest reversal — "conhost is within 10% of fast pipes _if it receives large writes_" — maps directly: Mire should accumulate the whole diff into one buffer and do **one large write to stdout per frame**, not many small `Write`s. Small writes to a terminal/pipe are the dominant cost. Verify Mire's `Diff` writer coalesces runs and flushes once. 🟢 (transposed lesson — worth auditing `Mire/Renderer/Diff.fs`)

3. **Glyph/cluster caching is the terminal's job, not Mire's** — Mire emits codepoints; the host terminal (Ghostty/WT) owns the glyph atlas. But Mire's grapheme/width computation (`Mire/Core/Grapheme.fs`) is the analog of "glyph hashing consuming a third of CPU." Cache or memoize width/cluster computation for repeated strings; it can dominate just like glyph hashing did in AtlasEngine. 🟡

4. **Cell-matrix monospace model is validated.** AtlasEngine's "viewport = matrix, edits = pointer arithmetic" is exactly Mire's `Surface` cell grid. The industry's fastest renderer converged on the same structure Mire already uses. 🟢

5. **SIMD pre-scan / fast plain-text path.** refterm v2's "check 16-byte blocks for control codes first" suggests: in Mire's `InputParser`, fast-path bulk printable runs before entering the escape-sequence state machine. Probably premature for Mire's current scale, but the pattern is noted. 🟡

6. **The a11y selection gotcha is a free design lesson.** If Mire ever models selections, use a _half-open_ range (or explicit caret) so an empty selection is representable — WT's inclusive two-point model couldn't, and it broke screen-reader caret reporting. 🟢

7. **Damage description > final-state-only.** WT's a11y push toward "notifications with payload of what changed" mirrors Mire's diff: Mire already computes _what changed_. If Mire ever adds an accessibility or scripting surface, reuse `Diff` output as the change feed rather than re-diffing. 🟡

---

## 6. Sources

- 🟢 AtlasEngine intro PR — [microsoft/terminal #11623](https://github.com/microsoft/terminal/pull/11623) (Leonard Hecker): DirectWrite-as-rasterizer, cell matrix, glyph atlas, perf numbers, 85% CPU in VT parsing, glyph-hashing cost, Hindi font fallback.
- 🟢 AtlasEngine architecture — [DeepWiki: Atlas Engine](https://deepwiki.com/microsoft/terminal/3.2-atlas-engine): `IRenderEngine`, `IBackend`, BackendD3D/D2D, `QuadInstance`, `AtlasGlyphEntry`, row invalidation, threading, `ShapedRow`.
- 🟢 "We were wrong" / 1.13 — [Visual Studio Magazine](https://visualstudiomagazine.com/articles/2022/02/07/windows-terminal-1-13.aspx).
- 🟡 1.16 default + gHacks — [gHacks](https://www.ghacks.net/2022/09/15/windows-terminal-preview-1-16-theming-text-rendering-engine/).
- 🟢 refterm repo + README — [cmuratori/refterm](https://github.com/cmuratori/refterm), [README](https://github.com/cmuratori/refterm/blob/main/README.md): minimum-not-maximum, worst-case primitives + glyph cache, tile renderer, conhost large-writes reversal, SIMD pre-scan, feature list.
- 🟢 refterm critical review — [Nibble Stew](https://nibblestew.blogspot.com/2021/07/looking-at-performance-of-refterm.html): memory/robustness caveats.
- 🟢 "it takes a PhD" framing — [Lobsters](https://lobste.rs/s/odxvsl/it_takes_phd_develop).
- 🟡 Controversy recap + threads/ring buffer/SIMD — [min.news](https://min.news/en/tech/ca5ab7c81f934a1a70b126d54420e361.html).
- 🟢 Accessibility — [terminal-a11y-2023.md](https://github.com/microsoft/terminal/blob/main/doc/terminal-a11y-2023.md): shared UIA provider, `ITextRangeProvider` units, TextChanged event storm + NVDA, notification payloads, inclusive two-point selection gotcha.
- 🟢 Project organization — [doc/ORGANIZATION.md](https://github.com/microsoft/terminal/blob/main/doc/ORGANIZATION.md): `renderer/base`, `renderer/gdi`, `renderer/inc`, `screenInfo.cpp` text buffer.

### `doc/` folder inventory (enumerated via `gh api`)

Top level: AddASetting, COOKED_READ_DATA, ConsoleCtrlEvent, ConsoleHostSettings, Debugging, EXCEPTIONS, Niksa, **ORGANIZATION**, STYLE, TAEF, UniversalTest, WIL, WindowsTestPasses, bot, building, color_nudging.html, creating_a_new_project, feature_flags, fuzzing, roadmap-2022/2023, submitting_code, **terminal-a11y-2023**, terminal-v1/v2-roadmap, virtual-dtors.
Subdirs: `cascadia/` (settings schema), `reference/` (sequence lists, UTF8 torture test), `specs/` (~60 feature specs incl. #11000 Marks, #13000 In-process ConPTY, #4993 Keyboard Selection, #605 Search), `user-docs/`, `images/`. No standalone AtlasEngine doc in `doc/` — the renderer detail lives in PR #11623 + source/DeepWiki, not the markdown docs.
