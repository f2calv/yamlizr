---
description: 'Dockerfile conventions — multi-architecture builds, stage structure, layer caching, provenance, image hardening.'
applyTo: '**/Dockerfile,**/Dockerfile.*,**/*.dockerfile,**/.dockerignore'
---

# Dockerfiles

These conventions describe the single-file, multi-architecture, cross-compiling
image build shared by every f2calv repository that ships a container. A Dockerfile
here is expected to be readable as documentation, not just executable as a recipe.

Compose files are out of scope.

## File Header

- Start every Dockerfile with `# syntax=docker/dockerfile:1`. It opts the build into the
  latest stable frontend (heredocs, `COPY --parents`, `--mount`, build checks) regardless
  of the local Docker version, and floats on patch releases.
- Follow the syntax line with a comment block explaining what the file builds and why it
  is structured the way it is. Include the sibling-repository links where they exist.
- Dockerfiles and the shell scripts they heredoc **must use LF line endings**. `.gitattributes`
  enforces this; a CRLF Dockerfile fails at build time with `/bin/sh: set: Illegal option -`.
- Use `# check=skip=<rule>` only for a rule that is deliberately violated, and comment why.
  Never blanket-skip. Prefer fixing the finding.
- New Dockerfiles should build clean under BuildKit checks. Treat every check warning as a
  defect to fix rather than noise to suppress.

## Stage Structure

- Keep one Dockerfile per image. **Never** add per-architecture Dockerfiles.
- Use exactly two meaningful stages, named `build` and `final`. Additional stages are
  permitted only where a tool must be pulled in as an image (e.g. `FROM ghcr.io/astral-sh/uv:x.y.z AS uv`).
- Introduce each stage with a banner comment in this form:

  ```dockerfile
  # ------------------------------------------------------------------------------
  # Stage 1 of 2: build
  #
  # Why this stage is pinned/based/structured the way it is.
  # ------------------------------------------------------------------------------
  ```

- Within a stage, use `-- Section ---` sub-banners for the dependency layer, the compile
  layer and the provenance block, so the same landmark appears in every language.
- Order inside `final` is fixed: `FROM` → `WORKDIR` → `COPY --from=build` → runtime `ENV`
  → provenance `ARG`/`ENV` → `LABEL` → `USER` → `ENTRYPOINT`.

## Multi-Architecture Builds

- Pin the `build` stage to `--platform=$BUILDPLATFORM` and **cross-compile** to the target.
  Emulating the target under QEMU is typically 10-50x slower and is not an acceptable default.
- Never put a `--platform` override on the `final` stage. Leaving it unset lets buildx resolve
  the base image for `$TARGETPLATFORM` so the resulting image is genuinely native.
- Declare `ARG TARGETARCH` / `ARG TARGETVARIANT` as late as possible — after every
  platform-agnostic layer — so dependency resolution is shared by all target legs.
- Concatenate the two into one flat token and switch on it. Keep the comment that documents
  the mapping:

  ```dockerfile
  # buildx injects TARGETARCH/TARGETVARIANT automatically:
  #   linux/amd64  -> TARGETARCH=amd64  TARGETVARIANT=
  #   linux/arm64  -> TARGETARCH=arm64  TARGETVARIANT=
  #   linux/arm/v7 -> TARGETARCH=arm    TARGETVARIANT=v7
  case "${TARGETARCH}${TARGETVARIANT}" in
      amd64) ... ;;
      arm64) ... ;;
      armv7) ... ;;
      *) echo "unsupported platform: linux/${TARGETARCH}/${TARGETVARIANT}" >&2; exit 1 ;;
  esac
  ```

- The `*)` arm is mandatory. An unrecognised platform must fail loudly at build time rather
  than silently produce an image for the wrong architecture.
- Resolve the platform mapping **once**. If more than one later stage needs it, write the
  result to a file (e.g. `/etc/rust-target.env`) and source it, rather than repeating the
  `case` statement.
- Supported platforms are `linux/amd64`, `linux/arm64` and `linux/arm/v7`. Adding or dropping
  one is a deliberate change: update the `case`, the base-image choice, `build.sh`/`build.ps1`,
  CI and the README together.
- Verify a base image publishes all three platforms before adopting it. `gcr.io/distroless/python`
  and several vendor images have no `arm/v7` manifest.

## Base Images

