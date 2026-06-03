# Cross-Cutting Terminal Rendering Performance

The shared, terminal-agnostic body of knowledge for a diff-based, retained-mode, ANSI-emitting TUI (Mire targets Kitty/Ghostty). These are the protocols and gotchas that no single terminal "owns" — they are decades-in-the-making conventions that every serious TUI runtime collides with. Every claim is traced to a URL; confidence is marked 🟢 (well-established / primary source), 🟡 (credible but secondary or partial), 🔴 (folklore / weakly sourced).

Relevance to Mire: Mire already has a `Surface` (back buffer) + `Diff` (damage computation) + ~30 FPS hand-rolled loop. The single most actionable upgrade is **wrapping each diff flush in synchronized-output brackets (mode 2026)**. Everything else here is about not corrupting that diff: width agreement, byte-minimal cursor/SGR emission, and resize handling.

---

## 1. Synchronized Output — DEC mode 2026 (the single most actionable thing)

**What it is.** A private DEC mode that lets an application bracket a frame so the terminal does **not** repaint mid-update. Without it, the terminal's own render thread can sample the cell grid in the middle of your write burst, producing **tearing/flicker** — visible especially on multi-line repaints, full-screen redraws, and anything driven by an animation/tick loop. 🟢 ([contour vt-extensions spec](https://github.com/contour-terminal/vt-extensions/blob/master/synchronized-output.md), [parpart gist](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))

**How it works.** Standard SM/RM (set/reset mode) on the private mode `2026`:

- **BSU — Begin Synchronized Update:** `CSI ? 2026 h` (`\x1b[?2026h`). Terminal keeps ingesting bytes and updating its internal grid, but freezes what it _displays_ at the last rendered state.
- **ESU — End Synchronized Update:** `CSI ? 2026 l` (`\x1b[?2026l`). Terminal re-samples the now-current grid and presents it as one atomic frame.

🟢 ([gist](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))

It descends from iTerm2's original synchronized-update feature, which used a rare DCS sequence; the 2026 redesign uses the well-known DECSET/DECRST forms instead, and iTerm2 adopted the new syntax. Because it is a _private_ mode, terminals that don't understand it silently drop the sequence and carry on. 🟢 ([gist](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))

**Detection via DECRQM.** Query: `CSI ? 2026 $ p` (`\x1b[?2026$p`). The reply is DECRPM-shaped: `CSI ? 2026 ; <N> $ y`.

| N   | Meaning             | Implication                    |
| --- | ------------------- | ------------------------------ |
| 0   | mode not recognized | **unsupported**                |
| 1   | set                 | supported, currently buffering |
| 2   | reset               | supported, currently immediate |
| 3   | permanently set     | undefined for this mode        |
| 4   | permanently reset   | **unsupported**                |

If you get nothing back at all (DECRQM not implemented), treat as unsupported. 🟢 ([gist](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))

**Practical guidance: don't bother querying — just bracket every frame.** Because unknown private modes are ignored, the field consensus (and what Zellij does) is to emit BSU/ESU unconditionally around each render rather than gating on DECRQM. The spec leaves frame timeout deliberately unspecified — a terminal may auto-end a sync update after some interval if ESU never arrives, so keep frames short and always emit the matching ESU. 🟢 ([bubbletea discussion #1320](https://github.com/charmbracelet/bubbletea/discussions/1320), [gist](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))

