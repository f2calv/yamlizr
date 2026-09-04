---
description: 'C# / .NET coding conventions, style, logging, performance and Web API rules.'
applyTo: '**/*.cs'
---

# C# / .NET

## Style (enforced by `.editorconfig`)

- **Class-per-file (one top-level type per file — strict)**: Every top-level type — `class`, `static class`, `record`, `record struct`, `struct`, `interface`, `enum` — lives in its **own** file named after the type (`MyService.cs` for `class MyService`; `FooConstants.cs` for `static class FooConstants`). **Never place two top-level types in the same file.** The *only* types allowed to share a file with another type are **nested types** (types declared inside another type, at any accessibility). If you are about to add a second top-level type to an existing file, stop and create a new file instead. There are exactly two grouping exceptions, both signalled by an underscore-prefixed filename (the underscore is not part of any type name):
  - **`_Enums.cs`** — consolidates every `enum` in a project into one file (enums only; this is the one file permitted to hold multiple top-level types).
  - **`_*Config.cs`** (e.g. `_AppConfig.cs` for `record AppConfig`) — groups an `IAppConfig` implementation with its related config types, to keep config files at the top of the explorer.

  **Generic types** are not an exception to the one-type rule; they only change the filename — encode type parameters with curly braces: `Foo{T}.cs`, `Bar{T,U}.cs`, `_FeatureConfig{T}.cs` (Microsoft .NET runtime convention).
