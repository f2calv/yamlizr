---
description: '.NET solution and build structure, central package management, solution format and SDK selection.'
applyTo: '**/*.csproj,**/*.slnx,**/Directory.Build.props,**/Directory.Packages.props,**/global.json'
---

# .NET Solution and Build Structure

## Central Build Configuration

- Keep shared MSBuild properties in the root `Directory.Build.props`, including namespace, language version, nullable reference types, implicit usings, warning policy, package metadata, and deterministic CI builds.
- Keep individual project files focused on project-specific properties and references.
- Centralize warning suppressions in `Directory.Build.props`, with a comment naming each suppressed diagnostic.
- Use conditional property groups for cross-cutting project categories, such as test documentation generation and packability.

## Central Package Management

- Define every NuGet package version in the root `Directory.Packages.props` and keep `ManagePackageVersionsCentrally` enabled.
- Use versionless `PackageReference` items in project files.

## Solution Format

- Use the modern XML `.slnx` solution format.
- This repository builds from a single root `yamlizr.slnx` containing the library, the CLI, and the test project.
- All three projects multi-target `net8.0`, `net9.0` and `net10.0`. A change that compiles on one framework must be verified on all of them before it is considered done.

## SDK Selection

- Stable .NET releases do not require an SDK version in `global.json`; allow the installed compatible stable SDK and CI setup to select the SDK by default.
- Pin the SDK `version` and `rollForward` policy when using a preview SDK, isolating an SDK regression, or when a workflow explicitly requires bit-for-bit SDK reproducibility.
- Keep `global.json` when it configures repository-wide .NET CLI behavior without pinning an SDK. This repository uses it to select `Microsoft.Testing.Platform` as the test runner.
