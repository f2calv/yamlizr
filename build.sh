#!/usr/bin/env bash
# Runs the shared PowerShell container build implementation.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec pwsh -NoProfile -File "${SCRIPT_DIR}/build.ps1" "$@"
