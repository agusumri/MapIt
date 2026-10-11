# MapIt behavior contract

This document describes the intended behavior and the currently verified
implementation status. Sections 1–11 are compatibility targets. Section 12 is
authoritative for the current implementation state; do not infer support from a
target rule without matching tests.

## 1. Safety (non-negotiable)

| ID | Rule |
|---|---|
| S1 | **Global depth cap.** Every top-level `Map` call tracks nesting depth (objects, collection elements, dictionary values, paths). Exceeding `cfg.MaxGraphDepth` (default **256**) throws `MapItMappingException` ("Maximum mapping depth of N exceeded") before the stack can overflow. Never a `StackOverflowException`. |
| S2 | **Self-referencing types.** When a type map's destination type graph can reach itself (directly, through members, or through collection/dictionary element types), and the map has no explicit `.MaxDepth(n)`, MapIt applies `MaxDepth = 64` and enables `PreserveReferences` automatically. Explicit `.MaxDepth(n)` overrides the default. |
| S3 | **MaxDepth semantics (truncation).** For a map with `MaxDepth = n`, the n-th nested occurrence of that type map in the current path is mapped; deeper occurrences are set to the destination member's default (null) instead of being mapped. Depth 1 means only the root. |
| S4 | **PreserveReferences.** The destination is registered in the per-call instance cache **immediately after construction**, before BeforeMap actions and members. Cache key = (source by **reference identity**, destination type). Cycles therefore produce cyclic destinations and terminate. `Equals`/`GetHashCode` of user objects are never called by the cache. |
| S5 | **Collection budget.** Enumerating any single source collection/dictionary beyond `cfg.MaxCollectionItems` (default **1,000,000**) throws `MapItMappingException`. Infinite enumerables therefore terminate. |
| S6 | **Sealed configuration.** All configuration is read and frozen in the `MapperConfiguration` constructor. Later changes to the expression/profiles have no effect. Mapping is thread-safe for concurrent calls on one mapper or many mappers sharing one configuration. Plans are cached per type pair in a thread-safe cache. |
| S7 | **Fail closed.** Exceptions from conditions, preconditions, resolvers, converters, actions and constructors are never swallowed; they surface as `MapItMappingException` (inner exception preserved) with `Types`, `TypeMap` and `MemberMap` set. A failing condition never causes a member to be mapped. |
| S8 | **Missing maps throw.** Mapping between two complex (non built-in) types without a resolvable type map throws `MapItMappingException` whose message contains "Missing type map configuration or unsupported mapping" and both type names. No silent empty DTOs. |
| S9 | **No values in messages.** Exception messages include type names and member paths, never member values. |
| S10 | **Exclusions are honored at mapping time.** `Ignore`, `[Ignore]`, `AddGlobalIgnore(prefix)`, `ShouldMapProperty`, `ShouldMapField`, `ShouldMapMethod` exclude members from convention discovery *and* mapping, not only validation. |
| S11 | **Scanning is constrained.** `AddMaps` only activates concrete, non-generic `Profile` types with a public parameterless constructor, plus `[AutoMap]` types. Abstract/open-generic profiles are skipped. |
| S12 | **Duplicate maps.** Configuring the same source/destination pair twice throws `DuplicateTypeMapConfigurationException` at configuration time, unless `cfg.AllowAdditiveTypeMapCreation = true`, which merges member configuration (later wins). |

## 2. Type map resolution

1. Exact (runtime source type, requested destination type) map.
2. Polymorphism: if the resolved map has included derived maps (`Include`, `IncludeBase` on the derived map, `IncludeAllDerived`), and the runtime source type matches a derived source, the derived map is used and the derived destination type is created.
3. Base-type fallback: walk the runtime source type's base classes, then interfaces, looking for a map to the requested destination (handles EF proxies and derived instances).
4. Open generic maps (`CreateMap(typeof(S<>), typeof(D<>))`) are closed on demand.
5. Built-in mappers (section 6) when no type map exists.
6. Otherwise throw (S8).

`MapperConfiguration.FindTypeMapFor` returns configured maps and caches matching
closed maps materialized from open-generic definitions and base/interface
fallback resolutions in a thread-safe cache.

## 3. Member discovery (conventions)

For each destination member (public-getter property with any setter including private/`init`, or public field when `ShouldMapField` allows), in this order:

1. Explicit configuration (`ForMember`, `ForPath`, attributes) wins.
2. Constructor parameters (section 4) that consume the name.
3. Name match, case-insensitive, against source properties, fields, and parameterless methods.
4. Prefix/postfix handling: `RecognizePrefixes/Postfixes`, `RecognizeDestinationPrefixes/Postfixes`, `ClearPrefixes`, `ReplaceMemberName(a, b)`.
5. Naming conventions: `SourceMemberNamingConvention` / `DestinationMemberNamingConvention`.
6. **Flattening:** `CustomerName` ← `Customer.Name`, greedy longest match first, any depth.
7. No match → member is unmapped (left at its constructed value) and reported by validation.