- Pin to the narrowest tag that still receives patch updates — `mcr.microsoft.com/dotnet/sdk:10.0`,
  `rust:1-bookworm`, `python:3.14-slim-bookworm`. Never use `latest`.
- Pin an exact version for a tool image whose binary is copied into a build stage
  (e.g. `ghcr.io/astral-sh/uv:0.12.10`), because it is not covered by a lockfile.
- Do not pin by digest. Floating on the patch tag is how base-image CVE fixes arrive; supply-chain
  integrity is handled by `--pull`, attestations and a rebuild, not by freezing a digest.
- Prefer the smallest runtime that still supports every target platform and the application's
  actual framework needs, in this order: distroless/chiseled → slim → full.
- Document the rejected alternatives in a comment above the `final` stage, smallest to largest,
  with the one-line reason each was or was not chosen. That comment is the main reason someone
  reads this file.
- Choose the runtime image to match the published framework reference, not the project type.
  A console application whose transitive packages carry a `FrameworkReference` to
  `Microsoft.AspNetCore.App` needs the `aspnet` image, not `runtime`; it otherwise fails at
  startup with "No frameworks were found".
- Never run `apt-get upgrade` or `dist-upgrade`. Pick up base-image patches by rebuilding on a
  refreshed base tag.

## Layer Caching

- Copy **only** the files that influence dependency resolution first (`*.csproj` + props,
  `go.mod`/`go.sum`, `Cargo.toml`/`Cargo.lock`, `pyproject.toml`/`uv.lock`), resolve dependencies,
  then `COPY` the sources. Editing a source file must not invalidate the dependency layer.
- Use `COPY --parents` to preserve directory structure when globbing manifests, rather than a
  flattening copy plus a fix-up `RUN`.
- Keep dependency resolution **before** `ARG TARGETARCH` whenever it is platform-agnostic, so
  one resolution is shared by all three architecture legs.
- Use BuildKit cache mounts for package and compiler caches, always with `sharing=locked`:

  ```dockerfile
  RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
      dotnet restore ...
  ```

- Give architecture-specific compiler caches a per-architecture `id` (e.g.
  `id=go-build-${TARGETARCH}${TARGETVARIANT}`) so the platform legs do not thrash a shared cache.
- A cache mount is **not** present in the resulting layer. Anything built into one must be
  copied out within the same `RUN` instruction (`install -D ... /out/...`).
- Prefer `COPY --link` for `COPY --from=build` into `final`; it produces an independently
  cacheable, rebasable layer. Do not use it where the destination path depends on a symlink or
  on content created by an earlier layer.

## RUN Instructions

- Use heredoc form for anything beyond a single command, and open it with `set -eux`:

  ```dockerfile
  RUN <<EOF
  set -eux
  ...
  EOF
  ```

  `-e` aborts on the first failure, `-u` catches an unset variable, `-x` puts the executed
  commands in the build log where a CI failure can be diagnosed.
- Without `set -e`, a heredoc reports success if only the last command succeeds. Never omit it.
- One logical step per `RUN`. Do not merge dependency resolution and compilation into one
  instruction — that destroys the cache split.
- Package installs must clean up in the **same** instruction, otherwise the removed files stay
  in the layer:

  ```dockerfile
  apt-get update
  apt-get install -y --no-install-recommends <packages>
  rm -rf /var/lib/apt/lists/*
  ```

- Always pass `--no-install-recommends`. Always list packages explicitly; never install a
  meta-package to obtain one binary.
- A **temporary runtime debugging dependency** (`curl`, `azcopy`, `strace`, `ffmpeg`) is legitimate
  and may be added to a `final` stage while an issue is being investigated. Keep it in one
  clearly-labelled block, and remove it once the investigation is done.
- A commented-out install block is held to the same standard as a live one. It must be correct,
  cleaned up and pinned, so that uncommenting it is the only edit required. A block that would
  fail, leak an apt cache, or download an unpinned artifact is a defect even while commented out.
- Use absolute `WORKDIR` paths. Never `cd` between instructions — each `RUN` is a new shell.

## Reproducibility

- Install from a committed lockfile and fail on drift: `--locked` (cargo), `dotnet restore`
  against committed `Directory.Packages.props`, `--require-hashes` (pip), `uv export --locked`.
  A build that silently resolves a newer dependency is a defect.
- Strip absolute paths and debug symbols from compiled binaries where the toolchain supports it
  (`-trimpath`, `-ldflags "-s -w"`).
