# Voice

Five rules. They apply to the README, landing copy, CLI help, error messages,
commit messages, and release notes. Five is the limit — at six, nobody follows any.

### 1. Lead with the verb

What it does, first. Not what category it belongs to.

- Yes: `Diffs a cell grid and paints only the runs that changed.`
- No:  `Mire is a framework that provides a rendering layer which can be used to update the terminal.`

### 2. No marketing adjectives. Numbers or nothing

"Fast" needs a number, or it gets cut.

- Yes: `Redraws at ~30 FPS; a diff touches only changed cells.`
- No:  `Blazing-fast, buttery-smooth rendering.`

### 3. You, never "users"

Address the reader directly. "Developers" and "users" turn copy generic.

- Yes: `You write update and view; Mire owns the loop.`
- No:  `Users implement their update logic and the framework handles the rest.`

### 4. Show, don't say

A four-line terminal frame beats a paragraph. Real code beats a feature list.

- Yes:
  ```
  ┌ counter ──────────┐
  │ count: 7          │
  │ [+] inc  [-] dec  │
  └───────────────────┘
  ```
- No:  `Mire makes it possible to build rich, interactive terminal interfaces with panels and borders.`

### 5. No emoji, no em-dash chains

No emoji in copy, headings, or commit messages — the symbol `└` is the brand mark.
One em-dash per paragraph, maximum. LLM prose sprays them.

- Yes: `Targets Ghostty and Kitty-compatible terminals. Legacy consoles are out of scope.`
- No:  `Mire is the next-gen ✨ runtime — built for the future — that redefines — yes, redefines — the terminal.`

## Notes

- Sentence case in headings: "Getting started", not "Getting Started".
- Be honest about limits. "Mouse input is enabled but not yet decoded" earns more
  trust than silence. (See `CLAUDE.md` — known gaps are documented, not hidden.)
- Oxford comma: yes.
