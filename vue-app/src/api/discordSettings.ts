import {
  normalizeDiscordSettingsView,
  type DiscordSettingsUpdateInput,
  type DiscordSettingsView,
} from '@/types/discordSettings';

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export type ApiResult<T> = { status: 'ok'; body: T } | { status: 'error'; body: null; error: string };

export async function fetchDiscordSettings(signal?: AbortSignal): Promise<ApiResult<DiscordSettingsView>> {
  const url = `${baseUrl}/api/settings/discord`;
  return runJsonRequest(() =>
    fetch(url, { method: 'GET', headers: { Accept: 'application/json' }, signal }),
  );
}

export async function updateDiscordSettings(
  input: DiscordSettingsUpdateInput,
  signal?: AbortSignal,
): Promise<ApiResult<DiscordSettingsView>> {
  const url = `${baseUrl}/api/settings/discord`;
  const body: Record<string, unknown> = {
    enabled: input.enabled,
    channelId: input.channelId,
  };
  if (input.botToken !== undefined) {
    body.botToken = input.botToken;
  }

  return runJsonRequest(() =>
    fetch(url, {
      method: 'PUT',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal,
    }),
  );
}

export async function deleteDiscordSettings(signal?: AbortSignal): Promise<ApiResult<null>> {
  const url = `${baseUrl}/api/settings/discord`;
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
  return {
    status: 'error',
    body: null,
    error: `HTTP ${response.status} ${response.statusText || ''}`.trim(),
  };
}

async function runJsonRequest(send: () => Promise<Response>): Promise<ApiResult<DiscordSettingsView>> {
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
    const raw = (await response.json()) as unknown;
    return { status: 'ok', body: normalizeDiscordSettingsView(raw) };
  } catch (err) {
    return { status: 'error', body: null, error: toMessage(err, 'invalid JSON response') };
  }
}

function toMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}