A null intermediate in a source chain yields the destination default (no `NullReferenceException`).

**Unflattening:** `ReverseMap()` reverses explicit member chains, simple member-path `MapFrom`, and conventionally flattened members (e.g. `CustomerName` ← `Customer.Name` reverses to a `ForPath` that writes `Customer.Name`, creating intermediate `Customer` when null).

**IncludeMembers:** `IncludeMembers(s => s.Inner, ...)` flattens members of the referenced source objects into the destination when they are not matched directly.

**Dynamic / `ExpandoObject`:** mapping a POCO to `ExpandoObject`/`IDictionary<string,object>` writes each readable source member into the dictionary; mapping an `ExpandoObject`/`IDictionary<string,object>` to a POCO assigns each writable destination member from the matching (case-insensitive) key, mapping nested values recursively.

## 4. Construction

Order: `ConvertUsing` (whole map) → `ConstructUsing` → `ConstructUsingServiceLocator` → constructor mapping → parameterless constructor.

Constructor mapping chooses the public constructor with the most parameters that can all be resolved by `ForCtorParam(name, ...)`, by source member name match (including flattening), or by default parameter value. Destination members already fed through a constructor parameter of the same name are not mapped again.

## 5. Member mapping pipeline

Per member, in `SetMappingOrder` order then declaration order:

1. `PreCondition` false → skip.
2. Resolve source value: resolver / converter / `MapFrom` / convention chain.
3. `NullSubstitute`: when the resolved value is null, use the substitute.
4. Value transformers (member-level in this engine).
5. `Condition` false → skip.
6. `UseDestinationValue` (or `[UseExistingValue]`).
7. Map value to the destination member type (full recursion with depth tracking).
8. Null handling: null value assigned as null when `AllowNullDestinationValues` (default true); when false, a new destination instance/empty string-default is created. Null source **collections** become empty collections unless `AllowNullCollections` (default false) — at member level and top level. `AllowNull()`/`DoNotAllowNull()` override per member.

`ForPath(d => d.A.B, ...)` creates null intermediate destination objects and honors its `Condition`.

`ForSourceMember(..., o => o.DoNotValidate())` excludes a source member from `MemberList.Source` validation.

Map-level: `BeforeMap` after construction and before members; `AfterMap` after members. Operation-level `opts.BeforeMap` runs before map-level BeforeMap; `opts.AfterMap` after map-level AfterMap.

Mapping into an existing destination (`Map(src, dest)`) updates it in place and returns the same instance.

## 6. Built-in conversions (no type map needed)

- Same type with no type map: return the source. With a member-carrying `T → T` map, a new instance is mapped.
- Assignable (dest assignable from source) with no type map: return source.
- `Nullable<T>` ↔ `T`.
- Enum → enum by name (case-insensitive) then by numeric value, for every underlying type.
- Enum ↔ underlying/any integral type; string → enum; enum → string = name.
- Primitives/decimal/`DateTime`/`DateTimeOffset`/`TimeSpan`/`bool`/`Guid` via `Convert.ChangeType`/parse with **InvariantCulture**; anything → string via `ToString()`/`IFormattable` invariant.
- User-defined implicit/explicit conversion operators.
- Collections and dictionaries (dictionaries detected first), element/key/value mapped.
- **Collection update-by-key:** when mapping into an existing destination `IList` and the element map declares `EqualityComparison((s, d) => ...)`, source items are matched to existing destination items by that comparison; matched items are updated in place, new items added, unmatched destination items removed.
- **Enum value overrides:** `ConvertUsingEnumMapping(opt => opt.MapValue(src, dst))` provides explicit enum-to-enum overrides; unmapped values fall back to by-name then by-numeric matching.
- Unsupported pair → throw (S8).

## 7. Services and extensibility

- `ServiceCtor` (from `ConstructServicesUsing`, `CreateMapper(serviceCtor)`, or `opts.ConstructServicesUsing`, which wins for that call) creates resolver/converter/action **types** at map time. Default: `Activator.CreateInstance`.
- Instance-based resolvers/converters are used as given (caller owns thread safety).
- `ResolutionContext.Mapper` is an `IRuntimeMapper` whose `Map` calls share the current context (same instance cache and depth tracking).

## 8. Attributes

