# CasCap.Api.AzureDevOps.Tests

xUnit v3 tests for `CasCap.Api.AzureDevOps`, running on `Microsoft.Testing.Platform`.

## Layout

```text
Tests/
|-- YamlizrTestData.cs        # Offline builders for Azure DevOps definitions
|-- Unit/                     # Self-contained, no external services
|   |-- YamlPipelineGeneratorTests.cs
|   `-- PipelineSerializationTests.cs
`-- Integration/              # Requires a live Azure DevOps organisation
    |-- TestBase.cs                 # Shared configuration, logging and service setup
    |-- ApiServiceTests.cs          # Task catalogue retrieval
    |-- PipelineValidationTests.cs  # Hand-written YAML submitted to the preview endpoint
    `-- FixtureConversionTests.cs   # Every fixture definition converted, then validated
```

## Test Counts

| Suite | Test methods | Expanded cases |
| --- | --- | --- |
| Unit | 18 | 27 |
| Integration | 8 | 8 |
| **Total** | **26** | **35** |

## Trait Categories

| Trait | Applied to | Runs in CI |
| --- | --- | --- |
| `Category=Generation` | `YamlPipelineGeneratorTests` | Yes |
| `Category=Serialization` | `PipelineSerializationTests` | Yes |
| `Category=Integration` | `ApiServiceTests`, `PipelineValidationTests`, `FixtureConversionTests` | Yes, when the repository secret is present |

## Skipped Tests

Integration tests skip rather than fail when no credential is configured, so a contributor without an
Azure DevOps organisation still gets a green run, as does a pull request from a fork, which cannot read
the secret. The reported reasons are:

| Reason | Resolved by |
| --- | --- |
| No Azure DevOps token configured | Setting `CasCap:AzureDevOpsOptions:PAT`. |
| No organisation configured | Setting `CasCap:AzureDevOpsOptions:OrganisationUri`. |
| No validation pipeline configured | Setting `CasCap:AzureDevOpsOptions:ValidationPipelineId`. |

Integration tests are read-only. They never create, update or delete a definition, task group or
variable group, and a preview run parses YAML without queueing anything.

## Validating Generated YAML

`FixtureConversionTests` converts every `yamlizr.test.*` definition in the fixture organisation and
submits each result to the Azure DevOps pipeline preview endpoint. That is stronger than a snapshot,
which only proves the output has not changed: this proves Azure DevOps will accept it. A regression
fails the build naming the definition, the error Azure DevOps returned, and the generated YAML.

The fixture organisation is built by [.scripts/New-FixtureDefinitions.ps1](../../.scripts/README.md),
which also creates the `yamlizr.test.validation` pipeline the preview endpoint parses against.

Task groups are inlined for validation, because a relative `template:` reference resolves against the
target pipeline's repository rather than the submitted document.

## Running

```bash
# everything, which is what CI now runs; integration tests skip without a token
dotnet test

# credential-free only
dotnet test --filter-not-trait Category=Integration

# live, after configuring the fixture organisation
dotnet user-secrets set CasCap:AzureDevOpsOptions:PAT "<your token here>"
dotnet user-secrets set CasCap:AzureDevOpsOptions:OrganisationUri "https://dev.azure.com/myorg"
dotnet user-secrets set CasCap:AzureDevOpsOptions:Project "myproject"
dotnet user-secrets set CasCap:AzureDevOpsOptions:ValidationPipelineId "19"
dotnet test --filter-trait Category=Integration
```

The token needs only read access, plus Build (read and execute) for the preview endpoint. It never
writes to the organisation.

## Dependencies

| NuGet package | Purpose |
| --- | --- |
| `CasCap.Common.Testing` | `AddXUnitLogging`, routing `ILogger` output to `ITestOutputHelper`. |
| `Microsoft.Extensions.Configuration.*` | Layered test configuration. |
| `Microsoft.Testing.Extensions.CodeCoverage` | Coverage collection. |
| `xunit.v3` | Test framework. |
