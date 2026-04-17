import { beforeEach, describe, expect, it, vi } from 'vitest';
import { resetAntiforgeryTokenCache, starterBaseQuery } from '../../src/shared/api/base/starterBaseQuery';

describe('starterBaseQuery', () => {
  const fetchMock = vi.fn<typeof fetch>();

  function readRequestUrl(input: Parameters<typeof fetch>[0]) {
    return typeof input === 'string'
      ? input
      : input instanceof Request
        ? input.url
        : String(input);
  }

  function readRequestHeaders(input: Parameters<typeof fetch>[0], init: Parameters<typeof fetch>[1]) {
    return input instanceof Request
      ? input.headers
      : new Headers(init?.headers);
  }

  beforeEach(() => {
    vi.restoreAllMocks();
    resetAntiforgeryTokenCache();
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });

  it('refreshes the antiforgery token after a successful sign-in before the next mutation', async () => {
    fetchMock.mockImplementation((input, init): Promise<Response> => {
      const url = readRequestUrl(input);

      if (url.endsWith('/api/v1/identity/antiforgery')) {
        const token = fetchMock.mock.calls.filter(([candidate]) => {
          return readRequestUrl(candidate).endsWith('/api/v1/identity/antiforgery');
        }).length === 1
          ? 'token-anonymous'
          : 'token-authenticated';

        return Promise.resolve(new Response(JSON.stringify({
          headerName: 'X-CSRF-TOKEN',
          requestToken: token,
        }), {
          headers: {
            'Content-Type': 'application/json',
          },
          status: 200,
        }));
      }

      if (url.endsWith('/api/v1/identity/session/login')) {
        const headers = readRequestHeaders(input, init);
        expect(headers.get('X-CSRF-TOKEN')).toBe('token-anonymous');

        return Promise.resolve(new Response(JSON.stringify({ actorId: 'identity:seeded-admin' }), {
          headers: {
            'Content-Type': 'application/json',
          },
          status: 200,
        }));
      }

      if (url.endsWith('/api/v1/knowledge-base/manage/entries')) {
        const headers = readRequestHeaders(input, init);
        expect(headers.get('X-CSRF-TOKEN')).toBe('token-authenticated');

        return Promise.resolve(new Response(JSON.stringify({ entryId: 'entry-1' }), {
          headers: {
            'Content-Type': 'application/json',
          },
          status: 200,
        }));
      }

      return Promise.reject(new Error(`Unexpected request: ${url}`));
    });

    const api = {
      dispatch: vi.fn(),
      endpoint: 'test',
      forced: false,
      getState: vi.fn(),
      signal: new AbortController().signal,
      type: 'mutation',
    } as never;

    await starterBaseQuery({
      body: { password: 'LocalOnly!123', userName: 'admin' },
      method: 'POST',
      url: '/api/v1/identity/session/login',
    }, api, {});

    await starterBaseQuery({
      body: { title: 'Entry' },
      method: 'POST',
      url: '/api/v1/knowledge-base/manage/entries',
    }, api, {});

    const antiforgeryRequests = fetchMock.mock.calls.filter(([input]) => {
      return readRequestUrl(input).endsWith('/api/v1/identity/antiforgery');
    });

    expect(antiforgeryRequests).toHaveLength(2);
  });

  it('refreshes the antiforgery token after a successful step-up before the next mutation', async () => {
    fetchMock.mockImplementation((input, init): Promise<Response> => {
      const url = readRequestUrl(input);

      if (url.endsWith('/api/v1/identity/antiforgery')) {
        const token = fetchMock.mock.calls.filter(([candidate]) => {
          return readRequestUrl(candidate).endsWith('/api/v1/identity/antiforgery');
        }).length === 1
          ? 'token-before-step-up'
          : 'token-after-step-up';

        return Promise.resolve(new Response(JSON.stringify({
          headerName: 'X-CSRF-TOKEN',
          requestToken: token,
        }), {
          headers: {
            'Content-Type': 'application/json',
          },
          status: 200,
        }));
      }

      if (url.endsWith('/api/v1/identity/session/step-up')) {
        const headers = readRequestHeaders(input, init);
        expect(headers.get('X-CSRF-TOKEN')).toBe('token-before-step-up');

        return Promise.resolve(new Response(JSON.stringify({ actorId: 'identity:seeded-admin' }), {
          headers: {
            'Content-Type': 'application/json',
          },
          status: 200,
        }));
      }

      if (url.endsWith('/api/v1/knowledge-base/manage/entries')) {
        const headers = readRequestHeaders(input, init);
        expect(headers.get('X-CSRF-TOKEN')).toBe('token-after-step-up');

        return Promise.resolve(new Response(JSON.stringify({ entryId: 'entry-2' }), {
          headers: {
            'Content-Type': 'application/json',
          },
          status: 200,
        }));
      }

      return Promise.reject(new Error(`Unexpected request: ${url}`));
    });

    const api = {
      dispatch: vi.fn(),
      endpoint: 'test',
      forced: false,
      getState: vi.fn(),
      signal: new AbortController().signal,
      type: 'mutation',
    } as never;

    await starterBaseQuery({
      body: { password: 'LocalOnly!123' },
      method: 'POST',
      url: '/api/v1/identity/session/step-up',
    }, api, {});

    await starterBaseQuery({
      body: { title: 'Entry' },
      method: 'POST',
      url: '/api/v1/knowledge-base/manage/entries',
    }, api, {});

    const antiforgeryRequests = fetchMock.mock.calls.filter(([input]) => {
      return readRequestUrl(input).endsWith('/api/v1/identity/antiforgery');
    });

    expect(antiforgeryRequests).toHaveLength(2);
  });
});
