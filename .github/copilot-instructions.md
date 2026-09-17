# Copilot Instructions

## Shared Instructions

Shared Copilot instruction files are maintained centrally in the [.github](https://github.com/f2calv/.github) repository under `instructions/`, and are applied to every workspace from the VS Code user profile via `~/.copilot/instructions`. They are deliberately not copied into this repository, so a change there takes effect everywhere without a pull request here.

Everything below is specific to this repository.

## Conversion Fidelity Boundary

The tool is deliberately a blunt instrument: it emits as much YAML as it can and expects the user
to review and edit the result. That does not license silent data loss.

- When a classic construct cannot be represented, record it and surface it in the run summary
  rather than dropping it without trace.
- Never emit YAML that looks complete but silently omits a step, a variable, a condition, or a
  dependency.
- Generated YAML is not "production ready" and the README must keep saying so.

## Known Transitive Advisories

The Azure DevOps client libraries drag in `System.Drawing.Common` 5.0.0 (GHSA-rxg9-xrhp-64gj,
critical, raising `NU1904`) and `System.Security.Cryptography.Xml` 5.0.0 (GHSA-vh55-786g-wjwj,
moderate, raising `NU1902`). There is no direct reference to remove and no upstream release that
drops them.
