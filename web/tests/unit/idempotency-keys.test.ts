import { afterEach, describe, expect, it } from 'vitest';
import { createIdempotencyKey } from '../../src/shared/api/base/idempotencyKeys';

describe('createIdempotencyKey', () => {
  const originalCrypto = globalThis.crypto;

  afterEach(() => {
    Object.defineProperty(globalThis, 'crypto', {
      configurable: true,
      value: originalCrypto,
    });
  });

  it('prefers crypto.randomUUID when available', () => {
    Object.defineProperty(globalThis, 'crypto', {
      configurable: true,
      value: {
        randomUUID: () => '11111111-1111-4111-8111-111111111111',
      },
    });

    expect(createIdempotencyKey()).toBe('11111111-1111-4111-8111-111111111111');
  });

  it('falls back to RFC 4122 v4 formatting from getRandomValues', () => {
    Object.defineProperty(globalThis, 'crypto', {
      configurable: true,
      value: {
        getRandomValues: (buffer: Uint8Array) => {
          buffer.set([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);
          return buffer;
        },
      },
    });

    expect(createIdempotencyKey()).toBe('00010203-0405-4607-8809-0a0b0c0d0e0f');
  });

  it('fails closed when secure randomness is unavailable', () => {
    Object.defineProperty(globalThis, 'crypto', {
      configurable: true,
      value: undefined,
    });

    expect(() => createIdempotencyKey()).toThrow('Secure idempotency keys require Web Crypto support.');
  });
});