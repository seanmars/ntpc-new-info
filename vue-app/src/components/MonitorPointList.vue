<script setup lang="ts">
import type { MonitorPoint } from '@/types/monitorPoint';

defineProps<{
  points: MonitorPoint[];
  isLoading: boolean;
  error: string | null;
}>();

const emit = defineEmits<{
  (event: 'delete', id: string): void;
  (event: 'edit', point: MonitorPoint): void;
  (event: 'retry'): void;
}>();

function formatCoord(value: number): string {
  return value.toFixed(6);
}

function confirmDelete(point: MonitorPoint) {
  const ok = window.confirm(`確定要刪除「${point.name}」?`);
  if (ok) emit('delete', point.id);
}
</script>

<template>
  <div class="mp-list">
    <div v-if="error" class="mp-list__error">
      <span>載入失敗: {{ error }}</span>
      <button type="button" class="mp-list__retry" @click="emit('retry')">重試</button>
    </div>

    <div v-else-if="isLoading && points.length === 0" class="mp-list__hint">載入中...</div>

    <div v-else-if="points.length === 0" class="mp-list__hint">尚未新增任何監測點</div>

    <ul v-else class="mp-list__items">
      <li v-for="point in points" :key="point.id" class="mp-item">
        <div class="mp-item__main">
          <div class="mp-item__name">{{ point.name }}</div>
          <div class="mp-item__coords">
            <span class="mp-item__chip">lat {{ formatCoord(point.latitude) }}</span>
            <span class="mp-item__chip">lng {{ formatCoord(point.longitude) }}</span>
            <span class="mp-item__chip mp-item__chip--radius">半徑 {{ point.radius }} m</span>
          </div>
        </div>
        <div class="mp-item__actions">
          <button
            type="button"
            class="mp-item__edit"
            :aria-label="`編輯 ${point.name}`"
            @click="emit('edit', point)"
          >
            編輯
          </button>
          <button
            type="button"
            class="mp-item__delete"
            :aria-label="`刪除 ${point.name}`"
            @click="confirmDelete(point)"
          >
            刪除
          </button>
        </div>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.mp-list {
  background: #ffffff;
  border: 1px solid #e0e0e0;
  border-radius: 6px;
  overflow: hidden;
}

.mp-list__hint,
.mp-list__error {
  padding: 16px;
  font-size: 13px;
  color: #555;
}

.mp-list__error {
  background: #f8d7da;
  color: #721c24;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.mp-list__retry {
  font-size: 12px;
  padding: 4px 10px;
  border: 1px solid #721c24;
  background: transparent;
  color: #721c24;
  border-radius: 4px;
  cursor: pointer;
}

.mp-list__items {
  list-style: none;
  margin: 0;
  padding: 0;
}

.mp-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border-bottom: 1px solid #f0f0f0;
}

.mp-item:last-child {
  border-bottom: none;
}

.mp-item__main {
  min-width: 0;
  flex: 1 1 auto;
}

.mp-item__name {
  font-size: 14px;
  font-weight: 600;
  color: #222;
}

.mp-item__coords {
  margin-top: 4px;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.mp-item__chip {
  font-size: 11px;
  padding: 2px 6px;
  background: #eef0f3;
  border-radius: 10px;
  color: #444;
  font-variant-numeric: tabular-nums;
}

.mp-item__chip--radius {
  background: #e3f2fd;
  color: #0d47a1;
}

.mp-item__actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.mp-item__edit {
  font-size: 12px;
  padding: 6px 12px;
  border: 1px solid #2a9d8f;
  background: #ffffff;
  color: #2a9d8f;
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.12s;
}

.mp-item__edit:hover {
  background: #e6f4f1;
}

.mp-item__delete {
  font-size: 12px;
  padding: 6px 12px;
  border: 1px solid #c62828;
  background: #ffffff;
  color: #c62828;
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.12s;
}

.mp-item__delete:hover {
  background: #ffebee;
}
</style>
