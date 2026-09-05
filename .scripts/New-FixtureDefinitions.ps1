#!/usr/bin/env pwsh
#Requires -Version 7.4
<#
.SYNOPSIS
    Creates or updates the yamlizr conversion fixture definitions in an existing Azure DevOps project.

.DESCRIPTION
    Issue #366 needs a curated set of deliberately awkward classic Build and Release definitions, so
    the integration tests can prove yamlizr converts a real definition correctly rather than only
    converting a hand-built one.

    The Azure DevOps MCP server cannot build that fixture. Its only write tool for pipelines,
    pipelines_write.create_pipeline, creates YAML pipelines, and the fixture needs classic designer
    definitions, task groups, variable groups and release definitions. This script calls the REST
    API directly instead, and is the reproducible record of how the fixture was built.

    The fixture shares a project with unrelated definitions, so every object it creates carries a
    common name prefix. That keeps the fixture identifiable, and it means the conversion can be
    driven with `yamlizr --filter <prefix>`, which exercises that filter rather than leaving it
    untested.

    Every operation is an upsert keyed on the object name, so the script is safe to re-run. It
    creates and updates; it never deletes, and it never touches an object outside the prefix.

    The token this script needs is the full-access one. It is deliberately not the read-only token
    the integration tests use, which lives in User Secrets under CasCap:AzureDevOpsOptions:PAT.

.PARAMETER OrganisationUri
    Absolute Uri of the Azure DevOps organisation, for example https://dev.azure.com/contoso.

.PARAMETER Project
    Name of an existing project to add the fixture definitions to. The script never creates one.

.PARAMETER Prefix
    Name prefix applied to every object the script creates, and the value to pass to yamlizr's
    --filter option.

.PARAMETER TemplateDefinition
    Name of an existing definition whose repository binding the fixtures should copy. Defaults to
    the first definition in the project.

.PARAMETER Pat
    Full-access Personal Access Token. When omitted, AZURE_DEVOPS_PAT is read from the environment
    or from the .env file named by -EnvFile.

.PARAMETER EnvFile
    Path to the .env file holding AZURE_DEVOPS_PAT. Defaults to .env in the repository root.

.PARAMETER IncludeGates
    Adds a pre-deployment gate to the release fixture. Off by default because a gate needs a shared
    work item query, which the project may not have.

.PARAMETER IncludeUnknownTask
    Adds a step referencing a task GUID that is not installed, to cover the null-reference path in
    issue #177. Off by default because Azure DevOps may reject the definition outright.

.EXAMPLE
    ./.scripts/New-FixtureDefinitions.ps1 -OrganisationUri https://dev.azure.com/contoso -Project demo -WhatIf

    Reports every object that would be created or updated, without calling any write API.

.EXAMPLE
    ./.scripts/New-FixtureDefinitions.ps1 -OrganisationUri https://dev.azure.com/contoso -Project demo

.NOTES
    Required token scopes: Build (Read & execute), Release (Read, write, execute & manage), Task
    Groups (Read, create & manage), Variable Groups (Read, create & manage), Project and Team (Read).

    Code is deliberately not required. The repository binding is copied from an existing definition
    rather than resolved against Azure Repos.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OrganisationUri,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Project,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$Prefix = 'yamlizr.test.',

    [Parameter()]
    [string]$TemplateDefinition,

    [Parameter()]
    [securestring]$Pat,

    [Parameter()]
    [string]$EnvFile = (Join-Path $PSScriptRoot '..' '.env'),

    [Parameter()]
    [switch]$IncludeGates,

    [Parameter()]
    [switch]$IncludeUnknownTask
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

#region Constants

$script:ApiVersion = '7.1'
$script:PreviewApiVersion = '7.1-preview.1'
$script:VariableGroupApiVersion = '7.1-preview.2'

$script:Prefix = $Prefix
$script:VariableGroupName = "${Prefix}common"
$script:TaskGroupWithSpaces = "${Prefix}Task Group With Spaces"
$script:NestedTaskGroupName = "${Prefix}Nested Task Group"

#endregion Constants

#region Connection

