# ADR: Cookie Authentication Over JWT for Browser Clients

- Status: Accepted
- Date: 2026-04-07
- Owners: Baseline maintainers

## Context

Most .NET SPA tutorials and starter templates use JWT bearer tokens for browser authentication. The typical pattern stores an access token in localStorage or sessionStorage and sends it via the `Authorization` header. This is familiar to developers but introduces security and complexity tradeoffs that are unnecessary when the frontend and backend share the same origin.

## Decision

The baseline uses ASP.NET Core Identity with secure HTTP-only cookies for browser authentication. JWT bearer auth is not used for browser clients.

This decision is specific to the baseline's same-origin browser posture. It is not a claim that cookie auth is the right answer for every frontend or deployment topology.

Specific choices:

- The auth cookie is HTTP-only, secure, SameSite=Lax, with a `__Host-` prefix.
- Antiforgery tokens are required for all state-changing browser requests.
- No tokens are stored in localStorage or sessionStorage.
- Machine-to-machine access uses a separate authentication scheme (`X-Machine-Key` header), not the browser cookie path.

## Rationale

1. **XSS resilience.** HTTP-only cookies are inaccessible to JavaScript. JWT tokens stored in localStorage or sessionStorage are readable by any script running on the page, including injected scripts from XSS vulnerabilities. Cookies with `HttpOnly` eliminate this entire attack surface.

2. **No client-side token management.** JWT-based SPAs must handle token storage, refresh token rotation, silent renewal, expiry detection, and retry-on-401 logic. Cookie auth delegates all of this to the browser's native cookie handling and ASP.NET Core's sliding expiration.

3. **Same-origin simplifies everything.** The baseline serves the frontend from the same origin as the API. When the browser and API share an origin, cookies are sent automatically with every request. There is no CORS preflight overhead, no `Authorization` header to manage, and no token refresh endpoint to build.

4. **Antiforgery is the right tradeoff.** Cookies require CSRF protection, which the baseline handles via ASP.NET Core Antiforgery with a `X-CSRF-TOKEN` header. This is one additional request before mutations, which is simpler than the full JWT lifecycle (access token + refresh token + silent renewal + secure storage).

5. **Revocation fits the baseline's identity model.** ASP.NET Core Identity security-stamp validation already covers auth-critical account changes such as password reset, lockout, role changes, and admin-triggered session revocation. Browser clients stay aligned with the current role-based Identity surface by re-reading the current actor and bootstrap data from the server rather than carrying browser-held legacy authorization snapshots. JWT-based systems typically need short token lifetimes, refresh-token rotation, or explicit revocation infrastructure to achieve comparable control.

## Consequences

- Browser clients never handle or store authentication tokens directly.
- All state-changing browser requests must include the antiforgery token.
- The frontend acquires the antiforgery token via `GET /api/v1/identity/antiforgery` before mutations and caches it until invalidation.
- Session and role revocation flows rely on ASP.NET Core Identity security stamps plus server-authoritative role-based session reads rather than browser-held token lifecycles.
- Machine clients use a completely separate auth path and never interact with cookies.
- If a future deployment requires cross-origin browser access with a separate frontend domain, the cookie approach still works with explicit CORS configuration and `SameSite=None; Secure`, but the same-origin model remains the default and preferred topology.
- Contributors familiar with JWT-based SPAs will need to learn the cookie + antiforgery pattern, but the reference modules demonstrate the complete flow.
