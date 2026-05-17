import type { GeocodeResult } from '@/types/monitorPoint';

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export type GeocodeFetchResult =
  | { status: 'ok'; body: GeocodeResult[] }
  | { status: 'error'; body: null; error: string };

export async function searchGeocode(
  query: string,
  limit: number,
  signal?: AbortSignal,
): Promise<GeocodeFetchResult> {
  const clamped = Math.max(1, Math.min(10, Math.trunc(limit) || 5));
  const url = `${baseUrl}/api/geocode/search?q=${encodeURIComponent(query)}&limit=${clamped}`;

  let response: Response;
  try {
    response = await fetch(url, {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal,
    });
  } catch (err) {
    if ((err as { name?: string })?.name === 'AbortError') {
      return { status: 'error', body: null, error: 'aborted' };
    }
    return { status: 'error', body: null, error: toMessage(err, 'network error') };
  }

  if (!response.ok) {
    return {
      status: 'error',
      body: null,
      error: `HTTP ${response.status} ${response.statusText || ''}`.trim(),
    };
  }

  try {
    const body = (await response.json()) as GeocodeResult[];
    return { status: 'ok', body };
  } catch (err) {
    return { status: 'error', body: null, error: toMessage(err, 'invalid JSON response') };
  }
}

function toMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}
