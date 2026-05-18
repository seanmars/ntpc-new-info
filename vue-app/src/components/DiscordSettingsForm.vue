<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { testDiscordSettings } from '@/api/discordSettings';
import type {
  DiscordSettingsUpdateInput,
  DiscordSettingsView,
} from '@/types/discordSettings';

const props = defineProps<{
  open: boolean;
  submitting?: boolean;
  submitError?: string | null;
  current: DiscordSettingsView | null;
}>();

const emit = defineEmits<{
  (event: 'close'): void;
  (event: 'submit', payload: DiscordSettingsUpdateInput): void;
}>();

const enabled = ref(false);
const notifyAllAlerts = ref(false);
const channelIdInput = ref<string>('');
const newTokenInput = ref<string>('');
const tokenInput = ref<HTMLInputElement | null>(null);

const testing = ref(false);
const testResult = ref<{ tone: 'ok' | 'error'; message: string } | null>(null);

const tokenPlaceholder = computed(() => {
  const preview = props.current?.tokenPreview;
  if (preview) return `輸入新值以替換 (目前: ${preview})`;
  return '輸入 bot token';
});

const hasStoredToken = computed(() => Boolean(props.current?.hasToken));

const willHaveToken = computed(() => newTokenInput.value.length > 0 || hasStoredToken.value);

const channelIdValue = computed<string | null>(() => {
  const raw = channelIdInput.value.trim();
  if (raw === '') return null;
  if (!/^\d+$/.test(raw)) return null;
  if (/^0+$/.test(raw)) return null;
  return raw.replace(/^0+/, '');
});

const channelIdError = computed<string | null>(() => {
  const raw = channelIdInput.value.trim();
  if (raw === '') return enabled.value ? '請輸入 channel id' : null;
  if (!/^\d+$/.test(raw)) return 'channel id 必須是正整數';
  if (channelIdValue.value === null) return 'channel id 必須大於 0';
  return null;
});

const tokenError = computed<string | null>(() => {
  if (!enabled.value) return null;
  if (!willHaveToken.value) return '啟用前需先設定 bot token';
  return null;
});

const canSubmit = computed(() => {
  if (props.submitting) return false;
  if (channelIdError.value) return false;
  if (tokenError.value) return false;
  return true;
});

const canTest = computed(() => {
  if (testing.value || props.submitting) return false;
  if (channelIdValue.value === null) return false;
  if (!willHaveToken.value) return false;
  return true;
});

watch([newTokenInput, channelIdInput], () => {
  testResult.value = null;
});

watch(
  () => props.open,
  async (opened) => {
    if (opened) {
      resetForm();
      await nextTick();
      tokenInput.value?.focus();
    }
  },
);

function resetForm() {
  const current = props.current;
  enabled.value = current?.enabled ?? false;
  notifyAllAlerts.value = current?.notifyAllAlerts ?? false;
  channelIdInput.value =
    current && current.channelId !== '' && current.channelId !== '0' ? current.channelId : '';
  newTokenInput.value = '';
  testResult.value = null;
}

function close() {
  newTokenInput.value = '';
  testResult.value = null;
  emit('close');
}

async function runTest() {
  if (!canTest.value) return;
  const channelId = channelIdValue.value ?? '0';
  testing.value = true;
  testResult.value = null;
  try {
    const result = await testDiscordSettings({
      channelId,
      botToken: newTokenInput.value.length > 0 ? newTokenInput.value : undefined,
    });
    if (result.status === 'ok') {
      testResult.value = {
        tone: result.body.success ? 'ok' : 'error',
        message: result.body.message,
      };
    } else {
      testResult.value = { tone: 'error', message: result.error };
    }
  } finally {
    testing.value = false;
  }
}

function submit() {
  if (!canSubmit.value) return;
  const channelId = channelIdValue.value ?? '0';
  const input: DiscordSettingsUpdateInput = {
    enabled: enabled.value,
    channelId,
    notifyAllAlerts: notifyAllAlerts.value,
    botToken: newTokenInput.value.length > 0 ? newTokenInput.value : undefined,
  };
  emit('submit', input);
}
</script>

