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
    |-- TestBase.cs           # Shared configuration, logging and service setup
    `-- ApiServiceTests.cs
```

## Test Counts

| Suite | Test methods | Expanded cases |
| --- | --- | --- |
| Unit | 9 | 13 |
| Integration | 1 | 1 |
| **Total** | **10** | **14** |

## Trait Categories

| Trait | Applied to | Runs in CI |
| --- | --- | --- |
| `Category=Generation` | `YamlPipelineGeneratorTests` | Yes |
| `Category=Serialization` | `PipelineSerializationTests` | Yes |
| `Category=Integration` | `ApiServiceTests` | No, excluded with `--filter-not-trait Category=Integration` |

## Skipped Tests

Integration tests skip rather than fail when no credential is configured, so a contributor without an
Azure DevOps organisation still gets a green credential-free run. The reported reasons are:

| Reason | Resolved by |
| --- | --- |
| No Azure DevOps token configured | Setting `CasCap:AzureDevOpsOptions:PAT`. |
| No organisation configured | Setting `CasCap:AzureDevOpsOptions:OrganisationUri`. |

Integration tests are read-only. They never create, update or delete a definition, task group or
variable group.

## Running

```bash
# credential-free, matching CI
dotnet test --filter-not-trait Category=Integration

# live, after configuring a token
dotnet user-secrets set CasCap:AzureDevOpsOptions:PAT "<your token here>"
dotnet user-secrets set CasCap:AzureDevOpsOptions:OrganisationUri "https://dev.azure.com/myorg"
dotnet test --filter-trait Category=Integration
```

## Dependencies

| NuGet package | Purpose |
| --- | --- |
| `CasCap.Common.Testing` | `AddXUnitLogging`, routing `ILogger` output to `ITestOutputHelper`. |
| `Microsoft.Extensions.Configuration.*` | Layered test configuration. |
| `Microsoft.Testing.Extensions.CodeCoverage` | Coverage collection. |
| `xunit.v3` | Test framework. |
