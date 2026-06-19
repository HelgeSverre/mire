# Terminal protocols Mire uses

The inventory of every escape sequence / terminal protocol Mire **emits** or **parses**,
where it lives in the code, and how complete our support is. Keep this honest — it's the
source of truth for "what terminals can run Mire" and for the public
[Terminal support](../website/src/content/docs/reference/terminal-support.md) page.

Mire targets **modern Kitty-compatible terminals** (Ghostty first); legacy / 16-color /
Windows-console support is explicitly out of scope.

## Emitted (output)

All sequences live in `Mire/Protocol/ANSI.fs`; the runtime writes them in
`Mire/App/Program.fs` (`Runtime.run` setup/teardown) and the per-frame painter in
`Mire/Renderer/Diff.fs` (`renderToTerminal`).

| Protocol | Sequence | DEC mode / spec | Binding | Status |
|---|---|---|---|---|
| Alternate screen | `CSI ?1049 h/l` | xterm private mode | `enterAltScreen`/`exitAltScreen` | ✅ |
| Cursor show/hide | `CSI ?25 h/l` | DECTCEM | `cursorShow`/`cursorHide` | ✅ |
| Cursor positioning | `CSI y;x H` etc. | ANSI | `cursorTo`/`cursorUp`… | ✅ |
| Truecolor SGR | `CSI 38;2;r;g;b m` / `48;2;…` | ISO 8613-6 | `foregroundRgb`/`backgroundRgb`, `Color.ToAnsiFg/Bg` | ✅ truecolor only (no 256/16 fallback — by design) |
| Text attributes | `CSI 1/2/3/9 m` | SGR | `bold`/`dim`/`italic`/`strikethrough` | ✅ |
| Colored & styled underlines | `CSI 4 m`, `4:3` (curly), `4:4` (dotted), `4:5` (dashed), `21` (double) | [kitty: underlines](https://sw.kovidgoyal.net/kitty/underlines/) | `Style.ToAnsi` | ✅ styles; ⬜ underline **color** (`58:2:…`) not emitted |
| Mouse: button-event tracking | `CSI ?1002 h/l` | xterm | `enableMouse`/`disableMouse` | ✅ press/release/drag (drag motion not flagged — see gaps) |
| Mouse: SGR extended coords | `CSI ?1006 h/l` | xterm SGR (1006) | `enableMouse` | ✅ |
| Bracketed paste | `CSI ?2004 h/l` | xterm | `enableBracketedPaste` | ✅ (+ cross-read reassembly) |
| Focus reporting | `CSI ?1004 h/l` | xterm | `enableFocusEvents` | ✅ |
| Light/dark theme notifications | `CSI ?2031 h/l` + query `CSI ?996 n` | Contour/kitty (DEC 2031, DSR 996) | `enableThemeNotifications`/`queryColorScheme` | ✅ opt-in (`Program.withThemeNotifications`) |
| Synchronized output | `CSI ?2026 h/l` | iTerm2/kitty/Ghostty (DEC 2026) | `beginSync`/`endSync` (wraps every frame) | ✅ |
| Kitty keyboard protocol | push `CSI > 3 u` / pop `CSI < u` | [kitty: keyboard](https://sw.kovidgoyal.net/kitty/keyboard-protocol/) | `enableKittyKeyboard`/`disableKittyKeyboard` | ✅ flags **1+2**; ⬜ flags 4/8/16 (see gaps) |
| OSC 8 hyperlinks | `OSC 8 ; ; url ST` | OSC 8 | `osc8Open`/`osc8Close` (Diff brackets linked runs) | ✅ |
| OSC 52 clipboard | `OSC 52 ; c ; <base64> ST` | xterm OSC 52 | `setClipboard` (via `Cmd.writeRaw`) | ✅ write; ⬜ no paste-from-clipboard query |
| Kitty graphics protocol | `APC _G … ST` (`f=100,a=T`, chunked) | [kitty: graphics](https://sw.kovidgoyal.net/kitty/graphics-protocol/) | `kittyImage`/`deleteImages` (`Cmd.kittyImage`) | ✅ PNG transmit-and-display + delete; ⬜ no animation/placement ids/Unicode-placeholder |

## Parsed (input)

All decoding lives in `Mire/Protocol/InputParser.fs`; the runtime drives it from
`readRawBytes` → `stepPasteBuffer` → `parseBytes` in `Runtime.run`.

| Protocol | Wire form | Decoder | Status |
|---|---|---|---|
| Kitty keyboard: Unicode keys | `CSI codepoint ; mods[:event] u` | `parseEscSequence` (`'u'`), `keyOfCodepoint` | ✅ |
| Kitty keyboard: event types | `:event` subparam (1 press / 2 repeat / 3 release) | `kittyEventType` (also on legacy CSI forms) | ✅ press/repeat/release; runtime drops Release unless `withKeyReleases` |
| Kitty keyboard: functional keys | PUA 57344–57454 (keypad, F13–F35, media/locks) | `keyOfFunctional` | ✅ |
| Legacy cursor/nav keys | `CSI [1;mods] A/B/C/D/H/F`, `CSI Z` (backtab) | `parseEscSequence` | ✅ (event types decoded too) |
| Legacy editing/F-keys | `CSI n [;mods] ~`, `CSI P/Q/S` | `parseEscSequence` (`'~'`) | ✅ (F3 via `CSI R` omitted — collides with cursor-position report; works via SS3) |
| Application cursor keys | `SS3 ESC O A/B/C/D/P–S/H/F` (DECCKM) | `parseEscSequence` (`0x4F`) | ✅ |
| Ctrl chords / control bytes | raw `0x00–0x1F` | `parseBytes` | ✅ |
| SGR mouse (1006) | `CSI < b ; x ; y M/m` | `parseMouseSgr` | ✅ button/wheel/mods/coords + motion bit `0x20` → `MouseEvent.Moved` (drag) |
| Multi-event reads | any back-to-back sequences in one `read()` | `parseAll` (tokenizer) | ✅ split into per-event spans; the runtime loops over them |
| Bracketed paste | `CSI 200~ … CSI 201~` | `parsePaste` + `stepPasteBuffer` | ✅ (reassembles across reads, 1 MiB cap) |
| Focus events | `CSI I` / `CSI O` | `parseFocus` | ✅ |
| Theme report | `CSI ?997;1 n` (dark) / `;2 n` (light) | `parseThemeReport` | ✅ |

## Known gaps / not yet supported

Tracked here so they don't get "rediscovered" as bugs. None block the targeted terminals
for ordinary use; listed worst-impact first.

1. ~~**One event per read (coalescing).**~~ **Fixed.** `InputParser.parseAll` tokenizes a
   raw buffer into per-event byte spans and the runtime loops over them, so back-to-back
   sequences (a scroll/drag burst, fast typing, queued escapes) each decode. Bracketed
   pastes stay one span. (Multi-byte UTF-8 keystrokes — accents/CJK/emoji — now decode too,
   a related fix in `parseBytes`.)
2. ~~**Mouse motion / drag not distinguished.**~~ **Fixed.** `parseMouseSgr` decodes the
   SGR `0x20` motion bit into `MouseEvent.Moved`, so a button-held drag (mode 1002) is
   distinguishable from a fresh click — the basis for mouse text-selection, drag-resize,
   and drag-scroll.
3. **No hover (mode 1003).** Any-motion tracking isn't enabled, so there are no
   move-without-button events (hover highlights, hover tooltips). Deliberate scope choice
   (1003 floods input); revisit if a widget needs hover.
4. **Kitty keyboard flags 4/8/16 not requested.** We push `1+2` only, so we don't get
   *alternate keys* (4 — shifted + base-layout codepoints, for layout-independent binds),
   *all-keys-as-escape* (8), or *associated text* (16). Consequences: `CSI u` keys carry
   `Text = None` (text entry relies on the legacy UTF-8 byte path, which works); and even if
   a terminal sent alternate-key codepoints, `parseCsi` keeps only the first `:`-field, so
   they'd be ignored.
5. **Modifier bits beyond Super.** `modifiersOf` folds Super→`Meta` and ignores Hyper (16),
   real-Meta (32), Caps-Lock (64), Num-Lock (128). Fine for typical binds.
6. **Underline color** (`SGR 58`) and clipboard **read-back** (OSC 52 query) are emit-only /
   not implemented.

## Maintenance

When you add or change a sequence: update `Mire/Protocol/ANSI.fs` (the one place raw escapes
live), add a row here, and — if it changes the user-visible capability set — update the
website [Terminal support](../website/src/content/docs/reference/terminal-support.md) page.
Add/extend an `InputParser` test for any new decode path.
