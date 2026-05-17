<script setup lang="ts">
import { computed, ref } from 'vue';
import RescueMap from '@/components/RescueMap.vue';
import { useRescueData } from '@/composables/useRescueData';
import type { RescueFeature } from '@/types/rescue';
import { colorFor, featureKey } from '@/utils/rescue';

const { featureCollection, meta, error, isLoading, lastFetchedAt, refresh } = useRescueData();

const mapRef = ref<InstanceType<typeof RescueMap> | null>(null);
const selectedFeatureId = ref<string | null>(null);

const timeFormatter = new Intl.DateTimeFormat('zh-TW', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
});

function formatTime(input: string | Date | null | undefined): string {
  if (!input) return '-';
  const date = input instanceof Date ? input : new Date(input);
  if (Number.isNaN(date.getTime())) return String(input);
  return timeFormatter.format(date);
}

const fetchedAtLabel = computed(() => {
  if (meta.value?.fetchedAt) return formatTime(meta.value.fetchedAt);
  if (lastFetchedAt.value) return formatTime(lastFetchedAt.value);
  return '-';
});

const features = computed(() => featureCollection.value?.features ?? []);
const featureCount = computed(() => features.value.length);

const upstreamError = computed(() => {
  if (!meta.value?.lastError) return null;
  return {
    message: meta.value.lastError,
    at: formatTime(meta.value.lastErrorAt),
  };
});

const isPending = computed(() => error.value?.kind === 'pending');
const failure = computed(() =>
  error.value && error.value.kind === 'failure'
    ? { message: error.value.message, at: formatTime(error.value.occurredAt) }
    : null,
);

const showEmpty = computed(
  () => !isPending.value && !failure.value && featureCollection.value !== null && featureCount.value === 0,
);

function handleSelect(feature: RescueFeature, index: number) {
  const key = featureKey(feature, index);
  selectedFeatureId.value = key;
  mapRef.value?.focusFeature(key);
}
</script>

<template>
  <div class="rescue-page">
    <header class="status-bar">
      <div class="status-bar__main">
        <h1 class="status-bar__title">新北市即時救援</h1>
        <span class="status-bar__chip">資料時間: {{ fetchedAtLabel }}</span>
        <span v-if="featureCount > 0" class="status-bar__chip status-bar__chip--info">
          進行中事件: {{ featureCount }}
        </span>
        <span v-if="showEmpty" class="status-bar__chip status-bar__chip--muted"> 目前無進行中的救援事件 </span>
      </div>

      <button type="button" class="status-bar__refresh" :disabled="isLoading" @click="refresh">
        {{ isLoading ? '載入中...' : '重新整理' }}
      </button>
    </header>

    <div v-if="isPending" class="banner banner--pending">資料尚未就緒, 將自動重試...</div>
    <div v-if="failure" class="banner banner--error">
      <strong>fetch 失敗:</strong> {{ failure.message }}
      <span class="banner__time">({{ failure.at }})</span>
    </div>
    <div v-if="upstreamError" class="banner banner--warning">
      <strong>後端上次抓取上游失敗:</strong> {{ upstreamError.message }}
      <span class="banner__time">({{ upstreamError.at }})</span>
    </div>

    <div class="body">
      <aside class="sidebar" aria-label="救援事件清單">
        <div class="sidebar__header">事件清單 ({{ featureCount }})</div>
        <ul v-if="featureCount > 0" class="sidebar__list">
          <li
            v-for="(feature, index) in features"
            :key="featureKey(feature, index)"
            class="sidebar__item"
            :class="{ 'sidebar__item--active': selectedFeatureId === featureKey(feature, index) }"
            @click="handleSelect(feature, index)"
          >
            <span class="sidebar__color" :style="{ backgroundColor: colorFor(index) }" aria-hidden="true"></span>
            <div class="sidebar__text">
              <div class="sidebar__type">
                {{ feature.properties.fireType ?? feature.properties.title ?? '救援事件' }}
              </div>
              <div class="sidebar__addr">{{ feature.properties.endPointInfo ?? '-' }}</div>
              <div class="sidebar__meta">
                #{{ feature.properties.featureId ?? '?' }} · 出勤 {{ feature.properties.caseList?.length ?? 0 }} 車
              </div>
            </div>
          </li>
        </ul>
        <div v-else class="sidebar__empty">目前無事件</div>
      </aside>

      <main class="map-wrapper">
        <RescueMap ref="mapRef" :feature-collection="featureCollection" />
      </main>
    </div>
  </div>