Destination member attributes are applied at seal time: `[Ignore]`, `[SourceMember("Name")]`, `[NullSubstitute(x)]`, `[MapAtRuntime]`, `[ValueResolver(typeof(R))]`, `[ValueConverter(typeof(C))]`, `[UseExistingValue]`, `[MappingOrder(n)]`.
`[AutoMap(typeof(Source))]` on a destination type creates `Source → Dest` during `AddMaps` scanning, honoring `ReverseMap`, `MaxDepth`, `PreserveReferences`, `IncludeAllDerived`, `TypeConverter`, `ConstructUsingServiceLocator`, `DisableCtorValidation`.

## 9. Validation (`AssertConfigurationIsValid`)

Collects all problems, then throws one `MapItConfigurationException` with `Errors`:
- `MemberList.Destination` (default): writable destination members with no source (excluding ignored, configured, constructor-fed, global ignores, convention-matched).
- `MemberList.Source`: source members not used (excluding `DoNotValidate`).
- `MemberList.None`: no member checks.
- Constructibility (unless `DisableCtorValidation`).
- Nested member type pairs that have neither a type map nor a built-in conversion.
- Maps with `ConvertUsing` are not member-validated.

`AssertConfigurationIsValid(profileName)`, `<TProfile>()` and `(TypeMap)` validate the subset. The sealed `Profiles` array lists the root `<global>` profile first, so `AssertConfigurationIsValid("<global>")` validates maps declared directly on `cfg`.

## 10. Configuration options (public)

`MaxGraphDepth` (int, default 256), `MaxCollectionItems` (int, default 1,000,000),
`AllowAdditiveTypeMapCreation` (default **false**), `AllowNullCollections` (default
**false**), `AllowNullDestinationValues` (default true), `ShouldMap*`, naming
conventions, prefixes, `ValueTransformers`.
`MaxGraphDepth`, `MaxCollectionItems` and `AllowAdditiveTypeMapCreation` are members
of `IMapperConfigurationExpression` (the static type of `cfg`); `MaxGraphDepth` and
`MaxCollectionItems` are also exposed on `IGlobalConfiguration`.

## 11. Explicitly unsupported

`AsProxy`, `As(Type)` beyond a destination-type override, `MapAtRuntime` (recorded,
no distinct runtime effect), `ExplicitExpansion` outside `ProjectTo`. These do not throw.

## 12. Current implementation status

Implemented behavior with regression coverage (see `tests/MapIt.Tests`):

- **Configuration sealing and thread safety (S6).** Discovery, convention,
  attribute application and the S2 self-reference walk all run once in the
  `MapperConfiguration` constructor and freeze an immutable snapshot; later
  mutation of the expression has no effect. Closed/fallback maps are memoized in a
  `ConcurrentDictionary`. Concurrent mapping on one mapper and on many mappers
  sharing one configuration is covered by `SealingTests`.
- **Section-1 safety.** Depth cap (S1), self-reference `MaxDepth=64`/`PreserveReferences`
  default (S2), `MaxDepth` truncation (S3), reference-identity cache that never calls
  user `Equals`/`GetHashCode` (S4), collection budget on every source enumeration (S5),
  fail-closed resolver/condition/action handling with populated exception fields (S7),
  and missing-map-throws (S8) are all implemented and tested (`EdgeCaseTests`,
  `SealingTests`, `AttributeAndResolverTests`).
- **Full open-generic configuration parity (Gap 2).** `CreateMap(typeof(S<>), typeof(D<>))`
  records its configuration as a replayable recipe (ForMember by name/expression,
  resolver/converter types, ForPath, ForCtorParam, Include/IncludeBase, open destination
  override, ReverseMap) and closes on demand against the closed reflection metadata,
  rewriting captured expressions onto the closed source type. Unclosable closures throw
  a clear `MapItMappingException` naming the pair. Covered by `OpenGenericTests`.
- **Full resolver and attribute semantics (Gap 3).** All destination-member attributes are
  behavioral at seal time; `[AutoMap]` scanning honors every option including `TypeConverter`
  and `ConstructUsingServiceLocator` (both added to `AutoMapAttribute`). Enum/primitive/
  string/`DateTimeOffset`/`TimeSpan`/`bool`/implicit-operator conversions, null-substitute,
  value transformers, member-level null handling, and operation-scoped
  `Items`/`BeforeMap`/`AfterMap`/`ConstructServicesUsing` are implemented. Covered by
  `AttributeAndResolverTests`.
- **Provider-specific `ProjectTo` translation (Gap 4, narrowed).** `ProjectTo` builds a
  provider-translatable `Select(x => new TDestination { ... })` member-init expression from
  the sealed map — convention access, simple `MapFrom(s => s.A.B)` chains, nested maps
  (recursive member-init truncated at `RecursiveQueriesMaxDepth`, default 10), and
  `ExplicitExpansion` members only when requested — with EF-equivalent null propagation.
  The following are **out of scope** and throw a clear exception at call time rather than
  evaluating in memory: resolvers/converters, runtime-delegate `MapFrom`/`ConvertUsing`/
  `ConstructUsing`, `BeforeMap`/`AfterMap`, conditions, `ForPath`/`ForCtorParam` with runtime
  delegates, and source chains the provider cannot translate. Covered by `ProjectToTests`.
