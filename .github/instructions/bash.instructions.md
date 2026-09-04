---
description: 'Bash scripting conventions for structure, error handling, logging and testability.'
applyTo: '**/*.sh'
---

# Bash / Shell Scripts

## Structure

- **Shebang**: Use `#!/usr/bin/env bash`, which resolves Bash through `PATH` instead of assuming it is installed at `/bin/bash`.
- **Header comment**: Open every script with a short comment block stating what it does. Scripts invoked from a GitHub Actions composite step must also list required and optional environment variables.
- **Location**: Place non-trivial or composite-action scripts in a dot-prefixed `.scripts/` folder at the repository or action root.
- **Executable bit**: Set `chmod +x` on scripts invoked directly by path. Verify the tracked mode with `git ls-files -s path/to/script.sh`; use `git update-index --chmod=+x path/to/script.sh` if it reports `100644`.
- **Manual runbooks**: Scripts intended to be read and executed step by step are exempt from automation conventions. Identify them in the header so they are not mistaken for unattended automation.

## Error Handling

- Validate required environment variables before enabling strict mode so missing values produce a clear error:

  ```bash
  for var in FOO BAR BAZ; do
    if [[ -z "${!var:-}" ]]; then
      echo "::error::Required environment variable $var is not set."
      exit 1
    fi
  done

  set -euo pipefail
  ```

- Require `set -euo pipefail` in automation scripts, immediately after required-variable validation.
- Default optional variables explicitly before `set -u` takes effect, using `: "${VAR:=}"` or a per-use `"${VAR:-}"`.
- Quote every variable expansion unless word splitting or globbing is intentional.
- Fail fast with a specific message rather than allowing an invalid value to reach a downstream command.

## Logging and GitHub Actions Annotations

- Use `::error::message` for failures, `::warning title=X::message` for non-fatal issues, and `::add-mask::$value` for secrets obtained or derived at runtime.
- Log progress and diagnostics with one `echo` per line.
- Never log a secret in full. Redact it or register it with `::add-mask::`.

## Testability

- Scripts extracted from composite actions must run standalone using only their documented environment variables. `GITHUB_ENV` and `GITHUB_OUTPUT` may point to temporary local files.
- Mock external commands by placing stub executables earlier on `PATH`. Exercise the success path and each distinct error path.
- Run `shellcheck` against new or modified scripts and resolve findings above informational severity.

## Style

- Declare function-scoped variables with `local`.
- Lowercase values passed to case-sensitive systems that require lowercase, such as OCI registries, with `${var,,}`.
- Clean up temporary files and directories, including artifacts created inside loops.
