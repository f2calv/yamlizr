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

    AZURE_DEVOPS_PAT carries the raw token under a name that means what it says, and this launcher
    derives what the server actually wants. An existing process environment value takes precedence;
    otherwise the launcher reads it from .env.

    Nothing is written to stdout. That stream carries the MCP JSON-RPC session, and any stray output
    corrupts it, so diagnostics go to stderr.

.PARAMETER Organisation
    Azure DevOps organisation name, the segment after dev.azure.com/.

.PARAMETER EnvFile
    Fallback path to the .env file holding AZURE_DEVOPS_PAT. Defaults to .env in the repository root.

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

#region Functions

function Write-Diagnostic {
    <#
    .SYNOPSIS
        Writes launcher diagnostics to stderr without corrupting MCP stdout.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Message)

    [Console]::Error.WriteLine($Message)
}

function Get-DotEnvValue {
    <#
    .SYNOPSIS
        Reads one value from a dotenv file without executing the file.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) { return $null }

    $value = $null
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -notmatch '^\s*(?:export\s+)?(?<Name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<Value>.*)$') { continue }
        if ($Matches.Name -cne $Name) { continue }

        $candidate = $Matches.Value.Trim()
        if ($candidate.Length -ge 2) {
            $first = $candidate[0]
            $last = $candidate[$candidate.Length - 1]
            if (($first -eq "'" -and $last -eq "'") -or ($first -eq '"' -and $last -eq '"')) {
                $candidate = $candidate.Substring(1, $candidate.Length - 2)
            }
        }
        $value = $candidate
    }

    return $value
}

function Resolve-AdoPat {
    <#
    .SYNOPSIS
        Resolves the raw Azure DevOps PAT, preferring the process environment over dotenv.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()][string]$EnvironmentToken,
        [Parameter(Mandatory)][string]$Path
    )

    if (-not [string]::IsNullOrWhiteSpace($EnvironmentToken)) { return $EnvironmentToken }

    $fileToken = Get-DotEnvValue -Path $Path -Name 'AZURE_DEVOPS_PAT'
    if (-not [string]::IsNullOrWhiteSpace($fileToken)) { return $fileToken }

    throw "No credential found. Set AZURE_DEVOPS_PAT=<raw token> in '$([IO.Path]::GetFullPath($Path))', then restart this server."
}

function ConvertTo-AdoMcpCredential {
    <#
    .SYNOPSIS
        Encodes the raw PAT in the Basic credential shape required by the MCP server.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Token)

    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("yamlizr:$Token"))
}

function Resolve-NpxCommand {
    <#
    .SYNOPSIS
        Resolves the native npx application rather than the PowerShell wrapper.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    $command = Get-Command 'npx' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) { throw 'npx was not found on PATH. Install Node.js 20 or later.' }
    return $command.Source
}

function Invoke-NpxCommand {
    <#
    .SYNOPSIS
        Starts npx with stdout left exclusively to the MCP JSON-RPC transport.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )

    & $Path @ArgumentList
    $script:NpxExitCode = $LASTEXITCODE
}

function Start-AdoMcpServer {
    <#
    .SYNOPSIS
        Configures and starts the Azure DevOps MCP server process.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$OrganisationName,
        [Parameter(Mandatory)][string]$RawToken,
        [Parameter(Mandatory)][string]$NpxPath,
        [string[]]$Domains
    )

    $env:PERSONAL_ACCESS_TOKEN = ConvertTo-AdoMcpCredential -Token $RawToken

    $arguments = @('-y', '@azure-devops/mcp', $OrganisationName, '--authentication', 'pat')
    if ($Domains) { $arguments += @('-d') + $Domains }

    Invoke-NpxCommand -Path $NpxPath -ArgumentList $arguments
}

#endregion Functions

#region Main Execution

if ($MyInvocation.InvocationName -ne '.') {
    $script:NpxExitCode = 1

    try {
        $token = Resolve-AdoPat -EnvironmentToken $env:AZURE_DEVOPS_PAT -Path $EnvFile
        $npx = Resolve-NpxCommand
        Start-AdoMcpServer -OrganisationName $Organisation -RawToken $token -NpxPath $npx -Domains $Domain
        exit $script:NpxExitCode
    }
    catch {
        Write-Diagnostic "yamlizr: $($_.Exception.Message)"
        exit 1
    }
}

#endregion Main Execution