function Get-AuthorizationHeader {
    <#
    .SYNOPSIS
        Builds the basic authorization header value, without ever emitting it.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [securestring]$Pat,
        [string]$EnvFile
    )

    if ($Pat) {
        $token = [System.Net.NetworkCredential]::new('', $Pat).Password
    }
    else {
        $token = $env:AZURE_DEVOPS_PAT

        if ([string]::IsNullOrWhiteSpace($token) -and (Test-Path -LiteralPath $EnvFile)) {
            foreach ($line in Get-Content -LiteralPath $EnvFile) {
                if ($line -match '^\s*AZURE_DEVOPS_PAT\s*=\s*(.+?)\s*$') {
                    $token = $Matches[1].Trim("'", '"')
                    break
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "No credential available. Pass -Pat, or set AZURE_DEVOPS_PAT in '$EnvFile'. See .env.example."
    }

    return 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(":$token"))
}

function Get-VsrmUri {
    <#
    .SYNOPSIS
        Maps an organisation Uri onto the Release Management host that serves release definitions.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([string]$OrganisationUri)

    $trimmed = $OrganisationUri.TrimEnd('/')

    if ($trimmed -match '^https://dev\.azure\.com/') {
        return $trimmed -replace '^https://dev\.azure\.com/', 'https://vsrm.dev.azure.com/'
    }

    if ($trimmed -match '^https://[^.]+\.visualstudio\.com') {
        return $trimmed -replace '^https://([^.]+)\.visualstudio\.com', 'https://$1.vsrm.visualstudio.com'
    }

    throw 'Unrecognised organisation Uri. Expected https://dev.azure.com/<org> or https://<org>.visualstudio.com.'
}

function Invoke-AdoApi {
    <#
    .SYNOPSIS
        Calls an Azure DevOps REST endpoint, reporting failures without echoing the credential.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Uri,

        [ValidateSet('Get', 'Post', 'Put', 'Patch', 'Delete')]
        [string]$Method = 'Get',

        [object]$Body
    )

    $arguments = @{
        Uri         = $Uri
        Method      = $Method
        Headers     = @{ Authorization = $script:AuthorizationHeader; Accept = 'application/json' }
        ContentType = 'application/json; charset=utf-8'
        ErrorAction = 'Stop'
    }

    if ($null -ne $Body) {
        $arguments.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 32))
    }

    try {
        $response = Invoke-WebRequest @arguments
    }
    catch {
        # The Uri and the request body never carry the token, so both are safe to report.
        $safeUri = ($Uri -split '\?')[0]
        $detail = if ($_.ErrorDetails) { " $($_.ErrorDetails.Message)" } else { '' }
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }

        # Azure DevOps answers a missing scope with a sign-in challenge, which reads like a bad token.
        $hint = if ($status -in 401, 203, 403) {
            " The token authenticated but lacks the scope for this endpoint. Check the scopes listed in .scripts/README.md."
        }
        else { '' }

        throw "Azure DevOps $Method $safeUri failed: $($_.Exception.Message).$detail$hint"
    }

    $content = $response.Content
    if ([string]::IsNullOrWhiteSpace($content)) { return $null }

    $trimmed = $content.TrimStart()
    if (-not ($trimmed.StartsWith('{') -or $trimmed.StartsWith('['))) {
        # A sign-in page arrives as HTML with a 200, which is how a missing scope usually presents.
        throw "Azure DevOps $Method $(($Uri -split '\?')[0]) returned $($response.StatusCode) with a non-JSON body, which normally means the token lacks the scope for this endpoint."
    }

    # -AsHashtable is not optional. The task catalogue carries a property with an empty name, which
    # the object parser rejects outright, and it gives one predictable shape for every response.
    return $content | ConvertFrom-Json -AsHashtable
}

function Get-AdoCollection {
    <#
    .SYNOPSIS
        Returns the items of a list endpoint.
    .DESCRIPTION
        Azure DevOps is not consistent about this. Most endpoints wrap results in a count/value
        envelope, but some, distributedtask/tasks among them, return a bare array. Reading .value
        blindly fails on the second shape under Set-StrictMode.
    #>
    [CmdletBinding()]
    [OutputType([array])]
    param([Parameter(Mandatory)][string]$Uri)

    $response = Invoke-AdoApi -Uri $Uri

    if ($null -eq $response) { return @() }
    if ($response -is [System.Collections.IDictionary]) {
        if ($response.ContainsKey('value')) { return @($response['value']) }
        return @($response)
    }

    return @($response)
}

#endregion Connection

#region Discovery

function Get-FixtureProject {
    <#
    .SYNOPSIS
        Returns the project the fixtures are added to, which must already exist.
    #>
    [CmdletBinding()]
    param([string]$Name)

    $projects = Get-AdoCollection -Uri "$script:OrgUri/_apis/projects?api-version=$script:ApiVersion"
    $existing = $projects | Where-Object { $_.name -eq $Name } | Select-Object -First 1

    if (-not $existing) {
        throw "Project '$Name' was not found. Available projects: $(($projects.name | Sort-Object) -join ', ')."
    }

    return $existing
}

