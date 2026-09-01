# Copilot Instructions for yamlizr

## Repository Overview

**yamlizr** is a .NET Global Tool that converts Azure DevOps Classic Designer Build/Release Definitions and Task Groups into YAML Pipeline or GitHub Actions equivalents. The tool uses the Azure DevOps .NET Client Libraries to pre-cache data and converts it into pipeline objects using YamlDotNet.

### Repository Details

- **Type**: .NET Console Application (CLI tool) packaged as a NuGet global tool
- **Size**: Small (~3.7 MB, ~126 files)
- **Target Frameworks**: Multi-targeted for net8.0, net9.0, and net10.0
- **Primary Language**: C# (19 source files)
- **Solution Format**: Modern XML-based solution file (`yamlizr.slnx`)
- **Runtime**: .NET 10.0.102 SDK is available

## Public Repository Confidentiality

- Treat every non-public repository's identity and contents as confidential, even when they appear in the local workspace, conversation context, diffs, logs, or tool output.
- Never publish private repository names, URLs, owner/repository coordinates, branches, file paths, architecture, deployment details, or inferred existence in tracked files, commit messages, issues, pull request titles/descriptions/reviews/comments, release notes, workflow annotations, examples, or other public-facing content.
- Describe required relationships generically (for example, "private GitOps repository" or "internal service") and supply private coordinates only through secrets, repository variables, or caller-provided values.
- Before creating or updating public GitHub content, review the proposed text and metadata for private identifiers and implementation details.

## Project Structure

### Main Projects

1. **CasCap.DevOpsYamlizrCli** - CLI tool (executable, packaged as global tool)
   - Location: `src/CasCap.DevOpsYamlizrCli/`
   - Entry Point: `Program.cs`
   - Output: `yamlizr` command-line tool

2. **CasCap.Api.AzureDevOps** - Core library
   - Location: `src/CasCap.Api.AzureDevOps/`
   - Contains: Models/, Services/, Utilities/

3. **CasCap.Api.AzureDevOps.Tests** - Test project (xUnit)
   - Location: `src/CasCap.Api.AzureDevOps.Tests/`
   - Framework: xUnit with coverlet for code coverage

### Key Configuration Files

- **Root Directory**:
  - `Directory.Build.props` - Common MSBuild properties for all projects
  - `Directory.Packages.props` - Central Package Management (CPM) configuration
  - `global.json` - .NET SDK configuration
  - `GitVersion.yml` - Semantic versioning configuration
  - `.editorconfig` - C# code style preferences
  - `.pre-commit-config.yaml` - Pre-commit hooks configuration

- **CI/CD**:
  - `.github/workflows/ci.yml` - Main CI workflow
  - Uses reusable workflows from `f2calv/gha-workflows@v1`

## Build & Test Instructions

### Prerequisites

- .NET 8.0, 9.0, or 10.0 SDK (any will work due to multi-targeting)
- No need to install pre-commit locally unless running linting

### Build Process

**IMPORTANT**: Always run these commands from the repository root directory (`/home/runner/work/yamlizr/yamlizr`).

1. **Restore Dependencies** (run first):

   ```bash
   dotnet restore
   ```

   - Duration: ~5-10 seconds
   - Expected warnings: NU1903 warnings about System.Data.SqlClient vulnerability (these are known and can be ignored)

2. **Build the Solution**:

   ```bash
   dotnet build
   ```

   - Duration: ~10-15 seconds
   - Builds all 3 projects for all target frameworks (net8.0, net9.0, net10.0)
   - Configuration: Debug (default) or Release
   - Expected warnings: Same NU1903 warnings as restore
   - Success criteria: "Build succeeded" with 0 errors

3. **Run Tests**:

   ```bash
   dotnet test
   ```

   - Duration: ~10-15 seconds
   - Runs xUnit tests with code coverage via coverlet
   - Currently: 1 test in the test project
   - Success criteria: "Passed! - Failed: 0, Passed: 1, Skipped: 0"

4. **Clean Build Artifacts**:

   ```bash
   dotnet clean
   ```

   OR use the PowerShell script:

   ```powershell
   pwsh clean.ps1
   ```

   - Removes all bin/ and obj/ directories recursively

### Build Sequence for Code Changes

**ALWAYS follow this order**:

1. `dotnet restore` - Required before building
2. `dotnet build` - Build and check for compilation errors
3. `dotnet test` - Verify tests pass
4. Commit changes

**Note**: Do not skip `dotnet restore` after modifying `.csproj` files or `Directory.Packages.props`.

## CI/CD Pipeline

