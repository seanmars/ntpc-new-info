import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue';
import {
  deleteDiscordSettings,
  fetchDiscordSettings,
  updateDiscordSettings,
} from '@/api/discordSettings';
import type { DiscordSettingsUpdateInput, DiscordSettingsView } from '@/types/discordSettings';

const DEFAULT_VIEW: DiscordSettingsView = {
  enabled: false,
  hasToken: false,
  tokenPreview: null,
  channelId: '0',
  notifyAllAlerts: false,
};

export type UpdateResult =
  | { status: 'ok'; settings: DiscordSettingsView }
  | { status: 'error'; error: string };

export type RemoveResult = { status: 'ok' } | { status: 'error'; error: string };

export function useDiscordSettings() {
  const settings = shallowRef<DiscordSettingsView | null>(null);
  const error = ref<string | null>(null);
  const isLoading = ref(false);

  let controller: AbortController | null = null;
  let stopped = false;

  async function refresh(): Promise<void> {
    if (stopped) return;
    controller?.abort();
    const current = new AbortController();
    controller = current;
    isLoading.value = true;
    try {
      const result = await fetchDiscordSettings(current.signal);
      if (stopped || controller !== current) return;
      if (result.status === 'ok') {
        settings.value = result.body;
        error.value = null;
      } else {
        if (result.error === 'aborted') return;
        error.value = result.error;
      }
    } finally {
      if (!stopped && controller === current) {
        isLoading.value = false;
      }
    }
  }

  async function update(input: DiscordSettingsUpdateInput): Promise<UpdateResult> {
    const result = await updateDiscordSettings(input);
    if (result.status === 'ok') {
      settings.value = result.body;
      error.value = null;
      return { status: 'ok', settings: result.body };
    }
    error.value = result.error;
    return { status: 'error', error: result.error };
  }

  async function remove(): Promise<RemoveResult> {
    const result = await deleteDiscordSettings();
    if (result.status === 'ok') {
      settings.value = { ...DEFAULT_VIEW };
      error.value = null;
      return { status: 'ok' };
    }
    error.value = result.error;
    return { status: 'error', error: result.error };
  }

  onMounted(() => {
    void refresh();
  });

  onBeforeUnmount(() => {
    stopped = true;
    controller?.abort();
    controller = null;
  });

  return {
    settings,
    error,
    isLoading,
    refresh,
    update,
    remove,
  };
}