function Get-FixtureRepositoryReference {
    <#
    .SYNOPSIS
        Returns the repository binding the fixture definitions should use, copied from an existing one.
    .DESCRIPTION
        A classic Build definition must name a repository, and the binding for a GitHub repository
        carries a service connection id plus a page of provider metadata that cannot be reconstructed
        from a repository name. Copying it from a definition that already builds keeps the fixtures
        pointing somewhere real, and keeps the Code scope off the token, which enumerating Azure
        Repos would otherwise demand.

        Nothing is ever pushed to the borrowed repository. The fixture definitions are never queued.
    #>
    [CmdletBinding()]
    param([string]$TemplateDefinition)

    $references = Get-AdoCollection -Uri "$script:ProjectUri/_apis/build/definitions?api-version=$script:ApiVersion"

    if (-not $references) {
        throw "Project '$script:ProjectName' has no build definition to copy a repository binding from."
    }

    $chosen = $null
    if ($TemplateDefinition) {
        $chosen = $references | Where-Object { $_.name -eq $TemplateDefinition } | Select-Object -First 1
        if (-not $chosen) {
            throw "Template definition '$TemplateDefinition' was not found in project '$script:ProjectName'."
        }
    }

    # Prefer a definition that is not itself a fixture, so a re-run does not copy from its own output.
    if (-not $chosen) {
        $chosen = $references | Where-Object { -not $_.name.StartsWith($script:Prefix, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    }
    if (-not $chosen) { $chosen = $references | Select-Object -First 1 }

    $full = Invoke-AdoApi -Uri "$script:ProjectUri/_apis/build/definitions/$($chosen.id)?api-version=$script:ApiVersion"

    if (-not $full.ContainsKey('repository') -or -not $full['repository']) {
        throw "Definition '$($chosen.name)' has no repository binding to copy."
    }

    Write-Host "Copying the repository binding from '$($chosen.name)' ($($full.repository.type))." -ForegroundColor DarkGray

    return $full.repository
}

function Get-HostedQueue {
    <#
    .SYNOPSIS
        Returns the Microsoft hosted agent queue the fixture definitions target.
    #>
    [CmdletBinding()]
    param()

    $queues = Get-AdoCollection -Uri "$script:ProjectUri/_apis/distributedtask/queues?api-version=$script:PreviewApiVersion"

    $queue = $queues | Where-Object { $_.name -eq 'Azure Pipelines' } | Select-Object -First 1
    if (-not $queue) { $queue = $queues | Select-Object -First 1 }
    if (-not $queue) { throw 'The fixture project has no agent queue.' }

    return $queue
}

function Get-InstalledTaskMap {
    <#
    .SYNOPSIS
        Builds a name to identifier map of the tasks installed in the organisation.
    .DESCRIPTION
        Task GUIDs are resolved at run time rather than hard coded, so the script keeps working when
        an organisation carries a different set of installed extensions.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param()

    $tasks = Get-AdoCollection -Uri "$script:OrgUri/_apis/distributedtask/tasks?api-version=$script:PreviewApiVersion"

    # The catalogue is not uniform, so anything missing a name, id or version is skipped rather than
    # allowed to throw under Set-StrictMode.
    $usable = $tasks | Where-Object {
        $_.ContainsKey('name') -and $_.ContainsKey('id') -and $_.ContainsKey('version') -and $_['version']
    }

    # Indexed by both name and friendlyName, because the two differ for several in-box tasks.
    $map = @{}
    foreach ($group in $usable | Group-Object -Property { $_['name'] }) {
        $newest = $group.Group |
            Sort-Object -Property { [int]$_['version']['major'] }, { [int]$_['version']['minor'] } |
            Select-Object -Last 1

        $entry = @{ Id = $newest['id']; VersionSpec = "$($newest['version']['major']).*" }
        $map[$group.Name] = $entry

        $friendly = if ($newest.ContainsKey('friendlyName')) { $newest['friendlyName'] } else { $null }
        if ($friendly -and -not $map.ContainsKey($friendly)) { $map[$friendly] = $entry }
    }

    if ($map.Count -eq 0) { throw 'No installed tasks were resolved, so no fixture step can be built.' }

    return $map
}

#endregion Discovery

#region Builders

function New-TaskStep {
    <#
    .SYNOPSIS
        Builds one classic step referencing an installed task.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][string]$DisplayName,
        [hashtable]$Inputs = @{},
        [bool]$Enabled = $true,
        [bool]$ContinueOnError = $false,
        [bool]$AlwaysRun = $false,
        [string]$Condition = 'succeeded()'
    )

    if (-not $script:Tasks.ContainsKey($TaskName)) {
        throw "Task '$TaskName' is not installed in this organisation, so the fixture cannot reference it."
    }

    $task = $script:Tasks[$TaskName]

    return @{
        environment      = @{}
        enabled          = $Enabled
        continueOnError  = $ContinueOnError
        alwaysRun        = $AlwaysRun
        displayName      = $DisplayName
        timeoutInMinutes = 0
        condition        = $Condition
        task             = @{ id = $task.Id; versionSpec = $task.VersionSpec; definitionType = 'task' }
        inputs           = $Inputs
    }
}

function New-TaskGroupStep {
    <#
    .SYNOPSIS
        Builds one classic step referencing a task group rather than a task.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)][string]$TaskGroupId,
        [Parameter(Mandatory)][string]$DisplayName,
        [hashtable]$Inputs = @{},
        [string]$VersionSpec = '1.*',
        [bool]$Enabled = $true,
        [string]$Condition = 'succeeded()'
    )

    return @{
        environment      = @{}
        enabled          = $Enabled
        continueOnError  = $false
        alwaysRun        = $false
        displayName      = $DisplayName
        timeoutInMinutes = 0
        condition        = $Condition
        task             = @{ id = $TaskGroupId; versionSpec = $VersionSpec; definitionType = 'metaTask' }
        inputs           = $Inputs
    }
}

function ConvertTo-WorkflowTask {
    <#
    .SYNOPSIS
        Rewrites a build step into the shape a release deploy phase expects.
    .DESCRIPTION
        The two are not interchangeable. A release workflow task carries taskId, version, name and
        definitionType at the top level, where a build step nests them under task.id,
        task.versionSpec, displayName and task.definitionType.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([Parameter(Mandatory)][hashtable]$Step)

    return @{
        environment      = @{}
        taskId           = $Step.task.id
        version          = $Step.task.versionSpec
        name             = $Step.displayName
        refName          = ''
        enabled          = $Step.enabled
        alwaysRun        = $Step.alwaysRun
        continueOnError  = $Step.continueOnError
        timeoutInMinutes = $Step.timeoutInMinutes
        definitionType   = $Step.task.definitionType
        overrideInputs   = @{}
        condition        = $Step.condition
        inputs           = $Step.inputs
    }
}

function New-AgentPhase {
    <#
    .SYNOPSIS
        Builds an agent phase, optionally depending on earlier phases.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$RefName,
        [Parameter(Mandatory)][array]$Steps,
        [string[]]$DependsOn = @(),
        [string]$Condition = 'succeeded()'
    )

    $phase = @{
        name                      = $Name
        refName                   = $RefName
        condition                 = $Condition
        jobAuthorizationScope     = 'projectCollection'
        jobCancelTimeoutInMinutes = 1
        steps                     = $Steps
        target                    = @{
            type                         = 1
            executionOptions             = @{ type = 0 }
            allowScriptsAuthAccessOption = $false
            queue                        = @{ id = $script:QueueId }
            demands                      = @()
        }
    }

    if ($DependsOn.Count -gt 0) {
        $phase.dependencies = @($DependsOn | ForEach-Object { @{ scope = $_; event = 'Completed' } })
    }

    return $phase
}

function New-ServerPhase {
    <#
    .SYNOPSIS
        Builds an agentless phase, which yamlizr currently skips.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$RefName,
        [Parameter(Mandatory)][array]$Steps,
        [string[]]$DependsOn = @()
    )

    $phase = @{
        name                      = $Name
        refName                   = $RefName
        condition                 = 'succeeded()'
        # Required on a server phase too, even though it has no agent to scope.
        jobAuthorizationScope     = 'projectCollection'
        jobCancelTimeoutInMinutes = 1
        steps                     = $Steps
        target                    = @{ type = 2; executionOptions = @{ type = 0 } }
    }

    if ($DependsOn.Count -gt 0) {
        $phase.dependencies = @($DependsOn | ForEach-Object { @{ scope = $_; event = 'Completed' } })
    }

    return $phase
}