- **Indentation**: 4 spaces, LF line endings, insert final newline
- **Interfaces**: Must start with `I` (PascalCase) and live in an Abstractions folder and an Abstractions namespace.
- **Types/Methods/Properties**: PascalCase
- **No `this.` prefix**: Qualification disabled
- **Implicit usings**: Enabled
- **Nullable reference types**: Enabled
- **C# Language Version**: 14.0
- **Braces**: Allman style (`csharp_new_line_before_open_brace = all`). For `if`, `else`, `foreach`, `for`, `while`, and `using` statements whose body is a single statement, omit the curly braces to reduce vertical verbosity.
- **Expression-bodied members**: Preferred for accessors, properties, indexers, lambdas; **not** for constructors, operators, or local functions. For methods, use an expression body (`=>`) when the method contains a single expression. If the combined method signature and expression would cause horizontal scrolling on smaller editor windows, place the `=>` and expression on the next line, indented.
- **Explicit interface implementations**: Every explicit interface property must have an accessor block (`{ get => …; }` or `{ get => …; set => …; }`), never an expression body (`=>`). This ensures a consistent shape for all property members and satisfies IDE analysers. Each implementation must also carry `/// <inheritdoc/>` XML documentation.
- **Async pass-through**: When a method is a thin wrapper that only returns another async call (no `using`, `try`/`catch`, or additional `await`s), drop `async`/`await` and return the `Task`/`ValueTask` directly to avoid unnecessary state-machine overhead.
- **Async/Await**: Always await async method calls.
- **Pattern matching**: Preferred (`is`, `not`, switch expressions)
- **Primary constructors**: Preferred (`csharp_style_prefer_primary_constructors = true`). Use parameters directly in the class body — do **not** copy them to private/protected fields (avoid `private ILogger _logger = logger;`). **Exception**: abstract base classes may expose a `protected` field for inheritors (`protected ILogger _logger = logger;` with `: base(logger)`).
- **`IOptions<T>` access**: Read `IOptions<T>` / `IOptionsMonitor<T>` values via `.Value` / `.CurrentValue` inline at the point of use — do **not** copy them into a private/protected field or a cached local.
- **Wrapping long parameter lists**: When a constructor or method parameter list is too long for a single line, wrap it one parameter per line, with the closing parenthesis (and any `: base(...)` / interface clause) on its own line.
- **`var`**: Preferred — use `var` unless the type is not obvious from the right-hand side
- **Records**: Prefer `record` types with `get; init;` properties over classes where object comparison semantics are useful
- **Constructors**: When injecting services use a 'Svc' suffix on the parameter name and its private field instead of 'Service' to make more concise.
- **DI parameter ordering**: In constructors that accept dependency-injected services, parameters should be ordered: `ILogger` first, then any `IOptions<T>` / `IOptionsMonitor<T>`, then custom/application services.
- **No magic strings**: Avoid using string literals as dictionary keys or lookup identifiers in multiple places. Instead, define a `const` field using `nameof()` so the key is a single point of change (e.g. `public const string SummaryValues = nameof(SummaryValues);`).
- **Namespaces**: The convention is folder-based namespacing. However, the `Services` folder is exempt — sub-folders under `Services` do **not** automatically get a sub-namespace. When creating a new sub-folder under `Services`, ask the user whether the sub-folder should introduce a sub-namespace (present a yes/no choice) before proceeding.
- **Namespace declarations**: File-scoped (not block-scoped). Using directives go above the namespace.
- **Using directive ordering**: Pure alphabetical — do **not** place `System.*` first (`dotnet_sort_system_directives_first = false`). No blank line separators between groups (`dotnet_separate_import_directive_groups = false`). This applies to both regular `using` directives and `global using` directives in `GlobalUsings.cs`.
- **Global usings file**: Every project must have a `GlobalUsings.cs` file located in the project root (not in a sub-folder). The file must always be named `GlobalUsings.cs`.
- **Global usings threshold**: When a namespace is imported by more than two files in a project, move it into that project's `GlobalUsings.cs` and delete the per-file `using` directives. Re-check after any refactor that adds files, and never leave a per-file `using` that duplicates a global one.
- **Standard overrides at bottom**: Standard C# overrides such as `ToString`, `GetHashCode`, and `Equals` should be placed at the bottom of the class/record body, just above any `#region` blocks for private/static helpers.
- **Property spacing**: Separate each public property declaration (`get`/`set`/`init`) with a blank line (including in records and classes with only auto-properties). Private backing fields, however, should appear on consecutive lines with **no** blank line between them.
- **Boolean property naming**: Boolean configuration properties should use `{Feature}{State}` suffix form (past-participle or adjective describing state), not an imperative-verb prefix. Properties describe state; methods describe actions (e.g. `DistributedLockingEnabled`, `LocalCacheInvalidationEnabled` — not `EnableDistributedLocking`).
- **Constants extraction**: When a configuration record accumulates `const` fields that serve as well-known keys, profile names, or identifiers (not bindable configuration properties), extract them into a dedicated `static class` in the same namespace (e.g. `RedlockProfiles` for `RedlockConfig`). This keeps config records focused on their bindable data shape and the constants discoverable via a single type.
- **Wire constants in a Constants folder**: REST request URIs, route templates, header names, and other on-the-wire literals belong in a dedicated `static class` under a `Constants` folder and namespace — not alongside DTOs in `Models`. Group them per API surface (e.g. `RequestUris`, `PickerRequestUris`, `UploadHeaders`). Applies to both APIs this code calls and APIs it exposes.
- **Enums vs string constants**: Use `enum` for closed sets within a single assembly or tightly-coupled projects (compile-time safety, IntelliSense, `switch` exhaustiveness). For values crossing library boundaries — config keys in `appsettings.json`, env-var feature flags, dictionary keys across independently-versioned packages, or identifiers exposed via MCP / REST — prefer a `static class` of `const string` fields using `nameof()` (e.g. `FeatureNames`, `SinkTypes`): config-friendly, JSON-serialisable, no assembly dependency. When such a set needs startup validation, expose a static `IReadOnlySet<string> ValidNames` built via reflection over the class's own `const` fields so new members register automatically.
- **Validation attributes on configuration properties**: Properties bound from `appsettings.json` / env vars must carry appropriate `System.ComponentModel.DataAnnotations` attributes (e.g. `[Url]` on URIs, `[Range(1, 65535)]` on TCP ports, `[MinLength(1)]` on secrets/identifiers, `[Range(1, int.MaxValue)]` on millisecond timings, `[Range(0.0, 1.0)]` on ratios, `[Phone]` on phone numbers). Inline same-family attributes on one line (`[Required, Range(1, 65535)]`); keep different families on separate lines (`[Required, Url]` vs. `[JsonPropertyName]`). Nested complex-object properties carry `[ValidateObjectMembers]` for recursive validation.
- **Validate at boundaries**: Validate configuration and external input where it enters the system, and fail with actionable messages that contain no secrets or personal data.

## Suppressed Warnings

Configured in `Directory.Build.props`: `IDE1006`, `IDE0079`, `IDE0042`, `CS0162`, `CS1574`, `S125`, `NETSDK1233`, `NU1901`, `NU1902`, `NU1903`

## XML Documentation

