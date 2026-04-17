# Adopt Governed Baseline

Use this prompt when forking, renaming, or adopting this baseline into a new product base through the governed adoption workflow.

Adopt this repo into a clean governed product base using the supported adoption workflow.

## Required approach

- Read [AGENTS.md](../AGENTS.md) first, then follow its required governed-doc read order before proposing or making changes.
- Read [README.md](../README.md), [docs/BASELINE_DECLARATION.md](../docs/BASELINE_DECLARATION.md), [docs/ADOPT.md](../docs/ADOPT.md), and [docs/ADD_MODULE.md](../docs/ADD_MODULE.md).
- Treat [scripts/Start-Adoption.ps1](../scripts/Start-Adoption.ps1) as the human-first guided wrapper around the same supported entry point.
- Treat [templates/adoption/adopt-spec.example.json](../templates/adoption/adopt-spec.example.json) and [scripts/Adopt-Baseline.ps1](../scripts/Adopt-Baseline.ps1) as the deterministic direct entry point for agent-driven or already-resolved adoption runs.
- Prefer `pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun` before any apply-mode mutation.
- Always keep `Platform` and `Identity`.
- Do not invent a parallel rename or module-removal workflow when [docs/ADOPT.md](../docs/ADOPT.md) already covers the supported path.

## Workflow

1. Resolve explicit adoption inputs:
   - the human-facing application or product name (`projectName`) — this is the actual app name shown to users, such as the README title and browser title
   - the technical slug (`projectSlug`) — this drives runtime identifiers such as the auth cookie name, Data Protection app name, telemetry service name, default solution file name, and default web package name
   - whether to keep none of the teaching modules or to keep an explicit subset from `Admin`, `SampleFeature`, `Blog`, and `KnowledgeBase`
2. If any input is unresolved, ask concise follow-up questions before changing files. When asking manually, keep the application name and technical slug distinct so you do not imply that the human-facing app name controls the runtime identity strings. When asking about teaching modules, include a short note that `SampleFeature` is the smallest scaffold-first EF reference slice, `Blog` is the first production-grade EF teaching slice, `KnowledgeBase` is the richer content and runtime-settings reference, and `Admin` is the advanced projection and recovery reference. Also point the user to `docs/MODULE_GUIDE.md` and `docs/ADOPT.md` for more detail. If the user explicitly wants the first-time guided local script experience, use or recommend `pwsh ./scripts/Start-Adoption.ps1` instead of inventing a separate questionnaire flow.
3. Convert those decisions into an adoption spec based on [templates/adoption/adopt-spec.example.json](../templates/adoption/adopt-spec.example.json), choosing the correct `modulePreset` and `keepModules` shape.
4. Run the adoption dry run and summarize:
   - rename targets
   - modules to keep and remove
   - validation profile
   - projected mutation scope
5. If the user asked to apply the adoption, run [scripts/Adopt-Baseline.ps1](../scripts/Adopt-Baseline.ps1) without `-DryRun`.
6. Validate through the governed gate wrappers:
   - `pwsh ./scripts/Invoke-LocalGates.ps1`
   - or targeted reruns via `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>`
7. Report missing prerequisites explicitly instead of silently skipping them.

## Guardrails

- Follow [docs/ADOPT.md](../docs/ADOPT.md) for the rename checklist, cookie rename, migration advisory lock rename, Dockerfile inputs, telemetry service name, package name, and teaching-module dependency cautions.
- Keep changes minimal and adoption-scoped.
- If a step fails, classify it as an environment issue, documentation mismatch, or repo defect.
- Do not hand-roll raw `dotnet test` or `pnpm` validation command lists when the canonical gate runner already covers the path.
- Do not remove teaching modules ad hoc without reconciling the documented dependency cautions.
- If contract generation or integration validation cannot run, report the missing prerequisite (`ConnectionStrings__BaselineDatabase`, Docker/Testcontainers, Playwright prerequisites, and so on).

## Response shape

When you respond, start by listing the resolved adoption inputs and whether the task is dry-run only or apply mode.