#endregion Builders

#region Upserts

function Set-FixtureVariableGroup {
    <#
    .SYNOPSIS
        Creates or updates the shared variable group the fixture definitions link.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Name)

    $body = @{
        name                          = $Name
        description                   = 'Fixture variables. No value here is a real credential.'
        type                          = 'Vsts'
        variables                     = @{
            'fixture.group.plain'  = @{ value = 'group-plain-value' }
            'fixture.group.spaced' = @{ value = 'value with spaces and a $(fixture.plain) reference' }
            'fixture.group.secret' = @{ value = 'placeholder-not-a-credential'; isSecret = $true }
        }
        variableGroupProjectReferences = @(@{
                name             = $Name
                description      = 'Fixture variables.'
                projectReference = @{ id = $script:ProjectId; name = $script:ProjectName }
            })
    }

    $existing = Get-AdoCollection -Uri "$script:ProjectUri/_apis/distributedtask/variablegroups?groupName=$([uri]::EscapeDataString($Name))&api-version=$script:VariableGroupApiVersion" |
        Select-Object -First 1

    if ($existing) {
        if (-not $PSCmdlet.ShouldProcess($Name, 'Update variable group')) { return $existing }
        Write-Host "Updating variable group '$Name'." -ForegroundColor DarkGray
        return Invoke-AdoApi -Method Put -Body $body `
            -Uri "$script:ProjectUri/_apis/distributedtask/variablegroups/$($existing.id)?api-version=$script:VariableGroupApiVersion"
    }

    if (-not $PSCmdlet.ShouldProcess($Name, 'Create variable group')) { return $null }
    Write-Host "Creating variable group '$Name'." -ForegroundColor Cyan
    return Invoke-AdoApi -Method Post -Body $body `
        -Uri "$script:ProjectUri/_apis/distributedtask/variablegroups?api-version=$script:VariableGroupApiVersion"
}

function Set-FixtureTaskGroup {
    <#
    .SYNOPSIS
        Creates or updates a task group by name.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][array]$Tasks,
        [array]$Inputs = @()
    )

    $body = @{
        name               = $Name
        friendlyName       = $Name
        description        = $Description
        author             = 'yamlizr fixtures'
        category           = 'Build'
        iconUrl            = ''
        instanceNameFormat = "$Name"
        runsOn             = @('Agent', 'DeploymentGroup')
        inputs             = $Inputs
        tasks              = $Tasks
        version            = @{ major = 1; minor = 0; patch = 0; isTest = $false }
    }

    $existing = Get-AdoCollection -Uri "$script:ProjectUri/_apis/distributedtask/taskgroups?api-version=$script:PreviewApiVersion" |
        Where-Object { $_.name -eq $Name } |
        Select-Object -First 1

    if ($existing) {
        if (-not $PSCmdlet.ShouldProcess($Name, 'Update task group')) { return $existing }
        Write-Host "Updating task group '$Name'." -ForegroundColor DarkGray
        $body.id = $existing.id
        $body.revision = $existing.revision
        $body.version = $existing.version
        return Invoke-AdoApi -Method Put -Body $body `
            -Uri "$script:ProjectUri/_apis/distributedtask/taskgroups?api-version=$script:PreviewApiVersion"
    }

    if (-not $PSCmdlet.ShouldProcess($Name, 'Create task group')) { return $null }
    Write-Host "Creating task group '$Name'." -ForegroundColor Cyan
    return Invoke-AdoApi -Method Post -Body $body `
        -Uri "$script:ProjectUri/_apis/distributedtask/taskgroups?api-version=$script:PreviewApiVersion"
}

function Set-FixtureBuildDefinition {
    <#
    .SYNOPSIS
        Creates or updates a classic Build definition by name.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][array]$Phases,
        [hashtable]$Variables = @{},
        [int[]]$VariableGroupIds = @(),
        [array]$Triggers = @()
    )

    $body = @{
        name                  = $Name
        path                  = '\'
        type                  = 'build'
        quality               = 'definition'
        # The fixtures carry real triggers against a real repository, so they must never be queueable.
        queueStatus           = 'disabled'
        queue                 = @{ id = $script:QueueId }
        jobAuthorizationScope = 'projectCollection'
        jobTimeoutInMinutes   = 60
        repository            = $script:RepositoryReference
        process               = @{ type = 1; phases = $Phases }
        variables             = $Variables
        variableGroups        = @($VariableGroupIds | ForEach-Object { @{ id = $_ } })
        triggers              = $Triggers
        retentionRules        = @()
    }

    $existing = Get-AdoCollection -Uri "$script:ProjectUri/_apis/build/definitions?name=$([uri]::EscapeDataString($Name))&api-version=$script:ApiVersion" |
        Select-Object -First 1

    if ($existing) {
        if (-not $PSCmdlet.ShouldProcess($Name, 'Update build definition')) { return $existing }
        Write-Host "Updating build definition '$Name'." -ForegroundColor DarkGray
        $body.id = $existing.id
        $body.revision = $existing.revision
        return Invoke-AdoApi -Method Put -Body $body `
            -Uri "$script:ProjectUri/_apis/build/definitions/$($existing.id)?api-version=$script:ApiVersion"
    }

    if (-not $PSCmdlet.ShouldProcess($Name, 'Create build definition')) { return $null }
    Write-Host "Creating build definition '$Name'." -ForegroundColor Cyan
    return Invoke-AdoApi -Method Post -Body $body `
        -Uri "$script:ProjectUri/_apis/build/definitions?api-version=$script:ApiVersion"
}

