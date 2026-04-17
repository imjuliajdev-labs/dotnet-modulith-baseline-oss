# Add Governed Module

Use this prompt when adding a new module, bounded context, or feature module through the governed scaffold workflow.

Add a new module to this repo using the governed scaffold workflow.

## Required approach

- Read [AGENTS.md](../AGENTS.md), [docs/BLUEPRINT.md](../docs/BLUEPRINT.md), [docs/QUALITY_GATES.md](../docs/QUALITY_GATES.md), [docs/ADD_MODULE.md](../docs/ADD_MODULE.md), and [docs/RULE_TO_GATE_CATALOG.md](../docs/RULE_TO_GATE_CATALOG.md) before scaffolding.
- Skim [docs/adr/](../docs/adr/) to confirm whether the area you are touching already has a governing ADR.
- Treat [scripts/New-Module.ps1](../scripts/New-Module.ps1) and [templates/module/module-spec.example.json](../templates/module/module-spec.example.json) as the only supported structural starting point.
- Do not clone, rename, or copy any existing module to create a new module. In particular, do not use `SampleFeature`, `KnowledgeBase`, `Admin`, `Platform`, or `Identity` as the structural starting point.
- Use the existing modules only as reference material after scaffolding, not as the scaffolding mechanism.

## Workflow

1. Convert the user's request into explicit values for every required module-spec input in [docs/ADD_MODULE.md](../docs/ADD_MODULE.md).
   - When the module has operator-managed settings, include typed setting definitions with names, display labels, primitive types, default values, and reload-safe metadata.
   - When the module is expected to reference another module's `.PublicContracts`, capture those module names explicitly in `crossModulePublicContractDependencies` and run `pwsh ./scripts/Report-ModuleDependencies.ps1` before scaffolding so BP-033 headroom is visible.
2. If any required input is missing or ambiguous, ask concise follow-up questions before writing code.
3. Create a module spec JSON shaped like [templates/module/module-spec.example.json](../templates/module/module-spec.example.json).
4. Run [scripts/New-Module.ps1](../scripts/New-Module.ps1) with that spec.
5. Summarize the generated shell and the selected capability flags.
6. Only after the scaffolded shell exists, implement the requested behavior inside the generated module.
7. Run the canonical local gate runner (`pwsh ./scripts/Invoke-LocalGates.ps1`) or targeted gate reruns through `pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>` for the relevant architecture, unit, integration, contract-generation, and frontend validations. Do not hand-roll a raw validation command list when the governed gate wrappers already cover the path.

## Guardrails

- If the scaffold cannot emit a required structural capability, stop and surface the gap instead of hand-assembling the missing structure.
- For module-owned relational persistence implementation after scaffolding, use EF Core DbContext patterns from `src/Modules/Platform/Platform.Infrastructure/Persistence` as the default reference.
- Extend the scaffolded `.Infrastructure/Persistence` shell and baseline entity-configuration example before reusing any persistence code from existing modules.
- Do not copy persistence implementations from `KnowledgeBase.Infrastructure.Persistence` as a scaffold baseline.
- Keep `ApiHost` composition-only.
- Keep browser DTOs in `.Api`, not `.PublicContracts`.
- Keep cross-module sharing limited to `.PublicContracts`.
- Keep all use cases going through the dispatcher.
- If the module spec declares cross-module `*.PublicContracts` dependencies, surface the current and projected BP-033 headroom before scaffolding instead of discovering it only at the architecture gate.

## Response shape

When you respond, start by listing the resolved module-spec decisions, then execute the scaffold and continue with implementation only if the user asked for more than structural onboarding.