### GitHub Actions Workflows

The CI pipeline consists of two reusable workflows:

1. **Lint Job** (`f2calv/gha-workflows/.github/workflows/lint.yml@v1`):
   - Runs pre-commit hooks via Python
   - Checks: XML, YAML, JSON5, large files, trailing whitespace, markdown formatting
   - Pre-commit version: 3.7.1
   - **Limitation**: Some hooks may fail locally due to network restrictions (e.g., gitlab.com access)

2. **Build & Publish Job** (`f2calv/gha-workflows/.github/workflows/dotnet-publish-nuget.yml@v1`):
   - Performs versioning using GitVersion
   - Builds in Release configuration by default
   - Executes tests (can be disabled with `execute-tests: false`)
   - Publishes NuGet packages when on main branch
   - Creates GitHub releases when appropriate

### Triggers

- Push to any branch except `preview/**` (ignoring LICENSE, README.md)
- Pull requests to main branch
- Manual workflow dispatch

## Code Style & Conventions

### C# Conventions (enforced via .editorconfig)

- **Indentation**: 4 spaces
- **Line endings**: LF (Unix-style)
- **Braces**: Always use braces for control flow
- **Namespaces**: Block-scoped (not file-scoped)
- **var usage**: Prefer explicit types (`false` for all var preferences)
- **Expression bodies**: Use for properties, accessors, lambdas; avoid for methods
- **Naming**: PascalCase for types/members, interfaces start with "I"

### Build Properties

- **LangVersion**: C# 14.0
- **Nullable**: Not enabled globally (per-project basis)
- **ImplicitUsings**: Enabled
- **Warnings**: IDE1006, IDE0079, IDE0042, CS0162, S125, NETSDK1233 are suppressed

## Known Issues & Workarounds

### Package Vulnerabilities

- **System.Data.SqlClient 4.8.5**: Known high severity vulnerability (GHSA-98g6-xh36-x2p7)
- This is a transitive dependency from Azure DevOps client libraries
- **Workaround**: Currently accepted as known issue; not blocking builds

### Pre-commit Hooks

- Pre-commit hooks reference gitlab.com which may be blocked in some environments
- **Workaround**: CI runs pre-commit in isolated Python venv; local pre-commit runs may fail due to network restrictions

### Multi-Targeting

- Projects target net8.0, net9.0, and net10.0
- NETSDK1233 warning is suppressed for .NET 10 support
- All frameworks must build successfully for CI to pass

## Dependencies

### Key External Dependencies

- Azure DevOps .NET Client Libraries (Microsoft.TeamFoundation.*, Microsoft.VisualStudio.Services.*)
- YamlDotNet - YAML serialization
- AzurePipelinesToGitHubActionsConverter.Core - GitHub Actions conversion
- McMaster.Extensions.CommandLineUtils - CLI framework
- xUnit - Testing framework
- coverlet - Code coverage

### Package Management

- Uses Central Package Management (CPM) via `Directory.Packages.props`
- All package versions are defined centrally
- Projects reference packages without version attributes

## Validation Steps

### Before Committing

1. Ensure code builds: `dotnet build`
2. Ensure tests pass: `dotnet test`
3. Check for code style issues (local .editorconfig compliance)
4. Review changes against existing conventions

### CI Will Validate

- Pre-commit hooks (YAML, JSON, markdown, file sizes, whitespace)
- Multi-framework builds (net8.0, net9.0, net10.0)
- All tests pass
- NuGet package creation succeeds

## Additional Notes

### Solution File

- Uses modern XML solution format (`.slnx`) instead of legacy `.sln`
- Contains 3 projects and solution items folder
- Visual Studio 2022 and later support this format

### Tool Usage

The CLI tool is intended to:

- Connect to Azure DevOps using a PAT token
- Download and convert Build/Release definitions to YAML
- Output can be Azure Pipelines or GitHub Actions format
- See README.md for full CLI usage documentation

### Architecture

- **CLI Layer**: Commands, parsing, progress bars
- **API Layer**: Azure DevOps integration, model conversion
- **Tests**: Configuration-based testing with user secrets support

## Quick Reference

```bash
# Full build validation sequence
cd /home/runner/work/yamlizr/yamlizr
dotnet restore
dotnet build
dotnet test

# Expected output: Build succeeded with ~12 NU1903 warnings (safe to ignore)
# Expected tests: 1 passed

# Clean artifacts
dotnet clean

# Build for specific configuration
dotnet build -c Release
```

**Trust these instructions** - they have been validated against the actual repository. Only perform additional searches if you encounter unexpected errors or need information not covered here.
