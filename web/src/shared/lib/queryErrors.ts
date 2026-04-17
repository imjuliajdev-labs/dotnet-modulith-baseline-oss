import type { FetchBaseQueryError } from '@reduxjs/toolkit/query';

export function isFetchBaseQueryError(error: unknown): error is FetchBaseQueryError {
  return typeof error === 'object' && error !== null && 'status' in error;
}

export function isConflictError(error: unknown) {
  return isFetchBaseQueryError(error) && error.status === 409;
}

export function isUnauthorizedError(error: unknown) {
  return isFetchBaseQueryError(error) && error.status === 401;
}

export function getErrorMessage(error: unknown) {
  if (!isFetchBaseQueryError(error)) {
    return 'The request failed unexpectedly.';
  }

  if (typeof error.status === 'string') {
    return error.error;
  }

  if (typeof error.data === 'object' && error.data !== null) {
    const payload = error.data as { detail?: string; title?: string };
    return payload.detail ?? payload.title ?? `The request failed with status ${error.status}.`;
  }

  return `The request failed with status ${error.status}.`;
}