namespace Mire.Demo.Agent

/// The "Dummy": maps a submitted prompt string to a canned response. No LLM. This is
/// the single source of truth for the demo's command vocabulary — `commands` is reused
/// by the `help` block and the command palette.
module Dummy =

    type MetaAction =
        | ToggleSidebar
        | CycleMode
        | OpenSkills
        | OpenPalette
        | OpenMcp
        | ShowWelcome

    type ModalKind =
        | PermissionModal
        | ApprovalModal
        | ConfirmClearModal

    type ModalSpec =
        { Title: string
          Intro: string
          Command: string
          Risk: string option
          AcceptLabel: string
          DenyLabel: string
          Kind: ModalKind }

    type ToolRun =
        { Name: string
          Cmd: string
          RunningOut: string
          DelayMs: int
          FinalStatus: ToolStatus
          FinalMeta: string
          FinalOut: string }

    type Response =
        | AppendBlocks of Block list
        | StreamMarkdown of string
        | RunningTool of ToolRun
        | SpawnToast of Theme.Tone * string * string
        | OpenModal of ModalSpec
        | MetaCmd of MetaAction
        | ClearTranscript

    /// (trigger, description) — drives `help` and the command palette.
    let commands: (string * string) list =
        [ "markdown", "markdown kitchen-sink"
          "code", "fenced code block"
          "list", "nested lists"
          "quote", "blockquote"
          "long", "long wrapped prose"
          "table", "data table"
          "stream", "streamed answer"
          "stream:long", "long streamed answer"
          "tool", "tool call (ok)"
          "tool:error", "failing tool"
          "tool:run", "running tool (spinner)"
          "tools", "multiple tool calls"
          "bash", "bash command"
          "search", "search results"
          "thinking", "reasoning block"
          "diff", "unified diff"
          "files", "file tree"
          "tasks", "task timeline"
          "plan", "plan checklist"
          "turn", "full agent turn"
          "usage", "token usage / cost"
          "image", "image preview (fallback)"
          "mention", "@file mentions"
          "paste", "paste → chip help"
          "permission", "permission modal (buttons)"
          "approval", "approval modal"
          "confirm", "confirm modal"
          "warning", "warning toast"
          "toast", "success toast"
          "error", "error card"
          "notice", "info notice"
          "help", "list commands"
          "clear", "clear transcript"
          "panel", "toggle sidebar"
          "mode", "cycle mode"
          "skills", "open skill explorer"
          "palette", "open command palette"
          "mcp", "MCP servers (connect/auth/tools)" ]

    // ── canned content ──────────────────────────────────────────────────────────
    let markdownKitchenSink =
        "# Markdown kitchen-sink\n\
         A paragraph with **bold**, *italic*, ~~strike~~, `inline code`, and a [link](https://ghostty.org).\n\n\
         ## Lists\n\
         - first bullet\n\
         - second bullet\n\
         - third, long enough that it wraps to the next line within the transcript region\n\n\
         1. ordered one\n\
         2. ordered two\n\n\
         ## Quote\n\
         > Mire is a tiny browser for modern terminals.\n\n\
         ## Code\n\
         ```fsharp\n\
         let view model = Dock.dock [ Dock.fill (transcript model) ]\n\
         ```\n\n\
         ---\n\
         That's the kitchen-sink."

    let codeSample =
        "Here's the layout call:\n\
         ```fsharp\n\
         Dock.dock [\n\
           Dock.top 3 (header model)\n\
           Dock.bottom 3 (promptBox model)\n\
           Dock.fill (transcript model)\n\
         ]\n\
         ```"

    let listSample =
        "## Nested lists\n\
         - layout\n\
         - input\n\
         - rendering\n\n\
         1. measure\n\
         2. render\n\
         3. diff"

    let quoteSample =
        "> Regions over widgets.\n\
         > Retained view tree, diffed renderer.\n\
         > Overlays are portals, not hacks."

    let longProse =
        "# A longer answer\n\
         This block is intentionally long so the transcript scrolls.\n\n\
         Mire follows The Elm Architecture, adapted for terminal realities. You describe your app as a Program: an init, an update, a view, an input mapper, and subscriptions. The runtime reads input, decodes it to an event, maps it to a message, updates the model, builds the view, lays it out onto a Surface, diffs against the previous frame, and writes only the changed cell runs.\n\n\
         ## Why diffing\n\
         Without diffing, a 160x50 terminal is 8,000 cells per frame; at 30 FPS that is 240,000 cell writes per second even if only a spinner changed. With run-based diffing a spinner frame is one cursor move and one character.\n\n\
         ## Why regions\n\
         Agent UIs need a fixed header, a fixed composer, a scrolling transcript, floating completion, and modal overlays — all at once. Regions make each of those a first-class, independently-scrolling area.\n\n\
         > The key sentence: Mire is a tiny browser for modern terminals, with F# as the UI language and coding-agent apps as the reference workload."

    let streamSample =
        "Streaming this answer word by word to exercise the coalesced render path. In the TUI a Sub.Every 45ms appends one word per tick while a flag is set, and Esc interrupts it."

    let thinkingSample =
        "The user wants a second demo. I should reuse the existing layout primitives rather than inventing new nodes, and keep agent concepts out of the core libraries. The transcript can be a vstack of Content-sized cards inside a single Scroll region…"

    let private diffLines: DiffLine list =
        [ { Sign = ' '
            Text = "| 0x44uy -> Some { Key = ArrowLeft; … }" }
          { Sign = '+'
            Text = "| 0x5Auy -> Some { Key = Tab; Modifiers = { None with Shift = true } }" }
          { Sign = ' '
            Text = "| 0x48uy -> Some { Key = Home; … }" } ]

    let private welcomeText =
        "# └ mire · AgentDemo\n\
         A testbed for the agentic-TUI features in `SPEC.md`. Not wired to an LLM — the **Dummy** module supplies canned responses.\n\n\
         Try `markdown`, `tool:run`, `diff`, `permission`, or `help`. Press `Ctrl+O` for the skill explorer, `Shift+Tab` to switch mode."

    let welcomeBlock = AssistantMd welcomeText

    let private helpBlock () =
        let body =
            commands
            |> List.map (fun (t, d) -> sprintf "- `%s` — %s" t d)
            |> String.concat "\n"

        AssistantMd(
            "# Commands\nType any of these and press Enter, or open the palette with `Ctrl+P`.\n\n"
            + body
        )

    /// Map a (lower-cased, trimmed) prompt to a response.
    let respond (text: string) : Response =
        match text with
        | "markdown"
        | "md" -> AppendBlocks [ AssistantMd markdownKitchenSink ]
        | "code" -> AppendBlocks [ AssistantMd codeSample ]
        | "list" -> AppendBlocks [ AssistantMd listSample ]
        | "quote" -> AppendBlocks [ AssistantMd quoteSample ]
        | "long"
        | "lorem" -> AppendBlocks [ AssistantMd longProse ]
        | "stream" -> StreamMarkdown streamSample
        | "stream:long" -> StreamMarkdown longProse
        | "stream:code" -> StreamMarkdown codeSample
        | "table" ->
            AppendBlocks
                [ TableBlock(
                      [ "ID"; "Title"; "Status" ],
                      [ [ "#128"; "Decode mouse (SGR 1006)"; "open" ]
                        [ "#131"; "Focus manager"; "open" ]
                        [ "#140"; "Scroll off-screen blit"; "done" ] ]
                  ) ]
        | "tool" ->
            AppendBlocks
                [ ToolCall(
                      "shell",
                      "dotnet build Mire.slnx",
                      Succeeded,
                      "1.2s",
                      "Build succeeded.\n  0 Warning(s)\n  0 Error(s)"
                  ) ]
        | "tool:error" ->
            AppendBlocks
                [ ToolCall(
                      "shell",
                      "npm test",
                      Failed,
                      "exit 1",
                      "FAIL  src/app.test.ts\n  ✗ renders (12 ms)\n2 failed, 5 passed"
                  ) ]
        | "tool:run" ->
            RunningTool
                { Name = "shell"
                  Cmd = "npm install"
                  RunningOut = "resolving packages…"
                  DelayMs = 1600
                  FinalStatus = Succeeded
                  FinalMeta = "4.1s"
                  FinalOut = "added 312 packages in 4s" }
        | "tools" ->
            AppendBlocks
                [ ToolCall("read", "Mire.Demo.Agent/Theme.fs", Succeeded, "", "")
                  ToolCall("edit", "Mire.Demo.Agent/Theme.fs", Succeeded, "+12 -3", "")
                  ToolCall("shell", "dotnet build", Succeeded, "1.2s", "Build succeeded.") ]
        | "bash" -> AppendBlocks [ ToolCall("bash", "git status -s", Succeeded, "0.1s", " M Program.fs\n ?? Theme.fs") ]
        | "search" ->
            AppendBlocks
                [ ToolCall(
                      "search",
                      "rg \"Scroll\" -n",
                      Succeeded,
                      "4 matches",
                      "Layout/Layout.fs:33:  | Scroll of Rect * ScrollState\nLayout/Layout.fs:200: | Scroll(viewport,..)\nWidgets/Widgets.fs:108: module Scroll"
                  ) ]
        | "thinking" -> AppendBlocks [ Thinking thinkingSample ]
        | "diff" -> AppendBlocks [ DiffBlock("Mire.Protocol/InputParser.fs", diffLines) ]
        | "files"
        | "tree" ->
            AppendBlocks
                [ FileTree
                      [ "└ Mire.Demo.Agent/"
                        "  Theme.fs"
                        "  Markdown.fs"
                        "  Blocks.fs"
                        "  Dummy.fs"
                        "  Skills.fs"
                        "  PromptInput.fs"
                        "  Program.fs" ] ]
        | "tasks"
        | "timeline" ->
            AppendBlocks
                [ TaskTimeline
                      [ "init model", Succeeded
                        "run dummy", Succeeded
                        "stream tokens", Running
                        "resolve tool", Running ] ]
        | "plan" ->
            AppendBlocks
                [ PlanBlock
                      [ true, "HTML prototype"
                        true, "Shift+Tab decode"
                        false, "AgentDemo project"
                        false, "DEMO-TODOS.md"
                        false, "docs" ] ]
        | "turn" ->
            AppendBlocks
                [ Thinking "Plan: read the file, make the edit, rebuild, then summarize."
                  ToolCall("edit", "Program.fs", Succeeded, "+8 -2", "")
                  AssistantMd "Done — added the **@mention** popup and rebuilt. See `Program.fs` and @DEMO-TODOS.md." ]
        | "usage"
        | "cost" ->
            AppendBlocks
                [ TableBlock(
                      [ "item"; "tokens"; "usd" ],
                      [ [ "input"; "8,210"; "$0.02" ]
                        [ "output"; "1,940"; "$0.03" ]
                        [ "total"; "10,150"; "$0.05" ] ]
                  ) ]
        | "image" ->
            AppendBlocks
                [ Notice(
                      Theme.Warning,
                      "▢ image: chart.png\n[image preview unavailable] — Kitty graphics not wired yet (ROADMAP v0.5)."
                  ) ]
        | "mention" ->
            AppendBlocks
                [ AssistantMd
                      "I read @Mire/Layout/Layout.fs and @README.md.\n\nType `@` in the prompt to pick a file — a completion popup appears; arrows + Enter insert it." ]
        | "paste" ->
            AppendBlocks
                [ Notice(
                      Theme.Info,
                      "Paste a long or multi-line block into the prompt — it collapses into a `[Pasted #N · L chars]` chip, and the content shows as a note when you submit."
                  ) ]
        | "permission" ->
            OpenModal
                { Title = "Permission required"
                  Intro = "Mire wants to run a tool:"
                  Command = "rm -rf bin obj && dotnet test"
                  Risk = Some "medium"
                  AcceptLabel = "Accept"
                  DenyLabel = "Deny"
                  Kind = PermissionModal }
        | "approval"
        | "approve" ->
            OpenModal
                { Title = "Approve command?"
                  Intro = "The agent proposes to run:"
                  Command = "git push --force origin main"
                  Risk = Some "high"
                  AcceptLabel = "Approve"
                  DenyLabel = "Reject"
                  Kind = ApprovalModal }
        | "confirm" ->
            OpenModal
                { Title = "Confirm"
                  Intro = "Clear the transcript?"
                  Command = ""
                  Risk = None
                  AcceptLabel = "Yes"
                  DenyLabel = "No"
                  Kind = ConfirmClearModal }
        | "warning" -> SpawnToast(Theme.Warning, "! warning", "3 files have uncommitted changes")
        | "toast"
        | "toast:success" -> SpawnToast(Theme.Success, "✓ success", "Tests passed in 1.2s")
        | "toast:info" -> SpawnToast(Theme.Info, "info", "Indexed 1,204 files")
        | "toast:error" -> SpawnToast(Theme.Danger, "✗ error", "Connection refused")
        | "error" ->
            AppendBlocks
                [ ErrorBlock
                      "Unhandled exception: Sequence contains no elements\n  at Mire.Layout.measure (Layout.fs:142)" ]
        | "notice" ->
            AppendBlocks
                [ Notice(Theme.Info, "This harness is not wired to an LLM. Responses are canned — type `help`.") ]
        | "help"
        | "?"
        | "commands" -> AppendBlocks [ helpBlock () ]
        | "clear"
        | "reset" -> ClearTranscript
        | "panel"
        | "split"
        | "sidebar" -> MetaCmd ToggleSidebar
        | "mode" -> MetaCmd CycleMode
        | "skills"
        | "explore" -> MetaCmd OpenSkills
        | "palette" -> MetaCmd OpenPalette
        | "mcp" -> MetaCmd OpenMcp
        | "welcome" -> MetaCmd ShowWelcome
        | other ->
            AppendBlocks
                [ AssistantMd(
                      sprintf
                          "You said: **%s**\n\nThis is a canned harness — type `help` to see what it can show."
                          other
                  ) ]
