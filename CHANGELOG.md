# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Full feature guide published as the project [Wiki](https://github.com/agusumri/MapIt/wiki), with every example backed by a passing test.
- Continuous integration on .NET 8 and .NET 10, running the suite with compiled plans both on and off, plus NuGet pack validation.
- CodeQL security-and-quality scanning and Dependabot updates for NuGet packages and GitHub Actions.
- Community health files: contributing guide, code of conduct, security policy, and issue/PR templates.

### Fixed
- `UseDestinationValue()` now maps into an existing nested object or collection instead of being ignored.
- `As<T>()` now resolves to the derived destination map so the requested subtype is produced.
- `AllowNullDestinationValues = false` is honored for strings and reference members (also on the compiled path).
- `MaxDepth(n)` fills cut-off levels with `null` rather than empty instances.
- Resolver/converter/action exceptions surface the original exception as `InnerException` (unwrapped from `TargetInvocationException`).
- Resolvers, converters, and mapping actions referenced by type are created through the service constructor, so they can use dependency injection.
- Dictionaries are matched before general collections; open generics close each side with its own type arguments.
- `AddGlobalIgnore` and `ShouldMapProperty` are honored at mapping time.

## [1.0.0]

- Initial release: convention-based mapping, flattening/unflattening, naming conventions, resolvers and converters, collections, enums, open generics, inheritance, projection (`ProjectTo`), validation, async mapping, diff/patch, deep clone, interceptors, and a compile-time source generator. Targets .NET 10.0, .NET 8.0, and .NET Standard 2.0.

[Unreleased]: https://github.com/agusumri/MapIt/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/agusumri/MapIt/releases/tag/v1.0.0
