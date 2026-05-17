<script setup lang="ts">
import { ref } from 'vue';
import { RouterLink } from 'vue-router';
import MonitorPointForm from '@/components/MonitorPointForm.vue';
import MonitorPointList from '@/components/MonitorPointList.vue';
import { useMonitorPoints } from '@/composables/useMonitorPoints';
import type { MonitorPoint, MonitorPointCreateInput } from '@/types/monitorPoint';

const { points, error, isLoading, refresh, create, update, remove } = useMonitorPoints({
  autoPoll: false,
});

const formOpen = ref(false);
const submitting = ref(false);
const submitError = ref<string | null>(null);
const editingPoint = ref<MonitorPoint | null>(null);
const toast = ref<{ tone: 'ok' | 'error'; message: string } | null>(null);

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
