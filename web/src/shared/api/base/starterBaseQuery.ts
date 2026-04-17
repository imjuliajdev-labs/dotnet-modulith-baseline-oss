import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from '@reduxjs/toolkit/query';
import { fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import type { AntiforgeryTokenResponse } from './contracts';
import { frontendEnvironment } from '../../lib/env';
import { reportBrowserFault } from '../../telemetry/browserTelemetry';

const rawBaseQuery = fetchBaseQuery({
  baseUrl: frontendEnvironment.apiUrl,
  credentials: 'include',
});

let antiforgeryToken: AntiforgeryTokenResponse | null = null;

export function resetAntiforgeryTokenCache() {
  antiforgeryToken = null;
}

function isMutation(method: string | undefined) {
  return !['GET', 'HEAD', 'OPTIONS'].includes((method ?? 'GET').toUpperCase());
}

function createHeaders(input?: HeadersInit | Record<string, string | undefined> | string[][]) {
  const headers = new Headers();

  if (!input) {
    return headers;
  }

  if (input instanceof Headers) {
    input.forEach((value, key) => {
      headers.set(key, value);
    });

    return headers;
  }

  if (Array.isArray(input)) {
    for (const entry of input) {
      if (entry.length === 2) {
        headers.set(entry[0] ?? '', entry[1] ?? '');
      }
    }

    return headers;
  }

  for (const [key, value] of Object.entries(input)) {
    if (typeof value === 'string') {
      headers.set(key, value);
    }
  }

  return headers;
}

function isAntiforgeryProblem(error: FetchBaseQueryError) {
  return typeof error.status === 'number'
    && error.status === 400
    && typeof error.data === 'object'
    && error.data !== null
    && 'code' in error.data
    && (error.data as { code?: string }).code === 'security.antiforgery_invalid';
}

function changesBrowserSession(url: string, method: string | undefined) {
  if ((method ?? 'GET').toUpperCase() !== 'POST') {
    return false;
  }

  return url === '/api/v1/identity/session/login'
    || url === '/api/v1/identity/session/logout'
    || url === '/api/v1/identity/session/step-up';
}

async function ensureAntiforgeryToken(api: Parameters<typeof rawBaseQuery>[1], extraOptions: Parameters<typeof rawBaseQuery>[2]) {
  if (antiforgeryToken) {
    return antiforgeryToken;
  }

  const response = await rawBaseQuery('/api/v1/identity/antiforgery', api, extraOptions);

  if (response.error) {
    return null;
  }

  antiforgeryToken = response.data as AntiforgeryTokenResponse;
  return antiforgeryToken;
}

export const starterBaseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (args, api, extraOptions) => {
  const request = typeof args === 'string' ? { url: args } : { ...args };

  if (isMutation(request.method)) {
    const token = await ensureAntiforgeryToken(api, extraOptions);

    if (!token) {
      reportBrowserFault('transport.unreachable', {
        detail: 'The antiforgery token could not be acquired before a browser mutation.',
      });

      return {
        error: {
          error: 'The antiforgery token could not be acquired before a browser mutation.',
          status: 'FETCH_ERROR',
        },
      } as { error: FetchBaseQueryError };
    }

    const headers = createHeaders(request.headers);
    headers.set(token.headerName, token.requestToken);
    request.headers = headers;
  }

  const result = await rawBaseQuery(request, api, extraOptions);

  if (result.error && result.error.status === 'FETCH_ERROR') {
    reportBrowserFault('transport.unreachable', {
      detail: result.error.error,
      url: request.url,
    });
  }

  if (!result.error && changesBrowserSession(request.url, request.method)) {
    resetAntiforgeryTokenCache();
  }

  if (result.error && isAntiforgeryProblem(result.error)) {
    resetAntiforgeryTokenCache();
  }

  return result;
};