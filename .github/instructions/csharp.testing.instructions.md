---
description: 'xUnit test structure, authentication, naming, theories and assertion conventions.'
applyTo: '**/*Tests/**/*.cs'
---

# Testing

## Folder Structure

Organize each `*.Tests` project by test type:

```text
Tests/
|-- Unit/           # Self-contained tests with no external services
`-- Integration/    # Tests requiring configuration or a live Azure DevOps organisation
    `-- TestBase.cs # Shared integration-test setup
```

## Integration Tests

- Apply `[Trait("Category", "Integration")]` to tests that call Azure DevOps or depend on a Personal Access Token.
- Place integration tests under `Tests/Integration/` and share configuration, logging, and service setup through `TestBase`.
- Build configuration in repository order: `appsettings.Test.json`, User Secrets for local runs, then environment variables for CI.
- Dispose any `ServiceProvider` created by the fixture through `IDisposable` or `IAsyncDisposable`.
- Skip rather than fail when no credential is configured, so a contributor without an Azure DevOps organisation still gets a green credential-free run.
- Read CI credentials only from secret-backed environment variables. Never commit a PAT, an organisation name, or a project name belonging to a real subscription, and never write a resolved secret to test output.
- Integration tests must be read-only against the target organisation. Never create, update, or delete a build definition, release definition, task group, or variable group.
- Do not run integration tests without explicit user approval.

## Unit Tests

- Place self-contained tests under `Tests/Unit/`.
- Do not inherit unit tests from an integration `TestBase` or require Azure DevOps credentials.
- Use domain-specific trait categories such as `Generation`, `Parsing`, or `Serialization`, rather than `Unit`.
- Prefer serialized Azure DevOps definitions captured as test assets over live calls, so generator behaviour is verified deterministically and offline.

## Diagnostic Output

- Write diagnostics through `ITestOutputHelper` or an `ILogger` configured to route output to xUnit.
- Never use `Debug.WriteLine` or `Console.WriteLine`; their output is unreliable in CI and the test explorer.
- Never call `Debugger.Break()` in a test; it hangs an unattended CI run.
- Prefer interpolation over concatenation or composite formatting.
- Emit only values that explain a failure, and include decisive context in assertion messages.

## Theory Parameterization

- Consolidate facts that differ only by input into one `[Theory]` with `[InlineData]`.
- Keep `[Fact]` for tests whose setup or assertions do not parameterize cleanly.

## Test Method Naming

- Name test methods after the method or feature under test, such as `GenPipeline` or `SanitizesDefinitionName`.
- Do not use verbose BDD-style sentence names.
- Use underscore-separated phases for lifecycle tests, such as `Definition_LoadGenerateSerialize`.

## Assertions

- Every test must contain meaningful assertions. Never use `Assert.True(true)` or another placeholder assertion.
- Never wrap the system under test in a `try`/`catch` that swallows the exception; a thrown exception must fail the test, or be asserted with `Assert.Throws`/`Assert.ThrowsAsync`.
- Prefer specific assertions such as `Assert.Equal`, `Assert.Contains`, `Assert.Single`, and `Assert.NotNull` over `Assert.True(condition)`.
- Move performance-only tests to BenchmarkDotNet rather than retaining timing loops without correctness assertions.

## Regression Tests for Reported Issues

- When fixing a reported issue, add a test that fails before the fix and passes after it, and name the issue in a comment on the test.
- Capture the offending input shape — a task group name containing spaces, an uninstalled extension identifier, a missing task version — as an offline test asset rather than reproducing it against a live organisation.

## Dead Code

- Delete commented-out tests, unreachable branches, and permanently skipped tests rather than leaving them in the suite.
- Remove helpers, fields, and using directives when their last test consumer is removed.

## Shared Test Data

- Keep shared generators and fixtures in dedicated `*TestData.cs` files at the `Tests/` root.
- Keep hardcoded regression data in `*Patterns.cs` files.
- Put stateless object-building helpers in static classes.

## Test Project README

Each test-project README must include the method count, expanded test-case count, trait categories, skipped-test reasons, and a diagram of the `Tests/` layout.
