# Mire — F# retained-mode TUI runtime

solution := "Mire.slnx"
framework := "Mire/Mire.fsproj"
tests := "Mire.Tests/Mire.Tests.fsproj"

# Show available recipes
default:
    @just --list

# Watch and run the scrollable-list demo.
dev *ARGS:
    dotnet watch --project Mire.Demo.List run -- {{ARGS}}

# Run the scrollable-list demo.
run *ARGS:
    dotnet run --project Mire.Demo.List -- {{ARGS}}

# Run the agent-shell demo / testbed.
agent *ARGS:
    dotnet run --project Mire.Demo.Agent -- {{ARGS}}

# Run the kitchen-sink widget showcase.
sink *ARGS:
    dotnet run --project Mire.Demo.KitchenSink -- {{ARGS}}

# Headless layout dump (no raw mode) — eyeball layout changes.
dump:
    dotnet run --project Mire.Demo.List -- --dump

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
