---
description: 'README consistency and Mermaid diagram conventions for Markdown documentation.'
applyTo: '**/*.md'
---

# Documentation

## README Consistency

- **Every project must have a `README.md`**: When adding a new `.csproj` project, create a `README.md` in the project directory as part of the same commit. Follow the existing pattern: Purpose → Services/Extensions → Configuration → Dependencies (NuGet packages table + Project references table).
- Every project's `README.md` must stay in sync with its implementation. During any refactoring — and **always** before creating a new PR — scan each affected project's `README.md` for inconsistencies: outdated service names, missing or removed configuration options, stale dependency tables, or inaccurate flow diagrams. Update the README as part of the same change, not as a follow-up.
- **Major refactorings** (renames, project moves, DI restructuring, model type splits): when a rename or restructure touches type names, configuration sections, or project references, update every `README.md` that mentions the old names **in the same commit**. Do not leave stale references for a follow-up.
- For large refactorings that touch multiple projects, review all impacted `README.md` files before opening the PR.
- **Markdown tables**: Table separator rows must use spaces around pipes to match the spaced style used in header and data rows (e.g. `| --- | --- |` not `|---|---|`). This prevents MD060 (table-column-style) warnings.
- **Configuration examples in library READMEs**: Library projects that expose `IAppConfig` records should include a `## Configuration Examples` section in their `README.md` with `appsettings.json` snippets progressing from minimal configuration through to fully configured. This documents the configuration surface area and provides copy-paste-ready templates for consumers.
- **Only document what exists**: Do not describe behaviour — encryption, resumability, deduplication, retry semantics, platform support — until the implementation and its tests are in place.
- **Placeholders in examples**: Examples must use synthetic values. Never include real credentials, tokens, connection strings, endpoints, hostnames, device addresses, or personal data.

### Structure and SEO

Apply to every file named `README.md`, wherever it lives — repository root, project, sample, chart, or documentation sub-folder. These rules are specific to `README.md` and do not apply to other Markdown files.

- **Exactly one `# H1`**, as the first content line after any front matter, naming the repository or project. Never repeat the H1 lower down and never open with `##`.
- **Never skip heading levels**: `#` → `##` → `###` in order. Skipping breaks document outline extraction and MD001.
- **Lead with a one- or two-sentence summary** directly under the H1 stating what the thing is and who it is for. Search engines and package registries surface this as the description.
- **Keep the H1 aligned with the package identity** so the README title, the `.csproj` `<Description>`, and the published registry listing agree.
- **Descriptive link text**: Never `click here`, and never a bare URL where a phrase reads better.
- **Alt text on every image** describing the content rather than the file (`![Service dependency graph](…)`, not `![diagram](…)`).
- **Unique headings within a file** so generated anchors resolve predictably.
- **No YAML front matter**: Front matter belongs only to Copilot customisation files (`*.instructions.md`, `*.prompt.md`, `*.agent.md`) and to GitHub issue and pull request templates, which require it. Never add a `title:` / `description:` block to a `README.md` or to any other documentation Markdown; GitHub renders it as a table above the content rather than as a heading. The `# H1` and the summary sentence beneath it already carry the title and the description.

## Mermaid Diagrams

Use Mermaid diagrams in `README.md` files to visualize complex relationships and flows. Choose the appropriate diagram type:

### Diagram Type Selection

- **`flowchart`**: Sequential processes, data flow, event flow, service orchestration, CI/CD pipelines
  - Direction: Use `TD` (top-down) for vertical flows; `LR` (left-right) for wide workflows
  - Example: Data moving from device → monitor service → broker → processor → sinks

- **`graph`**: Relationships, dependencies, hierarchies (non-sequential)
  - Direction: Use `TD` for dependency trees; `LR` for peer relationships
  - Example: NuGet package dependencies, project references, Helm chart hierarchies

- **`classDiagram`**: C# class hierarchies, inheritance, composition
  - Shows: Inheritance (`<|--`), composition (`*--`), aggregation (`o--`), association (`-->`), dependency (`..>`)
  - Example: Service class inheritance, interface implementation

