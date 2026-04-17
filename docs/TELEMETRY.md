# Telemetry & Observability

## Architecture

The baseline uses the standard .NET `System.Diagnostics.Activity` API for distributed tracing, which is natively compatible with OpenTelemetry. All dispatcher requests emit activities through the `BuildingBlocks.Dispatcher` activity source.

## Emitted Telemetry

### Traces

Every command, query, and event dispatch creates an `Activity` with these tags:

| Tag                       | Description                                       |
| ------------------------- | ------------------------------------------------- |
| `dispatcher.request.type` | Full type name of the request                     |
| `dispatcher.request.kind` | `command`, `query`, or `integration_event`        |
| `dispatcher.module.key`   | Module that owns the request                      |
| `correlation.id`          | Request correlation identifier                    |
| `request.id`              | Unique request identifier                         |
| `dispatcher.outcome`      | `success`, `failure`, `exception`, or `cancelled` |
| `error.code`              | Error code on failure                             |
| `error.kind`              | Error kind on failure                             |
| `exception.type`          | Exception type on unhandled errors                |

### Metrics

ASP.NET Core, HTTP client, and .NET runtime metrics are exported via OpenTelemetry instrumentation packages.

#### Custom business metrics

The baseline emits custom metrics via the `System.Diagnostics.Metrics` API:

**Meter: `BuildingBlocks.Dispatcher`**

| Instrument                       | Type      | Tags                                    | Description                              |
| -------------------------------- | --------- | --------------------------------------- | ---------------------------------------- |
| `dispatcher.requests.total`      | Counter   | `request.kind`, `module.key`, `outcome` | Total dispatcher requests processed      |
| `dispatcher.request.duration`    | Histogram | `request.kind`, `module.key`            | Request processing duration (ms)         |

**Meter: `BuildingBlocks.Outbox`**

| Instrument                       | Type      | Tags                          | Description                              |
| -------------------------------- | --------- | ----------------------------- | ---------------------------------------- |
| `outbox.events.published`        | Counter         | `module.key`, `event.type`    | Events published to the outbox                                                          |
| `outbox.events.dispatched`       | Counter         | `module.key`, `outcome`       | Events dispatched from the outbox                                                       |
| `outbox.events.dead_lettered`    | Counter         | `module.key`, `event.type`    | Events dead-lettered                                                                    |
| `outbox.signal.degraded`         | Observable Gauge | _none_                       | `1` when the LISTEN/NOTIFY listener has failed and the dispatcher is in poll-only mode; `0` when the signal listener is active. Paired with the `outbox_signal` readiness health check. |

**Meter: `BuildingBlocks.Modules`**

| Instrument                       | Type      | Tags                                         | Description                    |
| -------------------------------- | --------- | -------------------------------------------- | ------------------------------ |
| `module.state.transitions`       | Counter   | `module.key`, `from_state`, `to_state`       | Module state transitions       |

All meters are registered in `AddBaselineOpenTelemetry()` via `AddMeter(...)` and exported alongside ASP.NET Core and runtime metrics.

## ILogger Scope Enrichment

Every log line emitted during an HTTP request or a dispatched request carries structured context properties via `ILogger.BeginScope`. This enrichment is centralized — handlers and module code do not call `ILogger.BeginScope` directly.

### HTTP request scope

`RequestContextMiddleware` pushes the following properties onto every log line emitted during an HTTP request:

| Property        | Source                                          |
| --------------- | ----------------------------------------------- |
| `CorrelationId` | `X-Correlation-ID` header or generated fallback |
| `RequestPath`   | The HTTP request path                           |
| `ActorId`       | Authenticated user ID (null if anonymous)       |

### Dispatcher request scope

`RequestTelemetryBehavior` pushes the following properties onto every log line emitted during a dispatched command, query, or integration event:

