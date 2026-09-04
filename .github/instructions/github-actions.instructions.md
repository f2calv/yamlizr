---
description: 'GitHub Actions workflow and composite-action conventions — naming, YAML style, security and GitVersion.'
applyTo: '.github/workflows/**,.github/actions/**,**/action.yml,**/action.yaml'
---

# GitHub Actions

## General

- Always leave **one blank line between steps** within a job for readability.
- Pin actions to the **major version tag version by default** (e.g. `actions/checkout@v6`, `softprops/action-gh-release@v2`). Do not use SHA pinning or include minor/patch versions.
- Set `fetch-depth: 0` on `actions/checkout` whenever GitVersion is used so it can read the full commit history. For lint-only workflows where history is unnecessary, `fetch-depth: 1` is acceptable.
- Use explicit `permissions` blocks on every job; default to the minimum required (e.g. `contents: read`). Set global workflow-level permissions to `permissions: {}` (deny all) and grant per-job.

## Step Naming

- **One-liners**: When a step's `run` block is a single command, use that command (or a slightly abbreviated form) as the step `name` rather than a descriptive prose label (e.g. `name: npm install --global json5`, not `name: setup json5`).
- **Multi-part setup**: When a setup requires multiple steps, name each step with a `(N of M)` suffix (e.g. `name: setup yq (1 of 3)`, `name: setup yq (2 of 3)`, `name: setup yq (3 of 3)`).
- **Matrix-based names**: Include matrix variables in step names for identification (e.g. `name: test (${{ matrix.gv-source }}, ${{ matrix.gv-config }})`).

## Naming Conventions

- **Inputs/outputs**: kebab-case (e.g. `image-registry`, `tag-override`, `git-user-name`).
- **Environment variables**: ALL_UPPERCASE with underscores (e.g. `IMAGE_REGISTRY`, `TAG_OVERRIDE`, `MANIFEST_PATHS`).
- **Secrets**: ALL_UPPERCASE with underscores (e.g. `GITHUB_TOKEN`, `GH_PAT_GITOPS`, `NUGET_API_KEY`).

## Descriptions

- **Keep every `description:` to one short line.** It states what the value *is*, not how or why to use it. Prefer `e.g. <example>` over prose describing the format.
- **Applies to workflow inputs (`workflow_dispatch`, `workflow_call`) as well as action inputs.** `workflow_dispatch` descriptions render as field labels in the *Run workflow* dialog, where a long sentence wraps and makes the form look messy.
- **Move rationale, caveats, deprecation notices and cross-references into a `#` comment directly above the input**, not into the description string. Use the repo's `#no-space-after-hash` comment style.
- Avoid multi-sentence descriptions — they bloat the file and make the input list hard to scan.
- Keeping `key: value` pairs out of descriptions also avoids the colon-space sequence that would otherwise force the whole scalar to be quoted.

  ```yaml
  #DEPRECATED, superseded by nuget-user (Trusted Publishing). Ignored when nuget-user is set.
  NUGET_API_KEY:
    description: Long-lived NuGet API key e.g. secrets.NUGET_API_KEY
    type: string
  ```

## YAML Style

- **2-space indentation** for all workflow and action YAML files.
- Do not quote strings unless YAML requires it (e.g. values containing special characters, reserved words like `true`/`false`/`null`, or strings that could be misinterpreted as another type).
- For `workflow_dispatch` string inputs that represent booleans, use quoted defaults (e.g. `default: 'true'`).
- Use `|` (pipe) for multi-line `run` scripts. Use `>` for flowing multi-line description text.
- One blank line between major YAML sections (`on:`, `env:`, `jobs:`). No blank lines within input/output lists.

## Reusability

- **Reusability is a key requirement.** Factor cross-cutting GitHub Actions logic (build, test, lint, versioning, container/Helm packaging, EF migration-drift checks, etc.) into reusable `workflow_call` workflows in the `f2calv/gha-workflows` repo wherever it makes sense, so every repository consumes one implementation. Keep logic inline or in a repo-local reusable workflow only when it is genuinely repo-specific and unlikely to be reused.
- **`gha-workflows` is the ideal home** for shared workflows. Parameterize them with `inputs` (paths, project/context names, configuration, flags) so they stay repo-agnostic; a consumer passes specifics via `with:`.
- **Filename convention differs by scope:**
  - *Shared* (cross-repo, in `gha-workflows`): non-underscore filename with a `_`-prefixed `name:` (e.g. file `app-build-dotnet.yml`, `name: _app-build-dotnet`), consumed via `uses: f2calv/gha-workflows/.github/workflows/<file>.yml@v1`.
  - *Local* (same-repo): underscore-prefixed filename (e.g. `_gitops-helm-update.yml`), consumed via `uses: ./.github/workflows/_filename.yml`.

## Reusable Workflows

- **File naming**: Prefix local (same-repo) reusable workflow filenames with an underscore to distinguish them from top-level entry-point workflows (e.g. `_gitops-helm-update.yml`, `_deploy-maui-android.yml`).
- **Same repo**: `uses: ./.github/workflows/_filename.yml`
- **Cross-repo**: `uses: owner/repo/.github/workflows/filename.yml@v1`
- Prefer `secrets: inherit` unless there is a specific reason to restrict secrets passed to the called workflow.

## Composite Actions

- Declare `shell: bash` explicitly on every `run` step — composite actions do not inherit a default shell.
- Reference scripts relative to the action root using `${{ github.action_path }}/.scripts/name.sh`.
- **Extract sizeable or critical `run` logic into an external script** under `.scripts/` (e.g. `.scripts/check-release-exists.sh`, `.scripts/Invoke-CheckReleaseExists.ps1`) rather than inlining it in the composite action YAML. An external script can be run and tested standalone — locally or from a `.github/workflows/test.yml` — before it's ever exercised by a real Actions run; a `run: |` block embedded in YAML cannot be. Keep genuinely trivial one-liners inline.

## Security

- Deny all permissions at workflow level (`permissions: {}`), grant only what each job requires.
- Skip bot-triggered runs conditionally: `if: github.actor != 'dependabot[bot]'`.
- Pass tokens via `stdin` for registry logins (e.g. `echo "$TOKEN" | docker login --password-stdin`).
- OCI registry, repository and tag values must be forced to lowercase (e.g. `${IMAGE_REGISTRY,,}`).

## GitVersion

- Always set `fetch-depth: 0` on checkout when GitVersion is in use.
- Default config file is `GitVersion.yml` in the repository root.
- Prefer `semVer` for tags and releases; use `fullSemVer` (via the `version` output) for build versioning and pre-release identifiers.
