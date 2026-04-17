function normalizeBaseUrl(value: string | undefined, fallback: string) {
  const resolved = (value ?? fallback).trim();

  if (resolved === '/') {
    return '';
  }

  return resolved.replace(/\/+$/, '');
}

export const frontendEnvironment = {
  apiUrl: normalizeBaseUrl(import.meta.env.VITE_API_URL, 'https://localhost:5001'),
  signalRUrl: normalizeBaseUrl(import.meta.env.VITE_SIGNALR_URL, import.meta.env.VITE_API_URL ?? 'https://localhost:5001'),
};