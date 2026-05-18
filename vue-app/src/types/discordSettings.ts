export interface DiscordSettingsView {
  enabled: boolean;
  hasToken: boolean;
  tokenPreview: string | null;
  channelId: string;
  notifyAllAlerts: boolean;
}

export interface DiscordSettingsUpdateInput {
  enabled: boolean;
  botToken?: string | null;
  channelId: string;
  notifyAllAlerts: boolean;
}

export interface DiscordSettingsTestInput {
  botToken?: string | null;
  channelId: string;
}

export interface DiscordSettingsTestResult {
  success: boolean;
  message: string;
  field: string | null;
}

export function normalizeDiscordSettingsTestResult(input: unknown): DiscordSettingsTestResult {
  const raw = input as Partial<DiscordSettingsTestResult>;
  return {
    success: Boolean(raw.success),
    message: typeof raw.message === 'string' ? raw.message : '',
    field: typeof raw.field === 'string' && raw.field.length > 0 ? raw.field : null,
  };
}

export function normalizeDiscordSettingsView(input: unknown): DiscordSettingsView {
  const raw = input as {
    enabled?: unknown;
    hasToken?: unknown;
    tokenPreview?: unknown;
    channelId?: unknown;
    notifyAllAlerts?: unknown;
  };
  return {
    enabled: Boolean(raw.enabled),
    hasToken: Boolean(raw.hasToken),
    tokenPreview:
      typeof raw.tokenPreview === 'string' && raw.tokenPreview.length > 0 ? raw.tokenPreview : null,
    channelId: normalizeChannelId(raw.channelId),
    notifyAllAlerts: Boolean(raw.notifyAllAlerts),
  };
}

function normalizeChannelId(raw: unknown): string {
  if (typeof raw === 'string') return /^\d+$/.test(raw) ? raw : '0';
  if (typeof raw === 'number' && Number.isFinite(raw) && raw >= 0) return String(Math.trunc(raw));
  return '0';
}