function Set-FixtureReleaseDefinition {
    <#
    .SYNOPSIS
        Creates or updates a classic Release definition by name.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][array]$Environments,
        [array]$Artifacts = @(),
        [array]$Triggers = @(),
        [hashtable]$Variables = @{},
        [int[]]$VariableGroupIds = @()
    )

    $body = @{
        name              = $Name
        path              = '\'
        releaseNameFormat = 'Release-$(rev:r)'
        description       = 'yamlizr conversion fixture. Never deployed.'
        environments      = $Environments
        artifacts         = $Artifacts
        triggers          = $Triggers
        variables         = $Variables
        variableGroups    = $VariableGroupIds
        properties        = @{}
        tags              = @()
    }

    $existing = Get-AdoCollection -Uri "$script:VsrmProjectUri/_apis/release/definitions?searchText=$([uri]::EscapeDataString($Name))&api-version=$script:ApiVersion" |
        Where-Object { $_.name -eq $Name } |
        Select-Object -First 1

    if ($existing) {
        if (-not $PSCmdlet.ShouldProcess($Name, 'Update release definition')) { return $existing }
        Write-Host "Updating release definition '$Name'." -ForegroundColor DarkGray
        $current = Invoke-AdoApi -Uri "$script:VsrmProjectUri/_apis/release/definitions/$($existing.id)?api-version=$script:ApiVersion"
        $body.id = $current.id
        $body.revision = $current.revision
        return Invoke-AdoApi -Method Put -Body $body `
            -Uri "$script:VsrmProjectUri/_apis/release/definitions/$($current.id)?api-version=$script:ApiVersion"
    }

    if (-not $PSCmdlet.ShouldProcess($Name, 'Create release definition')) { return $null }
    Write-Host "Creating release definition '$Name'." -ForegroundColor Cyan
    return Invoke-AdoApi -Method Post -Body $body `
        -Uri "$script:VsrmProjectUri/_apis/release/definitions?api-version=$script:ApiVersion"
}

#endregion Upserts

#region Fixtures

$script:MultiLineBash = @'
set -euo pipefail

echo "First line of a multi-line script."
echo "Second line, which must survive as a literal block scalar."

for candidate in one two three; do
  echo "candidate: ${candidate}"
done

if [ -n "${BUILD_BUILDID:-}" ]; then
  echo "build ${BUILD_BUILDID}"
fi
'@

$script:MultiLinePowerShell = @'
$ErrorActionPreference = 'Stop'

Write-Host 'A multi-line PowerShell script.'
Write-Host "Trailing whitespace and a colon: value must not break the emitter."

@('alpha', 'beta') | ForEach-Object {
    Write-Host "item: $_"
}
'@

function New-FixtureTaskGroups {
    <#
    .SYNOPSIS
        Creates the leaf task group and the nested task group that calls it.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param()

    $leafInputs = @(
        @{ name = 'greeting'; label = 'Greeting'; defaultValue = 'hello from the default'; required = $false; type = 'string'; helpMarkDown = 'Has a default so an overriding step can be told apart.'; groupName = '' }
        @{ name = 'iterations'; label = 'Iterations'; defaultValue = '3'; required = $false; type = 'string'; helpMarkDown = ''; groupName = '' }
        @{ name = 'unusedByCaller'; label = 'Unused by caller'; defaultValue = 'never overridden'; required = $false; type = 'string'; helpMarkDown = ''; groupName = '' }
    )

    $leafTasks = @(
        New-TaskStep -TaskName 'Bash' -DisplayName 'Multi-line bash inside a task group' -Inputs @{
            targetType = 'inline'
            script     = "echo `"`$(greeting)`"`n" + $script:MultiLineBash
        }
        New-TaskStep -TaskName 'CmdLine' -DisplayName 'Disabled step inside a task group' -Enabled $false -Inputs @{
            script = 'echo This step is disabled and must be reported as such.'
        }
    )

    $leaf = Set-FixtureTaskGroup -Name $script:TaskGroupWithSpaces -Description 'Name contains spaces, referenced by more than one definition.' -Tasks $leafTasks -Inputs $leafInputs
    if (-not $leaf) { return $null }

    $nestedTasks = @(
        New-TaskGroupStep -TaskGroupId $leaf.id -DisplayName 'Nested call into the spaced task group' -Inputs @{
            greeting       = '$(outerGreeting)'
            iterations     = '2'
            unusedByCaller = 'never overridden'
        }
        New-TaskStep -TaskName 'PowerShell' -DisplayName 'Multi-line PowerShell after the nested call' -Inputs @{
            targetType = 'inline'
            script     = $script:MultiLinePowerShell
        }
    )

    $nestedInputs = @(
        @{ name = 'outerGreeting'; label = 'Outer greeting'; defaultValue = 'hello from the outer default'; required = $false; type = 'string'; helpMarkDown = ''; groupName = '' }
    )

    $nested = Set-FixtureTaskGroup -Name $script:NestedTaskGroupName -Description 'Calls another task group, to cover recursive expansion.' -Tasks $nestedTasks -Inputs $nestedInputs

    return @{ Leaf = $leaf; Nested = $nested }
}

