# syntax=docker/dockerfile:1
#
# Multi-architecture image, built from a single Dockerfile. Structure follows
# https://github.com/f2calv/multi-arch-container-dotnet
#
# ------------------------------------------------------------------------------
# Stage 1 of 2: build
#
# Pinned to $BUILDPLATFORM and CROSS-COMPILES to $TARGETPLATFORM; emulating the
# target under QEMU instead is typically 10-50x slower.
# ------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG PROJECT=src/CasCap.DevOpsYamlizrCli/CasCap.DevOpsYamlizrCli.csproj
ARG CONFIGURATION=Release
ARG TARGET_FRAMEWORK=net10.0
ARG VERSION=0.0.1

# -- Dependency layer ----------------------------------------------------------
# Copy only what restore reads, so editing a .cs file reuses the cached restore.
# Restore is platform-agnostic, so it precedes TARGETARCH and is shared by every
# architecture. Configuration is passed because the CasCap.Common references are
# packages in Release and sibling projects in Debug.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/CasCap.Api.AzureDevOps/CasCap.Api.AzureDevOps.csproj src/CasCap.Api.AzureDevOps/
COPY src/CasCap.DevOpsYamlizrCli/CasCap.DevOpsYamlizrCli.csproj src/CasCap.DevOpsYamlizrCli/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "$PROJECT" -p:Configuration="$CONFIGURATION"

# -- Compile layer -------------------------------------------------------------
COPY . .

# buildx injects TARGETARCH/TARGETVARIANT automatically:
#   linux/amd64 -> amd64, linux/arm64 -> arm64, linux/arm/v7 -> arm + v7
# Concatenating the two gives a single flat token to switch on.
ARG TARGETARCH
ARG TARGETVARIANT
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked <<EOF
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
    --runtime "$RID" \
    --self-contained false \
    -p:Version="$VERSION" \
    --output /out
EOF

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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /out .

# Generated YAML is written here; mount a host directory over it.
VOLUME /data

# -- Provenance ----------------------------------------------------------------
# Supplied by the CI workflow (.github/workflows/ci.yml).
ARG GIT_REPOSITORY=n/a
ENV GIT_REPOSITORY=$GIT_REPOSITORY
ARG GIT_BRANCH=n/a
ENV GIT_BRANCH=$GIT_BRANCH
ARG GIT_COMMIT=n/a
ENV GIT_COMMIT=$GIT_COMMIT
ARG GIT_TAG=n/a
ENV GIT_TAG=$GIT_TAG

ARG GITHUB_WORKFLOW=n/a
ENV GITHUB_WORKFLOW=$GITHUB_WORKFLOW
ARG GITHUB_RUN_ID=0
ENV GITHUB_RUN_ID=$GITHUB_RUN_ID
ARG GITHUB_RUN_NUMBER=0
ENV GITHUB_RUN_NUMBER=$GITHUB_RUN_NUMBER

# https://github.com/opencontainers/image-spec/blob/main/annotations.md
LABEL org.opencontainers.image.title="yamlizr" \
    org.opencontainers.image.description="Azure DevOps Classic Designer-to-YAML pipeline conversion tool" \
    org.opencontainers.image.source="https://github.com/f2calv/yamlizr" \
    org.opencontainers.image.licenses="MIT" \
    org.opencontainers.image.version="$GIT_TAG" \
    org.opencontainers.image.revision="$GIT_COMMIT"

# $APP_UID (1654) is defined by the .NET base images. Chiseled images already run
# as this user; setting it explicitly documents the intent.
USER $APP_UID

ENTRYPOINT ["dotnet", "yamlizr.dll"]
