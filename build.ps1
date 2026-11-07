#!/usr/bin/env pwsh
#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$scripts = @(
    (Join-Path $HOME '.copilot/skills/containers-workflows/scripts/Invoke-Build.ps1'),
    (Join-Path (Split-Path $PSScriptRoot -Parent) '.github/.github/skills/containers-workflows/scripts/Invoke-Build.ps1')
)
$implementation = $scripts | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $implementation) { throw "Invoke-Build.ps1 was not found. Link the central skills folder or clone the central repository beside this repository." }
& $implementation -RepositoryRoot $PSScriptRoot @args
exit $LASTEXITCODE
