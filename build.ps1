#!/usr/bin/env pwsh
#Requires -Version 7.4
<#
.SYNOPSIS
    Builds the yamlizr container image locally, and optionally publishes it to ghcr.io.

.DESCRIPTION
    Mirrors the image job in .github/workflows/ci.yml, so a Dockerfile change can be verified before
    it reaches CI.

    A local build targets one platform and uses --load, so the image lands in the local engine and
    can actually be run. That matters here: 3.1.6 shipped an image that built cleanly and then
    exited with "No frameworks were found", so the build finishes by running the image unless
    -SkipSmokeTest is passed.

    A push build targets every published platform. A multi-architecture manifest cannot be loaded
    into the local engine, so --push replaces --load and the smoke test is skipped.

.PARAMETER Push
    Authenticate to ghcr.io through the gh CLI and publish the image instead of building locally.

.PARAMETER Configuration
    Release builds Dockerfile. Debug builds Dockerfile.Debug, which compiles against the local
    sibling CasCap.Common repository, mirrored into deps/ first because a build context cannot reach
    outside the repository.

.PARAMETER Tag
    Image tag override. Defaults to the GitVersion semantic version when -Push, otherwise
    "latest-dev".

.PARAMETER Version
    Version compiled into the assembly. Must be a valid semantic version, so it is resolved
    separately from the image tag, which may be a moving label such as "latest-dev".

.PARAMETER Platforms
    Target platform list. Defaults to every published platform when -Push, otherwise the host
    architecture alone, because --load accepts a single platform and a cross-built image is slow.

.PARAMETER ImageName
    Image repository name under the registry.

.PARAMETER SkipSmokeTest
    Skip running the built image. The smoke test is skipped automatically for a push build.

.EXAMPLE
    ./build.ps1

    Builds for the host architecture, loads the image, then runs it to prove it starts.

.EXAMPLE
    ./build.ps1 -Platforms linux/amd64,linux/arm64,linux/arm/v7 -SkipSmokeTest

    Cross-builds every published platform without loading or running anything.

.EXAMPLE
    ./build.ps1 -Push

.NOTES
    Requires Docker with buildx. Pushing also requires the gh CLI, and tag resolution uses
    GitVersion.Tool, which is installed on demand.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Push,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Tag,

    [string]$Version,

    [string]$Platforms,

    [ValidateNotNullOrEmpty()]
    [string]$ImageName = 'yamlizr',

    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

# docker and gh report their own failures; letting PowerShell also throw doubles the noise.
$PSNativeCommandUseErrorActionPreference = $false

$REGISTRY = 'ghcr.io/f2calv'
$REPO_ROOT = [IO.Path]::GetFullPath($PSScriptRoot)
$BUILDER_NAME = 'yamlizr1'
$ALL_PLATFORMS = 'linux/amd64,linux/arm64,linux/arm/v7'

# Mirrored into deps/ for a Debug build, so a fix can be verified before those repos are published.
$DEP_REPOS = @('CasCap.Common')

$GIT_REPOSITORY = $REPO_ROOT | Split-Path -Leaf
$GIT_BRANCH = "$(git -C $REPO_ROOT branch --show-current)".Trim()
$GIT_COMMIT = "$(git -C $REPO_ROOT rev-parse HEAD)".Trim()

$GITHUB_WORKFLOW = 'local'
$GITHUB_RUN_ID = 0
$GITHUB_RUN_NUMBER = 0

#region Resolution

function Install-GitVersion {
    <#
    .SYNOPSIS
        Ensures dotnet-gitversion is on PATH, installing it when it is missing.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param()

    if (Get-Command dotnet-gitversion -ErrorAction SilentlyContinue) { return $true }

    Write-Host 'dotnet-gitversion not found, installing GitVersion.Tool globally.' -ForegroundColor Cyan
    dotnet tool install -g GitVersion.Tool
    if ($LASTEXITCODE -ne 0) { return $false }

    $toolsPath = Join-Path $HOME '.dotnet/tools'
    if ($env:PATH -notlike "*$toolsPath*") {
        $env:PATH = "$toolsPath$([IO.Path]::PathSeparator)$env:PATH"
    }

    return [bool](Get-Command dotnet-gitversion -ErrorAction SilentlyContinue)
}

