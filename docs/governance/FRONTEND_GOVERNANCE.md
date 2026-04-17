# Frontend Governance

This document maps the three blueprint non-negotiables that govern the `web/` workspace — **BP-010**, **BP-017**, and **BP-036** — to the concrete enforcement artifacts that keep them green. It exists so a contributor touching `web/` can discover which rules apply, which file fails first when the rule is broken, and how to escalate when the enforcement is wrong.

For the full catalog of non-negotiables and their primary enforcement gates, see [`docs/RULE_TO_GATE_CATALOG.md`](../RULE_TO_GATE_CATALOG.md).

---

## BP-010 — Generated contracts and RTK Query only

**Rule:** Frontend remote data uses generated contracts and RTK Query only.

**Primary enforcement:** Frontend Gate (lint, typecheck, temporary-workspace scaffolded frontend validation, scaffolded feature-owned wrapper anchors, scaffolded feature-owned settings tag-helper files, scaffolded feature-owned dependent projection shells, settings-tag unit-test anchors, settings-api invalidation unit-test anchors, dependent-read invalidation anchors, frontend unit tests, scaffolded Playwright anchors, and E2E).

**Enforcement mechanism:**

- [`web/eslint.config.js`](../../web/eslint.config.js) — the `no-restricted-syntax` block for `**/*.{ts,tsx}` forbids direct `fetch(...)` calls, `window.fetch`, and `localStorage`/`sessionStorage` access, steering all remote I/O through the shared RTK Query layer.
- [`scripts/Test-ModuleScaffoldFrontend.ps1`](../../scripts/Test-ModuleScaffoldFrontend.ps1) — scaffolded-workspace frontend validation exercises the generated contracts path end-to-end.
- `pnpm typecheck` — the generated contracts file is the single source of TypeScript types for server DTOs, so any hand-rolled fetch would fail to compile against the typed API slice.

**Guarded file(s):**

- [`web/src/shared/api/generated/contracts.generated.ts`](../../web/src/shared/api/generated/contracts.generated.ts) — the only source of request/response types and RTK Query endpoints. Regenerated via `scripts/Generate-Contracts.ps1`; listed in the `ignores` section of `eslint.config.js` so it is never hand-edited.
- All files under `web/src/features/**` and `web/src/shared/api/**` that consume remote data.

**Failure mode:** `pnpm lint` reports `no-restricted-syntax` with the message `Use RTK Query through the shared API layer instead of calling fetch directly.` Typecheck will additionally fail if a feature bypasses the generated types. CI `Frontend Gate` goes red.

**Escalation path:** Fix the offending code by routing the call through `web/src/shared/api/generated/contracts.generated.ts` (regenerate if needed), OR file a dated waiver in [`waivers/active.waivers.json`](../../waivers/active.waivers.json) referencing `BP-010` with the narrowest possible scope (a single feature or transport path).

---

## BP-017 — Shared real-time and module-aware feature loading

**Rule:** Shared real-time push and module-aware feature loading use shared infrastructure.

**Primary enforcement:** Frontend Gate (bundle-loading, feature-manifest reconciliation, and shared adapter tests).

**Enforcement mechanism:**

- [`web/eslint.config.js`](../../web/eslint.config.js) — the `src/features/**/*.{ts,tsx}` override forbids `new WebSocket(...)`, `new EventSource(...)`, and direct `import '@microsoft/signalr'` inside feature code, forcing features onto the shared realtime adapter.
- [`tests/Architecture.Tests/FrontendFeatureManifestReconciliationTests.cs`](../../tests/Architecture.Tests/FrontendFeatureManifestReconciliationTests.cs) — asserts that every module declaring `HasFrontendSurface` has a matching `web/src/features/<key>/index.ts` manifest with a correct `moduleKey`, and vice versa.
- [`web/tests/unit/feature-registry.test.ts`](../../web/tests/unit/feature-registry.test.ts) — unit-level validation of the module-aware feature registry.
- [`web/tests/unit/browser-realtime.test.ts`](../../web/tests/unit/browser-realtime.test.ts) and [`web/tests/unit/signalr-browser-realtime.test.ts`](../../web/tests/unit/signalr-browser-realtime.test.ts) — unit coverage for the shared realtime adapters.
- [`web/tests/e2e/app-shell.spec.ts`](../../web/tests/e2e/app-shell.spec.ts) — Playwright coverage of the app-shell feature loader.
- [`tests/Integration.Tests/PlatformRealtimeIntegrationTests.cs`](../../tests/Integration.Tests/PlatformRealtimeIntegrationTests.cs) — backend integration contract for the shared realtime surface.

