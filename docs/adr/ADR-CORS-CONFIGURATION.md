# ADR: CORS Configuration

- Status: Accepted
- Date: 2026-04-15 (supersedes 2026-04-04 version)
- Owners: Baseline maintainers

## Context

The BLUEPRINT.md states that same-origin browser access is the default and CORS stays disabled unless an ADR explicitly permits otherwise. During local development the Vite dev server runs on a different origin than the .NET backend, so CORS has to be enabled when the backend and the frontend shell are served from different origins.

The previous version of this ADR sanctioned `AllowAnyMethod()` + `AllowAnyHeader()` alongside `AllowCredentials()`. Even with an explicit origin allow-list, that combination leaves no defense-in-depth margin: an attacker who compromises (XSS, DNS hijack) any allowed origin can pivot to issuing arbitrary authenticated cross-origin requests with arbitrary headers. The shape also teaches adopters a pattern that is flagged by every mainstream security scanner.

## Decision

CORS enablement is configuration-driven via the `Frontend:AllowedOrigins` setting. All three allow-lists (origins, methods, headers) are **explicit** and **wildcard-free**. The configuration is bound to a strongly-typed `FrontendCorsOptions` record validated with `ValidateOnStart`, so misconfigurations fail boot instead of booting with a weaker policy.

- **No origins configured (default):** CORS middleware is not registered. The application operates in same-origin mode, matching the blueprint's default posture. This is the expected production configuration when the backend serves the built frontend assets directly.
- **Origins configured:** CORS is enabled with:
    - Specific origins only (no wildcard). Wildcard entries fail `ValidateOnStart` and abort startup.
    - Credentials allowed (required for cookie-based authentication).
    - An explicit methods allow-list (default: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`). Wildcard `*` is rejected.
    - An explicit request-headers allow-list (default: `Accept`, `Content-Type`, `X-CSRF-TOKEN`, `X-Requested-With`, `X-SignalR-User-Agent`). Wildcard `*` is rejected.
    - An optional exposed-headers allow-list (default: empty). Wildcard `*` is rejected.
    - A configurable preflight cache lifetime (default: 600 seconds).

Operators can override `Frontend:AllowedMethods`, `Frontend:AllowedHeaders`, `Frontend:ExposedHeaders`, and `Frontend:PreflightMaxAgeSeconds` per environment. The defaults are the minimum needed by the checked-in frontend (`web/src/shared/api/base/starterBaseQuery.ts`) plus the SignalR negotiate handshake.

## Consequences

- Production deployments with same-origin hosting require no CORS configuration and carry no CORS middleware overhead.
- Local development with the HTTPS Vite dev server continues to work by listing the Vite origin (for example, `https://localhost:3000`) in `Frontend:AllowedOrigins` in `src/ApiHost/appsettings.Development.json`.
- Adopters that add new request headers or response headers to the frontend must extend `Frontend:AllowedHeaders` / `Frontend:ExposedHeaders` explicitly. This is the intentional friction that keeps the CORS surface small.
- Wildcard entries in any of the allow-lists fail `ValidateOnStart` and abort boot. `BrowserCorsConfigurationIntegrationTests` exercises the wildcard rejection path.
- Adopters migrating from the previous CORS shape will need to add request headers to `Frontend:AllowedHeaders` if their frontend sends any headers outside the default set.