- Every public or internal class, record, method, property, and enum member should have an XML comment.
- **Exception — test projects**: XML comments are required on classes, records, and properties but **not** on test methods.
- **Document fully on the interface** — use `/// <inheritdoc/>` on implementing classes to avoid duplication.
- When an enum is a public method parameter or a public property typed as an enum, use `<inheritdoc cref="EnumType" path="/summary"/>` (in the `<param>` tag for parameters, or as the member summary for properties) rather than repeating the enum's documentation. Retain any value-adding `<remarks>` (e.g. defaults, local context).
- **Deep link referenced types**: When XML comments reference .NET classes, structs, interfaces, enums, or namespaces, use `<see cref="Fully.Qualified.TypeName" />` instead of plain text (e.g. `<see cref="Azure.Data.Tables.TableEntity" />`).
- **Timespan config properties**: Any duration property (conventionally `Ms`-suffixed) on an `IAppConfig` implementation must include a `<see cref="…"/>` deep link to every consuming service class (e.g. `/// Used by <see cref="CasCap.Services.MyMonitorBgService"/>.`), tying configuration to consuming code.
- **Preserve hyperlinks**: Inline comment hyperlinks to external resources (e.g. blog posts, StackOverflow answers, GitHub issues) must never be deleted. When refactoring a comment into XML documentation, move the URL into a `<remarks>` block using `<see href="…" />` (e.g. `/// <remarks>See <see href="https://example.com" />.</remarks>`).
- **Summary brevity**: Keep `<summary>` concise — one to two short sentences that define the type or member. Move implementation details, usage notes, background context, or examples into `<remarks>`. If a `<summary>` exceeds roughly two lines and reads more like a paragraph than a definition, split the extra content into `<remarks>`.
- **Defaults in `<remarks>`**: "Defaults to …" text must live in `<remarks>`, not `<summary>`. The default value is an implementation detail and does not help define the member.
- **One-line `<summary>` and/or `<remarks>`**: When a summary/remark fits on a single line (roughly ≤ 120 characters), collapse it to `/// <summary>Text here.</summary>` instead of the three-line block form.

## Logging

- **`{ClassName}` first**: Every structured log message must include `{ClassName}` as the first template parameter, using `nameof(EnclosingClass)` as the argument (e.g. `_logger.LogInformation("{ClassName} something happened", nameof(MyService));`).
- **Template parameters**: Use PascalCase for all template parameters and never enclose them in quotes (e.g. `{DesiredValue}`, `{RecordCount}`, `{ValueBefore}` not `'{DesiredValue}'`, `'{RecordCount}'`, `'{ValueBefore}'`). The logger handles value formatting automatically.
- **No `.Value` suffix bleed**: When logging a value accessed via `options.Value.PropertyName` (primary constructor `IOptions<T>` pattern), the template parameter name must **not** inherit the `.Value` segment and must **not** use a `Val` suffix either. Properties are already well-named — use the property name directly as the template parameter (e.g. `{ServiceFamily}` for `config.Value.ServiceFamily`).
- **No magic strings in log messages**: When a log message references an enum value, class name, or other identifiable symbol, pass it via `nameof()` as a template argument rather than embedding it as a literal string in the message template.
- **Avoid `nameof()` as label-only template parameters**: Do not inject property/type names as separate structured-log fields just to avoid a literal label — it clutters structured output (Grafana, Loki, OpenTelemetry). Use the property name as plain text in the template and reserve `{Braces}` for actual values. E.g. `"{ClassName} ServiceFamily={ServiceFamily}"` with args `nameof(MyService), config.Value.ServiceFamily` — not `"{ClassName} {ServiceFamily}={ServiceFamilyValue}"` with an extra `nameof(MyConfig.ServiceFamily)` arg.
- **`[LoggerMessage]` on hot paths**: Tight loops, `Channel` readers, stream consumers, tick processors, and sink iterators must use source-generated `[LoggerMessage]` logging to avoid `params object[]` boxing and interpolation. Declare `private static partial void` methods at the bottom of the partial class (or in a `{ClassName}.Logging.cs` file for larger services). First parameter is `ILogger logger` (not `this ILogger`). Call sites use `LogXxx(logger, ...)` / `LogXxx(_logger, ...)` — pass the primary-constructor `logger` or the `_logger` field. Leave dynamic-level calls (`logger.Log(level, ...)`) unconverted — `[LoggerMessage]` requires a compile-time-constant level.
- **Logging belongs in services, not controllers**: Domain-specific logging (`LogInformation` with request-specific fields) must live in the service method, not the controller. Controllers should not inject `ILogger` unless they perform work that cannot be delegated (e.g. SSE streaming loops with `LogTrace`).
- **`ILogger<T>` only**: Use `ILogger<T>` with structured message templates. Never use `Console.WriteLine` or `Debug.WriteLine` for application diagnostics.
- **Never log secrets or personal data**: Client secrets, access/refresh tokens, authorization headers, cached token contents, connection strings, SAS query strings, full local paths, and personally identifying values must never reach a log sink.

