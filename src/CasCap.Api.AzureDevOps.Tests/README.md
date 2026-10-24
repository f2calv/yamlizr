# CasCap.Api.AzureDevOps.Tests

xUnit v3 tests for the Azure DevOps client and YAML generator, running on Microsoft.Testing.Platform.
Use the offline suites without credentials; live suites read an explicitly configured Azure DevOps project.

## Layout

```text
Tests/
|-- YamlizrTestData.cs        # Offline builders for Azure DevOps definitions
|-- Unit/                     # Self-contained, no external services
|   |-- YamlPipelineGeneratorTests.cs
|   |-- PipelineSerializationTests.cs
|   `-- TaskCatalogSerializationTests.cs
`-- Integration/              # Requires a live Azure DevOps organisation
    |-- TestBase.cs                 # Shared configuration, logging and service setup
    |-- ApiServiceTests.cs          # Task catalogue retrieval
    |-- PipelineValidationTests.cs  # Hand-written YAML submitted to the preview endpoint
    `-- FixtureConversionTests.cs   # Every fixture definition converted, then validated
```

## Test Counts

| Suite | Test methods | Expanded cases |
| --- | --- | --- |
| Unit | 23 | 33 |
| Integration | 8 | 8 |
| **Total** | **31** | **41** |

Counts are source-declared cases per target framework, not observed execution results. The project
targets `net8.0`, `net9.0` and `net10.0`; use an SDK supporting the current C# source and the selected
runtime. There are no MemberData providers or untagged test classes.

## Trait Categories

| Trait | Applied to | Prerequisites |
| --- | --- | --- |
| `Category=Generation` | `YamlPipelineGeneratorTests` | Offline; synthetic definitions and task catalogues |
| `Category=Serialization` | `PipelineSerializationTests`, `TaskCatalogSerializationTests` | Offline; in-memory models and JSON |
| `Category=Integration` | `ApiServiceTests`, `PipelineValidationTests`, `FixtureConversionTests` | Live credentials plus organization/project and enabled validation pipeline where needed |

## Skipped Tests

The following are conditional skip guards, not permanently skipped tests. Counts are methods that
can report each reason; guards overlap, so do not sum them as an execution result.

| Reason | Methods | Required configuration |
| --- | --- | --- |
| No Azure DevOps token configured | 1 | `PAT` for task-catalogue retrieval |
| No organisation configured | 1 | `OrganisationUri` after the token guard passes |
| No validation pipeline configured | 6 | All of `PAT`, `OrganisationUri`, `Project`, `ValidationPipelineId`, despite the shorter message |
| No fixture organisation configured | 1 | All four fields above, plus accessible fixture definitions to make the test useful |

[`TestBase`](Tests/Integration/TestBase.cs) loads `appsettings.Test.json`, User Secrets, then environment
variables, binding
[`AzureDevOpsOptions`](../CasCap.Api.AzureDevOps/Models/AzureDevOpsOptions.cs).
[`PipelineValidationTests`](Tests/Integration/PipelineValidationTests.cs) and
[`FixtureConversionTests`](Tests/Integration/FixtureConversionTests.cs) each require all four connection
fields; supplying a token alone is not sufficient. There is no additional live-enable switch.

Integration tests are read-only. They never create, update or delete a definition, task group or
variable group, and a preview run parses YAML without queueing anything.

## Validating Generated YAML

`FixtureConversionTests` retrieves classic definitions whose names start with `yamlizr.test.`,
converts them with task groups inlined and submits non-null results to the preview endpoint.
It asserts at least one conversion and no preview rejections; a null conversion is skipped within
the loop, so it does not prove every matching definition converted. Preview acceptance proves parsing,
not semantic equivalence, runtime success or preservation of approvals and gates.

The fixtures are provisioned by
[New-FixtureDefinitions.ps1](../../.scripts/New-FixtureDefinitions.ps1)
([script guide](../../.scripts/README.md)),
which also creates the `yamlizr.test.validation` pipeline the preview endpoint parses against.

Task groups are inlined for validation, because a relative `template:` reference resolves against the
target pipeline's repository rather than the submitted document.

## Running

Run from the repository root. These commands are examples, not a record of tests executed.
Release configuration uses packages instead of requiring sibling shared-library checkouts.
The .NET 10 SDK's MTP runner supports these trait options without a `--` separator.

```bash
# credential-free only, one target framework
dotnet test --project src/CasCap.Api.AzureDevOps.Tests/CasCap.Api.AzureDevOps.Tests.csproj --configuration Release --framework net10.0 --filter-not-trait Category=Integration

# all source-declared cases; live tests skip when their configuration is absent
dotnet test --project src/CasCap.Api.AzureDevOps.Tests/CasCap.Api.AzureDevOps.Tests.csproj --configuration Release --framework net10.0
```

For a deliberately authorized live run, configure all four fields via User Secrets with
`--project src/CasCap.Api.AzureDevOps.Tests` or secret-backed `CasCap__AzureDevOpsOptions__*`
environment variables. Use an existing enabled YAML validation pipeline and the default fixture
prefix; do not publish its resolved ID or project details. The test harness reads only the regular
options configuration, not the CLI's `SYSTEM_*` fallback variables. Select live tests with
`--filter-trait Category=Integration`.

Failure output can contain generated YAML, definition names and server messages. Treat it as
potentially sensitive. No test provisions the fixture project on your behalf.

### Inside the Container Build

The Release `Dockerfile` has an optional `test` stage that runs the credential-free tests in the .NET
SDK image, with no local SDK needed. It is not part of the default image target:

```bash
docker buildx build --target test --progress=plain .
```

It runs on the build machine's native platform, for `net10.0` only because the SDK image ships only
the .NET 10 runtime, with `Category=Integration` filtered out and networking disabled. A failing test
fails the build.

The token needs only read access, plus Build (read and execute) for the preview endpoint. It never
writes to the organisation.

## Further Reading

- [Generator API and limits](../CasCap.Api.AzureDevOps/README.md)
- [Fixture provisioning and editor validation](../../.scripts/README.md)