**Support (broad).** Kitty, Ghostty, iTerm2, WezTerm, Contour, foot, Alacritty (≥ v0.13.0), Windows Terminal, mintty, notcurses, Jexer, Warp, Zellij, st (patch). xterm.js, VTE/gnome-terminal, Konsole, urxvt are aware/in-progress. App/framework side: tmux, neovim, btop, kakoune, Textual, crossterm. 🟢 ([contour spec list](https://contour-terminal.org/vt-extensions/synchronized-output/), [gist](https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036))

**Mire action:** add `ANSI.beginSyncUpdate = "\x1b[?2026h"` / `ANSI.endSyncUpdate = "\x1b[?2026l"` and have `Diff.write` (or the runtime flush) wrap the whole DiffRun stream. This is cheap (a few bytes per frame), safe on non-supporting terminals, and is the highest-leverage flicker fix available.

---

## 2. Scrolling & pixel reporting — in-band resize (mode 2048)

**What it is.** A private mode (`2048`) that makes the terminal _push_ text-area resize events in-band instead of relying on the `SIGWINCH` signal. `CSI ? 2048 h` enables; `CSI ? 2048 l` disables. On enable the terminal immediately emits one report of the current size; thereafter it reports on each resize. 🟢 ([rockorager gist](https://gist.github.com/rockorager/e695fb2924d36b2bcf1fff4a3704bd83))

**Why it exists.** `SIGWINCH` is unreliable across transports: serial lines, some telnet/ssh paths, and Windows (no `SIGWINCH` at all; `ReadConsoleInput` is a different, painful API for cross-platform TUIs) can't reliably propagate window size. In-band reporting routes the size through the same byte stream as everything else. 🟢 ([rockorager gist](https://gist.github.com/rockorager/e695fb2924d36b2bcf1fff4a3704bd83))

**Key rules.** Reported size is the **text area only** (excludes terminal padding). The terminal MUST report **both** characters and pixels in one report if it can report pixels at all. It MUST NOT send the notification until the internal resize is complete — i.e., the TTY/app can safely act at the new size when the report arrives (this avoids the resize-storm race). 🟢 ([rockorager gist](https://gist.github.com/rockorager/e695fb2924d36b2bcf1fff4a3704bd83))

**Pixel reporting → fractional/smooth scrolling & images.** The pixel dimensions let an app compute cell pixel size, which underpins pixel-precise positioning, image protocols (kitty graphics), and smooth/fractional scroll math. Textual notes the resize event now carries pixel size "useful in the future when adding image support," and that enabling it also disables line-wrapping on terminals that support it. 🟡 ([Textual PR #5217](https://github.com/Textualize/textual/pull/5217), [kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/))

**Support.** foot, Ghostty, iTerm2, kitty have it; Windows Terminal has an open request. 🟢 ([rockorager gist](https://gist.github.com/rockorager/e695fb2924d36b2bcf1fff4a3704bd83), [microsoft/terminal #19618](https://github.com/microsoft/terminal/issues/19618))

**Mire action:** optional. On Unix, `SIGWINCH` + `TIOCGWINSZ` ioctl is fine for now; mode 2048 becomes valuable if Mire ever wants Windows support, runs over flaky transports, or adds pixel-precise images. The architectural takeaway — _resize is data, not a signal_ — already aligns with a clean render loop.

---

## 3. Unicode width & grapheme gotchas (the corruption engine)

The whole class of bug: **if the app and the terminal disagree about how many cells a string occupies, the cursor desynchronizes and the display corrupts** — and because input echo is positioned by that same cursor, corrupted output corrupts input too. 🟢 ([Jeff Quast, State of Terminal Emulation 2025](https://www.jeffquast.com/post/state-of-terminal-emulation-2025/))

**wcwidth is a single-codepoint heuristic and that's the root flaw.** Historically libc `wcwidth` returns 1 or 2 cells per codepoint (wide East-Asian chars = 2). But a _user-perceived character_ (a Unicode grapheme cluster, per UAX #29) can be many codepoints. The farmer emoji 🧑‍🌾 is U+1F9D1 + ZWJ(U+200D) + U+1F33E — feeding each codepoint to `wcwidth` yields 2 + 0 + 2 = **4 cells**, but it renders as **2**. Cursor jumps two cells too far. 🟢 ([Mitchell Hashimoto, Grapheme Clusters and Terminal Emulators](https://mitchellh.com/writing/grapheme-clusters-in-terminals))

**Mode 2027 — grapheme clustering.** Proposed by Christian Parpart (Contour). When set, the terminal computes cell width by grapheme cluster (UAX #29) instead of per-codepoint `wcwidth`. Detect with `CSI ? 2027 $ p` (DECRQM). Hashimoto's rule: **do not assume either behavior** — query 2027, and if you need certainty, _print and then read back the cursor position_ with `CSI 6 n` (DSR) to learn what the terminal actually did. Assuming wcwidth OR assuming grapheme-clustering will both be wrong on some mainstream terminal. Support is still rare. 🟢 ([Hashimoto](https://mitchellh.com/writing/grapheme-clusters-in-terminals), [kitty #7799](https://github.com/kovidgoyal/kitty/issues/7799), [contour/terminal-unicode-core](https://github.com/contour-terminal/terminal-unicode-core))

**Variation selectors VS15/VS16.** VS16 (U+FE0F) forces emoji presentation, VS15 (U+FE0E) forces text presentation — and on some terminals this _changes width_. Kitty/Ghostty: `⚠` is 1 cell but `⚠️` (+VS16) is 2; `⚡` is 2 but `⚡︎` (+VS15) is 1. Most other terminals apply the presentation but keep the original width. Kitty and Ghostty are noted as the only terminals that correctly honor VS15 for width. This single inconsistency breaks real prompt-length math (e.g. cloud `☁️` in fish). 🟢 ([Hashimoto](https://mitchellh.com/writing/grapheme-clusters-in-terminals), [kitty #3998](https://github.com/kovidgoyal/kitty/issues/3998), [fish #10461](https://github.com/fish-shell/fish-shell/issues/10461))

**Ambiguous width (the older East-Asian-Width problem).** Some codepoints are classified "ambiguous" in the Unicode East Asian Width tables — 1 cell in a Western context, 2 in a CJK context (e.g. `·`). There is no single standard; libraries combine East Asian Width + General Category + hand-tuned overrides and expose a config knob (Ruby's `unicode-display_width` takes an ambiguous-width parameter of 1 or 2). 🟡 ([janlelis/unicode-display_width](https://github.com/janlelis/unicode-display_width), [jquast/wcwidth](https://github.com/jquast/wcwidth))

**Combining marks & ZWJ.** Combining marks (accents) are width 0 and overlay the previous cell; ZWJ (U+200D) is width 0 and fuses neighbors into one cluster — both invisible to naive per-codepoint summing. 🟢 ([Hashimoto](https://mitchellh.com/writing/grapheme-clusters-in-terminals))

**The multiplexer trap.** tmux/zellij are _themselves_ terminal emulators living inside another terminal. If tmux uses `wcwidth` (width 4 for the farmer) but the host terminal uses grapheme clustering (width 2), the two cursors diverge and you get "comical" corruption. Anything Mire emits may be re-interpreted by a multiplexer layer. 🟢 ([Hashimoto](https://mitchellh.com/writing/grapheme-clusters-in-terminals))

**Pragmatic escape hatch.** The lowest-risk app behavior is to _not emit ZWJ sequences at all_ (or replace them with a placeholder glyph) so nothing joins and widths stay predictable. 🟡 ([Quast 2025](https://www.jeffquast.com/post/state-of-terminal-emulation-2025/))

**Mire action:** `Mire/Core/Grapheme.fs` is exactly where this lives. Targeting Kitty/Ghostty (both grapheme-correct, both honor VS16) lets Mire pick grapheme-cluster widths — but it should still segment by grapheme cluster (not codepoint) when filling cells, treat ZWJ/combining as width 0 within a cluster, and treat VS16 as promoting the base to width 2. The DSR-readback technique is the gold-standard correctness check if width bugs ever appear.

---

## 4. VT parser performance — the Williams state machine

**The canonical design.** Paul Flo Williams' "A parser for DEC's ANSI-compatible video terminals" (vt100.net) models VT100→VT500 escape-sequence parsing as a **deterministic finite state machine**: states (ground, escape, csi-entry, csi-param, csi-intermediate, osc-string, dcs-…, etc.), byte-class-driven transitions, and _actions_ fired on transitions — plus an "anywhere" pseudo-state for transitions valid from any state (e.g. CAN/SUB/ESC abort). 🟢 ([vt100.net dec_ansi_parser](https://vt100.net/emu/dec_ansi_parser))

**Why table-driven beats branchy/regex parsing.** The state+byte-class → (action, next-state) mapping collapses to a 2D lookup table. Per input byte you do one table index and one dispatch — no backtracking, no regex engine, no deep `if/else` ladders, and it is _language-independent_ and total (every byte has a defined transition, so malformed input can't wedge the parser). VTParse (Joshua Haberman) and AnsiParser (Microlithix) are direct implementations; the design is what real terminals translate straight into lookup tables. 🟢 ([vtparse](https://github.com/haberman/vtparse), [Microlithix AnsiParser refs](https://www.microlithix.com/AnsiParser/docs/References.html), [vt100.net](https://vt100.net/emu/dec_ansi_parser))

**UTF-8 decode cost & SIMD.** The byte parser and UTF-8 decoding are the hot path for high-throughput output (`cat biglog`). Ghostty's read thread runs "a heavily optimized terminal parser that leverages CPU-specific SIMD instructions," and isolates parsing on a dedicated read thread so it never blocks rendering. This is the emulator side, but the lesson for a _TUI_ is symmetric: keep the input decode path (Mire's `InputParser`) allocation-free and table/branch-lean. 🟡 ([Ghostty README / itsfoss summary](https://itsfoss.com/ghostty-terminal-features/))

**Mire action:** Mire is the _application_, not the emulator, so its parser concern is `InputParser` (bytes → InputEvent). The Williams DFA is the reference architecture even for that: a table-driven CSI/SS3 decoder is more robust against partial reads and odd sequences than nested matching, and is the natural home for the still-undecoded Kitty `CSI u` / mouse / paste forms noted in the roadmap.

---

## 5. ANSI cost model — the bytes-per-frame mindset

The governing approximation: **the time a terminal needs to ingest and present a frame is proportional to the size of the rasterized byte stream.** Fewer bytes ≈ faster frame ≈ lower latency. notcurses tracks `raster_min_bytes`/`raster_max_bytes` precisely because frame cost is byte-count-dominated and varies with how much actually changed. 🟢 ([notcurses HACKING.md](https://github.com/dankamongmen/notcurses/blob/master/doc/HACKING.md), [notcurses_stats](https://notcurses.com/notcurses_stats.3.html))

Levers, in order of impact:

1. **Emit only changed cells (damage diffing).** Don't repaint the screen; diff against the last frame and write the changed runs. notcurses calls the skipped cells "elisions" (`cellelisions`) vs emitted (`cellemissions`). This is exactly Mire's `Surface` + `Diff.compute` model. 🟢 ([notcurses HACKING.md](https://github.com/dankamongmen/notcurses/blob/master/doc/HACKING.md))
2. **Prefer relative cursor moves over absolute addressing where shorter.** ncurses optimizes cursor motion with relative moves (e.g. a bare `\n` to go down one, after clearing `ONLCR` so the terminal doesn't inject a CR) because they're fewer bytes than a full `CSI row;col H`. ncurses even factors **line speed (baud)** into whether relative vs absolute wins. 🟢 ([genode #3380](https://github.com/genodelabs/genode/issues/3380), [ncurses curs_move](https://invisible-island.net/ncurses/man/curs_move.3x.html))
3. **Coalesce SGR / track a pen delta.** Don't re-send the full style for every cell. Track the current pen (fg/bg/attrs) and emit an SGR only when it changes — and set groups of attributes in one `SGR` rather than one sequence per attribute. Neovim's TUI does exactly this (terminfo `sgr` group-set + relative motion). Re-sending identical styles is pure wasted bytes. 🟢 ([neovim PR #6816](https://github.com/neovim/neovim/pull/6816))
4. **Overwrite rather than clear-then-write where possible.** A full clear (`ED`/`EL`) followed by repaint is more bytes and a bigger visible disturbance than overwriting only the cells that differ; full clears also invite flicker and (with a full-reset-per-resize) runaway redraw loops. 🟡 ([hermes-agent #19216](https://github.com/NousResearch/hermes-agent/issues/19216))

**Mire action:** `Mire/Renderer/Diff.fs` already does (1). Verify (2) and (3): the diff writer should carry pen state across runs and only emit `Style.ToAnsi` on a real change (SGR coalescing), and should consider `\n`/relative moves for adjacent runs instead of always re-addressing. Centralize every escape in `Mire/Protocol/ANSI.fs` (already the convention) so the cost model lives in one place.

---

## 6. Latency & frame pacing — measurement + targets

**Why latency, not throughput.** Dan Luu argues classic terminal benchmarks measure throughput (bytes/sec absorbed) which is "basically irrelevant to user experience"; what matters is **input-to-display latency**. 🟢 ([Dan Luu, Terminal latency](https://danluu.com/term-latency/))

**Measured idle median latencies (2014 MBP, Luu).** terminal.app ~6 ms, emacs-eshell ~5 ms, st ~25 ms, hyper ~32 ms, alacritty ~31 ms, iTerm2 ~44 ms. Tail latencies (99.9th pct) blow up for _every_ terminal into clearly-perceptible territory. Conclusion: most terminals would improve UX by spending less on features and more on latency. 🟢 ([Dan Luu](https://danluu.com/term-latency/))

**Perception threshold.** Added latency starts to be noticeable in roughly the **10–20 ms** range and up; people tolerate bad terminal latency only because everything is bad, so "most people just expect terrible latency." 🟡 ([Dan Luu](https://danluu.com/term-latency/))

**Measure end-to-end, not in isolation.** Two complementary methods:

- _Software_ — **Pavel Fatin's Typometer** synthesizes input events and screen-scrapes the result, capturing the _whole_ chain (OS queue, VM, app, GPU pipeline, buffering, WM, V-Sync). His "Typing with pleasure" article is the canonical write-up; goal: drive added latency toward ~1 ms ("zero-latency typing"). Caveat: tied to OS capture APIs and can mis-penalize GPU apps. 🟢 ([Typometer](https://github.com/pavelfatin/typometer), [Typing with pleasure](https://pavelfatin.com/typing-with-pleasure/))
- _Hardware_ — **Tristan Hume's keyboard-to-photon latency tester** uses a Teensy (1000 Hz USB polling to avoid the ~8 ms jitter of 125 Hz devices) + a light sensor to measure true end-to-end "what you see," excluding only keyboard firmware. He found Apple Terminal and kitty near-optimal; iTerm2 and Alacritty worse. This is the most honest method because it measures photons, not buffers. 🟢 ([Hume, Making a latency tester](https://thume.ca/2020/05/20/making-a-latency-tester/))

**Frame pacing.**

- **Fixed-interval render loop, not per-event repaint.** Run the loop at a fixed tick and let it absorb bursts; rendering synchronously on every input/output event causes redundant repaints and big perf cliffs under load. One ncurses+event-loop integration uses a **10 ms coalescing timer** (~100 fps cap) to batch "something changed" into a single refresh. 🟡 ([LinuxJedi, Event Loops and NCurses](https://linuxjedi.co.uk/event-loops-and-ncurses/))
- **30 vs 60 fps.** No authoritative terminal-specific 30-vs-60 study surfaced. The defensible position: a TUI's bottleneck is byte-stream cost and human perception (~10–20 ms), so a coalescing render at 30 fps (≈33 ms) is usually fine for content updates, while _input echo_ should bypass the tick and feel immediate; bumping to 60 fps mainly helps smooth-scroll/animation. (Mire's ~30 FPS loop is a reasonable default.) 🔴 (synthesis; no single primary source)
- **GPU/I-O decoupling lesson.** Ghostty hits 60 fps during `cat large_file.log` by giving each terminal a separate read/write/render thread, so a flood of output never stalls the renderer. The portable lesson for a single-threaded TUI runtime: keep produce-frame (view/diff) cheap and don't let output bursts block the loop. 🟡 ([itsfoss Ghostty](https://itsfoss.com/ghostty-terminal-features/))

**Mire action:** keep the fixed ~30 FPS loop; ensure input is reflected on the _next immediate_ frame (don't debounce echo). When wiring synchronized output, the loop becomes: coalesce messages → build Surface → `Diff.compute` → `BSU` + write runs + `ESU` → flush.

---

## 7. Double-buffering / flicker

**The classic model.** Console double-buffering = draw to an off-screen back buffer, then blit to the front (screen) — no partial states visible, no flicker. ncurses' deferred model is this: `wmove`/draws mutate an off-screen window; the cursor and screen don't change until `refresh()`, which computes and flushes a delta. 🟢 ([ncurses curs_move](https://invisible-island.net/ncurses/man/curs_move.3x.html))

**The modern, byte-minimal refinement (diff, not blit).** notcurses improves on full-buffer blitting: a **render** phase flattens z-ordered planes into a cell matrix; a **rasterize** phase diffs that matrix against a retained **"last frame"** via a 2D **damage map** and emits _only changed cells_ (eliding the rest) as an optimized escape stream. This both minimizes bytes (§5) and prevents flicker. 🟢 ([notcurses HACKING.md](https://github.com/dankamongmen/notcurses/blob/master/doc/HACKING.md), [notcurses_render](https://notcurses.com/notcurses_render.3.html))

**Non-obvious correctness rule.** _Rendering must never read the "last written frame,"_ because another render (or a resize) can land between your render and your rasterize, invalidating that assumption. The damage map is 2D (matches the grid); only the final rasterized output is flattened to 1D. 🟢 ([notcurses HACKING.md](https://github.com/dankamongmen/notcurses/blob/master/doc/HACKING.md))

**Residual flicker source.** Even with perfect diffing, pixel graphics (sprixels) that aren't a whole multiple of cell height can partially obstruct a cell row and force flickery redraws when underlying cells change — a non-text edge case to be aware of if Mire ever adds the kitty image protocol. 🟢 ([notcurses HACKING.md](https://github.com/dankamongmen/notcurses/blob/master/doc/HACKING.md))

**Resize handling done right.** `SIGWINCH` handler should do _almost nothing_ — set an atomic flag. The render loop notices the flag, does the `ioctl(TIOCGWINSZ)` to read new geometry at the top of the next render, and resizes its back/last-frame buffers (zero-initializing new area). Debounce resize storms (~150 ms) so a drag collapses to one final resize. **Do not full-clear/full-reset on every resize frame** — that's a documented cause of infinite render loops and flicker. (This pairs with mode 2048 from §2, which makes resize an in-band, already-complete event.) 🟢 ([notcurses discussion #2160](https://github.com/dankamongmen/notcurses/discussions/2160), [Koucha SIGWINCH](http://rkoucha.fr/tech_corner/sigwinch.html), [Kilo cloud #1195](https://github.com/Kilo-Org/cloud/issues/1195), [hermes-agent #19216](https://github.com/NousResearch/hermes-agent/issues/19216))

**Mire action:** Mire's `Surface` (back buffer) + `Diff.compute` (damage) is exactly the notcurses model — good. Apply the rule "diff against last frame, never let view/layout read the terminal." Make sure the resize path is flag → ioctl-at-render → resize buffers (no per-frame clear), and wrap each flush in mode 2026 so the diffed runs land as one atomic, tear-free frame.

---

## 8. Gotchas checklist (punchy)

- **Bracket every frame in `\x1b[?2026h … \x1b[?2026l`.** Cheap, safe on unsupporting terminals, kills tearing. Don't gate on DECRQM — just do it.
- **DECRQM replies 0 or 4 = unsupported; 1/2 = supported.** No reply also = unsupported.
- **`wcwidth` per-codepoint is wrong for emoji/ZWJ/VS16.** Segment by grapheme cluster (UAX #29), not codepoint.
- **VS16 (U+FE0F) can promote a base char from width 1 → 2** on Kitty/Ghostty. Account for it; other terminals don't.
- **ZWJ and combining marks are width 0.** Summing codepoint widths over-counts.
- **When in doubt about width, print + read cursor back with `CSI 6 n`.** Don't assume either width model.
- **Inside tmux/zellij you're talking to another emulator** — width mismatches between layers corrupt the display.
- **Frame cost ≈ bytes emitted.** Diff; emit only changed cells.
- **Track a pen; emit SGR only on style change; group attributes.** Re-sending identical styles is wasted bytes.
- **Prefer relative cursor moves (e.g. `\n`) over absolute `CSI r;c H` when shorter** (clear `ONLCR` first).
- **Overwrite, don't clear-then-repaint.** Full clears flicker and can trigger redraw loops.
- **Never read the terminal/last-written-frame during render** — it can change before your bytes land.
- **Resize: signal sets an atomic flag; render loop does the ioctl + buffer resize.** Debounce ~150 ms. Never full-reset per frame.
- **Consider mode 2048 for in-band resize + pixel size** if you need Windows, flaky transports, or images.
- **Latency, not throughput, is the metric.** Target the low-single-digit ms range; ~10–20 ms is where users start noticing.
- **Echo input on the next immediate frame — never debounce input echo.** Debounce only output bursts and resize.
- **Table-driven DFA parser (Williams)** for input decoding: total, branch-lean, robust to partial reads.

---

## 9. Sources

Synchronized output (2026):

- 🟢 contour vt-extensions spec — https://github.com/contour-terminal/vt-extensions/blob/master/synchronized-output.md
- 🟢 contour docs — https://contour-terminal.org/vt-extensions/synchronized-output/
- 🟢 christianparpart gist (DECRQM/DECRPM table, terminal list) — https://gist.github.com/christianparpart/d8a62cc1ab659194337d73e399004036
- 🟡 bubbletea discussion #1320 (don't-query advice) — https://github.com/charmbracelet/bubbletea/discussions/1320
- 🟡 zellij PR #3884 (reporting fix) — https://github.com/zellij-org/zellij/pull/3884

In-band resize / pixel reporting (2048):

- 🟢 rockorager gist (spec) — https://gist.github.com/rockorager/e695fb2924d36b2bcf1fff4a3704bd83
- 🟡 Textual PR #5217 — https://github.com/Textualize/textual/pull/5217
- 🟢 microsoft/terminal #19618 — https://github.com/microsoft/terminal/issues/19618
- 🟡 kitty graphics protocol — https://sw.kovidgoyal.net/kitty/graphics-protocol/

Unicode width & graphemes:

- 🟢 Mitchell Hashimoto, Grapheme Clusters and Terminal Emulators (mode 2027, VS16, multiplexer trap) — https://mitchellh.com/writing/grapheme-clusters-in-terminals
- 🟢 Jeff Quast, State of Terminal Emulation 2025 — https://www.jeffquast.com/post/state-of-terminal-emulation-2025/
- 🟢 kitty #7799 (mode 2027) — https://github.com/kovidgoyal/kitty/issues/7799
- 🟢 kitty #3998 (VS15/VS16 width) — https://github.com/kovidgoyal/kitty/issues/3998
- 🟢 fish #10461 (☁️ prompt bug) — https://github.com/fish-shell/fish-shell/issues/10461
- 🟡 contour terminal-unicode-core spec — https://github.com/contour-terminal/terminal-unicode-core
- 🟡 janlelis/unicode-display_width (ambiguous width) — https://github.com/janlelis/unicode-display_width
- 🟡 jquast/wcwidth — https://github.com/jquast/wcwidth

VT parser:

- 🟢 Paul Flo Williams, vt100.net DEC ANSI parser — https://vt100.net/emu/dec_ansi_parser
- 🟢 vtparse (Haberman) — https://github.com/haberman/vtparse
- 🟡 Microlithix AnsiParser references — https://www.microlithix.com/AnsiParser/docs/References.html
- 🟡 Ghostty (SIMD parser, threads) — https://itsfoss.com/ghostty-terminal-features/ ; https://mitchellh.com/writing/libghostty-is-coming

ANSI cost model:

- 🟢 ncurses curs_move (deferred refresh, relative moves) — https://invisible-island.net/ncurses/man/curs_move.3x.html
- 🟢 genode #3380 (relative cursor / ONLCR) — https://github.com/genodelabs/genode/issues/3380
- 🟢 neovim PR #6816 (SGR coalescing, relative motion) — https://github.com/neovim/neovim/pull/6816
- 🟢 notcurses stats (raster bytes, elisions) — https://notcurses.com/notcurses_stats.3.html

Latency & frame pacing:

- 🟢 Dan Luu, Terminal latency — https://danluu.com/term-latency/
- 🟢 Pavel Fatin, Typing with pleasure — https://pavelfatin.com/typing-with-pleasure/
- 🟢 Typometer — https://github.com/pavelfatin/typometer
- 🟢 Tristan Hume, Making a latency tester — https://thume.ca/2020/05/20/making-a-latency-tester/
- 🟡 LinuxJedi, Event Loops and NCurses (10 ms coalescing) — https://linuxjedi.co.uk/event-loops-and-ncurses/

Double-buffering / flicker / resize:

- 🟢 notcurses HACKING.md (render/rasterize, damage map, last-frame rule) — https://github.com/dankamongmen/notcurses/blob/master/doc/HACKING.md
- 🟢 notcurses_render — https://notcurses.com/notcurses_render.3.html
- 🟢 notcurses discussion #2160 (SIGWINCH→flag) — https://github.com/dankamongmen/notcurses/discussions/2160
- 🟢 R. Koucha, Playing with SIGWINCH — http://rkoucha.fr/tech_corner/sigwinch.html
- 🟡 Kilo cloud #1195 (resize debounce) — https://github.com/Kilo-Org/cloud/issues/1195
- 🟡 hermes-agent #19216 (full-reset render loop) — https://github.com/NousResearch/hermes-agent/issues/19216
