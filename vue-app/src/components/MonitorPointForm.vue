<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue';
import L, {
  type Circle,
  type LeafletMouseEvent,
  type Map as LeafletMap,
  type Marker,
} from 'leaflet';
import { searchGeocode } from '@/api/geocode';
import {
  DEFAULT_MONITOR_POINT_RADIUS_METERS,
  MAX_MONITOR_POINT_RADIUS_METERS,
  MIN_MONITOR_POINT_RADIUS_METERS,
  type GeocodeResult,
  type MonitorPoint,
  type MonitorPointCreateInput,
} from '@/types/monitorPoint';

type TabId = 'gps' | 'search' | 'manual';

const FORM_MAP_CENTER: [number, number] = [25.0124, 121.4651];
const FORM_MAP_ZOOM = 11;
const FORM_MAP_EDIT_ZOOM = 15;

const props = defineProps<{
  open: boolean;
  submitting?: boolean;
  submitError?: string | null;
  initialValue?: MonitorPoint | null;
}>();

const mode = computed<'create' | 'edit'>(() => (props.initialValue ? 'edit' : 'create'));
const dialogTitle = computed(() => (mode.value === 'edit' ? '編輯監測點' : '新增監測點'));
const submitLabel = computed(() => (mode.value === 'edit' ? '更新' : '儲存'));
const submittingLabel = computed(() => (mode.value === 'edit' ? '更新中...' : '儲存中...'));

const emit = defineEmits<{
  (event: 'close'): void;
  (event: 'submit', payload: MonitorPointCreateInput): void;
}>();

const activeTab = ref<TabId>('gps');
const name = ref('');
const radius = ref<number>(DEFAULT_MONITOR_POINT_RADIUS_METERS);
const radiusInput = ref<string>(String(DEFAULT_MONITOR_POINT_RADIUS_METERS));
const selectedCoords = ref<{ lat: number; lng: number } | null>(null);

const gpsState = ref<'idle' | 'loading' | 'ok' | 'denied' | 'unavailable' | 'error'>('idle');
const gpsErrorDetail = ref<string | null>(null);

const searchQuery = ref('');
const searchResults = ref<GeocodeResult[]>([]);
const searchState = ref<'idle' | 'loading' | 'ok' | 'empty' | 'error'>('idle');
const searchError = ref<string | null>(null);
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null;
let searchController: AbortController | null = null;

const manualLat = ref('');
const manualLng = ref('');
const manualLatError = ref<string | null>(null);
const manualLngError = ref<string | null>(null);

const nameInput = ref<HTMLInputElement | null>(null);
const mapContainer = ref<HTMLDivElement | null>(null);
let formMap: LeafletMap | null = null;
let formMarker: Marker | null = null;
let formCircle: Circle | null = null;

const isGeolocationSupported = typeof navigator !== 'undefined' && 'geolocation' in navigator;

const nameError = computed<string | null>(() => {
  const trimmed = name.value.trim();
  if (trimmed.length === 0) return '請輸入名稱';
  if (trimmed.length > 50) return '名稱最多 50 個字元';
  return null;
});

const radiusError = computed<string | null>(() => {
  const raw = radiusInput.value.trim();
  if (raw === '') return '請輸入半徑';
  const n = Number(raw);
  if (!Number.isFinite(n)) return '半徑必須是數值';
  if (!Number.isInteger(n)) return '半徑必須是整數公尺';
  if (n < MIN_MONITOR_POINT_RADIUS_METERS || n > MAX_MONITOR_POINT_RADIUS_METERS) {
    return `半徑必須介於 ${MIN_MONITOR_POINT_RADIUS_METERS} ~ ${MAX_MONITOR_POINT_RADIUS_METERS} 公尺`;
  }
  return null;
});

const canSubmit = computed(
  () =>
    !nameError.value &&
    !radiusError.value &&
    selectedCoords.value !== null &&
    !props.submitting,
);

watch(
  () => props.open,
  async (opened) => {
    if (opened) {
      resetForm();
      await nextTick();
      nameInput.value?.focus();
      ensureMap();
      await nextTick();
      // Map container may have just become visible; force size recompute.
      formMap?.invalidateSize();
      syncMarker();
    } else {
      destroyMap();
    }
  },
);

watch(selectedCoords, () => {
  syncMarker();
});

