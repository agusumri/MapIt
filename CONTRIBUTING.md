# Contributing to MapIt

Thanks for your interest in improving MapIt. This guide covers everything you need to get a change merged.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you agree to uphold it.

## Getting started

You need the **.NET 10 SDK** and the **.NET 8 SDK** (the library multi-targets `net10.0`, `net8.0`, and `netstandard2.0`).

```bash
git clone https://github.com/agusumri/MapIt.git
cd MapIt
dotnet restore MapIt.sln
dotnet build MapIt.sln -c Release
dotnet test MapIt.sln -c Release
```

## Running the tests

MapIt has two execution paths: compiled plans (the fast path) and the reflection interpreter. **Both must pass**, so run the suite twice:

```bash
# Compiled plans ON (default)
dotnet test MapIt.sln -c Release

# Compiled plans OFF
MAPIT_DISABLE_COMPILED_PLANS=1 dotnet test MapIt.sln -c Release
```

On PowerShell:

```powershell
$env:MAPIT_DISABLE_COMPILED_PLANS = '1'; dotnet test MapIt.sln -c Release; Remove-Item Env:MAPIT_DISABLE_COMPILED_PLANS
```

## The behavior contract

MapIt has a documented behavior contract in [`docs/BEHAVIOR.md`](docs/BEHAVIOR.md), including non-negotiable safety guarantees: the depth cap, automatic self-reference handling, the collection budget, fail-closed error handling, and missing-map-throws. Changes must not weaken these. If a change intentionally alters documented behavior, update `docs/BEHAVIOR.md` in the same PR and call it out.

## Making a change

1. **Open an issue first** for anything non-trivial, so we can agree on the approach.
2. Create a branch off `main`.
3. Keep the change focused. One logical change per PR.
4. **Add or update tests.** Every feature and bug fix needs coverage. Examples shown in the wiki are mirrored by tests in `tests/MapIt.Tests/WikiExamplesTests.cs` and `WikiExamplesMoreTests.cs` — if you change a documented example, update its test too.
5. Match the existing code style (see `.editorconfig`). Public members need XML doc comments.
6. Run both test passes and a Release build before opening the PR.
7. Fill in the pull request template.

## Commit messages

Short, imperative subject lines, optionally prefixed by area (`docs:`, `ci:`, `fix:`, `feat:`). Explain the "why" in the body when it isn't obvious.

## Reporting bugs and requesting features

Use the issue templates. A minimal reproduction (configuration + mapping call) makes bugs far faster to fix.

## Security issues

Please report security vulnerabilities privately. See [SECURITY.md](.github/SECURITY.md).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
