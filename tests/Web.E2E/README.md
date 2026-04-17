# Web.E2E

The runnable Playwright files live under `web/tests/e2e` so they execute inside the frontend workspace.

This directory remains the repo-level anchor for the frontend E2E gate.

Those runnable specs now target the built browser shell served by ApiHost, not a standalone preview-only surface, so the E2E gate exercises same-origin frontend and backend integration.
