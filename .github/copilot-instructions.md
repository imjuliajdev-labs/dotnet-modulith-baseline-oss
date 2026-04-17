# Copilot Instructions

This repo is scaffold-first and governance-first.

Before proposing or making structural changes, read these docs in order:

1. `docs/BLUEPRINT.md`
2. `docs/QUALITY_GATES.md`
3. `docs/ADD_MODULE.md`
4. `docs/RULE_TO_GATE_CATALOG.md`

When adding a new module or bounded context:

- Prefer the repo-owned prompt at `prompts/add-governed-module.md` when the user is explicitly adding a new module.

When adopting or forking the baseline into a new product base:

- Prefer the repo-owned prompt at `prompts/adopt-governed-baseline.md`.
- For first-time human-guided adoption, use `scripts/Start-Adoption.ps1`.
- For deterministic direct runs, use `docs/ADOPT.md`, `templates/adoption/adopt-spec.example.json`, and `scripts/Adopt-Baseline.ps1` instead of inventing a custom rename or cleanup flow.
- Start with `scripts/New-Module.ps1` and a module spec.
- Do not create a new module by copying or renaming `SampleFeature`.
- Do not create a new module by copying or renaming `KnowledgeBase`.
- Treat `SampleFeature` as the smallest scaffold-first reference slice.
- Treat `Blog` as the first production-grade EF-first teaching slice for a real aggregate, migration, taxonomy, scheduling, module-owned outbox publication, and frontend editorial workflow.
- Treat `KnowledgeBase` as a richer runtime-settings and content-flow reference, not as the default persistence pattern.
- For module-owned relational persistence, prefer EF Core DbContext patterns from `Platform.Infrastructure.Persistence` as the implementation reference.
- The scaffold now emits an EF Core persistence shell and baseline entity-configuration example under each module's `.Infrastructure/Persistence`; extend that shell before looking at other module implementations.
- If the module is expected to reference another module's `.PublicContracts`, declare those module names in `crossModulePublicContractDependencies` and run `pwsh ./scripts/Report-ModuleDependencies.ps1` before scaffolding so BP-033 headroom is explicit.
- Use existing modules as references after scaffolding, not as the structural starting point.
- If the scaffold does not emit a required structural capability, fix the scaffold or surface the gap. Do not hand-assemble the missing structure in the new module.

Core architecture rules remain non-negotiable:

- `ApiHost` is composition-only.
- Endpoints live in `.Api`.
- Persistence lives in `.Infrastructure`.
- All use cases go through the dispatcher.
- Cross-module sharing goes through `.PublicContracts` only.
