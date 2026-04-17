<!-- doc-tier: maintained-narrative -->
# Baseline Declaration

This repository is being treated as complete at its currently governed scope.

The baseline's intended shape, extension path, and enforcement model are in place and are expected to remain provable through the supported quality wall. Final completion was closed after a fresh real-module proof pass validated the live add-module path and the temporary proof module was removed cleanly.

The governed scope is defined by:

- [BLUEPRINT.md](BLUEPRINT.md)
- [QUALITY_GATES.md](QUALITY_GATES.md)
- [ADD_MODULE.md](ADD_MODULE.md)
- [RULE_TO_GATE_CATALOG.md](RULE_TO_GATE_CATALOG.md)
- [../README.md](../README.md)
- [../AGENTS.md](../AGENTS.md)

This declaration means the baseline is no longer being evaluated as an early architecture build-out or a completion candidate with an open proof obligation. It is being treated as a maintained baseline whose documented scope is expected to match the live repo.

## What Is Already Established

- The repo has a governed modular-monolith shape and an executable drift-resistance model.
- The supported scaffold path is the only supported structural path for adding new bounded contexts, and that path has been re-proven through a fresh real proof module after the recent scaffold fixes.
- That re-proof was performed in the live repo with a temporary module that was scaffolded in, validated end to end, used to drive shared root-cause fixes, and then removed cleanly again.
- The checked-in modules now present a deliberate learning split: `Platform` and `Identity` are core, while `SampleFeature`, `Blog`, `KnowledgeBase`, and `Admin` remain the teaching path for the current seams after scaffolding.
- Supported backend, contract, frontend, and operational validation paths exist and are part of the normal workflow.
- The supported quality wall now includes stronger workflow and security posture checks such as whitespace-format enforcement, dependency-vulnerability scanning, and CI-hosted static analysis, alongside the newer deployment and migration-rollback guidance.
- The supported composed runtime path is PostgreSQL-backed; missing `ConnectionStrings:BaselineDatabase` is not a supported host mode.
- The architecture described in [BLUEPRINT.md](BLUEPRINT.md) is the intended product shape unless changed explicitly through the governed documentation set.

## What This Does Not Mean

- It does not mean future work stops.
- It does not mean every future feature can bypass the scaffold or shared seams.
- It does not mean every future use case is already solved without module-specific design and test work.
- It does not mean new architectural goals can be added casually.
- It does not mean this document overrides failing evidence from the quality wall.
- It does not mean regressions are acceptable because the baseline was previously completed.

## Ongoing Preservation Rules

To preserve the baseline:

1. Add modules only through [../scripts/New-Module.ps1](../scripts/New-Module.ps1) and the workflow in [ADD_MODULE.md](ADD_MODULE.md).
2. Keep the governed documentation set synchronized whenever architecture, workflow, bootstrap, or supported scope changes.
3. Reuse the shared contract, runtime, telemetry, realtime, and frontend composition seams instead of introducing feature-local alternatives without a governed architecture change.
4. Treat a failing full validation wall as evidence against the baseline until the failure is classified and resolved.
5. Treat any future failure of the supported fresh-module path or cleanup path as evidence that the baseline must be reopened.

## Deferred Concern

The previously deferred module-execution split is now closed for the supported baseline:

- module-owned dispatcher requests, integration-event handlers, outbox dispatch, and polling workers enter through the shared module-execution gate
- disable and re-enable drain semantics now converge on one execution-control model instead of separate request and worker-entry patterns

Reopen this concern only if a future execution path bypasses the shared gate or if a new execution mode needs materially different drain semantics.

## Reference Baseline

For a first-time OSS proof, the recommended path is now:

1. use the full Docker composed startup path in [../README.md](../README.md)
2. sign in with the Development-only bootstrap credential documented there
3. inspect `Platform` first to understand the live shell and enabled-module set
4. then study the teaching modules through the contributor teaching path below
5. use [REFERENCE_MODULE_TEACHING_MAP.md](REFERENCE_MODULE_TEACHING_MAP.md) when you want to jump straight to a seam instead of reading the module narratives front to back

The recommended teaching path for contributors remains:

1. [../src/Modules/SampleFeature/README.md](../src/Modules/SampleFeature/README.md)
2. [../src/Modules/Blog/README.md](../src/Modules/Blog/README.md)
3. [../src/Modules/KnowledgeBase/README.md](../src/Modules/KnowledgeBase/README.md)
4. [../src/Modules/Admin/README.md](../src/Modules/Admin/README.md)

## Validation Expectation

This declaration remains credible only while the supported quality wall is trustworthy and repeatable.

Preserve the baseline by re-running the supported quality wall and treating it as the source of truth for drift. Some checks remain CI-hosted by nature, especially CodeQL and Dependabot, but the local preservation path should still start from the canonical gate runner instead of reconstructing the wall by hand:

```powershell
pwsh ./scripts/Invoke-LocalGates.ps1
```

When you need a narrower rerun after a fix, dispatch through the canonical gate wrapper instead of raw `dotnet test` or `pnpm` command lists. Common examples:

```powershell
pwsh ./scripts/Invoke-CiGate.ps1 -Id architecture-tests
pwsh ./scripts/Invoke-CiGate.ps1 -Id integration-tests
pwsh ./scripts/Invoke-CiGate.ps1 -Id contract-generation-and-compatibility
```

If your environment can satisfy heavyweight local gates that declare `skipLocally`, you can opt into them explicitly:

```powershell
pwsh ./scripts/Invoke-LocalGates.ps1 -IncludeSkipped
```

## Reopen Conditions

If any of the following becomes true, the repo should be treated as no longer complete and completion work should be reopened:

- new modules require manual structural onboarding outside the supported scaffold path
- executable gates no longer prove a blueprint non-negotiable
- shared seams are bypassed by feature-local alternatives
- the governed documentation set and the live code drift apart
- the quality wall only goes green through selective reruns, hidden prerequisites, or non-deterministic behavior
- the reference modules stop representing the intended learning path for the documented seams
- operational bootstrap and hosting no longer match the documented supported path