function New-FixtureBuildDefinitions {
    <#
    .SYNOPSIS
        Creates every classic Build definition in the fixture.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [hashtable]$TaskGroups,
        [int]$VariableGroupId
    )

    $created = @{}

    # Phase-to-phase dependencies, including a fan-in.
    $created.MultiPhase = Set-FixtureBuildDefinition -Name "$($script:Prefix)multi-phase" -Phases @(
        New-AgentPhase -Name 'Phase one' -RefName 'Phase_1' -Steps @(
            New-TaskStep -TaskName 'CmdLine' -DisplayName 'Single line' -Inputs @{ script = 'echo phase one' }
        )
        New-AgentPhase -Name 'Phase two' -RefName 'Phase_2' -DependsOn @('Phase_1') -Steps @(
            New-TaskStep -TaskName 'Bash' -DisplayName 'Depends on phase one' -Inputs @{ targetType = 'inline'; script = 'echo phase two' }
        )
        New-AgentPhase -Name 'Phase three, fan-in' -RefName 'Phase_3' -DependsOn @('Phase_1', 'Phase_2') -Condition 'succeededOrFailed()' -Steps @(
            New-TaskStep -TaskName 'Bash' -DisplayName 'Runs even when an earlier phase failed' -AlwaysRun $true -Condition 'succeededOrFailed()' -Inputs @{ targetType = 'inline'; script = 'echo phase three' }
        )
    )

    # An agentless phase, which yamlizr skips, next to an agent phase which it does not.
    $created.ServerPhase = Set-FixtureBuildDefinition -Name "$($script:Prefix)server-phase" -Phases @(
        New-AgentPhase -Name 'Agent work' -RefName 'Phase_1' -Steps @(
            New-TaskStep -TaskName 'CmdLine' -DisplayName 'Agent step' -Inputs @{ script = 'echo agent' }
        )
        New-ServerPhase -Name 'Agentless wait' -RefName 'Phase_2' -DependsOn @('Phase_1') -Steps @(
            New-TaskStep -TaskName 'Delay' -DisplayName 'Delay' -Inputs @{ delayForMinutes = '0' }
        )
    )

    # Literal block scalar rendering.
    $created.MultiLine = Set-FixtureBuildDefinition -Name "$($script:Prefix)multiline-scripts" -Phases @(
        New-AgentPhase -Name 'Scripts' -RefName 'Phase_1' -Steps @(
            New-TaskStep -TaskName 'Bash' -DisplayName 'Multi-line bash' -Inputs @{ targetType = 'inline'; script = $script:MultiLineBash }
            New-TaskStep -TaskName 'PowerShell' -DisplayName 'Multi-line PowerShell' -Inputs @{ targetType = 'inline'; script = $script:MultiLinePowerShell }
            New-TaskStep -TaskName 'CmdLine' -DisplayName 'Multi-line cmd with trailing blank line' -Inputs @{ script = "echo one`necho two`n`n" }
            New-TaskStep -TaskName 'Bash' -DisplayName 'Script that is only whitespace' -Inputs @{ targetType = 'inline'; script = "   `n   `n" }
        )
    )

    # Task groups, including a nested one, an override, and a disabled task group call.
    if ($TaskGroups -and $TaskGroups.Leaf -and $TaskGroups.Nested) {
        $created.TaskGroups = Set-FixtureBuildDefinition -Name "$($script:Prefix)task-groups" -Phases @(
            New-AgentPhase -Name 'Task groups' -RefName 'Phase_1' -Steps @(
                New-TaskGroupStep -TaskGroupId $TaskGroups.Leaf.id -DisplayName 'Spaced task group, defaults kept' -Inputs @{
                    greeting       = 'hello from the default'
                    iterations     = '3'
                    unusedByCaller = 'never overridden'
                }
                New-TaskGroupStep -TaskGroupId $TaskGroups.Leaf.id -DisplayName 'Spaced task group, defaults overridden' -Inputs @{
                    greeting       = 'overridden by the caller'
                    iterations     = '7'
                    unusedByCaller = 'never overridden'
                }
                New-TaskGroupStep -TaskGroupId $TaskGroups.Nested.id -DisplayName 'Nested task group' -Inputs @{
                    outerGreeting = 'outer override'
                }
                New-TaskGroupStep -TaskGroupId $TaskGroups.Leaf.id -DisplayName 'Disabled task group call' -Enabled $false -Inputs @{
                    greeting       = 'never runs'
                    iterations     = '1'
                    unusedByCaller = 'never overridden'
                }
            )
        )
    }

    # Triggers, variables and a linked variable group.
    $triggers = @(
        @{
            triggerType                  = 'continuousIntegration'
            branchFilters                = @('+refs/heads/main', '-refs/heads/experimental/*')
            pathFilters                  = @('+/src', '-/docs')
            batchChanges                 = $true
            maxConcurrentBuildsPerBranch = 1
            settingsSourceType           = 1
        }
        @{
            triggerType                          = 'pullRequest'
            branchFilters                        = @('+refs/heads/main', '+refs/heads/release/*')
            pathFilters                          = @('+/src')
            forks                                = @{ enabled = $false; allowSecrets = $false }
            isCommentRequiredForPullRequest      = $false
            requireCommentsForNonTeamMembersOnly = $false
            settingsSourceType                   = 1
        }
        @{
            triggerType = 'schedule'
            schedules   = @(@{
                    branchFilters           = @('+refs/heads/main')
                    timeZoneId              = 'UTC'
                    startHours              = 3
                    startMinutes            = 30
                    # Flags enum, 31 is Monday through Friday.
                    daysToBuild             = 31
                    scheduleJobId           = [guid]::Empty.ToString()
                    scheduleOnlyWithChanges = $true
                })
        }
    )

    $variables = @{
        'fixture.plain'          = @{ value = 'definition-scoped value'; allowOverride = $false }
        'fixture.settableAtQueue' = @{ value = 'queue-time default'; allowOverride = $true }
        'fixture.secret'         = @{ value = 'placeholder-not-a-credential'; isSecret = $true; allowOverride = $false }
        'fixture.withReference'  = @{ value = 'prefix-$(fixture.plain)-suffix'; allowOverride = $false }
    }

    $created.Triggers = Set-FixtureBuildDefinition -Name "$($script:Prefix)triggers-and-variables" `
        -Variables $variables `
        -VariableGroupIds @($VariableGroupId) `
        -Triggers $triggers `
        -Phases @(
        New-AgentPhase -Name 'Report variables' -RefName 'Phase_1' -Steps @(
            New-TaskStep -TaskName 'Bash' -DisplayName 'Echo variables' -Inputs @{
                targetType = 'inline'
                script     = 'echo "$(fixture.plain) $(fixture.group.plain)"'
            }
            New-TaskStep -TaskName 'PublishBuildArtifacts' -DisplayName 'Publish drop' -Inputs @{
                PathtoPublish = '$(Build.ArtifactStagingDirectory)'
                ArtifactName  = 'drop'
                publishLocation = 'Container'
            }
        )
    )

    if ($IncludeUnknownTask) {
        Write-Host 'Adding the unknown task fixture. Azure DevOps may reject this.' -ForegroundColor Yellow
        $unknown = @{
            environment      = @{}
            enabled          = $true
            continueOnError  = $false
            alwaysRun        = $false
            displayName      = 'Step from an extension that is not installed'
            timeoutInMinutes = 0
            condition        = 'succeeded()'
            # Deliberately not a real task, to cover the null path reported in issue #177.
            task             = @{ id = '00000000-0000-0000-0000-0000000f0177'; versionSpec = '1.*'; definitionType = 'task' }
            inputs           = @{ someInput = 'value' }
        }

        try {
            $created.UnknownTask = Set-FixtureBuildDefinition -Name "$($script:Prefix)uninstalled-extension" -Phases @(
                New-AgentPhase -Name 'Uninstalled extension' -RefName 'Phase_1' -Steps @($unknown)
            )
        }
        catch {
            Write-Warning "Azure DevOps rejected the unknown task fixture, so it was skipped: $($_.Exception.Message)"
        }
    }

    return $created
}

