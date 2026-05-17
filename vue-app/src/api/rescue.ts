import type { RescuePendingMeta, RescueResponse } from '@/types/rescue';

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export type RescueFetchResult =
  | { status: 'ok'; body: RescueResponse }
  | { status: 'pending'; body: null; pending: RescuePendingMeta }
  | { status: 'error'; body: null; error: string };

export async function fetchLatestRescue(signal?: AbortSignal): Promise<RescueFetchResult> {
  const url = `${baseUrl}/api/rescue/latest`;

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

  if (response.status === 503) {
    let pending: RescuePendingMeta = {};
    try {
      pending = (await response.json()) as RescuePendingMeta;
    } catch {
      // body may not be JSON; ignore
    }
    return { status: 'pending', body: null, pending };
  }

  if (!response.ok) {
    return {
      status: 'error',
      body: null,
      error: `HTTP ${response.status} ${response.statusText || ''}`.trim(),
    };
  }

  try {
    const body = (await response.json()) as RescueResponse;
    return { status: 'ok', body };
  } catch (err) {
    return { status: 'error', body: null, error: toMessage(err, 'invalid JSON response') };
  }
}

function toMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}