watch(radiusInput, (raw) => {
  const n = Number(raw);
  if (Number.isFinite(n) && Number.isInteger(n)) {
    radius.value = n;
  }
});

watch(radius, () => {
  syncCircleRadius();
});

watch(activeTab, (tab) => {
  if (tab !== 'search') {
    if (searchController) {
      searchController.abort();
      searchController = null;
    }
    if (searchDebounceTimer) {
      clearTimeout(searchDebounceTimer);
      searchDebounceTimer = null;
    }
  }
});

watch(searchQuery, (q) => {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
  if (!q.trim()) {
    searchState.value = 'idle';
    searchResults.value = [];
    searchError.value = null;
    return;
  }
  searchState.value = 'loading';
  searchDebounceTimer = setTimeout(() => {
    void runGeocode(q.trim());
  }, 300);
});

watch([manualLat, manualLng, activeTab], () => {
  if (activeTab.value !== 'manual') return;
  validateManual();
});

function resetForm() {
  const initial = props.initialValue;
  if (initial) {
    activeTab.value = 'manual';
    name.value = initial.name;
    const lat = Number(initial.latitude.toFixed(6));
    const lng = Number(initial.longitude.toFixed(6));
    selectedCoords.value = { lat, lng };
    manualLat.value = String(lat);
    manualLng.value = String(lng);
    radius.value = initial.radius;
    radiusInput.value = String(initial.radius);
  } else {
    activeTab.value = 'gps';
    name.value = '';
    selectedCoords.value = null;
    manualLat.value = '';
    manualLng.value = '';
    radius.value = DEFAULT_MONITOR_POINT_RADIUS_METERS;
    radiusInput.value = String(DEFAULT_MONITOR_POINT_RADIUS_METERS);
  }
  gpsState.value = 'idle';
  gpsErrorDetail.value = null;
  searchQuery.value = '';
  searchResults.value = [];
  searchState.value = 'idle';
  searchError.value = null;
  manualLatError.value = null;
  manualLngError.value = null;
}

function switchTab(tab: TabId) {
  activeTab.value = tab;
  selectedCoords.value = null;
  if (tab === 'gps') {
    gpsState.value = isGeolocationSupported ? 'idle' : 'unavailable';
  }
  if (tab === 'manual') {
    validateManual();
  }
}

function requestGps() {
  if (!isGeolocationSupported) {
    gpsState.value = 'unavailable';
    return;
  }
  gpsState.value = 'loading';
  gpsErrorDetail.value = null;
  navigator.geolocation.getCurrentPosition(
    (position) => {
      const lat = Number(position.coords.latitude.toFixed(6));
      const lng = Number(position.coords.longitude.toFixed(6));
      selectedCoords.value = { lat, lng };
      gpsState.value = 'ok';
    },
    (err) => {
      selectedCoords.value = null;
      if (err.code === err.PERMISSION_DENIED) {
        gpsState.value = 'denied';
      } else {
        gpsState.value = 'error';
        gpsErrorDetail.value = err.message;
      }
    },
    { enableHighAccuracy: false, timeout: 10_000, maximumAge: 60_000 },
  );
}

async function runGeocode(query: string) {
  if (searchController) searchController.abort();
  const current = new AbortController();
  searchController = current;
  searchError.value = null;

  const result = await searchGeocode(query, 5, current.signal);
  if (searchController !== current) return;

  if (result.status === 'ok') {
    searchResults.value = result.body;
    searchState.value = result.body.length === 0 ? 'empty' : 'ok';
  } else if (result.error === 'aborted') {
    // ignore
  } else {
    searchResults.value = [];
    searchState.value = 'error';
    searchError.value = result.error;
  }
}

function chooseSearchResult(item: GeocodeResult) {
  selectedCoords.value = { lat: item.latitude, lng: item.longitude };
  if (!name.value.trim()) {
    name.value = item.displayName.slice(0, 50);
  }
}

function validateManual() {
  selectedCoords.value = null;
  manualLatError.value = null;
  manualLngError.value = null;

  if (manualLat.value === '' && manualLng.value === '') return;

  const lat = Number(manualLat.value);
  const lng = Number(manualLng.value);

  if (!Number.isFinite(lat)) {
    manualLatError.value = 'lat 必須是數值';
  } else if (lat < -90 || lat > 90) {
    manualLatError.value = 'lat 必須介於 -90 ~ 90';
  }

  if (!Number.isFinite(lng)) {
    manualLngError.value = 'lng 必須是數值';
  } else if (lng < -180 || lng > 180) {
    manualLngError.value = 'lng 必須介於 -180 ~ 180';
  }

  if (!manualLatError.value && !manualLngError.value) {
    selectedCoords.value = { lat, lng };
  }
}

