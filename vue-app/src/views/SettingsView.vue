<script setup lang="ts">
import { ref } from 'vue';
import { RouterLink } from 'vue-router';
import DiscordSettingsForm from '@/components/DiscordSettingsForm.vue';
import MonitorPointForm from '@/components/MonitorPointForm.vue';
import MonitorPointList from '@/components/MonitorPointList.vue';
import { useDiscordSettings } from '@/composables/useDiscordSettings';
import { useMonitorPoints } from '@/composables/useMonitorPoints';
import type { DiscordSettingsUpdateInput } from '@/types/discordSettings';
import type { MonitorPoint, MonitorPointCreateInput } from '@/types/monitorPoint';

const { points, error, isLoading, refresh, create, update, remove } = useMonitorPoints({
  autoPoll: false,
});

const {
  settings: discordSettings,
  error: discordError,
  isLoading: discordLoading,
  refresh: refreshDiscord,
  update: updateDiscord,
  remove: removeDiscord,
} = useDiscordSettings();

const formOpen = ref(false);
const submitting = ref(false);
const submitError = ref<string | null>(null);
const editingPoint = ref<MonitorPoint | null>(null);
const toast = ref<{ tone: 'ok' | 'error'; message: string } | null>(null);

const discordFormOpen = ref(false);
const discordSubmitting = ref(false);
const discordSubmitError = ref<string | null>(null);

function openForm() {
  submitError.value = null;
  editingPoint.value = null;
  formOpen.value = true;
}

function openEditForm(point: MonitorPoint) {
  submitError.value = null;
  editingPoint.value = point;
  formOpen.value = true;
}

function closeForm() {
  formOpen.value = false;
  editingPoint.value = null;
}

async function handleSubmit(input: MonitorPointCreateInput) {
  submitting.value = true;
  submitError.value = null;
  try {
    const target = editingPoint.value;
    const result = target ? await update(target.id, input) : await create(input);
    if (result.status === 'ok') {
      formOpen.value = false;
      const verb = target ? '已更新' : '已新增';
      flashToast('ok', `${verb}監測點「${result.point.name}」`);
      editingPoint.value = null;
    } else {
      submitError.value = result.error;
    }
  } finally {
    submitting.value = false;
  }
}

async function handleDelete(id: string) {
  const target = points.value.find((p) => p.id === id);
  const result = await remove(id);
  if (result.status === 'ok') {
    flashToast('ok', target ? `已刪除監測點「${target.name}」` : '已刪除監測點');
  } else {
    flashToast('error', `刪除失敗: ${result.error}`);
  }
}

function openDiscordForm() {
  discordSubmitError.value = null;
  discordFormOpen.value = true;
}

function closeDiscordForm() {
  discordFormOpen.value = false;
}

async function handleDiscordDelete() {
  const ok = window.confirm('確定要刪除 Discord 通知設定? Token 與 channel id 都會被清除.');
  if (!ok) return;
  const result = await removeDiscord();
  if (result.status === 'ok') {
    flashToast('ok', '已刪除 Discord 通知設定');
  } else {
    flashToast('error', `刪除失敗: ${result.error}`);
  }
}

async function handleDiscordSubmit(input: DiscordSettingsUpdateInput) {
  discordSubmitting.value = true;
  discordSubmitError.value = null;
  try {
    const result = await updateDiscord(input);
    if (result.status === 'ok') {
      discordFormOpen.value = false;
      flashToast('ok', '已更新 Discord 通知設定');
    } else {
      discordSubmitError.value = result.error;
    }
  } finally {
    discordSubmitting.value = false;
  }
}

let toastTimer: ReturnType<typeof setTimeout> | null = null;
function flashToast(tone: 'ok' | 'error', message: string) {
  toast.value = { tone, message };
  if (toastTimer) clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    toast.value = null;
  }, 3500);
}
</script>

