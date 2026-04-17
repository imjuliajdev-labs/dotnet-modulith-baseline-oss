# Contributing

Thanks for contributing.

This repository is intentionally governance-first. The fastest way to contribute successfully is to understand the supported architecture and then work through the scaffold, contracts, tests, and docs instead of bypassing them.

## Before You Start

Read these files in order before proposing structural changes:

1. [`README.md`](README.md)
2. [`docs/BLUEPRINT.md`](docs/BLUEPRINT.md)
3. [`docs/QUALITY_GATES.md`](docs/QUALITY_GATES.md)
4. [`docs/ADD_MODULE.md`](docs/ADD_MODULE.md)
5. [`docs/RULE_TO_GATE_CATALOG.md`](docs/RULE_TO_GATE_CATALOG.md)
6. [`AGENTS.md`](AGENTS.md) if you are using AI coding tools in this repo

## Development Setup

Prerequisites:

- .NET 10 SDK
- Node.js with Corepack and pnpm
- PostgreSQL
- `mkcert` for local HTTPS frontend development if you use Vite dev mode

Install frontend dependencies:

```powershell
Set-Location web
corepack enable
pnpm install
Pop-Location
```

Set the database connection string for local work:

```powershell
$env:ConnectionStrings__BaselineDatabase = "Host=localhost;Database=baseline;Username=postgres;Password=postgres"
```

Start the application through the supported path:

```powershell
pwsh ./scripts/Start-ApiHost.ps1
```

## Common Workflows

Add a new module shell:

```powershell
pwsh ./scripts/New-Module.ps1 -SpecFile templates/module/module-spec.example.json
```

Before adding a new cross-module `*.PublicContracts` reference, check headroom against the per-module cap (BP-033):

```powershell
pwsh ./scripts/Report-ModuleDependencies.ps1
```

Refresh OpenAPI and generated TypeScript contracts after API changes:

```powershell
pwsh ./scripts/Generate-Contracts.ps1 -Configuration Release
```

Run the supported validation wall:

```powershell
pwsh ./scripts/Invoke-LocalGates.ps1
```

When you need a targeted rerun after a fix, use the canonical gate dispatcher instead of reassembling raw command lists by hand:

```powershell
pwsh ./scripts/Invoke-CiGate.ps1 -Id <id>
```

Use `-IncludeSkipped` when your local environment can satisfy heavyweight gates that default to `skipLocally`.

## Pull Request Expectations

- Fix root causes instead of adding compatibility shims.
- Add or update tests for the behavior you changed.
- Update docs in the same review when workflow, architecture, setup, or supported scope changes.
- Keep the governed documentation set synchronized when a governed rule or enforcement path changes.
- Use the scaffold for structural onboarding. Do not create a new module by copying `SampleFeature` or `KnowledgeBase`.
- Regenerate contracts when browser-consumed HTTP surface changes.

## Changes That Need Extra Care

- Architecture or rule changes must keep [`docs/BLUEPRINT.md`](docs/BLUEPRINT.md), [`docs/QUALITY_GATES.md`](docs/QUALITY_GATES.md), [`docs/RULE_TO_GATE_CATALOG.md`](docs/RULE_TO_GATE_CATALOG.md), [`README.md`](README.md), and [`AGENTS.md`](AGENTS.md) in sync when affected.
- Module-add workflow changes must continue to flow through [`scripts/New-Module.ps1`](scripts/New-Module.ps1) and [`docs/ADD_MODULE.md`](docs/ADD_MODULE.md).
- Security-sensitive behavior should be reviewed against [`docs/SECRET_MANAGEMENT.md`](docs/SECRET_MANAGEMENT.md) and [`SECURITY.md`](SECURITY.md).

## Community Standards

By participating, you agree to follow [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).

For vulnerability reporting, use the process in [`SECURITY.md`](SECURITY.md) instead of opening a public issue.