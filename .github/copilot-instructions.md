# Copilot Instructions

<!-- Synced section ------------------------------------------------------
	This file plus the shared files under `.github/instructions/` are kept
	aligned across f2calv .NET repositories. The repo-specific
	"Project-Specific Overrides" section below is excluded from sync.
	Edit once, sync everywhere.
	------------------------------------------------------------------- -->

## Instruction Files

Detailed conventions live in scoped instruction files under `.github/instructions/`, auto-applied by file type:

| File | Applies to | Covers |
| --- | --- | --- |
| `csharp.instructions.md` | `**/*.cs` | C# / .NET style, XML docs, logging, performance, Web API |
| `csharp.testing.instructions.md` | `**/*Tests/**/*.cs` | xUnit test structure, naming, theories, assertions |
| `dotnet.instructions.md` | `**/*.csproj`, `*.slnx`, `Directory.*.props` | Central build/package config, solution format, SDK selection |
| `github-actions.instructions.md` | workflows / `action.yml` | GitHub Actions naming, YAML, security, GitVersion |
| `bash.instructions.md` | `**/*.sh` | Bash scripting structure, error handling, logging, testability |
| `documentation.instructions.md` | `**/*.md` | README consistency and Mermaid diagrams |
| `configuration.instructions.md` | `**/appsettings*.json` | Options/appsettings synchronization and secret safety |

The conventions below always apply, regardless of the file being edited.

## Copilot Workflow

- **Test execution**: Never run tests automatically; they may be integration tests requiring an Azure DevOps organisation and a Personal Access Token. Always prompt (ideally with a visual yes/no button) before running any tests.
- **Preserve git history during renames/moves**: When renaming or relocating files, first perform the rename/move (preferably via `git mv`), then make content edits to the file at its new path. Do not delete and recreate files when a rename or move is intended.
- **Multi-repo commits**: When a single change spans multiple repositories, separate per-repository commit messages are acceptable (but not mandatory). Prefer them where the changes are disconnected, or where one repository should not know about the other.
- **Build after refactoring**: After any refactoring, build the entire solution (not only the affected project) to catch compilation errors in dependent projects. When multiple solutions exist, prefer `yamlizr.Debug.slnx`.

## Public Repository Confidentiality

- Treat every non-public repository's identity and contents as confidential, even when they appear in the local workspace, conversation context, diffs, logs, or tool output.
- Never publish private repository names, URLs, owner/repository coordinates, branches, file paths, architecture, deployment details, or inferred existence in tracked files, commit messages, issues, pull request titles/descriptions/reviews/comments, release notes, workflow annotations, examples, or other public-facing content.
- Describe required relationships generically (for example, "private GitOps repository" or "internal service") and supply private coordinates only through secrets, repository variables, or caller-provided values.
- Before creating or updating public GitHub content, review the proposed text and metadata for private identifiers and implementation details.

## Repository Structure

Every f2calv repository follows a consistent layout, regardless of language:

- Root files include `README.md`, `LICENSE`, `GitVersion.yml`, `.editorconfig`, `.gitattributes`, `.gitignore`, and `.pre-commit-config.yaml`.
- Source code lives under `src/`.
- Tooling lives in dot-prefixed folders such as `.github/`, `.scripts/`, `.devcontainer/`, `.docker/`, `.config/`, and `.vscode/`.
- Additional documentation beyond the root `README.md` lives as Markdown under `docs/`.
- `.editorconfig` is the source of truth for indentation, line endings, and analyzer or formatting rules.
- `GitVersion.yml` in the root drives semantic-versioning rules.

## Miscellaneous

- When detecting new conventions or patterns, add them to the appropriate `.github/instructions/*.instructions.md` file (or this file for cross-cutting workflow rules) and apply them retroactively where applicable.
- Keep this file and the shared `.github/instructions/` files aligned with the common guidelines used by sibling .NET repositories.

---

## Project-Specific Overrides

### Repository Purpose

This repository is a .NET global tool named `yamlizr` which converts Azure DevOps Classic Designer
Build and Release Definitions, and any Task Groups they reference, into their YAML Pipeline or
GitHub Actions equivalent. It is published to NuGet as the `yamlizr` package.

It contains three projects:

| Project | Purpose |
| --- | --- |
| `CasCap.Api.AzureDevOps` | Library: Azure DevOps REST access, pipeline models, and the YAML generator |
| `CasCap.DevOpsYamlizrCli` | The `yamlizr` global tool: command surface, console presentation, orchestration |
| `CasCap.Api.AzureDevOps.Tests` | xUnit v3 tests running on `Microsoft.Testing.Platform` |

### Conversion Fidelity Boundary

The tool is deliberately a blunt instrument: it emits as much YAML as it can and expects the user
to review and edit the result. That does not license silent data loss.

- When a classic construct cannot be represented, record it and surface it in the run summary
  rather than dropping it without trace.
- Never emit YAML that looks complete but silently omits a step, a variable, a condition, or a
  dependency.
- Generated YAML is not "production ready" and the README must keep saying so.

### Console Output

- Console presentation is this tool's user interface and legitimately uses `IConsole`, tables and
  progress bars. This is the single exception to the `csharp.instructions.md` rule against writing
  to the console. It does not license `Console.WriteLine` or `Debug.WriteLine` for **diagnostics**,
  which must still flow through `ILogger<T>`.
- Never swallow an exception into `Debug.WriteLine`. Log it through `ILogger<T>` and report a
  user-actionable message through `IConsole`.
- Never call `Debugger.Break()` in shipped code or in tests.

### Credential Handling

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

### Known Transitive Advisories

The Azure DevOps client libraries drag in packages that carry published advisories. `System.Drawing.Common`
5.0.0 (GHSA-rxg9-xrhp-64gj, critical) currently raises `NU1904` on every project. There is no direct
reference to remove and no upstream release that drops it.

These warnings are deliberately **not** added to `NoWarn`. Suppressing a critical advisory hides the
risk without reducing it, and the warning is the only signal that an upstream fix has landed. Re-check
on every dependency bump.