<template>
  <div class="settings-page">
    <header class="settings-header">
      <div class="settings-header__left">
        <RouterLink to="/" class="settings-header__back" aria-label="返回地圖">← 返回地圖</RouterLink>
        <h1 class="settings-header__title">設定</h1>
      </div>
    </header>

    <main class="settings-main">
      <section class="settings-section">
        <div class="settings-section__head">
          <div>
            <h2 class="settings-section__title">監測點</h2>
            <p class="settings-section__desc">
              新增關心的地點 (例如住家、公司), 並會以藍色標記顯示於救援地圖.
            </p>
          </div>
          <button type="button" class="settings-section__cta" @click="openForm">+ 新增監測點</button>
        </div>

        <MonitorPointList
          :points="points"
          :is-loading="isLoading"
          :error="error"
          @delete="handleDelete"
          @edit="openEditForm"
          @retry="refresh"
        />
      </section>

      <section class="settings-section">
        <div class="settings-section__head">
          <div>
            <h2 class="settings-section__title">Discord 通知</h2>
            <p class="settings-section__desc">
              監測點被救援事件命中時, 透過 Discord bot 發送訊息至指定 channel.
            </p>
          </div>
          <button type="button" class="settings-section__cta" @click="openDiscordForm">
            {{ discordSettings?.hasToken ? '編輯' : '設定' }}
          </button>
        </div>

        <div v-if="discordLoading && !discordSettings" class="ds-card ds-card--muted">載入中...</div>
        <div v-else-if="discordError" class="ds-card ds-card--error">
          <div>載入失敗: {{ discordError }}</div>
          <button type="button" class="ds-card__retry" @click="refreshDiscord">重試</button>
        </div>
        <div v-else-if="discordSettings" class="ds-card">
          <div class="ds-card__row">
            <span class="ds-card__label">狀態</span>
            <span
              class="ds-badge"
              :class="discordSettings.enabled ? 'ds-badge--ok' : 'ds-badge--muted'"
            >
              {{ discordSettings.enabled ? '已啟用' : '已停用' }}
            </span>
          </div>
          <div class="ds-card__row">
            <span class="ds-card__label">Bot Token</span>
            <span class="ds-card__value">
              {{ discordSettings.tokenPreview ?? '(未設定)' }}
            </span>
          </div>
          <div class="ds-card__row">
            <span class="ds-card__label">Channel ID</span>
            <span class="ds-card__value">
              {{
                discordSettings.channelId && discordSettings.channelId !== '0'
                  ? discordSettings.channelId
                  : '(未設定)'
              }}
            </span>
          </div>
          <div class="ds-card__row">
            <span class="ds-card__label">通知所有災訊</span>
            <span
              class="ds-badge"
              :class="discordSettings.notifyAllAlerts ? 'ds-badge--ok' : 'ds-badge--muted'"
            >
              {{ discordSettings.notifyAllAlerts ? '已啟用' : '已停用' }}
            </span>
          </div>
          <div
            v-if="discordSettings.hasToken || (discordSettings.channelId && discordSettings.channelId !== '0')"
            class="ds-card__actions"
          >
            <button type="button" class="ds-card__delete" @click="handleDiscordDelete">
              刪除設定
            </button>
          </div>
        </div>
      </section>

      <div v-if="toast" class="settings-toast" :class="`settings-toast--${toast.tone}`">
        {{ toast.message }}
      </div>
    </main>

    <MonitorPointForm
      :open="formOpen"
      :submitting="submitting"
      :submit-error="submitError"
      :initial-value="editingPoint"
      @close="closeForm"
      @submit="handleSubmit"
    />

    <DiscordSettingsForm
      :open="discordFormOpen"
      :submitting="discordSubmitting"
      :submit-error="discordSubmitError"
      :current="discordSettings"
      @close="closeDiscordForm"
      @submit="handleDiscordSubmit"
    />
  </div>
</template>

<style scoped>
.settings-page {
  min-height: 100vh;
  background: #f5f5f7;
  color: #222;
  display: flex;
  flex-direction: column;
}

.settings-header {
  background: #ffffff;
  border-bottom: 1px solid #e0e0e0;
  padding: 10px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.settings-header__left {
  display: flex;
  align-items: center;
  gap: 16px;
}

.settings-header__back {
  font-size: 13px;
  color: #2a9d8f;
  text-decoration: none;
}

.settings-header__back:hover {
  text-decoration: underline;
}

.settings-header__title {
  font-size: 16px;
  margin: 0;
  font-weight: 600;
}

.settings-main {
  flex: 1 1 auto;
  padding: 16px;
  max-width: 720px;
  width: 100%;
  margin: 0 auto;
}

.settings-section {
  margin-bottom: 24px;
}

.settings-section__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.settings-section__title {
  font-size: 14px;
  margin: 0;
  font-weight: 600;
}

.settings-section__desc {
  margin: 4px 0 0 0;
  font-size: 12px;
  color: #666;
}

.settings-section__cta {
  font-size: 13px;
  padding: 8px 14px;
  border: 1px solid #2a9d8f;
  background: #2a9d8f;
  color: #ffffff;
  border-radius: 4px;
  cursor: pointer;
  white-space: nowrap;
}

.ds-card {
  background: #ffffff;
  border: 1px solid #e0e0e0;
  border-radius: 6px;
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.ds-card--muted {
  color: #888;
  font-size: 12px;
}

.ds-card--error {
  border-color: #f5c6cb;
  background: #fff5f5;
  color: #721c24;
  font-size: 12px;
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.ds-card__retry {
  font-size: 12px;
  padding: 4px 10px;
  border: 1px solid #c62828;
  background: #ffffff;
  color: #c62828;
  border-radius: 4px;
  cursor: pointer;
}

.ds-card__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-size: 13px;
}

.ds-card__label {
  color: #666;
  font-size: 12px;
}

.ds-card__value {
  color: #222;
  font-variant-numeric: tabular-nums;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 12px;
}

.ds-card__actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 4px;
  border-top: 1px solid #f0f0f0;
}

.ds-card__delete {
  font-size: 12px;
  padding: 6px 12px;
  border: 1px solid #c62828;
  background: #ffffff;
  color: #c62828;
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.12s;
}

.ds-card__delete:hover {
  background: #ffebee;
}

.ds-badge {
  font-size: 11px;
  padding: 3px 8px;
  border-radius: 999px;
  font-weight: 600;
}

.ds-badge--ok {
  background: #d4edda;
  color: #1b5e20;
}

.ds-badge--muted {
  background: #eceff1;
  color: #555;
}

.settings-toast {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  padding: 10px 16px;
  border-radius: 4px;
  font-size: 13px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  z-index: 800;
}

.settings-toast--ok {
  background: #2a9d8f;
  color: #ffffff;
}

.settings-toast--error {
  background: #c62828;
  color: #ffffff;
}
</style>
