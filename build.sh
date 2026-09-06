#!/usr/bin/env bash
#
# Builds the yamlizr container image locally, and optionally publishes it to ghcr.io.
#
# Mirrors the image job in .github/workflows/ci.yml, so a Dockerfile change can be verified
# before it reaches CI. A local build targets one platform and uses --load, so the image lands
# in the local engine and is then run: 3.1.6 shipped an image that built cleanly and exited with
# "No frameworks were found", so building is not evidence that it starts.
#
# Usage:
#   ./build.sh                      build for the host architecture, load it, run it
#   ./build.sh --push               build every published platform and publish to ghcr.io
#   ./build.sh --platforms linux/amd64,linux/arm64 --skip-smoke-test
#   ./build.sh --tag my-test --configuration Debug
#
# Requires Docker with buildx. Pushing also requires the gh CLI, and tag resolution uses
# GitVersion.Tool, which is installed on demand.

set -euo pipefail

REGISTRY="ghcr.io/f2calv"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILDER_NAME="yamlizr1"
ALL_PLATFORMS="linux/amd64,linux/arm64,linux/arm/v7"

# Mirrored into deps/ for a Debug build, so a fix can be verified before those repos are published.
DEP_REPOS=("CasCap.Common")

PUSH=false
CONFIGURATION="Release"
IMAGE_NAME="yamlizr"
SKIP_SMOKE_TEST=false
TAG=""
VERSION=""
PLATFORMS=""

usage() {
  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --push) PUSH=true; shift ;;
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    --tag) TAG="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --platforms) PLATFORMS="$2"; shift 2 ;;
    --image-name) IMAGE_NAME="$2"; shift 2 ;;
    --skip-smoke-test) SKIP_SMOKE_TEST=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
  esac
done

if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
  echo "--configuration must be Debug or Release, got '$CONFIGURATION'." >&2
  exit 1
fi

GIT_REPOSITORY="$(basename "$(git -C "$REPO_ROOT" rev-parse --show-toplevel)")"
GIT_BRANCH="$(git -C "$REPO_ROOT" branch --show-current)"
GIT_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD)"

GITHUB_WORKFLOW="local"
GITHUB_RUN_ID=0
GITHUB_RUN_NUMBER=0

install_gitversion() {
  if command -v dotnet-gitversion >/dev/null 2>&1; then
    return 0
  fi

  echo "dotnet-gitversion not found, installing GitVersion.Tool globally."
  dotnet tool install -g GitVersion.Tool >/dev/null 2>&1 || return 1
  export PATH="$HOME/.dotnet/tools:$PATH"

  command -v dotnet-gitversion >/dev/null 2>&1
}

# The version compiled into the assembly is resolved separately from the image tag. The Dockerfile
# feeds it to dotnet publish as -p:Version, which rejects a moving label such as "latest-dev".
resolve_version() {
  if [[ -n "$VERSION" ]]; then
    echo "$VERSION"
    return 0
  fi

  local resolved=""
  if install_gitversion; then
    resolved="$(dotnet-gitversion "$REPO_ROOT" /showvariable SemVer 2>/dev/null | tr -d '[:space:]')"
  fi

  if [[ -n "$resolved" ]]; then
    echo "$resolved"
    return 0
  fi

  if [[ "$PUSH" == true ]]; then
    echo "Could not resolve a version from GitVersion, which a push build requires. Pass --version." >&2
    return 1
  fi

  # Matches the Dockerfile default, so a local build without GitVersion still succeeds.
  echo "Could not resolve a version from GitVersion, falling back to 0.0.1." >&2
  echo "0.0.1"
}

resolve_platforms() {
  if [[ -n "$PLATFORMS" ]]; then
    echo "$PLATFORMS"
    return 0
  fi

  if [[ "$PUSH" == true ]]; then
    echo "$ALL_PLATFORMS"
    return 0
  fi

  # --load accepts one platform only, and emulating another is far slower.
  case "$(uname -m)" in
    aarch64|arm64) echo "linux/arm64" ;;
    armv7l|armv6l) echo "linux/arm/v7" ;;
    *) echo "linux/amd64" ;;
  esac
}

