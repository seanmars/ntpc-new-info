<script setup lang="ts">
import { computed, ref } from 'vue';
import { RouterLink } from 'vue-router';
import RescueMap from '@/components/RescueMap.vue';
import { useRescueData } from '@/composables/useRescueData';
import { useMonitorPoints } from '@/composables/useMonitorPoints';
import { useCollapsibleGroups } from '@/composables/useCollapsibleGroups';
import type { RescueFeature } from '@/types/rescue';
import type { MonitorPoint } from '@/types/monitorPoint';
import { colorFor, featureKey } from '@/utils/rescue';

const {
  featureCollection,
  meta,
  error,
  isLoading,
  isForcing,
  cooldownRemainingSeconds,
  lastFetchedAt,
  refresh,
  forceRefresh,
} = useRescueData();
const { points: monitorPoints } = useMonitorPoints({ autoPoll: true });

type SidebarGroup = 'rescue' | 'monitor';
const { groups: sidebarGroups, toggle: toggleGroup } = useCollapsibleGroups<SidebarGroup>(
  'rescue-map.sidebar.groups',
  { rescue: true, monitor: true },
);

const { groups: monitorOptions } = useCollapsibleGroups<'showRangeCircle'>(
  'rescue-map.sidebar.monitor-options',
  { showRangeCircle: true },
);

const mapRef = ref<InstanceType<typeof RescueMap> | null>(null);
const selectedFeatureId = ref<string | null>(null);
const pinnedMonitorId = ref<string | null>(null);
const hoveredMonitorId = ref<string | null>(null);

const focusedMonitorId = computed(() => pinnedMonitorId.value ?? hoveredMonitorId.value);

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

function formatCoord(value: number): string {
  return value.toFixed(6);
}

const fetchedAtLabel = computed(() => {
  if (meta.value?.fetchedAt) return formatTime(meta.value.fetchedAt);
  if (lastFetchedAt.value) return formatTime(lastFetchedAt.value);
  return '-';
});

const features = computed(() => featureCollection.value?.features ?? []);
const featureCount = computed(() => features.value.length);
const monitorCount = computed(() => monitorPoints.value.length);

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

function handleSelectFeature(feature: RescueFeature, index: number) {
  const key = featureKey(feature, index);
  selectedFeatureId.value = key;
  pinnedMonitorId.value = null;
  mapRef.value?.focusFeature(key);
}

function handleMonitorClick(point: MonitorPoint) {
  pinnedMonitorId.value = pinnedMonitorId.value === point.id ? null : point.id;
  selectedFeatureId.value = null;
  if (pinnedMonitorId.value) {
    mapRef.value?.focusMonitor(point.id);
  }
}

function handleMonitorEnter(point: MonitorPoint) {
  hoveredMonitorId.value = point.id;
}

function handleMonitorLeave(point: MonitorPoint) {
  if (hoveredMonitorId.value === point.id) {
    hoveredMonitorId.value = null;
  }
}

function handleMapHoverMonitor(id: string | null) {
  hoveredMonitorId.value = id;
}
</script>