| Property        | Source                                                    |
| --------------- | --------------------------------------------------------- |
| `CorrelationId` | From `RequestContext` (HTTP or background-generated)      |
| `ModuleKey`     | From `IModuleScoped` on the request (null if unscoped)    |
| `RequestType`   | Short type name of the command, query, or event           |
| `RequestKind`   | `command`, `query`, or `integration_event`                |

### Request timeout scope

`RequestTimeoutBehavior` enforces per-request execution time limits. When a request implements `ITimeoutRequest`, the declared timeout is used; otherwise a 30-second default applies. If the timeout fires before the handler completes, the behavior returns a `Result.Failure` with `ErrorKind.Cancelled` and error code `request.timeout`. The timeout outcome is visible through the existing dispatcher telemetry span (`dispatcher.outcome = cancelled`) and the `dispatcher.requests.total` counter.

### Relationship to OTel traces

ILogger scope enrichment and OpenTelemetry trace tags serve complementary purposes. OTel spans carry correlation context in the distributed tracing backend. ILogger scopes carry the same context on every structured log line so operators can search logs by `CorrelationId`, `ModuleKey`, or `ActorId` without cross-referencing the trace backend.

## Outbox Dead-Letter Visibility

Dead-lettered integration events are queryable through the admin API at `GET /api/v1/platform/outbox/dead-letters`. This endpoint returns event details including `event_id`, `event_type`, `module_key`, `dead_lettered_utc`, `attempts`, `last_error_code`, and `last_error_message`.

This is a read-only surface. Replay is not implemented — see `docs/adr/ADR-OUTBOX-DEAD-LETTER-VISIBILITY.md` for the rationale.

Operators can alert on dead-letter accumulation using the `outbox.events.dead_lettered` counter metric.

## Configuration

Telemetry export is configured in `src/ApiHost/ApiHostComposition.cs` via `AddBaselineOpenTelemetry()` during host composition.

### Environment Variables

| Variable                      | Description                                                  | Default                   |
| ----------------------------- | ------------------------------------------------------------ | ------------------------- |
| `OpenTelemetry__OtlpEndpoint` | OTLP collector endpoint (e.g., `http://otel-collector:4317`) | Console exporter          |
| `OpenTelemetry__ServiceName`  | Service name for resource attribution                        | `dotnet-modulith-baseline` |

### OTLP Collector

When `OpenTelemetry:OtlpEndpoint` is set, traces, metrics, and logs are exported via OTLP gRPC. When unset, traces and metrics go to the console exporter for development visibility outside the `Testing` environment. The integration-test host runs as `Testing`, so it keeps instrumentation active without dumping trace and metric payloads into test output.

### Local Development with Docker Compose

Add a collector and backend to `docker-compose.yml`:

```yaml
otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    ports:
        - "4317:4317"
    volumes:
        - ./otel-collector-config.yaml:/etc/otelcol-contrib/config.yaml

jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
        - "16686:16686"
```

Then set the API container's environment:

```yaml
api:
    environment:
        OpenTelemetry__OtlpEndpoint: "http://otel-collector:4317"
```

## Structured Logging Conventions

### Event Names

Use dot-separated, lowercase event names matching the module and operation:

```
platform.module.enabled
platform.module.disabled
sample-feature.announcement.published
knowledge-base.entry.published
admin.announcement.projected
```

### Structured Properties

Always include:

| Property        | Description                                         |
| --------------- | --------------------------------------------------- |
| `ModuleKey`     | The module key (e.g., `platform`, `sample-feature`) |
| `CorrelationId` | The request correlation ID from `RequestContext`    |
| `RequestId`     | The unique request ID                               |
| `ActorId`       | The authenticated actor (when available)            |

Example:

```csharp
_logger.LogInformation(
    "Module {ModuleKey} state changed to {NewState} by {ActorId}.",
    moduleKey,
    newState,
    actorId);
```

### What Not to Log

- Secret values (passwords, API keys, tokens)
- Full request/response bodies (use trace spans instead)
- PII beyond actor identifiers
