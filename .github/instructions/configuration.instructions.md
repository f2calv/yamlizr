---
description: 'Azure DevOps options, appsettings synchronization and secret-handling conventions.'
applyTo: '**/appsettings*.json'
---

# Configuration

## Environment Layering

- Load `appsettings.json` first and then `appsettings.{Environment}.json`; later providers override earlier values by key.
- Load .NET User Secrets after JSON for local development and test credentials.
- Load environment variables last so CI and deployment environments can override JSON and User Secrets.
- Use the standard double-underscore form for nested environment variables, for example `CasCap__AzureDevOpsOptions__PAT`.

## Configuration Synchronization

- `AzureDevOpsOptions` defines the `CasCap:AzureDevOpsOptions` configuration shape. When adding, renaming, or removing one of its bindable properties, update every applicable `appsettings*.json` file and README example in the same change.
- Environment-specific files only need to repeat values that differ from the base file.
- Keep tracked examples runnable after credentials are supplied, using safe public defaults or `null` placeholders for sensitive values.
- Apply data-annotation validation to required values and preserve configuration validation when changing the options model.

## Command-Line Precedence

- The CLI is the primary entry point, so a command-line option always wins over a configuration value.
- Every credential or connection value accepted as a command-line option must also be bindable from configuration, so the tool can run unattended in a pipeline without the value appearing in a process command line.

## Secret Safety

- Never commit an Azure DevOps organisation name, project name, Personal Access Token, OAuth access token, or pipeline identifier belonging to a real subscription.
- Store local development and test credentials with .NET User Secrets. Supply CI credentials through GitHub Actions secrets and environment variables.
- Treat every file packaged into a NuGet package or container image as public.
- Never log or echo a PAT, an `Authorization` header, or the Base64 basic-auth string derived from a PAT, including in progress output, error messages, and exception detail.