function close() {
  emit('close');
}

function submit() {
  if (!canSubmit.value || !selectedCoords.value) return;
  emit('submit', {
    name: name.value.trim(),
    latitude: selectedCoords.value.lat,
    longitude: selectedCoords.value.lng,
    radius: radius.value,
  });
}

function ensureMap() {
  if (!mapContainer.value || formMap) return;
  const initial = selectedCoords.value;
  const center: [number, number] = initial ? [initial.lat, initial.lng] : FORM_MAP_CENTER;
  const zoom = initial ? FORM_MAP_EDIT_ZOOM : FORM_MAP_ZOOM;
  formMap = L.map(mapContainer.value, {
    center,
    zoom,
    zoomControl: true,
  });
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  }).addTo(formMap);
  formMap.on('click', onMapClick);
}

function destroyMap() {
  if (formMarker) {
    formMarker.off();
    formMarker.remove();
    formMarker = null;
  }
  if (formCircle) {
    formCircle.remove();
    formCircle = null;
  }
  if (formMap) {
    formMap.off();
    formMap.remove();
    formMap = null;
  }
}

function syncMarker() {
  if (!formMap) return;
  const coords = selectedCoords.value;
  if (!coords) {
    if (formMarker) {
      formMarker.off();
      formMarker.remove();
      formMarker = null;
    }
    if (formCircle) {
      formCircle.remove();
      formCircle = null;
    }
    return;
  }
  const latlng: [number, number] = [coords.lat, coords.lng];
  if (!formMarker) {
    formMarker = L.marker(latlng, { draggable: true }).addTo(formMap);
    formMarker.on('dragend', onMarkerDragEnd);
  } else {
    formMarker.setLatLng(latlng);
  }
  if (!formCircle) {
    formCircle = L.circle(latlng, {
      radius: radius.value,
      color: '#2a9d8f',
      weight: 1,
      opacity: 0.6,
      fillColor: '#2a9d8f',
      fillOpacity: 0.15,
      interactive: false,
    }).addTo(formMap);
  } else {
    formCircle.setLatLng(latlng);
  }
  formMap.panTo(latlng);
}

function syncCircleRadius() {
  if (!formCircle) return;
  formCircle.setRadius(radius.value);
}

function onMapClick(event: LeafletMouseEvent) {
  const lat = Number(event.latlng.lat.toFixed(6));
  const lng = Number(event.latlng.lng.toFixed(6));
  applyMapCoords(lat, lng);
}

function onMarkerDragEnd() {
  if (!formMarker) return;
  const pos = formMarker.getLatLng();
  const lat = Number(pos.lat.toFixed(6));
  const lng = Number(pos.lng.toFixed(6));
  applyMapCoords(lat, lng);
}

function applyMapCoords(lat: number, lng: number) {
  selectedCoords.value = { lat, lng };
  if (activeTab.value === 'manual') {
    manualLat.value = String(lat);
    manualLng.value = String(lng);
    manualLatError.value = null;
    manualLngError.value = null;
  }
}

function formatCoord(value: number): string {
  return value.toFixed(6);
}

onBeforeUnmount(() => {
  if (searchController) searchController.abort();
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
  destroyMap();
});
</script>