- **`sequenceDiagram`**: Time-based interactions between components
  - Shows: Method calls, async operations, timing
  - Example: Request/response flows, background service timing

### Standard Headings

Use these consistent heading patterns before Mermaid diagrams:

| Heading | Use For |
| --- | --- |
| `## Data Flow` | How data moves through the system (device → service → storage) |
| `## Event Flow` | Event-driven processing (pub/sub, channels, streams) |
| `## Service Architecture` | How services interact (SignalR hubs, background services, API clients) |
| `## Dependency Graph` | Package/project dependencies, references |
| `## Application Hierarchy` | Nested application or component structures |
| `## Class Hierarchy` | C# class structures, inheritance trees |
| `## Deployment Flow` | CI/CD pipelines, GitHub Actions workflows, Helm/K8s deployments |
| `## Configuration Hierarchy` | IAppConfig structure, nested configuration objects |

### Styling Guidelines

- **Subgraphs**: Group related components (e.g., `subgraph Monitor["MonitorBgService"]`)
- **Custom styling**: Define `classDef` for highlighting (e.g., owned vs. third-party actions)
- **Node shapes**:
  - `[ ]` rectangle (default) - services, components
  - `([ ])` stadium - entry/exit points
  - `[( )]` cylinder - databases, storage
  - `{ }` diamond - decision points
  - `(( ))` circle - events

### Synchronization

- Mermaid diagrams must stay in sync with code during refactoring
- When renaming services, update corresponding diagram nodes
- When adding/removing dependencies, update dependency graphs
- Review all `README.md` diagrams before creating PRs

## Markdown Linting

The `lint` job in CI, which runs `pre-commit`, is the authoritative gate. Reproduce it locally before pushing rather than discovering failures in CI.

- **Prefer native `pre-commit`** when Python is available: `pre-commit run --all-files`, or `pre-commit run markdownlint --all-files` to scope to Markdown.
- **No local Python? Run `pre-commit` in a container.** Build once with `pre-commit` and Node baked in, then mount the repository. Pin the `pre-commit` version to the one CI uses, and pass the repository through a named cache volume so hook environments survive between runs:

  ```bash
  docker build -t pre-commit-runner:local - <<'EOF'
  FROM python:3.12-slim
  RUN apt-get update \
      && apt-get install -y --no-install-recommends git nodejs npm \
      && rm -rf /var/lib/apt/lists/*
  RUN pip install --no-cache-dir pre-commit==3.7.1
  WORKDIR /src
  EOF

  docker run --rm \
      -v "$PWD:/src" \
      -v pre-commit-cache:/root/.cache/pre-commit \
      pre-commit-runner:local \
      sh -c 'git config --global --add safe.directory /src && pre-commit run --all-files'
  ```

  The `safe.directory` line is required because the mounted repository is owned by a different UID inside the container.

- **If the image build fails on `pip install` with an SSL handshake error**, the network is blocking `files.pythonhosted.org`, the PyPI package CDN. `pypi.org` itself may still resolve, so the index is reachable while no package can be downloaded. Neither `--trusted-host` nor a different index fixes this; it is a middlebox rejecting the CDN's TLS. Fall back to invoking the underlying linter directly.
- **Direct linter fallback** — read the version and arguments from the repository's own `.pre-commit-config.yaml` rather than assuming, because they differ between repositories. For a hook pinned at `v0.49.1` with `--disable MD013 --disable MD034`:

  ```bash
  npx --yes markdownlint-cli@0.49.1 --disable MD013 --disable MD034 -- "**/*.md"
  ```

- **Run from the repository root** so `.markdownlint.json` is auto-discovered. Passing files from a parent directory silently skips that config and produces false MD025 failures.
- **Honour `exclude:` blocks manually.** Invoking the linter directly bypasses any `exclude:` regex in `.pre-commit-config.yaml`, so generated or vendored files that the real gate skips will report failures. Check for an `exclude:` before treating a direct-invocation failure as real.
