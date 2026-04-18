# Deployment

This document describes how to deploy the ApiHost to a real environment. For local development use [`docker-compose.yml`](../docker-compose.yml). For secret backends see [`SECRET_MANAGEMENT.md`](SECRET_MANAGEMENT.md). For schema evolution and rollback see [`MIGRATION_ROLLBACK.md`](MIGRATION_ROLLBACK.md).

## Required configuration

All settings can be supplied through environment variables using the standard ASP.NET Core key-mapping (`__` separator).

### Database

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings__BaselineDatabase` | Yes | Single connection string used by every module's persistence layer. The schema separation between modules is enforced by EF Core defaults and architecture tests, not by separate connections. |

The PostgreSQL server behind `ConnectionStrings__BaselineDatabase` must allow prepared transactions (`max_prepared_transactions > 0`). The baseline's command-transaction flow can coordinate shared-runtime idempotency, platform audit writes, module persistence, and outbox persistence against the same database during one command execution. The checked-in `docker-compose.yml` already starts PostgreSQL with `max_prepared_transactions=64`; production deployments need the equivalent server-side setting.

### Identity bootstrap

| Key | Required | Notes |
|---|---|---|
| `Modules__Identity__SeededAdmin__Password` | Yes (Development/Testing only) | The seeded admin and machine credentials are **rejected at startup** outside Development and Testing environments. Production deployments must provision real users through the admin UI or an external IdP. |
| `Modules__Identity__SeededMachine__ApiKey` | Optional | Same restriction as above. |

### OpenTelemetry

| Key | Required | Default | Notes |
|---|---|---|---|
| `OpenTelemetry__OtlpEndpoint` | Recommended | unset → console exporter (Development) or no exporter (Testing) | Point at an OTLP gRPC endpoint (port 4317) for traces, metrics, and logs. The local docker-compose stack ships an `otel-collector` service with a `debug` exporter; replace its config to forward to your real backend. |
| `OpenTelemetry__ServiceName` | Recommended | `IHostEnvironment.ApplicationName` | Use this to distinguish multiple ApiHost instances or environments in your APM. |

### ASP.NET Core

| Key | Required | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Yes | Use `Production` outside CI/dev. Anything other than `Development`/`Testing` activates the seeded-credential refusal and the production secret resolver requirements documented in [`SECRET_MANAGEMENT.md`](SECRET_MANAGEMENT.md). |
| `ASPNETCORE_URLS` | Yes | The container image listens on `8080` by default. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | When behind a proxy | Required for correct request scheme/host detection. |

## Data Protection keys

ASP.NET Core Data Protection keys must be persisted to a shared durable store when running more than one ApiHost instance, otherwise antiforgery tokens and authentication cookies will not be valid across instances.

On the **default composed runtime**, the baseline already satisfies this requirement with a **PostgreSQL-backed** key ring stored in the shared-runtime database (`building_blocks.building_blocks_data_protection_keys`). No extra override is required for the supported baseline deployment shape.

If an adopter intentionally replaces the default shared-runtime durability or hosts Data Protection outside PostgreSQL, the replacement must still provide one shared durable store across all ApiHost instances. Common alternatives include:

- a shared filesystem volume (network share, EFS, etc.) suitable for fewer than ~5 replicas;
- Azure Blob Storage / Azure Key Vault for HSM-backed protection;
- AWS S3 + KMS for the equivalent on AWS;
- a Kubernetes secret mounted into every replica when running on k8s.

The wire-up is documented under [`SECRET_MANAGEMENT.md`](SECRET_MANAGEMENT.md). Treat a shared durable key store as a hard prerequisite for any multi-instance deployment.

## Health probes

| Probe | URL | Purpose |
|---|---|---|
| Liveness | `GET /health` | Returns `200` while the host is running and the composed health checks are healthy. Use as the default host liveness probe. |
| Readiness | `GET /ready` | Returns `200` only when the readiness-tagged checks pass, including database readiness. Use as Kubernetes `readinessProbe`. |

The DbMigrator runs as a separate container (`Dockerfile.migrator`) and must complete successfully **before** the ApiHost replicas come up. In Kubernetes this is naturally expressed as a `Job` that the Deployment depends on; in docker-compose it is wired through `service_completed_successfully`.

## Rolling deployments

Every migration is expected to follow expand-and-contract sequencing (BP-020). The deployment pipeline must:

1. Run the DbMigrator job to head before rolling new ApiHost replicas.
2. Validate that the new replicas are healthy on the new schema for the full compatibility window before merging the **contract** migration that removes the legacy shape.
3. Treat any contract migration as a one-way door: rollback past it requires backup restore. See [`MIGRATION_ROLLBACK.md`](MIGRATION_ROLLBACK.md).

## Multi-instance considerations

- **Outbox dispatch** is module-scoped and uses transactional locking; running multiple ApiHost replicas is safe.
- **Process managers and recovery workers** use database-backed checkpoint stores; they coordinate through the database and tolerate replica restarts.
- **Rate limiting** is currently in-process. Set per-replica limits with that in mind, or front the cluster with a shared rate-limiter (gateway, sidecar) when stricter global limits are required.
- **SignalR / browser realtime** uses an in-memory backplane by default. Add a Redis or other shared backplane when running more than one replica with realtime fan-out.

## Smoke checklist before declaring a release healthy

1. `/ready` returns `200` from every replica.
2. The OTLP collector reports traces from every replica's `Activity` source.
3. `dotnet list package --vulnerable --include-transitive` is clean (CI gate).
4. Architecture, integration, and contract tests are green on the deployed commit.
5. The production secret backend has rotated within the documented window.