function Resolve-Version {
    <#
    .SYNOPSIS
        Returns the semantic version compiled into the assembly.
    .DESCRIPTION
        Kept separate from the image tag. The Dockerfile feeds this to dotnet publish as
        -p:Version, which rejects a moving label such as "latest-dev".
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([bool]$Required)

    if ($Version) { return $Version }

    if (Install-GitVersion) {
        $resolved = "$(dotnet-gitversion $REPO_ROOT /showvariable SemVer)".Trim()
        if (-not [string]::IsNullOrWhiteSpace($resolved)) { return $resolved }
    }

    if ($Required) {
        throw 'Could not resolve a version from GitVersion, which a push build requires. Pass -Version.'
    }

    # Matches the Dockerfile default, so a local build without GitVersion still succeeds.
    Write-Warning 'Could not resolve a version from GitVersion, falling back to 0.0.1.'
    return '0.0.1'
}

function Resolve-Platforms {
    <#
    .SYNOPSIS
        Returns the platform list to build for.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    if ($Platforms) { return $Platforms }
    if ($Push) { return $ALL_PLATFORMS }

    # --load accepts one platform only, and emulating another is far slower.
    switch ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
        'Arm64' { return 'linux/arm64' }
        'Arm' { return 'linux/arm/v7' }
        default { return 'linux/amd64' }
    }
}

function Connect-Ghcr {
    <#
    .SYNOPSIS
        Authenticates the local Docker client to ghcr.io using the gh CLI token.
    #>
    [CmdletBinding()]
    param()

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'gh CLI not found. Install it from https://cli.github.com'
    }

    if (-not (gh auth status 2>&1 | Select-String -SimpleMatch 'write:packages')) {
        Write-Host 'Refreshing gh auth to add the write:packages scope.' -ForegroundColor Cyan
        gh auth refresh -h github.com -s write:packages
        if ($LASTEXITCODE -ne 0) { throw 'gh auth refresh failed.' }
    }

    $ghUser = "$(gh api user --jq .login)".Trim()
    Write-Host "Authenticating Docker to ghcr.io as $ghUser." -ForegroundColor Cyan

    gh auth token | docker login ghcr.io -u $ghUser --password-stdin
    if ($LASTEXITCODE -ne 0) { throw 'docker login ghcr.io failed.' }
}

#endregion Resolution

#region Build

function Initialize-Builder {
    <#
    .SYNOPSIS
        Selects the buildx builder, creating it on first use.
    #>
    [CmdletBinding()]
    param()

    docker buildx inspect $BUILDER_NAME *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating buildx builder '$BUILDER_NAME'." -ForegroundColor Cyan
        docker buildx create --name $BUILDER_NAME | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker buildx create failed for '$BUILDER_NAME'." }
    }

    docker buildx use $BUILDER_NAME
    if ($LASTEXITCODE -ne 0) { throw "docker buildx use failed for '$BUILDER_NAME'." }
}

function Invoke-SmokeTest {
    <#
    .SYNOPSIS
        Runs the built image, because a successful build does not prove the image starts.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Image,
        [Parameter(Mandatory)][string]$ExpectedVersion
    )

    Write-Host 'Running the image to prove it starts.' -ForegroundColor Cyan

    $reported = "$(docker run --rm $Image --version)".Trim()
    if ($LASTEXITCODE -ne 0) { throw 'docker run --version failed, so the image does not start.' }

    # SourceLink may append +<sha> to the informational version, so match the prefix.
    if (-not $reported.StartsWith($ExpectedVersion)) {
        throw "Expected a version starting with '$ExpectedVersion', got '$reported'."
    }
    Write-Host "  reported version: $reported" -ForegroundColor DarkGray

    docker run --rm $Image generate --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker run generate --help failed.' }
    Write-Host '  generate --help: ok' -ForegroundColor DarkGray
}

