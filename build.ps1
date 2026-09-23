#!/usr/bin/env pwsh
#Requires -Version 7.4
$scripts = @((Join-Path $HOME '.copilot/skills/container-workflows/scripts/Invoke-Build.ps1'), (Join-Path (Split-Path $PSScriptRoot -Parent) '.github/.github/skills/container-workflows/scripts/Invoke-Build.ps1'))
$implementation = $scripts | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $implementation) { throw "Invoke-Build.ps1 was not found. Checked: $($scripts -join ', ')" }
& $implementation -RepositoryRoot $PSScriptRoot -BuildMode Yamlizr @args