function New-FixtureReleaseDefinition {
    <#
    .SYNOPSIS
        Creates the multi-stage release fixture, with approvals, artifacts and stage dependencies.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]$SourceBuildDefinition,
        [hashtable]$TaskGroups,
        [int]$VariableGroupId
    )

    $artifactAlias = 'fixture-drop'

    $artifacts = @(@{
            alias               = $artifactAlias
            type                = 'Build'
            sourceId            = "$script:ProjectId`:$($SourceBuildDefinition.id)"
            isPrimary           = $true
            isRetained          = $false
            definitionReference = @{
                project                = @{ id = $script:ProjectId; name = $script:ProjectName }
                definition             = @{ id = "$($SourceBuildDefinition.id)"; name = $SourceBuildDefinition.name }
                defaultVersionType     = @{ id = 'latestType'; name = 'Latest' }
                defaultVersionBranch   = @{ id = ''; name = '' }
                defaultVersionSpecific = @{ id = ''; name = '' }
                defaultVersionTags     = @{ id = ''; name = '' }
                IsMultiDefinitionType  = @{ id = 'False'; name = 'False' }
            }
        })

    $gates = @{ id = 0; gatesOptions = $null; gates = @() }
    if ($IncludeGates) {
        $sharedQuery = Get-FixtureSharedQuery
        if ($sharedQuery) {
            $gates = @{
                id           = 0
                gatesOptions = @{ isEnabled = $true; timeout = 60; samplingInterval = 5; stabilizationTime = 0; minimumSuccessDuration = 0 }
                gates        = @(@{
                        tasks = @(New-TaskStep -TaskName 'queryWorkItems' -DisplayName 'Query work items gate' -Inputs @{
                                queryId      = $sharedQuery.id
                                maxThreshold = '0'
                                minThreshold = '0'
                            })
                    })
            }
        }
        else {
            Write-Warning 'No shared work item query found, so the release fixture has no gate.'
        }
    }

    function New-Environment {
        param(
            [string]$Name,
            [int]$Rank,
            [array]$Tasks,
            [bool]$ManualApproval,
            [string]$AfterEnvironment,
            [hashtable]$Variables = @{},
            [hashtable]$PreDeploymentGates = @{ id = 0; gatesOptions = $null; gates = @() }
        )

        $conditions = if ($AfterEnvironment) {
            # conditionType 2 is "after stage", value 4 is "succeeded".
            @(@{ name = $AfterEnvironment; conditionType = 2; value = '4' })
        }
        else {
            @(@{ name = 'ReleaseStarted'; conditionType = 1; value = '' })
        }

        $preApprovals = if ($ManualApproval) {
            @(@{ rank = 1; isAutomated = $false; isNotificationOn = $true; approver = @{ id = $script:AuthenticatedUserId }; id = 0 })
        }
        else {
            @(@{ rank = 1; isAutomated = $true; isNotificationOn = $false; id = 0 })
        }

        # @() matters on every one of these. A single-element array collapses to a bare object when
        # it leaves an if statement, and Azure DevOps rejects the result as an empty collection.
        return @{
            name                = $Name
            rank                = $Rank
            owner               = @{ id = $script:AuthenticatedUserId }
            variables           = $Variables
            variableGroups      = @()
            conditions          = @($conditions)
            demands             = @()
            schedules           = @()
            environmentOptions  = @{
                emailNotificationType   = 'OnlyOnFailure'
                emailRecipients         = 'release.environment.owner;release.creator'
                skipArtifactsDownload   = $false
                timeoutInMinutes        = 0
                enableAccessToken       = $false
                publishDeploymentStatus = $true
                badgeEnabled            = $false
                autoLinkWorkItems       = $false
            }
            executionPolicy     = @{ concurrencyCount = 1; queueDepthCount = 0 }
            retentionPolicy     = @{ daysToKeep = 30; releasesToKeep = 3; retainBuild = $true }
            preDeployApprovals  = @{ approvals = @($preApprovals); approvalOptions = @{ releaseCreatorCanBeApprover = $true } }
            postDeployApprovals = @{ approvals = @(@{ rank = 1; isAutomated = $true; isNotificationOn = $false; id = 0 }) }
            preDeploymentGates  = $PreDeploymentGates
            postDeploymentGates = @{ id = 0; gatesOptions = $null; gates = @() }
            deployStep          = @{ id = 0 }
            deployPhases        = @(@{
                    rank            = 1
                    phaseType       = 'agentBasedDeployment'
                    name            = 'Agent job'
                    refName         = $null
                    workflowTasks   = @($Tasks | ForEach-Object { ConvertTo-WorkflowTask -Step $_ })
                    deploymentInput = @{
                        parallelExecution            = @{ parallelExecutionType = 'none' }
                        agentSpecification           = $null
                        skipArtifactsDownload        = $false
                        artifactsDownloadInput       = @{}
                        queueId                      = $script:QueueId
                        demands                      = @()
                        enableAccessToken            = $false
                        timeoutInMinutes             = 0
                        jobCancelTimeoutInMinutes    = 1
                        condition                    = 'succeeded()'
                        overrideInputs               = @{}
                    }
                })
        }
    }

    $devTasks = @(
        New-TaskStep -TaskName 'Bash' -DisplayName 'Multi-line deploy script' -Inputs @{ targetType = 'inline'; script = $script:MultiLineBash }
        New-TaskStep -TaskName 'CmdLine' -DisplayName 'Disabled release step' -Enabled $false -Inputs @{ script = 'echo disabled in a release' }
    )

    $testTasks = @(if ($TaskGroups -and $TaskGroups.Leaf) {
            New-TaskGroupStep -TaskGroupId $TaskGroups.Leaf.id -DisplayName 'Task group in a release' -Inputs @{
                greeting       = 'released'
                iterations     = '1'
                unusedByCaller = 'never overridden'
            }
        }
        else {
            New-TaskStep -TaskName 'CmdLine' -DisplayName 'Test stage' -Inputs @{ script = 'echo test' }
        })

    $prodTasks = @(
        New-TaskStep -TaskName 'PowerShell' -DisplayName 'Production script' -Inputs @{ targetType = 'inline'; script = $script:MultiLinePowerShell }
    )

    $environments = @(
        New-Environment -Name 'Dev' -Rank 1 -Tasks $devTasks -ManualApproval $false -Variables @{
            'fixture.stage' = @{ value = 'dev' }
        }
        New-Environment -Name 'Test' -Rank 2 -Tasks $testTasks -ManualApproval $true -AfterEnvironment 'Dev' -Variables @{
            'fixture.stage' = @{ value = 'test' }
        }
        New-Environment -Name 'Prod' -Rank 3 -Tasks $prodTasks -ManualApproval $true -AfterEnvironment 'Test' -PreDeploymentGates $gates -Variables @{
            'fixture.stage' = @{ value = 'prod' }
        }
    )

    $triggers = @(@{ triggerType = 1; artifactAlias = $artifactAlias; triggerConditions = @() })

    return Set-FixtureReleaseDefinition -Name "$($script:Prefix)release-multi-stage" `
        -Environments $environments `
        -Artifacts $artifacts `
        -Triggers $triggers `
        -VariableGroupIds @($VariableGroupId) `
        -Variables @{ 'fixture.release.plain' = @{ value = 'release scoped value' } }
}

function Get-FixtureSharedQuery {
    <#
    .SYNOPSIS
        Returns the first shared work item query, used by the optional release gate.
    #>
    [CmdletBinding()]
    param()

    try {
        $folder = Invoke-AdoApi -Uri "$script:ProjectUri/_apis/wit/queries/Shared%20Queries?`$depth=1&api-version=$script:ApiVersion"
        return $folder.children | Where-Object { -not $_.isFolder } | Select-Object -First 1
    }
    catch {
        Write-Verbose "Shared query lookup failed: $($_.Exception.Message)"
        return $null
    }
}

