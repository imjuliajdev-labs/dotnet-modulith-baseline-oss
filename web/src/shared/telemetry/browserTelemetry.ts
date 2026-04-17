type TelemetryEventName =
  | 'asset.chunk_failed'
  | 'route.crashed'
  | 'shell.bootstrap_failed'
  | 'transport.unreachable';

type TelemetryMetadata = Record<string, unknown>;

const redactedKeyPattern = /authorization|cookie|password|secret|token/i;

function isPlainObject(value: unknown): value is TelemetryMetadata {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const prototype = Reflect.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function sanitizeValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(sanitizeValue);
  }

  if (isPlainObject(value)) {
    return Object.fromEntries(
      Object.entries(value).map(([key, entry]) => [
        key,
        redactedKeyPattern.test(key) ? '[redacted]' : sanitizeValue(entry),
      ]),
    );
  }

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' || value === null) {
    return value;
  }

  return Object.prototype.toString.call(value);
}

export function sanitizeTelemetryMetadata(metadata: TelemetryMetadata): TelemetryMetadata {
  return sanitizeValue(metadata) as TelemetryMetadata;
}

export function createBrowserFaultPayload(eventName: TelemetryEventName, metadata: TelemetryMetadata = {}) {
  return {
    eventName,
    metadata: sanitizeTelemetryMetadata(metadata),
    observedAt: new Date().toISOString(),
  };
}

export function reportBrowserFault(eventName: TelemetryEventName, metadata: TelemetryMetadata = {}) {
  try {
    const payload = createBrowserFaultPayload(eventName, metadata);

    if (import.meta.env.DEV) {
      console.warn('[browser-telemetry]', payload);
    }
  }
  catch {
    return;
  }
}