<template>
  <div class="rescue-page">
    <header class="status-bar">
      <div class="status-bar__main">
        <h1 class="status-bar__title">即時災訊</h1>
        <span class="status-bar__chip">資料時間: {{ fetchedAtLabel }}</span>
        <span v-if="featureCount > 0" class="status-bar__chip status-bar__chip--info">
          進行中事件: {{ featureCount }}
        </span>
        <span v-if="showEmpty" class="status-bar__chip status-bar__chip--muted"> 目前無進行中的救援事件 </span>
      </div>

      <div class="status-bar__actions">
        <RouterLink to="/settings" class="status-bar__link">設定</RouterLink>
        <button
          type="button"
          class="status-bar__refresh"
          :disabled="isLoading"
          title="重新讀取後端 cache (不會向上游 NTPC 重抓)"
          @click="refresh"
        >
          {{ isLoading ? '載入中...' : '重新整理' }}
        </button>
        <button
          type="button"
          class="status-bar__refresh status-bar__refresh--force"
          :disabled="isForcing || cooldownRemainingSeconds > 0"
          title="強制 backend 向上游 NTPC 重抓最新資料"
          @click="forceRefresh"
        >
          <template v-if="isForcing">強制更新中...</template>
          <template v-else-if="cooldownRemainingSeconds > 0">強制更新 ({{ cooldownRemainingSeconds }}s)</template>
          <template v-else>強制更新</template>
        </button>
      </div>
    </header>

    <div v-if="cooldownRemainingSeconds > 0" class="banner banner--cooldown">
      上游冷卻中, 剩餘 {{ cooldownRemainingSeconds }} 秒可再次強制更新
    </div>
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
      <aside class="sidebar" aria-label="側邊欄">
        <section
          class="group"
          :class="{ 'group--collapsed': !sidebarGroups.rescue }"
        >
          <button
            type="button"
            class="group__header"
            :aria-expanded="sidebarGroups.rescue"
            @click="toggleGroup('rescue')"
          >
            <span class="group__caret" aria-hidden="true">{{ sidebarGroups.rescue ? '▾' : '▸' }}</span>
            <span class="group__title">災情狀況 ({{ featureCount }})</span>
          </button>
          <div v-if="sidebarGroups.rescue" class="group__body">
            <ul v-if="featureCount > 0" class="group__list">
              <li
                v-for="(feature, index) in features"
                :key="featureKey(feature, index)"
                class="row row--rescue"
                :class="{ 'row--active': selectedFeatureId === featureKey(feature, index) }"
                @click="handleSelectFeature(feature, index)"
              >
                <span class="row__color" :style="{ backgroundColor: colorFor(index) }" aria-hidden="true"></span>
                <div class="row__text">
                  <div class="row__type">
                    {{ feature.properties.fireType ?? feature.properties.title ?? '救援事件' }}
                  </div>
                  <div class="row__addr">{{ feature.properties.endPointInfo ?? '-' }}</div>
                  <div class="row__meta">
                    #{{ feature.properties.featureId ?? '?' }} · 出勤 {{ feature.properties.caseList?.length ?? 0 }} 車
                  </div>
                </div>
              </li>
            </ul>
            <div v-else class="group__empty">目前無事件</div>
          </div>
        </section>

        <section
          class="group"
          :class="{ 'group--collapsed': !sidebarGroups.monitor }"
        >
          <button
            type="button"
            class="group__header"
            :aria-expanded="sidebarGroups.monitor"
            @click="toggleGroup('monitor')"
          >
            <span class="group__caret" aria-hidden="true">{{ sidebarGroups.monitor ? '▾' : '▸' }}</span>
            <span class="group__title">自設追蹤 ({{ monitorCount }})</span>
          </button>
          <div v-if="sidebarGroups.monitor" class="group__body">
            <div class="group__toolbar">
              <label class="toolbar-check">
                <input
                  type="checkbox"
                  v-model="monitorOptions.showRangeCircle"
                />
                <span>顯示範圍圓圈</span>
              </label>
            </div>
            <ul v-if="monitorCount > 0" class="group__list">
              <li
                v-for="point in monitorPoints"
                :key="point.id"
                class="row row--monitor"
                :class="{
                  'row--active': pinnedMonitorId === point.id,
                  'row--hover': pinnedMonitorId !== point.id && hoveredMonitorId === point.id,
                }"
                @click="handleMonitorClick(point)"
                @mouseenter="handleMonitorEnter(point)"
                @mouseleave="handleMonitorLeave(point)"
              >
                <span class="row__color row__color--monitor" aria-hidden="true"></span>
                <div class="row__text">
                  <div class="row__type">{{ point.name }}</div>
                  <div class="row__addr">
                    lat {{ formatCoord(point.latitude) }}, lng {{ formatCoord(point.longitude) }}
                  </div>
                  <div class="row__meta">半徑 {{ point.radius }} m</div>
                </div>
              </li>
            </ul>
            <div v-else class="group__empty">尚未新增自設追蹤點</div>
          </div>
        </section>
      </aside>

      <main class="map-wrapper">
        <RescueMap
          ref="mapRef"
          :feature-collection="featureCollection"
          :monitor-points="monitorPoints"
          :focused-monitor-id="focusedMonitorId"
          :show-range-circle="monitorOptions.showRangeCircle"
          @hover-monitor="handleMapHoverMonitor"
        />
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

.status-bar__actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.status-bar__link {
  font-size: 13px;
  color: #2a9d8f;
  text-decoration: none;
  padding: 6px 10px;
  border: 1px solid transparent;
  border-radius: 6px;
}

.status-bar__link:hover,
.status-bar__link:focus-visible {
  border-color: #2a9d8f;
  outline: none;
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

.status-bar__refresh--force {
  background: #e76f51;
  border-color: #e76f51;
}

.status-bar__refresh--force:hover:not(:disabled) {
  background: #d65a3c;
  border-color: #d65a3c;
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

.banner--cooldown {
  background: #e8eaf6;
  color: #283593;
  border-bottom-color: #c5cae9;
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

.group {
  display: flex;
  flex-direction: column;
  min-height: 0;
  border-bottom: 1px solid #eee;
}

.group:last-child {
  border-bottom: none;
}

.group:not(.group--collapsed) {
  flex: 1 1 0;
}

.group__header {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 10px 14px;
  font-size: 13px;
  font-weight: 600;
  color: #555;
  background: #fafafa;
  border: none;
  border-bottom: 1px solid #eee;
  cursor: pointer;
  text-align: left;
}

.group--collapsed .group__header {
  border-bottom: none;
}

.group__header:hover {
  background: #f0f0f3;
}

.group__caret {
  display: inline-block;
  width: 12px;
  color: #888;
  font-size: 11px;
}

.group__title {
  flex: 1 1 auto;
}

.group__body {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}

.group__list {
  list-style: none;
  margin: 0;
  padding: 0;
  overflow-y: auto;
  flex: 1 1 auto;
}

.group__toolbar {
  padding: 8px 14px;
  border-bottom: 1px solid #f0f0f0;
  background: #fcfcfd;
}

.toolbar-check {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #444;
  cursor: pointer;
  user-select: none;
}

.toolbar-check input {
  margin: 0;
  cursor: pointer;
}

.group__empty {
  padding: 16px 14px;
  font-size: 13px;
  color: #999;
  font-style: italic;
}

.row {
  display: flex;
  gap: 10px;
  padding: 10px 14px;
  cursor: pointer;
  border-bottom: 1px solid #f0f0f0;
  transition: background 0.12s;
}

.row:hover {
  background: #f5f9ff;
}

.row--active {
  background: #e3f2fd;
}

.row--hover {
  background: #f0f7ff;
}

.row__color {
  flex: 0 0 10px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  margin-top: 6px;
}

.row__color--monitor {
  background-color: #1976d2;
}

.row__text {
  flex: 1 1 auto;
  min-width: 0;
}

.row__type {
  font-size: 13px;
  font-weight: 600;
  color: #222;
}

.row__addr {
  font-size: 12px;
  color: #555;
  margin-top: 2px;
  word-break: break-all;
}

.row__meta {
  font-size: 11px;
  color: #888;
  margin-top: 3px;
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