<template>
  <div v-if="open" class="ds-form-backdrop" role="presentation">
    <div class="ds-form" role="dialog" aria-modal="true" aria-labelledby="ds-form-title">
      <header class="ds-form__header">
        <h2 id="ds-form-title" class="ds-form__title">Discord 通知設定</h2>
        <button type="button" class="ds-form__close" aria-label="關閉" @click="close">×</button>
      </header>

      <div class="ds-form__body">
        <label class="ds-form__check">
          <input v-model="enabled" type="checkbox" />
          <span>啟用 Discord 通知</span>
        </label>

        <label class="ds-form__check">
          <input v-model="notifyAllAlerts" type="checkbox" :disabled="!enabled" />
          <span>
            通知所有災訊
            <span class="ds-form__hint ds-form__hint--inline">
              (啟用後不論是否有監測點, 所有新災訊都會發送通知)
            </span>
          </span>
        </label>

        <label class="ds-form__field">
          <span class="ds-form__label">Bot Token</span>
          <input
            ref="tokenInput"
            v-model="newTokenInput"
            type="password"
            autocomplete="off"
            class="ds-form__input"
            :placeholder="tokenPlaceholder"
          />
          <span v-if="hasStoredToken" class="ds-form__hint">
            目前已儲存 token: {{ current?.tokenPreview }}. 留空表示保留, 輸入新值將取代.
          </span>
          <span v-if="tokenError" class="ds-form__error">{{ tokenError }}</span>
        </label>

        <label class="ds-form__field">
          <span class="ds-form__label">Channel ID</span>
          <input
            v-model="channelIdInput"
            type="text"
            inputmode="numeric"
            class="ds-form__input"
            placeholder="例如: 1234567890123456789"
          />
          <span v-if="channelIdError" class="ds-form__error">{{ channelIdError }}</span>
        </label>

        <p
          v-if="testResult"
          class="ds-form__test-result"
          :class="`ds-form__test-result--${testResult.tone}`"
        >
          {{ testResult.message }}
        </p>

        <p v-if="submitError" class="ds-form__submit-error">儲存失敗: {{ submitError }}</p>
      </div>

      <footer class="ds-form__footer">
        <button
          type="button"
          class="ds-form__secondary ds-form__test"
          :disabled="!canTest"
          @click="runTest"
        >
          {{ testing ? '測試中...' : '測試連線' }}
        </button>
        <div class="ds-form__footer-spacer"></div>
        <button type="button" class="ds-form__secondary" @click="close">取消</button>
        <button type="button" class="ds-form__primary" :disabled="!canSubmit" @click="submit">
          {{ submitting ? '儲存中...' : '儲存' }}
        </button>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.ds-form-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 16px;
}

.ds-form {
  background: #ffffff;
  border-radius: 8px;
  width: 100%;
  max-width: 440px;
  max-height: calc(100vh - 32px);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.18);
}

.ds-form__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid #eee;
}

.ds-form__title {
  font-size: 15px;
  font-weight: 600;
  margin: 0;
}

.ds-form__close {
  background: transparent;
  border: none;
  font-size: 22px;
  line-height: 1;
  cursor: pointer;
  color: #888;
}

.ds-form__body {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  overflow-y: auto;
}

.ds-form__check {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #333;
}

.ds-form__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ds-form__label {
  font-size: 12px;
  color: #555;
}

.ds-form__input {
  font-size: 13px;
  padding: 8px 10px;
  border: 1px solid #ccc;
  border-radius: 4px;
  outline: none;
  transition: border-color 0.12s;
  font-family: inherit;
}

.ds-form__input:focus {
  border-color: #2a9d8f;
}

.ds-form__hint {
  font-size: 11px;
  color: #777;
}

.ds-form__hint--inline {
  display: block;
  margin-top: 2px;
}

.ds-form__error {
  font-size: 11px;
  color: #c62828;
}

.ds-form__submit-error {
  margin: 0;
  font-size: 12px;
  padding: 6px 10px;
  background: #f8d7da;
  color: #721c24;
  border-radius: 4px;
}

.ds-form__test-result {
  margin: 0;
  font-size: 12px;
  padding: 6px 10px;
  border-radius: 4px;
  word-break: break-word;
}

.ds-form__test-result--ok {
  background: #d4edda;
  color: #1b5e20;
}

.ds-form__test-result--error {
  background: #f8d7da;
  color: #721c24;
}

.ds-form__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid #eee;
}

.ds-form__primary {
  font-size: 13px;
  padding: 8px 16px;
  border: 1px solid #2a9d8f;
  background: #2a9d8f;
  color: #ffffff;
  border-radius: 4px;
  cursor: pointer;
}

.ds-form__primary:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.ds-form__secondary {
  font-size: 13px;
  padding: 8px 16px;
  border: 1px solid #ccc;
  background: #ffffff;
  color: #333;
  border-radius: 4px;
  cursor: pointer;
}

.ds-form__secondary:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.ds-form__footer-spacer {
  flex: 1 1 auto;
}
</style>