- A build must produce the same image from the same commit on any machine. Nothing in the
  Dockerfile may read the host clock, the host network beyond pinned artifacts, or ambient
  host state.

## Provenance and Labels

- Every image carries the same flat provenance block in the `final` stage, each `ARG` with a
  safe default immediately followed by its `ENV`:

  ```dockerfile
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
  ```

  The defaults make the image buildable by hand; CI and `build.sh`/`build.ps1` supply real values.
- Declare OCI annotations as a single `LABEL` instruction with a link to the spec. Keep at least
  `title`, `description`, `source`, `licenses`, `version` and `revision`.
- An `ARG` value is visible in the image history. Provenance values are public by definition;
  nothing else belongs there.

## Runtime Hardening

- The final image runs as a **non-root** user. State it explicitly even when the base image
  already defaults to it — it documents the intent and survives a base-image change:
  - .NET images: `USER $APP_UID` (1654).
  - distroless `:nonroot` tags: `USER nonroot:nonroot`.
  - anything else: a numeric `USER 65532:65532`, which needs no `passwd` entry.
- Place `USER` after the last instruction that requires root, immediately before `ENTRYPOINT`.
- Use **exec form** for `ENTRYPOINT` and `CMD`: `ENTRYPOINT ["dotnet", "app.dll"]`. Shell form
  makes the application a child of `/bin/sh`, which does not forward `SIGTERM`, so the container
  is killed on timeout instead of shutting down cleanly.
- If a variable must be expanded at start-up, `exec` the real process so it becomes PID 1:
  `ENTRYPOINT ["sh", "-c", "exec dotnet ${WORKLOAD}.dll"]`. A bare `sh -c "dotnet ..."` is a defect.
  This also requires a shell in the final image, which rules out distroless and chiseled — prefer
  a fixed entrypoint over a variable one.
- Never bake a secret into the image. No credential, token, connection string, key or
  certificate private key in `ARG`, `ENV`, `COPY` or a `LABEL`. Build-time secrets use
  `RUN --mount=type=secret,id=...`; runtime secrets are injected by the orchestrator.
- Only configuration with safe, non-sensitive defaults may be copied in (e.g. `appsettings.json`),
  and every value in it must be overridable by an environment variable.
- Design for a read-only root filesystem: write only to an explicit `VOLUME` or a mounted path,
  never next to the application binaries.
- `EXPOSE` is documentation only and does not publish anything. Use ports above 1024 (8080/8081)
  because a non-root user cannot bind a privileged port.
- Do not add `HEALTHCHECK` for images destined for Kubernetes — it is ignored there, and liveness
  and readiness belong in the chart. Add one only where docker-compose is the deployment target.
- Do not leave a compiler, SDK or build toolchain in the final image. If the runtime needs a
  build tool, that is a design problem to solve in the build stage.
- An interpreted-language runtime that inherently ships its own package manager (e.g. `pip` in
  the official Python images) is exempt, provided the smallest maintained runtime supporting every
  target platform was chosen. Note the exemption in the `final` stage comment.

## .dockerignore

- Every repository with a Dockerfile has a `.dockerignore`.
- Use the deny-everything-then-allow form. An allowlist keeps the context minimal, speeds up the
  build, and stops an unrelated edit (docs, charts, IDE state, `.git`) invalidating cached layers:

  ```gitignore
  # Deny everything, then explicitly allow only what the build needs.
  *

  !src/**/*.cs
  ```

- The allowlist is also a security boundary: it is what stops a local secrets file, a `.env`, a
  key, or an untracked scratch file being uploaded into the build context.
- When a Dockerfile starts consuming a new file, update `.dockerignore` in the same change.

## Building

- Local builds go through `build.sh` / `build.ps1`, which mirror the `image` job in CI and derive
  every value from git. Keep the two scripts byte-identical across sibling repositories.
- A multi-platform image cannot be loaded into the local image store. A local build targets one
  platform with `--load`; exercising all three requires `--output=type=oci,dest=...` or a push.
- Always pass `--pull` so a stale local base image cannot mask a broken build.
- Push builds should publish attestations: `--provenance=mode=max --sbom=true`.
- Registry, repository and tag values must be lowercase.
- Build the image after any change to the Dockerfile or to packaging, and validate each declared
  platform before claiming multi-architecture support.
