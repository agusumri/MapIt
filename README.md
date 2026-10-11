# MapIt

[![CI](https://github.com/agusumri/MapIt/actions/workflows/ci.yml/badge.svg)](https://github.com/agusumri/MapIt/actions/workflows/ci.yml)
[![CodeQL](https://github.com/agusumri/MapIt/actions/workflows/codeql.yml/badge.svg)](https://github.com/agusumri/MapIt/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/agusumri/MapIt/branch/main/graph/badge.svg)](https://codecov.io/gh/agusumri/MapIt)
[![Release](https://img.shields.io/github/v/release/agusumri/MapIt?sort=semver)](https://github.com/agusumri/MapIt/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A fast, convention-based object-to-object mapper for .NET.

MapIt maps one object to another without the dreary boilerplate of hand-written property copying. You declare the mappings once (by convention or explicitly) and MapIt does the rest.

## Why MapIt?

MapIt is a fully open-source (MIT) mapper that:

- Offers a **fluent configuration API** with convention-based matching, flattening/unflattening, nested maps, collections, enums, open generics, projection, and validation.
- Is **safe by default**: a global depth cap, automatic self-reference handling, a collection budget, fail-closed error handling, and explicit missing-map errors keep mapping predictable and crash-free.
- Ships **extra capabilities** out of the box: diff/patch mapping, async resolvers, expression translation, deep clone, interceptors, and a **compile-time (AOT) source generator**.
- Runs on **.NET 10.0**, **.NET 8.0**, and **.NET Standard 2.0** (broadest compatibility).

The authoritative behavior contract — including the non-negotiable safety guarantees (depth cap, self-reference handling, collection budget, fail-closed, missing-map-throws) — is documented in [`docs/BEHAVIOR.md`](docs/BEHAVIOR.md).

## Documentation

The **[MapIt Wiki](https://github.com/agusumri/MapIt/wiki)** is a beginner-friendly guide to every feature — what it's for and how to use it. Every example in it is backed by a passing test. Start with [Getting Started](https://github.com/agusumri/MapIt/wiki/Getting-Started), then explore [Dependency Injection](https://github.com/agusumri/MapIt/wiki/Dependency-Injection), [Customizing Members](https://github.com/agusumri/MapIt/wiki/Customizing-Members), [Projection and Queries](https://github.com/agusumri/MapIt/wiki/Projection-and-Queries), and more.

## Sample application

A real-world example of using MapIt in an ASP.NET Core minimal API lives in [`samples/MapIt.ProductApi`](samples/MapIt.ProductApi). It's a small product-store API that maps domain entities to DTOs (and back) using MapIt, including nested objects and computed members:

```bash
cd samples/MapIt.ProductApi
dotnet run
```

Then:

```
GET  http://localhost:5000/products      # list products (entity -> DTO)
GET  http://localhost:5000/products/1    # single product
POST http://localhost:5000/products      # create a product (DTO -> entity)
GET  http://localhost:5000/orders        # nested entity -> DTO with computed Total
```

## Install

```
dotnet add package MapIt
```

For ASP.NET Core / `IServiceCollection` integration:

```
dotnet add package MapIt.Extensions.Microsoft.DependencyInjection
```

## Quick start

```csharp
using MapIt;
using MapIt.Extensions.Microsoft.DependencyInjection;

// Configure mappings
var configuration = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<Foo, FooDto>();
    cfg.CreateMap<Bar, BarDto>();
});

configuration.AssertConfigurationIsValid();
IMapper mapper = configuration.CreateMapper();

var dto = mapper.Map<FooDto>(foo);
```

### With dependency injection

```csharp
using MapIt.Extensions.Microsoft.DependencyInjection;

services.AddMapIt(cfg =>
{
    cfg.CreateMap<Foo, FooDto>();
});

// Inject IMapper into your services
```

## Target frameworks

| Framework | Supported |
|---|---|
| .NET 10.0 | Yes |
| .NET 8.0 | Yes |
| .NET Standard 2.0 | Yes |

## Feature support

| Feature | Supported |
|---|---|
| Convention-based mapping by member name | Yes |
| `CreateMap<TSource, TDestination>()` (+ `Type` overloads) | Yes |
| Member configuration (`ForMember`, `ForAllMembers`) | Yes |
| `Ignore`, `ResolveUsing`/`MapFrom`, `Condition`, `PreCondition` | Yes |
| `IValueResolver`, `IMemberValueResolver` | Yes |
| `ITypeConverter` / `ConvertUsing` | Yes |
| `IValueConverter` / `ConvertUsing` | Yes |
| `BeforeAll`/`AfterAll` (pre/post mapping) | Yes |
| `BeforeMap`/`AfterMap`, `IMappingAction` | Yes |
| Collection mapping (list, array, IEnumerable, dictionary) | Yes |
| Enum, primitive and common string conversions | Yes |
| Nullable handling (`AllowNullDestinationValues`, `AllowNullCollections`) | Yes |
| `ConstructUsing`, `ForCtorParam` | Yes |
| Open-generic maps (`CreateMap(typeof(S<>), typeof(D<>))`) | Yes |
| `Include` / `IncludeBase` member inheritance and polymorphic dispatch | Yes |
| `ReverseMap` | Yes |
| `MaxDepth`, `PreserveReferences` | Yes |
| Flattening (`CustomerName` ← `Customer.Name`, any depth) | Yes |
| Unflattening (`ReverseMap` of flattened members) | Yes |
| Naming conventions (`PascalCase` ↔ `snake_case`) and prefix/postfix recognition | Yes |
| `Get`-prefixed source methods | Yes |
| `IncludeMembers` (flatten related objects) | Yes |
| Dynamic / `ExpandoObject` mapping (both directions) | Yes |
| Collection update-by-key into existing destination (`EqualityComparison`) | Yes |
| Enum mapping with explicit value overrides (`ConvertUsingEnumMapping`) | Yes |
| Included source extension methods (`IncludeSourceExtensionMethods`) | Yes |
| `AddMaps` for profiles and `[AutoMap]` / member attributes | Yes |
| Configuration sealing and thread safety | Yes |
| Configuration validation and `BuildExecutionPlan` | Yes |
| Operation-scoped context items and service construction | Yes |
| `RegisterAsMap` open-generic registration | Yes |
| `ProjectTo` (provider-side `IQueryable` projection) | Partial — translatable member-init subset; out-of-scope constructs throw rather than evaluating in memory (see [`docs/BEHAVIOR.md`](docs/BEHAVIOR.md) §12) |
| `AssertConfigurationIsValid` | Yes |
| Naming conventions (`RecognizePostfixes/Prefixes`, etc.) | Yes |
| Profiles (`AddProfile`, `CreateProfile`) | Yes |
| `AddMapIt` DI extensions (with `ServiceLifetime`) | Yes |

## Extra capabilities

MapIt ships several capabilities beyond basic object mapping, all additive and opt-in:

- **Non-throwing mapping** — `mapper.TryMap<TDestination>(source, out var result)` returns `false` instead of throwing on a missing map, for pipeline-style code.
- **Deep clone** — `mapper.Clone(obj)` produces a true deep copy via a configured `CreateMap<T, T>()`, forcing construction of new instances for same-type members (ordinary same-type mapping still passes references through, per the behavior contract).
- **Null-safe mapping** — `mapper.MapOrDefault<TDestination>(source, fallback)` returns the fallback for a null source without a null guard.
- **Global mapping interceptors** — register `cfg.AddInterceptor(new MyInterceptor())` (implementing `IMappingInterceptor`, or deriving from `MappingInterceptor`) to run cross-cutting logic (audit, logging, metrics, enrichment) around every top-level map. An interceptor can also short-circuit a result from `OnBeforeMap`.
- **Diff / patch mapping** — `mapper.MapChanges(source, destination)` writes only members whose mapped value differs and returns a `MapChangeResult` listing every change (old/new value per member). Ideal for audit trails and change tracking.
- **Async resolvers** — configure `opt.MapFromAsync(resolver)` (an `IAsyncValueResolver<,,>` or `Func<TSource, Task<TMember>>`) and run `await mapper.MapAsync<TSource, TDestination>(source)` for members that need I/O.
- **Expression translation (`UseAsDataSource`)** — write queries against the DTO and translate them to the entity: `source.UseAsDataSource(mapper).For<TDto>().Where(d => d.Age > 30).ToQueryable()`, or translate a predicate directly with `mapper.TranslateExpression<TDto, TEntity>(...)`.
- **Compile-time (AOT) mapper generator** — the `MapIt.SourceGenerator` package generates reflection-free mapping code at build time. Decorate a partial class with `[Mapper]` and declare partial methods; the generator writes the bodies (trimming/Native-AOT friendly).

```csharp
// Compile-time generated mapper (no reflection)
[MapIt.Generated.Mapper]
public partial class PeopleMapper
{
    public partial PersonDto ToDto(Person src);
}
```

```csharp
var config = new MapperConfiguration(cfg =>
{
    cfg.AddInterceptor(new AuditInterceptor());
    cfg.CreateMap<Order, OrderDto>();
});

var mapper = config.CreateMapper();

if (mapper.TryMap<OrderDto>(order, out var dto)) { /* ... */ }
var copy = mapper.Clone(order);
```

## Defaults worth knowing

- **`AllowAdditiveTypeMapCreation` defaults to `false`.** Configuring the same source/destination pair twice throws `DuplicateTypeMapConfigurationException` at startup. Set `cfg.AllowAdditiveTypeMapCreation = true` to merge duplicates (later wins).
- **`AllowNullCollections` defaults to `false`.** Null source collections map to **empty** collections by default. Set `cfg.AllowNullCollections = true` (applies to every profile that does not override it) or set it on an individual profile to get null-propagation.
- **`AllowNullDestinationValues` defaults to `true`.** When set to `false` (globally, per-profile, or per-member via `opt.DoNotAllowNull()`), a null source yields a new destination instance / empty string instead of null.
- The `Profiles` collection lists the root `<global>` profile first, so `AssertConfigurationIsValid("<global>")` validates maps declared directly on `cfg`.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test (both execution paths), and submit changes, and please follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security issues should be reported privately per the [security policy](.github/SECURITY.md).

Release notes live in [CHANGELOG.md](CHANGELOG.md).

## License

MIT. See [LICENSE](LICENSE).