#endregion Fixtures

#region Main

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $script:OrgUri = $OrganisationUri.TrimEnd('/')
        $script:AuthorizationHeader = Get-AuthorizationHeader -Pat $Pat -EnvFile $EnvFile

        $connection = Invoke-AdoApi -Uri "$script:OrgUri/_apis/connectionData?api-version=$script:PreviewApiVersion"
        $script:AuthenticatedUserId = $connection.authenticatedUser.id
        Write-Host "Connected to $script:OrgUri." -ForegroundColor Green

        $projectObject = Get-FixtureProject -Name $Project

        $script:ProjectId = $projectObject.id
        $script:ProjectName = $projectObject.name
        $script:ProjectUri = "$script:OrgUri/$([uri]::EscapeDataString($script:ProjectName))"
        $script:VsrmProjectUri = "$(Get-VsrmUri -OrganisationUri $script:OrgUri)/$([uri]::EscapeDataString($script:ProjectName))"

        $script:RepositoryReference = Get-FixtureRepositoryReference -TemplateDefinition $TemplateDefinition

        $script:QueueId = (Get-HostedQueue).id
        $script:Tasks = Get-InstalledTaskMap
        Write-Verbose "Resolved $($script:Tasks.Count) installed tasks."

        $variableGroup = Set-FixtureVariableGroup -Name $script:VariableGroupName
        $variableGroupId = if ($variableGroup) { [int]$variableGroup.id } else { 0 }

        $taskGroups = New-FixtureTaskGroups
        $builds = New-FixtureBuildDefinitions -TaskGroups $taskGroups -VariableGroupId $variableGroupId

        if ($builds.ContainsKey('Triggers') -and $builds.Triggers) {
            New-FixtureReleaseDefinition -SourceBuildDefinition $builds.Triggers -TaskGroups $taskGroups -VariableGroupId $variableGroupId | Out-Null
        }
        else {
            Write-Warning 'No source build definition, so the release fixture was skipped.'
        }

        Write-Host "Fixtures prefixed '$script:Prefix' are up to date in project '$script:ProjectName'." -ForegroundColor Green
        Write-Host "Convert them with: yamlizr --filter $script:Prefix" -ForegroundColor Green
        exit 0
    }
    catch {
        Write-Error -ErrorAction Continue "Fixture setup failed: $($_.Exception.Message)"
        exit 1
    }
}

#endregion Main
