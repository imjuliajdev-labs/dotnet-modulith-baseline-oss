# Documentation Index

This baseline ships a larger documentation set than most OSS templates because the docs are part of the drift-resistance mechanism: rules live in `BLUEPRINT.md`, are enforced by gates in `QUALITY_GATES.md`, and are tracked in `RULE_TO_GATE_CATALOG.md`. That coupling is intentional — the governance docs are part of the product, not overhead.

The tiers below are a reading order, not a rewrite. Start at the top and descend only as far as your task requires.

## Start here

Read these first to build, run, and extend the baseline.

- [../README.md](../README.md) — repo purpose, quick start, and core rules.
- [BASELINE_OVERVIEW.md](BASELINE_OVERVIEW.md) — what this repo is and is not, plus adoption tradeoffs.
- [MODULE_GUIDE.md](MODULE_GUIDE.md) — what each checked-in module is for and when to use it as reference.
- [ADD_MODULE.md](ADD_MODULE.md) — the only supported workflow for adding a module.
- [ADOPT.md](ADOPT.md) — using the baseline as the base for a new project, including the guided `Start-Adoption.ps1` wrapper and the direct config-first path.

## Governance (the strict governed set)

Read these when you change a non-negotiable, add a rule, or need to understand why a gate exists. Changes here are cross-file by design — see the governed-set rules in `BLUEPRINT.md`.

- [BLUEPRINT.md](BLUEPRINT.md) — target architecture and non-negotiables.
- [QUALITY_GATES.md](QUALITY_GATES.md) — how the architecture is enforced and how exceptions are governed.
- [ADD_MODULE.md](ADD_MODULE.md) — the only supported structural onboarding workflow.
- [RULE_TO_GATE_CATALOG.md](RULE_TO_GATE_CATALOG.md) — every non-negotiable mapped to its enforcing artifact.
- [../README.md](../README.md) — local bootstrap and workflow surface in the governed set.
- [../AGENTS.md](../AGENTS.md) — agent and contributor entry point into the governed set.
- [adr/](adr/) — Architecture Decision Records for long-lived architectural choices.

## Maintained narrative docs

These docs are not part of the strict governed set, but they are expected to stay accurate. They carry the `<!-- doc-tier: maintained-narrative -->` marker and are covered by lightweight freshness checks so important narrative guidance does not silently drift.

- [BASELINE_DECLARATION.md](BASELINE_DECLARATION.md)
- [BASELINE_OVERVIEW.md](BASELINE_OVERVIEW.md)
- [ARCHITECTURAL_RISKS.md](ARCHITECTURAL_RISKS.md)
- [MODULE_GUIDE.md](MODULE_GUIDE.md)
- [REFERENCE_MODULE_TEACHING_MAP.md](REFERENCE_MODULE_TEACHING_MAP.md)

## Deep dives

Read these when you're touching the relevant subsystem.

- [EF_MODULE_PERSISTENCE_GUIDE.md](EF_MODULE_PERSISTENCE_GUIDE.md) — EF Core persistence shape for new modules.
- [REFERENCE_MODULE_TEACHING_MAP.md](REFERENCE_MODULE_TEACHING_MAP.md) — which reference module demonstrates which pattern.
- [SECRET_MANAGEMENT.md](SECRET_MANAGEMENT.md) — secret handling conventions.
- [DEPLOYMENT.md](DEPLOYMENT.md) — required configuration, OTLP wiring, Data Protection, health probes, and rolling-deploy considerations for shipping the ApiHost to a real environment.
- [MIGRATION_ROLLBACK.md](MIGRATION_ROLLBACK.md) — supported reversal mechanisms for schema changes, decision tree for rollback paths, and authoring guidelines for expand-and-contract migrations.
- [TELEMETRY.md](TELEMETRY.md) — telemetry and observability wiring.
- [ARCHITECTURAL_RISKS.md](ARCHITECTURAL_RISKS.md) — known risks and their mitigations.

## Governance notes and runbooks

These are narrower supporting docs for specific governance areas and operator flows.

- [governance/FRONTEND_GOVERNANCE.md](governance/FRONTEND_GOVERNANCE.md) — frontend rule-to-gate guidance for BP-010, BP-017, and BP-036.
- [governance/HANDLER_COVERAGE.md](governance/HANDLER_COVERAGE.md) — BP-031 handler coverage model, artifact shape, and review workflow.
- [runbooks/outbox-dead-letter-recovery.md](runbooks/outbox-dead-letter-recovery.md) — operator recovery for non-empty outbox dead-letter queries.

## Agent and contributor entry points

Repo-owned prompts under `../prompts/` are task-specific agent workflow accelerators. They complement the canonical docs; they do not replace them.

- [../AGENTS.md](../AGENTS.md) — agent and new-contributor entry point into the governed set.
- [../prompts/add-governed-module.md](../prompts/add-governed-module.md) — canonical repo-owned prompt for adding a module through the governed scaffold workflow.
- [../prompts/adopt-governed-baseline.md](../prompts/adopt-governed-baseline.md) — canonical repo-owned prompt for forking or adopting the baseline through the supported adoption workflow.
- [../CONTRIBUTING.md](../CONTRIBUTING.md) — contribution workflow.
