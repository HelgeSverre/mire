# Mire — F# retained-mode TUI runtime

solution := "Mire.slnx"
framework := "Mire/Mire.fsproj"
tests := "Mire.Tests/Mire.Tests.fsproj"

# Show available recipes
default:
    @just --list

# Watch and run the scrollable-list demo.
dev *ARGS:
    dotnet watch --project Mire.Demo run -- {{ARGS}}

# Run the scrollable-list demo.
run *ARGS:
    dotnet run --project Mire.Demo -- {{ARGS}}

# Run the agent-shell demo / testbed.
agent *ARGS:
    dotnet run --project Mire.AgentDemo -- {{ARGS}}

# Headless layout dump (no raw mode) — eyeball layout changes.
dump:
    dotnet run --project Mire.Demo -- --dump

# Build the solution.
build:
    dotnet build {{solution}}

# Build just the framework.
build-framework:
    dotnet build {{framework}}

# Run the test suite.
test:
    dotnet test {{tests}} --nologo

# Format sources (F# via fantomas, markdown via oxfmt).
format:
    dotnet tool restore
    dotnet fantomas .
    bunx --bun oxfmt "**/*.md"

# Check formatting.
lint:
    dotnet tool restore
    dotnet fantomas --check .
    bunx --bun oxfmt --check "**/*.md"

# Pre-commit gate.
check: lint build test

# Remove build output.
clean:
    dotnet clean {{solution}}
    rm -rf bin obj
