# web

This workspace hosts the browser shell for the baseline: React 19, Vite, strict TypeScript, Redux Toolkit, RTK Query, Vitest, and Playwright.

## Local Setup

The dev server is intentionally HTTPS-only and now uses `vite-plugin-mkcert` for local certificates. The plugin handles mkcert integration for the Vite dev server instead of requiring checked-in certificate files or a manual `.cert` setup step.

Install dependencies and start the Vite dev server:

```powershell
corepack enable
pnpm install
pnpm dev
```

On first run, the plugin may download or install its local certificate tooling and prompt to trust a development CA.

The default backend target is `https://localhost:5001` through `VITE_API_URL`. Override `VITE_API_URL` and `VITE_SIGNALR_URL` through `.env.*` files when an environment needs different browser-facing endpoints.

The Vite dev server itself runs on `https://localhost:3000` with a strict port requirement. If `3000`, `5000`, or `5001` are already in use, stop only the specific conflicting process or choose alternate ports explicitly. When ApiHost is not running on the default `https://localhost:5001`, keep `VITE_API_URL` and `VITE_SIGNALR_URL` aligned with the backend URL you actually start.

Example `.env.local` override:

```text
VITE_API_URL=http://127.0.0.1:5079
VITE_SIGNALR_URL=http://127.0.0.1:5079
```

Playwright E2E is different from local Vite development: `pnpm test:e2e` builds with `web/.env.e2e`, points browser calls at same-origin relative paths, and lets Playwright start the real ApiHost so the shell runs against the live backend.

## Contracts

The frontend consumes generated API contracts from `src/shared/api/generated/contracts.generated.ts`.

Refresh them from the repository root with:

```powershell
$env:ConnectionStrings__BaselineDatabase = "Host=localhost;Database=baseline;Username=postgres;Password=postgres"
./scripts/Generate-Contracts.ps1 -Configuration Release
```

That command rewrites both the OpenAPI snapshot at `../contracts/http/openapi.v1.json` and the generated TypeScript contracts used by RTK Query.

## Governance

The `web/` workspace has three non-negotiables, all declared in [`docs/RULE_TO_GATE_CATALOG.md`](../docs/RULE_TO_GATE_CATALOG.md) and mapped to concrete enforcement artifacts in [`../docs/governance/FRONTEND_GOVERNANCE.md`](../docs/governance/FRONTEND_GOVERNANCE.md):

- **BP-010** — Frontend remote data uses generated contracts and RTK Query only. Every HTTP call flows through `src/shared/api/generated/contracts.generated.ts`; direct `fetch`, `localStorage`, and `sessionStorage` usage is lint-blocked.
- **BP-017** — Shared real-time push and module-aware feature loading use shared infrastructure. Features must compose with `src/shared/realtime/*`, and every frontend-capable module must have a matching feature manifest under `src/features/<module>/index.ts`.
- **BP-036** — Every feature route is wrapped in a `FeatureErrorBoundary` via `src/app/router/AppRouter.tsx`, so a single feature failure never collapses the shell.

Breaking one of these surfaces first as a lint, typecheck, unit, E2E, or architecture-test failure; fix the offending code or file a dated waiver in `waivers/active.waivers.json` referencing the BP id.

## Validation

Run the supported quality gates from this directory:

```powershell
pnpm lint
pnpm typecheck
pnpm test
pnpm build
pnpm exec playwright install chromium
pnpm test:e2e
```

Before `pnpm test:e2e`, make PostgreSQL reachable through `ConnectionStrings__BaselineDatabase`. The default local value used by Playwright is `Host=127.0.0.1;Port=5432;Database=baseline;Username=postgres;Password=postgres`, so `docker compose up -d postgres` from the repo root is the supported quick-start path.

Runnable frontend tests live under `tests/unit` and `tests/e2e`. The repo-level `tests/Web.UnitTests` and `tests/Web.E2E` directories remain as the governance anchors for those gates.
