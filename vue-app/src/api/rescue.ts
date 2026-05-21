import type { RescuePendingMeta, RescueResponse } from '@/types/rescue';

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export type RescueFetchResult =
  | { status: 'ok'; body: RescueResponse }
  | { status: 'pending'; body: null; pending: RescuePendingMeta }
  | { status: 'error'; body: null; error: string };

export type ForceRefreshResult =
  | { status: 'ok'; body: RescueResponse }
  | { status: 'throttled'; retryAfterSeconds: number }
  | { status: 'error'; error: string };

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

export async function forceRefreshRescue(signal?: AbortSignal): Promise<ForceRefreshResult> {
  const url = `${baseUrl}/api/rescue/refresh`;

  let response: Response;
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: { Accept: 'application/json' },
      signal,
    });
  } catch (err) {
    if ((err as { name?: string })?.name === 'AbortError') {
      return { status: 'error', error: 'aborted' };
    }
    return { status: 'error', error: toMessage(err, 'network error') };
  }

  if (response.status === 429) {
    // Source of truth: max(Retry-After header, body.retryAfterSeconds). Both may be present.
    const headerSeconds = parseRetryAfter(response.headers.get('Retry-After'));
    let bodySeconds = 0;
    try {
      const body = (await response.json()) as { retryAfterSeconds?: number };
      if (typeof body.retryAfterSeconds === 'number' && Number.isFinite(body.retryAfterSeconds)) {
        bodySeconds = Math.max(0, Math.ceil(body.retryAfterSeconds));
      }
    } catch {
      // ignore unparseable body
    }
    const retryAfterSeconds = Math.max(headerSeconds, bodySeconds, 1);
    return { status: 'throttled', retryAfterSeconds };
  }

  if (!response.ok) {
    let detail = '';
    try {
      const body = (await response.json()) as { error?: string; lastError?: string };
      detail = body.error ?? body.lastError ?? '';
    } catch {
      // ignore
    }
    const message = detail
      ? `HTTP ${response.status}: ${detail}`
      : `HTTP ${response.status} ${response.statusText || ''}`.trim();
    return { status: 'error', error: message };
  }

  try {
    const body = (await response.json()) as RescueResponse;
    return { status: 'ok', body };
  } catch (err) {
    return { status: 'error', error: toMessage(err, 'invalid JSON response') };
  }
}

function parseRetryAfter(value: string | null): number {
  if (!value) return 0;
  const seconds = Number(value);
  if (Number.isFinite(seconds) && seconds > 0) return Math.ceil(seconds);
  // RFC 7231 also allows HTTP-date; fall back gracefully.
  const dateMs = Date.parse(value);
  if (Number.isFinite(dateMs)) {
    return Math.max(0, Math.ceil((dateMs - Date.now()) / 1000));
  }
  return 0;
}

function toMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}
