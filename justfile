# Mire — F# retained-mode TUI runtime

solution := "Mire.slnx"
framework := "Mire/Mire.fsproj"
tests := "Mire.Tests/Mire.Tests.fsproj"

# Show available recipes
default:
    @just --list

# Watch and run the agent demo — the comprehensive showcase.
dev *ARGS:
    dotnet watch --project Mire.Demo.Agent run -- {{ARGS}}

# Run the agent demo — the comprehensive showcase.
run *ARGS:
    dotnet run --project Mire.Demo.Agent -- {{ARGS}}

# Run the RSS-reader demo.
feed *ARGS:
    dotnet run --project Mire.Demo.Feed -- {{ARGS}}

# Run the spreadsheet demo.
sheet *ARGS:
    dotnet run --project Mire.Demo.Spreadsheet -- {{ARGS}}

# Run the Mire.Agent MVP — a minimal agent shell (the composition sample).
shell *ARGS:
    dotnet run --project samples/AgentShell -- {{ARGS}}

# Headless layout dump (no raw mode) — eyeball layout changes.
dump:
    dotnet run --project Mire.Demo.Agent -- --dump

# Build the solution.
build:
    dotnet build {{solution}}

# Build just the framework.
build-framework:
    dotnet build {{framework}}

# Pack the framework into a NuGet package (dist/Mire.<version>.nupkg).
pack:
    dotnet pack {{framework}} -c Release -o dist

# Manual publish to NuGet (fallback — CI uses trusted publishing, see .github/workflows/publish.yml).
# Requires a long-lived NUGET_API_KEY in the environment.
publish: pack
    dotnet nuget push "dist/*.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate

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
