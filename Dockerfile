# syntax=docker/dockerfile:1
#
# Multi-architecture image, built from a single Dockerfile. Structure follows
# https://github.com/f2calv/multi-arch-container-dotnet
#
# A command-line tool: run with `docker run --rm`, arguments passed straight to
# yamlizr, generated YAML written to the /data volume.
#
# Shape: tool
#
# ------------------------------------------------------------------------------
# Stage 1 of 2: build
#
# Pinned to $BUILDPLATFORM and CROSS-COMPILES to $TARGETPLATFORM; emulating the
# target under QEMU instead is often an order of magnitude slower.
# ------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "./"]

ARG WORKLOAD=yamlizr
ARG PROJECT=src/CasCap.DevOpsYamlizrCli/CasCap.DevOpsYamlizrCli.csproj
ARG CONFIGURATION=Release
ARG TARGET_FRAMEWORK=net10.0
ARG VERSION=0.0.0-local
ARG GIT_REPOSITORY=n/a
ARG GIT_BRANCH=n/a
ARG GIT_COMMIT=n/a
ARG GITHUB_WORKFLOW=n/a
ARG GITHUB_RUN_ID=0
ARG GITHUB_RUN_NUMBER=0

# -- Dependency layer ----------------------------------------------------------
# Copy only what restore reads, so editing a .cs file reuses the cached restore.
# Restore is platform-agnostic, so it precedes TARGETARCH and is shared by every
# architecture. Configuration is passed because the CasCap.Common references are
# packages in Release and sibling projects in Debug. Every runtime identifier is
# restored here so each platform's publish runs offline with --no-restore.
COPY --parents src/**/*.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "$PROJECT" \
    -p:Configuration="$CONFIGURATION" \
    "-p:RuntimeIdentifiers=\"linux-x64;linux-arm64;linux-arm\""

# -- Compile layer -------------------------------------------------------------
# The test project is excluded so a test edit never invalidates the publish layer.
COPY --exclude=src/*.Tests . .

# buildx injects TARGETARCH/TARGETVARIANT automatically:
#   linux/amd64 -> amd64, linux/arm64 -> arm64, linux/arm/v7 -> arm + v7
# Concatenating the two gives a single flat token to switch on. The publish only
# reads packages the restore already wrote, so the platform legs share the cache
# and need no network.
ARG TARGETARCH
ARG TARGETVARIANT
RUN --network=none --mount=type=cache,target=/root/.nuget/packages,sharing=shared <<EOF
set -eux
# https://learn.microsoft.com/dotnet/core/rid-catalog
case "${TARGETARCH}${TARGETVARIANT}" in
    amd64) RID=linux-x64   ;;
    arm64) RID=linux-arm64 ;;
    armv7) RID=linux-arm   ;;
    *) echo "unsupported platform: linux/${TARGETARCH}/${TARGETVARIANT}" >&2; exit 1 ;;
esac
dotnet publish "$PROJECT" \
    --configuration "$CONFIGURATION" \
    --framework "$TARGET_FRAMEWORK" \
    --output /app/publish \
    --runtime "$RID" \
    --self-contained false \
    --no-restore \
    -p:Version="$VERSION" \
    -p:SourceRevisionId="$GIT_COMMIT" \
    -p:GitRepository="$GIT_REPOSITORY" \
    -p:GitBranch="$GIT_BRANCH" \
    -p:BuildWorkflow="$GITHUB_WORKFLOW" \
    -p:BuildRunId="$GITHUB_RUN_ID" \
    -p:BuildRunNumber="$GITHUB_RUN_NUMBER"
ln -s "$WORKLOAD.dll" /app/publish/entrypoint.dll
# The chiselled runtime has no shell to create the volume directory, so it is
# created here and copied across owned by the runtime user.
install -d /state/data
EOF

# ------------------------------------------------------------------------------
# Optional stage: test
#
# Local development only. `final` does not depend on this stage, so BuildKit skips
# it unless it is requested explicitly:
#
#   docker buildx build --target test --progress=plain .
#
# Pinned to $BUILDPLATFORM because test assemblies are architecture-neutral, so
# the tests run natively without emulation. Only credential-free tests run:
# Category=Integration is filtered out, and the network is disabled so a test
# that reaches an external service fails instead of silently depending on it.
# global.json selects Microsoft.Testing.Platform, which `dotnet test --project`
# and the trait filters require.
# ------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS test
WORKDIR /src

ARG TEST_PROJECT=src/CasCap.Api.AzureDevOps.Tests/CasCap.Api.AzureDevOps.Tests.csproj
ARG CONFIGURATION=Release
ARG TARGET_FRAMEWORK=net10.0

# -- Dependency layer ----------------------------------------------------------
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY --parents src/**/*.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "$TEST_PROJECT" -p:Configuration="$CONFIGURATION"

# -- Test layer ----------------------------------------------------------------
# The SDK image ships only the .NET 10 runtime, so the multi-targeted test
# project runs for net10.0 here; run the other target frameworks on the host.
COPY . .
RUN --network=none --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet test --project "$TEST_PROJECT" \
    --configuration "$CONFIGURATION" \
    --framework "$TARGET_FRAMEWORK" \
    --no-restore \
    --filter-not-trait "Category=Integration"

# ------------------------------------------------------------------------------
# Stage 2 of 2: final
#
# No --platform override, so buildx resolves the base image for $TARGETPLATFORM
# and the result is genuinely native to the target.
#
# aspnet, not runtime: Microsoft.Extensions.Http.Resilience arrives through
# CasCap.Common.Net and carries a FrameworkReference to Microsoft.AspNetCore.App,
# so the published runtimeconfig.json requires that shared framework even though
# this is a console application. On the runtime image it fails at startup with
# "No frameworks were found".
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --link --from=build /app/publish .

# Generated YAML is written here; mount a host directory over it. The directory is
# owned by the runtime user, otherwise an anonymous volume is created root-owned
# and the non-root process cannot write to it.
COPY --link --from=build --chown=$APP_UID:$APP_UID /state/data /data
VOLUME /data

# -- Provenance ----------------------------------------------------------------
# Supplied by the CI workflow (.github/workflows/ci.yml).
ARG WORKLOAD=yamlizr
ARG GIT_REPOSITORY=n/a
ARG GIT_BRANCH=n/a
ARG GIT_COMMIT=n/a
ARG GIT_TAG=n/a

ARG GITHUB_WORKFLOW=n/a
ARG GITHUB_RUN_ID=0
ARG GITHUB_RUN_NUMBER=0

# https://github.com/opencontainers/image-spec/blob/main/annotations.md
LABEL org.opencontainers.image.title="$WORKLOAD" \
    org.opencontainers.image.description="Azure DevOps Classic Designer-to-YAML pipeline conversion tool" \
    org.opencontainers.image.source="https://github.com/f2calv/yamlizr" \
    org.opencontainers.image.licenses="MIT" \
    org.opencontainers.image.version="$GIT_TAG" \
    org.opencontainers.image.revision="$GIT_COMMIT"

# $APP_UID (1654) is defined by the .NET base images. Chiseled images already run
# as this user; setting it explicitly documents the intent.
USER $APP_UID

ENTRYPOINT ["dotnet", "entrypoint.dll"]

# ------------------------------------------------------------------------------
# Stage 4 of 4: final
# ------------------------------------------------------------------------------
FROM runtime AS final