<template>
  <div v-if="open" class="mp-form-backdrop" role="presentation">
    <div class="mp-form" role="dialog" aria-modal="true" aria-labelledby="mp-form-title">
      <header class="mp-form__header">
        <h2 id="mp-form-title" class="mp-form__title">{{ dialogTitle }}</h2>
        <button type="button" class="mp-form__close" aria-label="關閉" @click="close">×</button>
      </header>

      <div class="mp-form__body">
        <label class="mp-form__field">
          <span class="mp-form__label">名稱</span>
          <input
            ref="nameInput"
            v-model="name"
            type="text"
            maxlength="50"
            class="mp-form__input"
            placeholder="例如: 家裡 / 公司"
          />
          <span v-if="nameError" class="mp-form__error">{{ nameError }}</span>
        </label>

        <label class="mp-form__field">
          <span class="mp-form__label">半徑 (公尺)</span>
          <input
            v-model="radiusInput"
            type="number"
            :min="MIN_MONITOR_POINT_RADIUS_METERS"
            :max="MAX_MONITOR_POINT_RADIUS_METERS"
            step="1"
            class="mp-form__input"
            placeholder="50 ~ 50000"
          />
          <span v-if="radiusError" class="mp-form__error">{{ radiusError }}</span>
        </label>

        <div class="mp-form__tabs" role="tablist">
          <button
            type="button"
            role="tab"
            class="mp-form__tab"
            :class="{ 'mp-form__tab--active': activeTab === 'gps' }"
            :aria-selected="activeTab === 'gps'"
            @click="switchTab('gps')"
          >
            GPS
          </button>
          <button
            type="button"
            role="tab"
            class="mp-form__tab"
            :class="{ 'mp-form__tab--active': activeTab === 'search' }"
            :aria-selected="activeTab === 'search'"
            @click="switchTab('search')"
          >
            搜尋地址
          </button>
          <button
            type="button"
            role="tab"
            class="mp-form__tab"
            :class="{ 'mp-form__tab--active': activeTab === 'manual' }"
            :aria-selected="activeTab === 'manual'"
            @click="switchTab('manual')"
          >
            手動輸入
          </button>
        </div>

        <section v-if="activeTab === 'gps'" class="mp-form__panel">
          <button
            type="button"
            class="mp-form__primary"
            :disabled="!isGeolocationSupported || gpsState === 'loading'"
            @click="requestGps"
          >
            {{ gpsState === 'loading' ? '取得中...' : '使用目前位置' }}
          </button>
          <p v-if="!isGeolocationSupported" class="mp-form__hint mp-form__hint--warn">
            此瀏覽器不支援地理定位, 請改用搜尋或手動輸入.
          </p>
          <p v-if="gpsState === 'denied'" class="mp-form__hint mp-form__hint--warn">
            無法存取定位權限, 請改用搜尋或手動輸入.
          </p>
          <p v-if="gpsState === 'error' && gpsErrorDetail" class="mp-form__hint mp-form__hint--warn">
            取得位置失敗: {{ gpsErrorDetail }}
          </p>
          <p v-if="gpsState === 'ok' && selectedCoords" class="mp-form__hint">
            已取得位置: lat {{ formatCoord(selectedCoords.lat) }}, lng
            {{ formatCoord(selectedCoords.lng) }}
          </p>
        </section>

        <section v-if="activeTab === 'search'" class="mp-form__panel">
          <input
            v-model="searchQuery"
            type="text"
            class="mp-form__input"
            placeholder="輸入地址或地標名稱"
          />
          <p v-if="searchState === 'loading'" class="mp-form__hint">搜尋中...</p>
          <p v-else-if="searchState === 'empty'" class="mp-form__hint">找不到符合的地點.</p>
          <p v-else-if="searchState === 'error'" class="mp-form__hint mp-form__hint--warn">
            搜尋失敗: {{ searchError }}
          </p>
          <ul v-if="searchState === 'ok'" class="mp-form__results">
            <li
              v-for="(item, index) in searchResults"
              :key="`${item.displayName}-${index}`"
              class="mp-form__result"
              :class="{
                'mp-form__result--active':
                  selectedCoords && selectedCoords.lat === item.latitude && selectedCoords.lng === item.longitude,
              }"
              @click="chooseSearchResult(item)"
            >
              <div class="mp-form__result-name">{{ item.displayName }}</div>
              <div class="mp-form__result-coords">
                lat {{ formatCoord(item.latitude) }}, lng {{ formatCoord(item.longitude) }}
              </div>
            </li>
          </ul>
        </section>

        <section v-if="activeTab === 'manual'" class="mp-form__panel">
          <label class="mp-form__field">
            <span class="mp-form__label">lat</span>
            <input
              v-model="manualLat"
              type="number"
              step="any"
              class="mp-form__input"
              placeholder="-90 ~ 90"
            />
            <span v-if="manualLatError" class="mp-form__error">{{ manualLatError }}</span>
          </label>
          <label class="mp-form__field">
            <span class="mp-form__label">lng</span>
            <input
              v-model="manualLng"
              type="number"
              step="any"
              class="mp-form__input"
              placeholder="-180 ~ 180"
            />
            <span v-if="manualLngError" class="mp-form__error">{{ manualLngError }}</span>
          </label>
          <p
            v-if="selectedCoords && !manualLatError && !manualLngError"
            class="mp-form__hint"
          >
            目前座標: lat {{ formatCoord(selectedCoords.lat) }}, lng
            {{ formatCoord(selectedCoords.lng) }}
          </p>
        </section>

        <section class="mp-form__map-section">
          <div class="mp-form__map-head">
            <span class="mp-form__label">預覽地圖</span>
            <span class="mp-form__map-hint">
              {{
                selectedCoords
                  ? '可拖曳標記或點擊地圖微調位置'
                  : '透過上方任一方式選定座標, 或直接點擊地圖'
              }}
            </span>
          </div>
          <div ref="mapContainer" class="mp-form__map"></div>
        </section>

        <p v-if="submitError" class="mp-form__submit-error">儲存失敗: {{ submitError }}</p>
      </div>

      <footer class="mp-form__footer">
        <button type="button" class="mp-form__secondary" @click="close">取消</button>
        <button
          type="button"
          class="mp-form__primary"
          :disabled="!canSubmit"
          @click="submit"
        >
          {{ submitting ? submittingLabel : submitLabel }}
        </button>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.mp-form-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 16px;
}