# Debug compiles against the local sibling repositories, which a build context cannot reach, so they
# are mirrored into deps/ first.
sync_deps() {
  local parent repo source destination
  parent="$(dirname "$REPO_ROOT")"

  for repo in "${DEP_REPOS[@]}"; do
    source="${parent}/${repo}"
    if [[ ! -d "$source" ]]; then
      echo "A Debug build needs the sibling repository '${repo}' at '${source}', which was not found. Use --configuration Release instead." >&2
      exit 1
    fi

    destination="${REPO_ROOT}/deps/${repo}"
    echo "Syncing ${repo} -> deps/${repo}"
    mkdir -p "$destination"

    if command -v rsync >/dev/null 2>&1; then
      rsync -a --delete \
        --exclude 'bin/' --exclude 'obj/' --exclude '.git/' --exclude '.vs/' \
        --exclude 'node_modules/' --exclude 'deps/' \
        --exclude 'appsettings.Local*.json' --exclude '*.user' \
        "${source}/" "${destination}/"
    else
      rm -rf "${destination:?}"
      mkdir -p "$destination"
      tar -C "$source" \
        --exclude='./bin' --exclude='./obj' --exclude='./.git' --exclude='./.vs' \
        --exclude='./node_modules' --exclude='./deps' \
        --exclude='appsettings.Local*.json' --exclude='*.user' \
        -cf - . | tar -C "$destination" -xf -
    fi
  done
}

connect_ghcr() {
  if ! command -v gh >/dev/null 2>&1; then
    echo "gh CLI not found. Install it from https://cli.github.com" >&2
    exit 1
  fi

  if ! gh auth status 2>&1 | grep -q "write:packages"; then
    echo "Refreshing gh auth to add the write:packages scope."
    gh auth refresh -h github.com -s write:packages
  fi

  local gh_user
  gh_user="$(gh api user --jq .login)"
  echo "Authenticating Docker to ghcr.io as ${gh_user}."
  gh auth token | docker login ghcr.io -u "$gh_user" --password-stdin
}

initialize_builder() {
  docker buildx inspect "$BUILDER_NAME" >/dev/null 2>&1 \
    || docker buildx create --name "$BUILDER_NAME" >/dev/null
  docker buildx use "$BUILDER_NAME"
}

# A successful build does not prove the image starts.
smoke_test() {
  local image="$1"
  local expected_version="$2"

  echo "Running the image to prove it starts."

  local reported
  reported="$(docker run --rm "$image" --version | tr -d '\r')"

  # SourceLink may append +<sha> to the informational version, so match the prefix.
  case "$reported" in
    "${expected_version}"*) ;;
    *) echo "Expected a version starting with '${expected_version}', got '${reported}'." >&2; exit 1 ;;
  esac
  echo "  reported version: ${reported}"

  docker run --rm "$image" generate --help >/dev/null
  echo "  generate --help: ok"
}

main() {
  local version tag platforms image output_arg dockerfile
  version="$(resolve_version)"
  platforms="$(resolve_platforms)"

  if [[ "$CONFIGURATION" == "Debug" ]]; then
    dockerfile="Dockerfile.Debug"
  else
    dockerfile="Dockerfile"
  fi

  if [[ "$PUSH" == true && "$CONFIGURATION" == "Debug" ]]; then
    echo "A Debug image is built against unpublished local sources and must not be pushed." >&2
    exit 1
  fi

  if [[ -n "$TAG" ]]; then
    tag="${TAG,,}"
  elif [[ "$PUSH" == true ]]; then
    tag="${version,,}"
  else
    tag="latest-dev"
  fi

  image="${REGISTRY}/${IMAGE_NAME,,}:${tag}"

  echo "Image     : ${image}"
  echo "Version   : ${version}"
  echo "Platforms : ${platforms}"
  echo "Dockerfile: ${dockerfile}"

  if [[ "$CONFIGURATION" == "Debug" ]]; then
    sync_deps
  fi
  if [[ "$PUSH" == true ]]; then
    connect_ghcr
  fi
  initialize_builder

  # A multi-architecture manifest cannot be loaded into the local engine.
  if [[ "$PUSH" == true ]]; then
    output_arg="--push"
  elif [[ "$platforms" == *","* ]]; then
    output_arg="--pull"
  else
    output_arg="--load"
  fi

  docker buildx build \
    --tag "$image" \
    --file "${REPO_ROOT}/${dockerfile}" \
    --build-arg VERSION="$version" \
    --build-arg CONFIGURATION="$CONFIGURATION" \
    --build-arg GIT_REPOSITORY="$GIT_REPOSITORY" \
    --build-arg GIT_BRANCH="$GIT_BRANCH" \
    --build-arg GIT_COMMIT="$GIT_COMMIT" \
    --build-arg GIT_TAG="$tag" \
    --build-arg GITHUB_WORKFLOW="$GITHUB_WORKFLOW" \
    --build-arg GITHUB_RUN_ID="$GITHUB_RUN_ID" \
    --build-arg GITHUB_RUN_NUMBER="$GITHUB_RUN_NUMBER" \
    --platform "$platforms" \
    "$output_arg" \
    "$REPO_ROOT"

  if [[ "$PUSH" == true ]]; then
    echo "Pushed: ${image}"
    return 0
  fi

  if [[ "$output_arg" == "--load" && "$SKIP_SMOKE_TEST" == false ]]; then
    smoke_test "$image" "$version"
  fi

  echo "Built (not pushed): ${image}"
}

main "$@"
