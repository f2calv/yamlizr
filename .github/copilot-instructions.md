# Copilot Instructions

## Shared Instructions

Shared Copilot instructions, skills and prompts are maintained centrally in the [.github](https://github.com/f2calv/.github) repository, under `.github/instructions/`, `.github/skills/` and `.github/prompts/`. They are deliberately not copied into this repository, so a change there takes effect everywhere without a pull request here.

To load them, clone that repository and either add it to this VS Code workspace, or link its folders into `~/.copilot/`. Its README explains both.

If those shared files are not visible, stop and tell the user rather than guessing the conventions — this repository depends on them.

Everything below is specific to this repository.

## Conversion Fidelity Boundary

The tool is deliberately a blunt instrument: it emits as much YAML as it can and expects the user
to review and edit the result. That does not license silent data loss.

- When a classic construct cannot be represented, record it and surface it in the run summary
  rather than dropping it without trace.
- Never emit YAML that looks complete but silently omits a step, a variable, a condition, or a
  dependency.
- Generated YAML is not "production ready" and the README must keep saying so.

## Azure DevOps Dependency Override

The current Azure DevOps clients (`20.256.2`) request `System.Security.Cryptography.Xml` 5.0.0
through their WCF dependencies, also bringing `System.Drawing.Common` 5.0.0 into the graph.
The library explicitly references supported XML `10.0.13` to address GHSA-vh55-786g-wjwj and
remove the vulnerable Drawing dependency path (GHSA-rxg9-xrhp-64gj).
Reassess the colocated project-file TODO when updating those clients; remove the override only
after verifying the resolved dependency graph and offline tests across all target frameworks.
