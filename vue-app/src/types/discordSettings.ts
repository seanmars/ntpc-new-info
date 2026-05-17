export interface DiscordSettingsView {
  enabled: boolean;
  hasToken: boolean;
  tokenPreview: string | null;
  channelId: number;
}

export interface DiscordSettingsUpdateInput {
  enabled: boolean;
  botToken?: string | null;
  channelId: number;
}

export function normalizeDiscordSettingsView(input: unknown): DiscordSettingsView {
  const raw = input as Partial<DiscordSettingsView>;
  return {
    enabled: Boolean(raw.enabled),
    hasToken: Boolean(raw.hasToken),
    tokenPreview:
      typeof raw.tokenPreview === 'string' && raw.tokenPreview.length > 0 ? raw.tokenPreview : null,
    channelId: Number(raw.channelId ?? 0),
  };
}
