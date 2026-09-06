---
description: 'PowerShell scripting conventions for structure, strict mode, error handling and secret safety.'
applyTo: '**/*.ps1,**/*.psm1,**/*.psd1'
---

# PowerShell Scripts

## Structure

- **Shebang**: Open every `.ps1` with `#!/usr/bin/env pwsh` so the script is portable to Linux and macOS. Omit it from `.psm1` and `.psd1`.
- **Version requirement**: Follow the shebang with `#Requires -Version 7.4`. Windows PowerShell 5.1 is not a target.
- **Comment-based help**: Every non-trivial script needs `.SYNOPSIS`, `.DESCRIPTION`, one `.PARAMETER` per parameter, at least one `.EXAMPLE`, and `.NOTES` where preconditions such as credential scopes apply.
- **CmdletBinding and typed parameters**: Declare `[CmdletBinding()]` and a typed `param()` block. Add `SupportsShouldProcess` to any script that writes to a remote system, and gate every write behind `$PSCmdlet.ShouldProcess(...)` so `-WhatIf` is honest.
- **Location**: Place scripts in the dot-prefixed `.scripts/` folder at the repository root. Top-level developer entry points invoked directly by name, such as `build.ps1` and `clean.ps1`, live at the repository root instead, and have a matching `.sh` sibling where the task is not Windows-specific.
- **Naming**: `Verb-Noun.ps1` using an approved verb. Check with `Get-Verb`. `Set-` is the correct verb for a create-or-update, not `New-`.
- **Regions**: Group logical sections with `#region` / `#endregion`.
- **Invocation guard**: Wrap main execution in `if ($MyInvocation.InvocationName -ne '.')` so the script can be dot-sourced by a test without running.

## Strict Mode

- **Require `Set-StrictMode -Version 3.0`** immediately after `$ErrorActionPreference`. It catches uninitialised variables, missing properties, missing hashtable keys, and array indexes past the end.
- **Pin the number; never use `Latest`.** `Latest` is a moving target that silently adopts new rules when the PowerShell version changes, so a script that passes today can fail on a runner with a newer build. In PowerShell 7.6 the two are behaviourally identical, which is precisely why pinning costs nothing.
- Versions above `3.0` are accepted by the parameter but define no additional checks. `3.0` is the strictest meaningful level.
- Strict mode makes property access on loosely shaped JSON throw. Guard it with `ContainsKey()` on a hashtable, or `PSObject.Properties.Name -contains` on an object, rather than relaxing the mode.

## Error Handling

- Set `$ErrorActionPreference = 'Stop'` immediately after the `param()` block.
- Set `$PSNativeCommandUseErrorActionPreference = $false` before invoking a native command whose own exit code reporting is sufficient.
- Wrap main execution in `try` / `catch`, and exit with an explicit code: `0` on success, `1` on failure.
- `throw` for validation failures inside a function; `Write-Error -ErrorAction Continue` in a top-level catch before exiting.
- Fail with a message that names the operation and the remedy. An HTTP status alone is not diagnosable.

## Arrays and Shapes

- **Wrap in `@()` whenever a collection leaves an `if`, `switch`, or `foreach`.** A single-element array collapses to a bare object on assignment, which silently serialises as an object instead of an array and is rejected by REST APIs as an empty collection.
- Type a parameter that must be a collection as `[array]` or `[object[]]`.
- Do not assume a REST response shape. Handle both a bare array and a `count`/`value` envelope.

## Secrets

- Accept credentials as `[securestring]`, or read them from an environment variable or git-ignored `.env`. Never accept a plain-text credential as a defaulted parameter.
- Never write a credential, an `Authorization` header, or a value derived from either to any stream, including verbose and debug output.
- Strip the query string before including a Uri in an error message.
- Never commit a populated `.env`. Ship a `.env.example` documenting each variable instead.

## Output

- A script whose stdout is consumed by another process, such as an MCP server host, must write nothing to stdout. Send diagnostics to stderr with `[Console]::Error.WriteLine(...)`.
- Otherwise use `Write-Host` for progress, `Write-Verbose` for detail, and `Write-Warning` for recoverable problems.
- Do not use emoji in script output.