- **Full validation and constructor diagnostics (Gap 5).** `AssertConfigurationIsValid`
  collects all problems across maps and throws one `MapItConfigurationException`:
  `MemberList.Destination`/`Source`/`None` coverage, constructibility (`CanConstruct`),
  and nested member-pair resolvability. The exception's lead line is conditional on whether
  any unmapped members exist. The four overloads validate the right subset.
  `BuildExecutionPlan` returns a non-null `(TSource, TDestination, ResolutionContext)` lambda
  whose body dispatches into the reflection engine (a reflection-dispatch shim, not a fully
  inlined expression tree). Covered by `ValidationTests`.

Two documented residuals:

- `ProjectTo` is deliberately narrowed to the translatable member-init subset above; the
  out-of-scope constructs throw rather than silently evaluating in memory.
- `BuildExecutionPlan` is a reflection-dispatch shim: the returned lambda has the
  `(TSource, TDestination, ResolutionContext)` signature and maps correctly whether invoked with a
  live context or a `null` context, but its body is not a fully inlined expression tree (the
  reflection engine performs the mapping).

`MapAtRuntime` is recorded and ignored at runtime (§11). The library builds and behaves across
`net10.0`, `net8.0`, and `netstandard2.0`.

## 13. Extra capabilities

These are additive MapIt features; none change the behavior of the sections above.

- **Non-throwing mapping.** `IMapperBase.TryMap<TDestination>(source, out result)` and
  `TryMap<TSource, TDestination>` return `false` (and `default`) on a `MapItMappingException`
  instead of throwing.
- **Deep clone.** `IMapper.Clone(source)` deep-copies via a configured `CreateMap<T, T>()`,
  forcing new instances for same-type members through an internal `ResolutionContext.ForceConstruct`
  flag. Ordinary same-type mapping still passes references through (§6), so this does not change
  the default contract.
- **Null-safe mapping.** `IMapperBase.MapOrDefault<TDestination>(source, fallback)` returns the
  fallback for a null source.
- **Global mapping interceptors.** `cfg.AddInterceptor(IMappingInterceptor)` registers cross-cutting
  interceptors invoked around every top-level map. `OnBeforeMap` may short-circuit by returning a
  non-null result; `OnAfterMap` may replace the result. Interceptors run outermost-first on the way
  in and reverse on the way out. They do not run for nested member maps.
- **Collection update-by-key** and **enum value overrides** (§6).
- **Diff/patch mapping.** `IMapper.MapChanges(source, destination)` writes only members whose
  mapped value differs (compared with `object.Equals`) and returns a `MapChangeResult<TDestination>`
  with the patched destination and an ordered list of `MemberChange` (member name, old value, new
  value). Driven by `ResolutionContext.ChangeTracker`.
- **Async resolvers.** `opt.MapFromAsync(IAsyncValueResolver<,,>)` / `MapFromAsync(Func<TSource,
  Task<TMember>>)` records an async resolver on the member. A plain `Map` leaves such members at
  their default; `mapper.MapAsync<TSource, TDestination>(source, ct)` runs the synchronous pipeline
  (deferring async members via `ResolutionContext.PendingAsync`), then awaits and applies them.
- **Expression translation (`UseAsDataSource`).** `queryable.UseAsDataSource(mapper).For<TDto>()
  .Where(dtoPredicate).ToQueryable()` and `mapper.TranslateExpression<TDto, TEntity>(predicate)`
  rewrite a destination-typed expression into a source-typed one by mapping each destination member
  access to its source member chain (member-based maps only; resolver/converter/delegate maps are
  not translatable and throw).
- **`ProjectTo` collection projection.** Nested source collections project to destination collections
  via `Enumerable.Select(e => new DestElement { ... })` materialized to array/`List<T>`/`IEnumerable<T>`,
  truncated at `RecursiveQueriesMaxDepth`.
- **Compile-time mapper generator (`MapIt.SourceGenerator`).** An incremental Roslyn generator emits
  reflection-free mapping bodies for partial methods on a `[MapIt.Generated.Mapper]` partial class:
  same-named settable destination properties are assigned from source properties, nested pairs call
  a sibling mapping method, and simple numeric conversions are cast. This path uses no reflection and
  is trimming/Native-AOT friendly. It is independent of the runtime engine; the two can coexist.
