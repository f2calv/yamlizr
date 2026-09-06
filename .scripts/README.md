# Repository Scripts

Tooling that supports development of yamlizr but ships with none of it.

## New-FixtureDefinitions.ps1

Creates or updates the Azure DevOps conversion fixtures that [issue #366](https://github.com/f2calv/yamlizr/issues/366) calls for: a set of deliberately awkward classic Build and Release definitions, so the integration tests can prove yamlizr converts a real definition correctly rather than only converting a hand-built one.

The script is the reproducible record of how those definitions were built. It never deletes; every operation is an upsert keyed on the object name, so it is safe to re-run.

### Sharing a project, and the name prefix

The script adds definitions to an existing project and never creates one. The fixture organisation is a GitHub-provisioned account whose single project already holds unrelated pipelines, so the fixtures have to coexist with them.

Every object the script creates therefore carries a common prefix, `yamlizr.test.` by default. That keeps the fixtures identifiable, guarantees the script never touches anything outside the prefix, and means the conversion is driven with yamlizr's own filter:

```powershell
yamlizr --filter yamlizr.test.
```

which exercises `--filter` rather than leaving it untested.

### Why a script rather than the MCP server

The [Azure DevOps MCP server](https://github.com/microsoft/azure-devops-mcp) is configured in [.vscode/mcp.json](../.vscode/mcp.json) and is useful for inspecting the fixtures, but it cannot build them. Its only pipeline write tool, `pipelines_write.create_pipeline`, creates YAML pipelines. The fixtures need classic designer definitions, task groups, variable groups and release definitions, none of which the MCP server exposes. The script calls the REST API directly instead.

### Credentials

Two tokens are in play and they are deliberately different.

| Token | Scope | Used by | Stored in |
| --- | --- | --- | --- |
| Full access | Read and write | This script and the MCP server | `.env` as `AZURE_DEVOPS_PAT` |
| Read only | Build, Release, Task Groups and Variable Groups read | The integration tests | User Secrets as `CasCap:AzureDevOpsOptions:PAT` |

Copy [.env.example](../.env.example) to `.env` and set `AZURE_DEVOPS_PAT` to the raw token exactly as Azure DevOps issued it. No encoding, no quotes. `.env` is git-ignored and must never be committed.

The MCP server itself reads a variable named `PERSONAL_ACCESS_TOKEN` whose contents are not a personal access token but base64 of `<email>:<pat>`. Rather than push that onto whoever populates `.env`, [Start-AdoMcpServer.ps1](Start-AdoMcpServer.ps1) derives it. Restart the `ado` server from the MCP view after changing `.env`; it is read only at start-up.

Pass `-Pat` instead if you would rather not keep a token on disk:

```powershell
$token = Read-Host 'PAT' -MaskInput
./.scripts/New-FixtureDefinitions.ps1 -OrganisationUri https://dev.azure.com/contoso -Project demo -Pat $token
```

Required scopes for the full-access token: Project and Team (read), Build (read and execute), Release (read, write, execute and manage), Task Groups (read, create and manage), Variable Groups (read, create and manage).

`Code` is deliberately not required. A classic Build definition must name a repository, and the binding for a GitHub repository carries a service connection id plus a page of provider metadata that cannot be reconstructed from a repository name. The script copies that binding from an existing definition rather than enumerating Azure Repos, which would need `Code` and would fail in an organisation that has no Azure Repos repository. Nothing is ever pushed to the borrowed repository, and no fixture definition is ever queued.

### Usage

Review what would change before writing anything:

```powershell
./.scripts/New-FixtureDefinitions.ps1 -OrganisationUri https://dev.azure.com/contoso -Project demo -WhatIf
```

Then apply it:

```powershell
./.scripts/New-FixtureDefinitions.ps1 -OrganisationUri https://dev.azure.com/contoso -Project demo
```

`-WhatIf` cannot report the whole set. Objects that depend on one the script would have created — the nested task group, the task group definition and the release definition — are skipped, because their dependency does not exist in a dry run.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-OrganisationUri` | required | Absolute organisation Uri, for example `https://dev.azure.com/contoso` |
| `-Project` | required | Existing project to add the fixtures to. Never created |
| `-Prefix` | `yamlizr.test.` | Applied to every object created, and the value to pass to `yamlizr --filter` |
| `-TemplateDefinition` | first definition | Existing definition whose repository binding the fixtures copy |
| `-Pat` | from `.env` | Full-access token as a `SecureString` |
| `-EnvFile` | `../.env` | Location of the file holding `AZURE_DEVOPS_PAT` |
| `-IncludeGates` | off | Adds a pre-deployment gate, which needs a shared work item query |
| `-IncludeUnknownTask` | off | Adds a step referencing an uninstalled task, which Azure DevOps may reject |

### What the fixture contains

Names below assume the default prefix.

| Object | Covers |
| --- | --- |
| `yamlizr.test.multi-phase` | Three agent phases, phase-to-phase dependencies, a fan-in, and a phase condition other than `succeeded()` |
| `yamlizr.test.server-phase` | An agentless phase next to an agent phase, so the phase yamlizr skips is exercised |
| `yamlizr.test.multiline-scripts` | Multi-line Bash, PowerShell and cmd scripts, a trailing blank line, and a whitespace-only script |
| `yamlizr.test.task-variety` | Fourteen in-box tasks across two phases, covering step-level `env`, `timeoutInMinutes`, `continueOnError` and conditions other than `succeeded()` |
| `yamlizr.test.task-groups` | A task group whose name contains spaces, referenced four times: defaults kept, defaults overridden, nested, and disabled |
| `yamlizr.test.triggers-and-variables` | Continuous integration, pull request and scheduled triggers with branch and path filters, definition variables including a secret and one settable at queue time, and a linked variable group |
| `yamlizr.test.uninstalled-extension` | A step whose task is not installed, covering the null path in [issue #177](https://github.com/f2calv/yamlizr/issues/177). Only created with `-IncludeUnknownTask` |
| `yamlizr.test.Task Group With Spaces` | Parameters with default values, a multi-line script, and a disabled step |
| `yamlizr.test.Nested Task Group` | Calls the task group above, covering recursive expansion |
| `yamlizr.test.common` | A variable group carrying plain, spaced and secret values |
| `yamlizr.test.release-multi-stage` | Three environments, a build artifact, automated and manual pre-deployment approvals, stage-after-stage conditions, a continuous deployment trigger, stage-scoped variables, a disabled step and an optional gate |
| `yamlizr.test.validation` | An empty YAML pipeline used only as a target for validating generated YAML. Deliberately the one fixture left enabled |

No value in the fixture is a real credential, and no definition is ever queued.

## Start-AdoMcpServer.ps1

Launches the Azure DevOps MCP server for the `ado` entry in [.vscode/mcp.json](../.vscode/mcp.json). Not run by hand.

It exists so `.env` can hold the raw token under a name that means what it says. The server's own contract is a variable called `PERSONAL_ACCESS_TOKEN` holding base64 of `<email>:<pat>` — a name that promises a token and contents that are not one. Getting it wrong fails as a 401 that reads like a missing scope. The launcher reads `AZURE_DEVOPS_PAT`, derives what the server wants, and writes nothing to stdout, which carries the MCP JSON-RPC session.

It also removes a start-up ordering trap: VS Code's `envFile` fails the whole connection when `.env` is absent, whereas the launcher reports a readable message on stderr.

## Validating the generated YAML

There are two routes, and they disagree in useful ways.

### Against Azure DevOps

The pipeline preview endpoint parses a document and expands its templates without queueing anything, which is the only check that proves Azure DevOps will actually accept the output. `IApiService.Validate` wraps it, and `PipelineValidationTests` exercises it.

It needs an existing YAML pipeline to parse against, which is what `yamlizr.test.validation` is for. That pipeline must be **enabled**; a disabled one fails the call with `DefinitionDisabledException` rather than validating anything. Point the tests at it:

```powershell
dotnet user-secrets set CasCap:AzureDevOpsOptions:ValidationPipelineId 19 --project src/CasCap.Api.AzureDevOps.Tests
```

One limitation: a relative `template:` reference resolves against the target pipeline's repository, not the submitted document. Generated YAML that references a task group template therefore only validates when those templates are committed, or when it was generated with `--inline`.

### In the editor

Install the recommended workspace extensions from [.vscode/extensions.json](../.vscode/extensions.json). The [Azure Pipelines extension](https://marketplace.visualstudio.com/items?itemName=ms-azure-devops.azure-pipelines) checks converted YAML against the published schema, and [.vscode/settings.json](../.vscode/settings.json) associates the folders yamlizr writes with it. Note that the extension only diagnoses **open** documents, so a closed file reporting no problems has not been checked.

The editor schema is stricter than the server in places: it rejects `variables: []`, which a preview run accepts.