function Sync-Deps {
    <#
    .SYNOPSIS
        Mirrors the sibling repositories a Debug build compiles against into deps/.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param()

    $parent = Split-Path $REPO_ROOT -Parent

    foreach ($repo in $DEP_REPOS) {
        $source = Join-Path $parent $repo
        if (-not (Test-Path -LiteralPath $source)) {
            throw "A Debug build needs the sibling repository '$repo' at '$source', which was not found. Use -Configuration Release instead."
        }

        $destination = Join-Path (Join-Path $REPO_ROOT 'deps') $repo
        if (-not $PSCmdlet.ShouldProcess($repo, 'Mirror sibling repository into deps/')) { continue }

        Write-Host "Syncing $repo -> deps/$repo" -ForegroundColor Cyan
        robocopy $source $destination /MIR `
            /XD bin obj .git .vs node_modules deps `
            /XF 'appsettings.Local*.json' '*.user' `
            /NFL /NDL /NJH /NJS /NP | Out-Null

        # robocopy uses exit codes below 8 to report work done, not failure.
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $repo with exit code $LASTEXITCODE." }
    }

    $global:LASTEXITCODE = 0
}

function Invoke-Build {
    <#
    .SYNOPSIS
        Builds, and optionally publishes, the container image.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param()

    $versionValue = Resolve-Version -Required:$Push
    $tagValue = if ($Tag) { $Tag.ToLower() } elseif ($Push) { $versionValue.ToLower() } else { 'latest-dev' }
    $platformValue = Resolve-Platforms
    $image = "$REGISTRY/$($ImageName.ToLower()):$tagValue"
    $dockerfile = if ($Configuration -eq 'Debug') { 'Dockerfile.Debug' } else { 'Dockerfile' }

    if ($Push -and $Configuration -eq 'Debug') {
        throw 'A Debug image is built against unpublished local sources and must not be pushed.'
    }

    if ($Push -and -not $PSCmdlet.ShouldProcess($image, 'Publish container image to ghcr.io')) { return }

    Write-Host "Image     : $image" -ForegroundColor Cyan
    Write-Host "Version   : $versionValue" -ForegroundColor Cyan
    Write-Host "Platforms : $platformValue" -ForegroundColor Cyan
    Write-Host "Dockerfile: $dockerfile" -ForegroundColor Cyan

    if ($Configuration -eq 'Debug') { Sync-Deps }
    if ($Push) { Connect-Ghcr }
    Initialize-Builder

    # A multi-architecture manifest cannot be loaded into the local engine.
    $outputArg = if ($Push) { '--push' } elseif ($platformValue.Contains(',')) { '--pull' } else { '--load' }

    docker buildx build `
        --tag $image `
        --file (Join-Path $REPO_ROOT $dockerfile) `
        --build-arg VERSION=$versionValue `
        --build-arg CONFIGURATION=$Configuration `
        --build-arg GIT_REPOSITORY=$GIT_REPOSITORY `
        --build-arg GIT_BRANCH=$GIT_BRANCH `
        --build-arg GIT_COMMIT=$GIT_COMMIT `
        --build-arg GIT_TAG=$tagValue `
        --build-arg GITHUB_WORKFLOW=$GITHUB_WORKFLOW `
        --build-arg GITHUB_RUN_ID=$GITHUB_RUN_ID `
        --build-arg GITHUB_RUN_NUMBER=$GITHUB_RUN_NUMBER `
        --platform $platformValue `
        $outputArg `
        $REPO_ROOT

    if ($LASTEXITCODE -ne 0) { throw "docker buildx build failed with exit code $LASTEXITCODE." }

    if ($Push) {
        Write-Host "Pushed: $image" -ForegroundColor Green
        return
    }

    if ($outputArg -eq '--load' -and -not $SkipSmokeTest) {
        Invoke-SmokeTest -Image $image -ExpectedVersion $versionValue
    }

    Write-Host "Built (not pushed): $image" -ForegroundColor Green
}

#endregion Build

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-Build
        exit 0
    }
    catch {
        Write-Error -ErrorAction Continue "build failed: $($_.Exception.Message)"
        exit 1
    }
}
