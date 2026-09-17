# Copilot Instructions

## Shared Instructions

Shared Copilot instruction files are maintained centrally in the [.github](https://github.com/f2calv/.github) repository under `instructions/`, and are applied to every workspace from the VS Code user profile via `~/.copilot/instructions`. They are deliberately not copied into this repository, so a change there takes effect everywhere without a pull request here.

Everything below is specific to this repository.

## Repository Purpose

This repository is a .NET global tool named `yamlizr` which converts Azure DevOps Classic Designer
Build and Release Definitions, and any Task Groups they reference, into their YAML Pipeline or
GitHub Actions equivalent. It is published to NuGet as the `yamlizr` package.

It contains three projects:

| Project | Purpose |
| --- | --- |
| `CasCap.Api.AzureDevOps` | Library: Azure DevOps REST access, pipeline models, and the YAML generator |
| `CasCap.DevOpsYamlizrCli` | The `yamlizr` global tool: command surface, console presentation, orchestration |
| `CasCap.Api.AzureDevOps.Tests` | xUnit v3 tests running on `Microsoft.Testing.Platform` |

## Conversion Fidelity Boundary

The tool is deliberately a blunt instrument: it emits as much YAML as it can and expects the user
to review and edit the result. That does not license silent data loss.

- When a classic construct cannot be represented, record it and surface it in the run summary
  rather than dropping it without trace.
- Never emit YAML that looks complete but silently omits a step, a variable, a condition, or a
  dependency.
- Generated YAML is not "production ready" and the README must keep saying so.

## Console Output

- Console presentation is this tool's user interface and legitimately uses `IConsole`, tables and
  progress bars. This is the single exception to the `csharp.instructions.md` rule against writing
  to the console. It does not license `Console.WriteLine` or `Debug.WriteLine` for **diagnostics**,
  which must still flow through `ILogger<T>`.
- Never swallow an exception into `Debug.WriteLine`. Log it through `ILogger<T>` and report a
  user-actionable message through `IConsole`.
- Never call `Debugger.Break()` in shipped code or in tests.

## Credential Handling

- The only credential is an Azure DevOps Personal Access Token, or an OAuth access token issued to
  a pipeline's build service identity. Both are supplied by the caller; the tool must never persist
  either one.
- Do not validate a credential by its length or shape. A pipeline-issued access token is a
  different length from a PAT, and Azure DevOps is free to change both. Validate by attempting the
  call and reporting the failure.
- Never log, echo, or embed a PAT, an access token, an `Authorization` header, or the Base64
  basic-auth string derived from a token — including in progress output, error text, exception
  detail, and generated YAML.
- Never write a real organisation name, project name, or definition name into a tracked example.

## Known Transitive Advisories

The Azure DevOps client libraries drag in packages that carry published advisories. `System.Drawing.Common`
5.0.0 (GHSA-rxg9-xrhp-64gj, critical) raises `NU1904` and `System.Security.Cryptography.Xml` 5.0.0
(GHSA-vh55-786g-wjwj, moderate) raises `NU1902`. There is no direct reference to remove and no upstream
release that drops them.

These warnings are deliberately **not** added to `NoWarn`. Suppressing a critical advisory hides the
risk without reducing it, and the warning is the only signal that an upstream fix has landed. Re-check
on every dependency bump.

The build sets `TreatWarningsAsErrors`, so the `NU190x` codes are listed in `WarningsNotAsErrors`
instead. That keeps them visible on every build without letting a newly published advisory fail a
build whose code has not changed.
