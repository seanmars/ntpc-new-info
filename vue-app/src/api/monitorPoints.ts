import type { MonitorPoint, MonitorPointCreateInput } from '@/types/monitorPoint';

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export type ApiResult<T> = { status: 'ok'; body: T } | { status: 'error'; body: null; error: string };

export async function fetchMonitorPoints(signal?: AbortSignal): Promise<ApiResult<MonitorPoint[]>> {
  const url = `${baseUrl}/api/monitor-points`;
  return runJsonRequest<MonitorPoint[]>(() =>
    fetch(url, { method: 'GET', headers: { Accept: 'application/json' }, signal }),
  );
}

export async function createMonitorPoint(
  input: MonitorPointCreateInput,
  signal?: AbortSignal,
): Promise<ApiResult<MonitorPoint>> {
  const url = `${baseUrl}/api/monitor-points`;
  return runJsonRequest<MonitorPoint>(() =>
    fetch(url, {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
      signal,
    }),
  );
}

export async function updateMonitorPoint(
  id: string,
  input: MonitorPointCreateInput,
  signal?: AbortSignal,
): Promise<ApiResult<MonitorPoint>> {
  const url = `${baseUrl}/api/monitor-points/${encodeURIComponent(id)}`;
  return runJsonRequest<MonitorPoint>(() =>
    fetch(url, {
      method: 'PUT',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
      signal,
    }),
  );
}

export async function deleteMonitorPoint(id: string, signal?: AbortSignal): Promise<ApiResult<null>> {
  const url = `${baseUrl}/api/monitor-points/${encodeURIComponent(id)}`;
  let response: Response;
  try {
    response = await fetch(url, { method: 'DELETE', signal });
  } catch (err) {
    if ((err as { name?: string })?.name === 'AbortError') {
      return { status: 'error', body: null, error: 'aborted' };
    }
    return { status: 'error', body: null, error: toMessage(err, 'network error') };
  }
  if (response.status === 204) return { status: 'ok', body: null };
  if (response.status === 404) return { status: 'error', body: null, error: 'not found' };
  return {
    status: 'error',
    body: null,
    error: `HTTP ${response.status} ${response.statusText || ''}`.trim(),
  };
}

async function runJsonRequest<T>(send: () => Promise<Response>): Promise<ApiResult<T>> {
  let response: Response;
  try {
    response = await send();
  } catch (err) {
    if ((err as { name?: string })?.name === 'AbortError') {
      return { status: 'error', body: null, error: 'aborted' };
    }
    return { status: 'error', body: null, error: toMessage(err, 'network error') };
  }

  if (!response.ok) {
    let detail = '';
    try {
      detail = await response.text();
    } catch {
      // ignore
    }
    const message = detail
      ? `HTTP ${response.status} ${response.statusText || ''}: ${detail}`.trim()
      : `HTTP ${response.status} ${response.statusText || ''}`.trim();
    return { status: 'error', body: null, error: message };
  }

  try {
    const body = (await response.json()) as T;
    return { status: 'ok', body };
  } catch (err) {
    return { status: 'error', body: null, error: toMessage(err, 'invalid JSON response') };
  }
}

function toMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}