</template>

<style scoped>
.rescue-page {
  display: flex;
  flex-direction: column;
  height: 100vh;
  width: 100vw;
  background: #f5f5f7;
  color: #222;
}

.status-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 10px 16px;
  background: #ffffff;
  border-bottom: 1px solid #e0e0e0;
  flex-wrap: wrap;
}

.status-bar__main {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.status-bar__title {
  font-size: 16px;
  font-weight: 600;
  margin: 0;
}

.status-bar__chip {
  font-size: 12px;
  padding: 4px 8px;
  border-radius: 12px;
  background: #eef0f3;
  color: #333;
}

.status-bar__chip--info {
  background: #e3f2fd;
  color: #0d47a1;
}

.status-bar__chip--muted {
  background: transparent;
  color: #888;
  font-style: italic;
}

.status-bar__refresh {
  font-size: 13px;
  padding: 6px 14px;
  border: 1px solid #2a9d8f;
  background: #2a9d8f;
  color: #fff;
  border-radius: 6px;
  cursor: pointer;
  transition: opacity 0.15s;
}

.status-bar__refresh:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.banner {
  padding: 8px 16px;
  font-size: 13px;
  border-bottom: 1px solid transparent;
}

.banner--pending {
  background: #fff8e1;
  color: #6d4c41;
  border-bottom-color: #ffe082;
}

.banner--warning {
  background: #fff3cd;
  color: #856404;
  border-bottom-color: #ffeeba;
}

.banner--error {
  background: #f8d7da;
  color: #721c24;
  border-bottom-color: #f5c6cb;
}

.banner__time {
  margin-left: 8px;
  opacity: 0.7;
  font-size: 12px;
}

.body {
  flex: 1 1 auto;
  display: flex;
  min-height: 0;
}

.sidebar {
  width: 300px;
  flex: 0 0 300px;
  background: #ffffff;
  border-right: 1px solid #e0e0e0;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.sidebar__header {
  padding: 10px 14px;
  font-size: 13px;
  font-weight: 600;
  color: #555;
  border-bottom: 1px solid #eee;
  background: #fafafa;
}

.sidebar__list {
  list-style: none;
  margin: 0;
  padding: 0;
  overflow-y: auto;
  flex: 1 1 auto;
}

.sidebar__item {
  display: flex;
  gap: 10px;
  padding: 10px 14px;
  cursor: pointer;
  border-bottom: 1px solid #f0f0f0;
  transition: background 0.12s;
}

.sidebar__item:hover {
  background: #f5f9ff;
}

.sidebar__item--active {
  background: #e3f2fd;
}

.sidebar__color {
  flex: 0 0 10px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  margin-top: 6px;
}

.sidebar__text {
  flex: 1 1 auto;
  min-width: 0;
}

.sidebar__type {
  font-size: 13px;
  font-weight: 600;
  color: #222;
}

.sidebar__addr {
  font-size: 12px;
  color: #555;
  margin-top: 2px;
  word-break: break-all;
}

.sidebar__meta {
  font-size: 11px;
  color: #888;
  margin-top: 3px;
}

.sidebar__empty {
  padding: 16px 14px;
  font-size: 13px;
  color: #999;
  font-style: italic;
}

.map-wrapper {
  flex: 1 1 auto;
  position: relative;
  min-height: 0;
  min-width: 0;
}

@media (max-width: 720px) {
  .body {
    flex-direction: column;
  }
  .sidebar {
    width: 100%;
    flex: 0 0 auto;
    max-height: 40vh;
    border-right: none;
    border-bottom: 1px solid #e0e0e0;
  }
}
</style>
