#!/usr/bin/env pwsh
#Requires -Version 7.4
<#
.SYNOPSIS
    Launches the Azure DevOps MCP server with a credential read from .env.

.DESCRIPTION
    The server's PAT mode reads an environment variable called PERSONAL_ACCESS_TOKEN, and expects it
    to hold the base64 encoding of "<email>:<pat>" rather than the token itself. The name says token
    and the contents are not a token, which is easy to get wrong by hand and fails as a 401 that
    reads like a permissions problem.

    So .env carries AZURE_DEVOPS_PAT, the raw token under a name that means what it says, and this
    launcher derives what the server actually wants.

    Nothing is written to stdout. That stream carries the MCP JSON-RPC session, and any stray output
    corrupts it, so diagnostics go to stderr.

.PARAMETER Organisation
    Azure DevOps organisation name, the segment after dev.azure.com/.

.PARAMETER EnvFile
    Path to the .env file holding AZURE_DEVOPS_PAT. Defaults to .env in the repository root.

.PARAMETER Domain
    Tool domains to load. Fewer domains means a shorter tool list for the agent to choose from.

.EXAMPLE
    ./.scripts/Start-AdoMcpServer.ps1 -Organisation contoso

.NOTES
    Invoked by the `ado` server in .vscode/mcp.json rather than run by hand.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Organisation,

    [Parameter()]
    [string]$EnvFile = (Join-Path $PSScriptRoot '..' '.env'),

    [Parameter()]
    [string[]]$Domain = @('core', 'pipelines', 'repositories')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

# npx reports its own failures; letting PowerShell also throw on a non-zero exit adds noise.
$PSNativeCommandUseErrorActionPreference = $false

function Write-Diagnostic {
    param([string]$Message)
    [Console]::Error.WriteLine($Message)
}

$token = $env:AZURE_DEVOPS_PAT

if ([string]::IsNullOrWhiteSpace($token) -and (Test-Path -LiteralPath $EnvFile)) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*AZURE_DEVOPS_PAT\s*=\s*(.+?)\s*$') {
            $token = $Matches[1].Trim("'", '"')
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Diagnostic "yamlizr: no credential found."
    Write-Diagnostic "yamlizr: set AZURE_DEVOPS_PAT=<raw token> in '$([IO.Path]::GetFullPath($EnvFile))', then restart this server."
    Write-Diagnostic 'yamlizr: see .env.example. The variable was renamed from PERSONAL_ACCESS_TOKEN and now holds the raw token, not base64.'
    exit 1
}

# The server wants base64 of "<email>:<pat>". The email half is never validated.
$env:PERSONAL_ACCESS_TOKEN = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("yamlizr:$token"))

# -CommandType Application skips npx.ps1, which PowerShell would otherwise prefer and run in-process.
$npx = Get-Command 'npx' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $npx) {
    Write-Diagnostic 'yamlizr: npx was not found on PATH. Install Node.js 20 or later.'
    exit 1
}

$arguments = @('-y', '@azure-devops/mcp', $Organisation, '--authentication', 'pat')
if ($Domain) { $arguments += @('-d') + $Domain }

& $npx.Source @arguments
exit $LASTEXITCODE
