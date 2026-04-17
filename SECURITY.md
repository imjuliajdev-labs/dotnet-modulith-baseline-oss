# Security Policy

Security issues should not be reported through a public GitHub issue.

## Reporting a Vulnerability

If GitHub private vulnerability reporting or GitHub Security Advisories are enabled for this repository, use that path first.

If a private reporting path is not enabled, contact the repository owner or maintainer privately through GitHub before public disclosure.

Please include:

- the affected area or module
- the impact you believe the issue has
- clear reproduction steps or a proof of concept
- any configuration assumptions needed to reproduce it
- whether the issue is safe to disclose publicly yet

## What to Expect

The goal is to triage reports promptly, reproduce the issue, determine impact, and prepare a fix on the maintained baseline.

If the report is valid, the fix will normally be applied to the maintained default branch first.

## Scope Notes

- Development-only seeded credentials in development config are for local use only and are not intended for deployed environments.
- Review [`docs/SECRET_MANAGEMENT.md`](docs/SECRET_MANAGEMENT.md) before changing secret handling or local development credentials.
- Architectural security posture is also constrained by the governed docs, especially [`docs/BLUEPRINT.md`](docs/BLUEPRINT.md).