.mp-form {
  background: #ffffff;
  border-radius: 8px;
  width: 100%;
  max-width: 480px;
  max-height: calc(100vh - 32px);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.18);
}

.mp-form__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid #eee;
}

.mp-form__title {
  font-size: 15px;
  font-weight: 600;
  margin: 0;
}

.mp-form__close {
  background: transparent;
  border: none;
  font-size: 22px;
  line-height: 1;
  cursor: pointer;
  color: #888;
}

.mp-form__body {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  overflow-y: auto;
}

.mp-form__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.mp-form__label {
  font-size: 12px;
  color: #555;
}

.mp-form__input {
  font-size: 13px;
  padding: 8px 10px;
  border: 1px solid #ccc;
  border-radius: 4px;
  outline: none;
  transition: border-color 0.12s;
}

.mp-form__input:focus {
  border-color: #2a9d8f;
}

.mp-form__error {
  font-size: 11px;
  color: #c62828;
}

.mp-form__tabs {
  display: flex;
  gap: 4px;
  border-bottom: 1px solid #eee;
}

.mp-form__tab {
  flex: 1 1 auto;
  font-size: 13px;
  padding: 8px 0;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  color: #555;
}

.mp-form__tab--active {
  color: #2a9d8f;
  border-bottom-color: #2a9d8f;
  font-weight: 600;
}

.mp-form__panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.mp-form__primary {
  font-size: 13px;
  padding: 8px 16px;
  border: 1px solid #2a9d8f;
  background: #2a9d8f;
  color: #ffffff;
  border-radius: 4px;
  cursor: pointer;
}

.mp-form__primary:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.mp-form__secondary {
  font-size: 13px;
  padding: 8px 16px;
  border: 1px solid #ccc;
  background: #ffffff;
  color: #333;
  border-radius: 4px;
  cursor: pointer;
}

.mp-form__hint {
  margin: 0;
  font-size: 12px;
  color: #555;
}

.mp-form__hint--warn {
  color: #b26a00;
}

.mp-form__map-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.mp-form__map-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}

.mp-form__map-hint {
  font-size: 11px;
  color: #888;
}

.mp-form__map {
  height: 220px;
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  overflow: hidden;
}

.mp-form__submit-error {
  margin: 0;
  font-size: 12px;
  padding: 6px 10px;
  background: #f8d7da;
  color: #721c24;
  border-radius: 4px;
}

.mp-form__results {
  list-style: none;
  margin: 0;
  padding: 0;
  border: 1px solid #eee;
  border-radius: 4px;
  max-height: 180px;
  overflow-y: auto;
}

.mp-form__result {
  padding: 8px 10px;
  font-size: 12px;
  cursor: pointer;
  border-bottom: 1px solid #f0f0f0;
}

.mp-form__result:last-child {
  border-bottom: none;
}

.mp-form__result:hover {
  background: #f5f9ff;
}

.mp-form__result--active {
  background: #e3f2fd;
}

.mp-form__result-name {
  color: #222;
}

.mp-form__result-coords {
  margin-top: 2px;
  color: #777;
  font-variant-numeric: tabular-nums;
}

.mp-form__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid #eee;
}
</style>
