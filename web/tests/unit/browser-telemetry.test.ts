import { describe, expect, it } from 'vitest';
import { createBrowserFaultPayload, sanitizeTelemetryMetadata } from '../../src/shared/telemetry/browserTelemetry';

describe('browserTelemetry', () => {
  it('redacts banned sensitive metadata keys recursively', () => {
    const sanitized = sanitizeTelemetryMetadata({
      cookie: 'session-cookie',
      nested: {
        authorization: 'Bearer secret',
        keep: 'visible',
      },
      values: [
        { password: 'hidden' },
        { label: 'safe' },
      ],
    });

    expect(sanitized).toEqual({
      cookie: '[redacted]',
      nested: {
        authorization: '[redacted]',
        keep: 'visible',
      },
      values: [
        { password: '[redacted]' },
        { label: 'safe' },
      ],
    });
  });

  it('reduces non-plain objects to safe type markers in telemetry payloads', () => {
    const payload = createBrowserFaultPayload('route.crashed', {
      error: new Error('boom'),
      observedAt: new Date('2026-04-05T14:00:00.000Z'),
    });

    expect(payload.metadata).toEqual({
      error: '[object Error]',
      observedAt: '[object Date]',
    });
    expect(payload.observedAt).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });
});