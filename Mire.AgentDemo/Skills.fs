namespace Mire.AgentDemo

/// Fake "skills" for the skill-explorer overlay. Each has a multi-paragraph markdown
/// body so the preview pane actually scrolls (exercising a second, independent Scroll).
type Skill =
    { Name: string
      Summary: string
      Markdown: string }

module Skills =

    let all: Skill list =
        [ { Name = "brainstorming"
            Summary = "ideas → designs"
            Markdown =
              "# Brainstorming\n\
                 Turn ideas into fully-formed designs through collaborative dialogue.\n\n\
                 ## When to use\n\
                 - starting any new feature\n\
                 - when the scope is still fuzzy\n\
                 - before writing an implementation plan\n\n\
                 ## Steps\n\
                 1. explore the project context first\n\
                 2. ask one question at a time\n\
                 3. propose 2-3 approaches with trade-offs\n\
                 4. present the design, section by section\n\n\
                 > The terminal state is a written, approved spec — never code." }
          { Name = "writing-plans"
            Summary = "design → plan"
            Markdown =
              "# Writing plans\n\
                 Turn an approved design into an executable, reviewable plan.\n\n\
                 - one task per discrete step\n\
                 - name the **critical files** to touch\n\
                 - reuse existing utilities; don't reinvent\n\
                 - include a verification section\n\n\
                 ```\n\
                 dotnet build Mire.slnx   # must pass before 'done'\n\
                 ```" }
          { Name = "debugging"
            Summary = "root cause, not symptom"
            Markdown =
              "# Debugging\n\
                 Find the *root cause*, never patch the symptom.\n\n\
                 ## The loop\n\
                 1. reproduce reliably\n\
                 2. form one hypothesis\n\
                 3. add an observation that would confirm or refute it\n\
                 4. only then change code\n\n\
                 Never fix what you cannot explain." }
          { Name = "test-driven"
            Summary = "red → green → refactor"
            Markdown =
              "# Test-driven\n\
                 Write the failing test first.\n\n\
                 - **red** — a test that fails for the right reason\n\
                 - **green** — the simplest code that passes\n\
                 - **refactor** — clean up under green\n\n\
                 > Mire has no test project yet — see `ROADMAP.md`." }
          { Name = "code-review"
            Summary = "review against intent"
            Markdown =
              "# Code review\n\
                 Review against intent, not personal taste.\n\n\
                 1. correctness first\n\
                 2. then maintainability\n\
                 3. then performance — *only if it hurts*\n\n\
                 Leave the diff better than you found it." }
          { Name = "using-git"
            Summary = "small, honest commits"
            Markdown =
              "# Using git\n\
                 Small commits with honest messages.\n\n\
                 - branch off the default branch\n\
                 - commit only when the user asks\n\
                 - never force-push shared history\n\
                 - interactive flags aren't supported in this harness" }
          { Name = "subagents"
            Summary = "delegate & verify"
            Markdown =
              "# Subagents\n\
                 Delegate independent work to parallel agents.\n\n\
                 - **fan out** for breadth\n\
                 - **verify adversarially** before trusting a finding\n\
                 - **synthesize** the result\n\n\
                 The conclusion comes back to you — not the file dumps." } ]