**Guarded file(s):**

- [`web/src/shared/realtime/browserRealtime.ts`](../../web/src/shared/realtime/browserRealtime.ts) and [`web/src/shared/realtime/signalRBrowserRealtime.ts`](../../web/src/shared/realtime/signalRBrowserRealtime.ts) — the shared realtime adapters features must compose with.
- `web/src/features/*/index.ts` — feature manifests keyed by module.
- All files under `web/src/features/**`.

**Failure mode:** `pnpm lint` reports `no-restricted-syntax` with a message steering the author to the shared realtime adapter. The architecture test fails with a manifest mismatch (`Assert.Equal` on the sorted module-key arrays). Unit or E2E tests fail if the shared adapter contract regresses. CI `Frontend Gate` goes red.

**Escalation path:** Fix the offending code by routing through `web/src/shared/realtime/*` and reconciling the feature manifest, OR file a dated waiver in [`waivers/active.waivers.json`](../../waivers/active.waivers.json) referencing `BP-017` scoped to a single feature channel.

---

## BP-036 — Feature-level error boundaries

**Rule:** The frontend wraps each feature route in a feature-level error boundary.

**Primary enforcement:** Frontend Gate (feature error boundary source inspection and E2E tests).

**Enforcement mechanism:**

- [`web/src/app/router/AppRouter.tsx`](../../web/src/app/router/AppRouter.tsx) — the single composition point that wraps every feature route in `<FeatureErrorBoundary featureLabel={entry.label}>`. Any feature registered through the router automatically inherits the boundary; bypassing the router is what a reviewer must catch.
- [`web/tests/unit/feature-registry.test.ts`](../../web/tests/unit/feature-registry.test.ts) — unit coverage of the registry that feeds `AppRouter`.
- [`web/tests/e2e/app-shell.spec.ts`](../../web/tests/e2e/app-shell.spec.ts) — Playwright coverage that exercises the app-shell feature host. Currently enforced via router-composition source inspection plus feature-registry unit coverage and the app-shell E2E spec — see the files above.

**Guarded file(s):**

- [`web/src/shared/ui/FeatureErrorBoundary.tsx`](../../web/src/shared/ui/FeatureErrorBoundary.tsx) — the shared boundary component.
- [`web/src/app/router/AppRouter.tsx`](../../web/src/app/router/AppRouter.tsx) — the enforcement composition point.
- All feature routes under `web/src/features/**` reached through the router.

**Failure mode:** A feature registered outside the `AppRouter` composition slips its error boundary; an unhandled render error collapses the entire shell instead of the single feature pane. The `app-shell.spec.ts` Playwright check and feature-registry unit test are the first gates to surface the regression; CI `Frontend Gate` goes red.

**Escalation path:** Fix the offending code by registering the route through `AppRouter`'s feature-registry composition (so the `FeatureErrorBoundary` wrap is inherited), OR file a dated waiver in [`waivers/active.waivers.json`](../../waivers/active.waivers.json) referencing `BP-036` scoped to a single feature route.

---

## End-to-end specs hit the real backend

**Rule:** Playwright specs under `web/tests/e2e/` must assert against the real ApiHost started by `pnpm test:e2e`; mocking `**/api/v1/**` responses with `page.route(...)` is forbidden. Component- and flow-level UI tests that need mocked hooks live in the Vitest suite under `web/tests/unit/`.

**Primary enforcement:** [`tests/Architecture.Tests/FrontendE2eMockingGuardrailTests.cs`](../../tests/Architecture.Tests/FrontendE2eMockingGuardrailTests.cs) — `E2eSpecsDoNotUsePlaywrightPageRouteToMockBackendResponses` scans `web/tests/e2e/*.spec.ts` and fails if any file contains `page.route(`.

**Failure mode:** The architecture test fails with a list of offending filenames. Governance gate goes red.

**Escalation path:** Move the mocked flow coverage into a Vitest unit test under `web/tests/unit/` (where hooks and API services can be stubbed cleanly), or rewrite the Playwright spec to drive the real backend — seed data via real management endpoints, sign in via the actual `Sign in with cookie auth` button, and assert against real responses. `web/tests/e2e/knowledge-base-live.spec.ts` is the reference implementation.