## HTTP and Streams

- **Reuse DI-supplied `HttpClient` instances**: Obtain clients from `IHttpClientFactory` or a typed client. Never construct a new `HttpClient` per request.
- **Propagate `CancellationToken`**: Thread it through every HTTP, stream, file, database, and paging operation, and pass it on to framework and SDK calls.
- **Never block on async code**: No `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, or other sync-over-async wrappers.
- **Stream large payloads**: Do not buffer an entire file or response body into memory unless the public method explicitly returns a byte array.
- **Dispose owned resources deterministically**: `HttpRequestMessage`, `HttpResponseMessage`, streams, and cancellation registrations via `using` / `await using`.
- **Validate before deserialising**: Check the response status and content before reading the body into a model.
- **Preserve response context in exceptions**: Include the response status and body in a domain exception, with credentials and user data redacted.
- **Bounded queues and explicit backpressure**: Prefer them over unbounded in-memory work collections.

## Performance

### ValueTask vs Task

- **Use `ValueTask` / `ValueTask<T>`** when a method frequently completes synchronously — cache hits, `TryRead` on channels, dictionary lookups that short-circuit, or wrappers returning a pre-computed result. Avoids allocating a `Task` on the synchronous path.
- **Use `Task` / `Task<T>`** when the method almost always goes async (HTTP, database, file I/O).
- **Never cache, await twice, or concurrently await a `ValueTask`**. Call `.AsTask()` at the call site if needed.
- **Hot-path interface signatures** (channel brokers, cache accessors) should prefer `ValueTask<T>` so implementations can avoid allocation when data is already available.

### sealed Classes

- **Mark every concrete class `sealed`** unless it is explicitly designed for inheritance (has `virtual`/`abstract` members or is documented as a base class). The JIT devirtualises and inlines method calls on sealed types.
- Background services, DI-registered services, entity types, converters, and middleware should default to `sealed`.
- Removing `sealed` later is a non-breaking change.

### Frozen Collections

- **`FrozenDictionary<TKey, TValue>` / `FrozenSet<T>`** for any collection populated once at startup and never mutated. Optimised for read throughput at the expense of creation cost.
- Build via `.ToFrozenDictionary()` / `.ToFrozenSet()` at the end of the initialisation path. Store as the concrete `FrozenDictionary<,>` type (not `IReadOnlyDictionary<,>`) so the JIT can devirtualise lookups.

### ConfigureAwait(false) in Library Code

- **Library projects** that do not touch `HttpContext` after an await must use `ConfigureAwait(false)` on every `await` to avoid unnecessary `SynchronizationContext` capture.
- **Application entry-point projects** (workloads, controllers) may omit it — ASP.NET Core has no sync context by default.

### Span-Based Parsing

- **`ReadOnlySpan<byte>` for raw byte streams**: Parse directly from `ReadOnlySpan<byte>` when data arrives as UTF-8 bytes (`PipeReader`, Redis buffers, network streams) — do not materialise a `string` first.
- **`ReadOnlySpan<char>` for API convenience**: Exists so callers holding a span do not need `.ToString()`. No speed benefit over the `string` overload for already-materialised strings.
- **Extension method deduplication**: The span version holds the implementation; the string version delegates via `.AsSpan()`.

### SearchValues\<T\>

- **`SearchValues<char>` / `SearchValues<byte>`** (static field) for repeated `IndexOfAny` / `ContainsAny` on a fixed set of delimiters or sentinels. The runtime selects the optimal vectorised implementation at startup.

### System.Threading.Lock

- Prefer `System.Threading.Lock` over `object` for dedicated lock instances. Enables a thinner locking path and signals intent more clearly.

### Hot-Path Conventions

- **`[MethodImpl(MethodImplOptions.AggressiveInlining)]`**: Apply to leaf-level parsing/conversion methods called in tight loops. Do not apply to methods with complex control flow — the JIT already inlines small methods.
- **Avoid allocations in tight loops** (`Channel` readers, `PipeReader` loops, stream consumers):
  - Use `stackalloc` or `ArrayPool<T>` for temporary buffers instead of `new byte[]`.
  - Prefer `ReadOnlySequence<byte>` slicing over `.ToArray()`.
  - Use source-generated `[LoggerMessage]` to eliminate `params object[]` boxing (see Logging section).
- **Async pass-through on wrappers**: Drop `async`/`await` on thin single-call wrappers to avoid state-machine allocation (see Style section).
- **`PipeReader` for line-oriented binary streams**: Process data in-place from the pipe's buffer without copying to intermediate `string` objects.

### TimeProvider for Current Time

- **Never call `DateTime.UtcNow`, `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.UtcNow`, or `DateTimeOffset.Now` directly in service / production code.** Inject `TimeProvider` and read the current instant via `timeProvider.GetUtcNow()` (returns `DateTimeOffset`) or `timeProvider.GetUtcNow().UtcDateTime` (for a `DateTime`). This keeps time deterministic and testable (`FakeTimeProvider`) and lets simulation runs advance a controlled clock.
- **DI**: `TimeProvider` is registered as a singleton (`TimeProvider.System`) and injected via the primary constructor, ordered after `ILogger` and `IOptions<T>` but before application services. Static helpers that cannot take a constructor may accept an optional `TimeProvider? timeProvider = null` parameter and fall back to `TimeProvider.System`.
- **Delays and timers on the injected clock**: Use the `TimeProvider` overloads — `Task.Delay(TimeSpan, timeProvider, ct)`, `timeProvider.CreateTimer(...)` — so simulated time controls scheduling too.
- **Permitted direct use**: Test code, `TimeProvider` implementations themselves, and static/`const` field initialisers or model default-property initialisers where DI is unavailable. Prefer refactoring to inject `TimeProvider` when practical.

## Controllers / Web API

- **Thin controllers**: Controllers must be pure pass-through — no business logic, no LINQ projections, no dictionary lookups, no logging. All domain logic and structured logging belongs in the service layer. A controller method should delegate to a single service call, map the result to an HTTP response type, and nothing else.
- **No `ILogger` in pass-through controllers**: If every method in a controller simply delegates to a service, remove the `ILogger` injection entirely. The service layer owns observability.
- **Expression-bodied methods**: Thin pass-through methods that are a single expression (or a single `await` + return) should use expression bodies (`=>`). For methods that branch on a nullable result (NotFound vs Ok), use a ternary with pattern matching.
- **`<inheritdoc cref="..."/>` on controller methods**: When a controller method is a thin pass-through, use `/// <inheritdoc cref="ServiceType.Method(ParamTypes)"/>` referencing the service method's XML docs. Do not duplicate documentation between the controller and the service.
- **Typed service methods over generic**: Controllers must not call generic base-class methods (e.g. `GetEntities<T>(tableName, ...)`, `Enqueue<T>(obj, ...)`) directly. Instead, add typed methods to the service interface that encapsulate domain knowledge (table names, queue keys) and include domain-specific logging. This keeps controllers ignorant of infrastructure details.
- **Nullable returns for NotFound patterns**: Service methods consumed by controllers that may return HTTP 404 should use nullable return types (e.g. `ItemDetail?`) rather than throwing exceptions. The controller uses pattern matching to map the result:

```csharp
public Results<Ok<ItemDetail>, NotFound> GetItem(int id)
    => itemSvc.TryGetItem(id) is { } item
        ? TypedResults.Ok(item)
        : TypedResults.NotFound();
```

- **`<example>` tags on DTOs**: All public properties on Web API request/response DTOs should have `/// <example>value</example>` XML doc tags. Swashbuckle uses these to populate example values in the Swagger UI, improving API discoverability.

## Disposable Resources

- `ServiceProvider` instances built in tests must be disposed via `using`/`await using`.
- Test helper classes should be `static` when they have no instance state.
- Avoid shared mutable static state in test fixtures — each test should be independently repeatable.

## Multi-Targeting

- Library code using APIs unavailable in lower target frameworks must use `#if` preprocessor guards (e.g. `#if NET8_0_OR_GREATER`).
