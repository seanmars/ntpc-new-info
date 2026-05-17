<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import L, { type Circle, type Map as LeafletMap } from 'leaflet';
import type { LatLngTuple, RescueFeature, RescueFeatureCollection } from '@/types/rescue';
import type { MonitorPoint } from '@/types/monitorPoint';
import { colorFor, featureKey } from '@/utils/rescue';

const props = withDefaults(
  defineProps<{
    featureCollection: RescueFeatureCollection | null;
    monitorPoints?: MonitorPoint[];
    focusedMonitorId?: string | null;
    showRangeCircle?: boolean;
  }>(),
  {
    monitorPoints: () => [],
    focusedMonitorId: null,
    showRangeCircle: true,
  },
);

const emit = defineEmits<{
  (event: 'hover-monitor', id: string | null): void;
}>();

const NEW_TAIPEI_CENTER: LatLngTuple = [25.0124, 121.4651];
const DEFAULT_ZOOM = 11;
const MONITOR_COLOR = '#1976d2';

const mapContainer = ref<HTMLDivElement | null>(null);
let map: LeafletMap | null = null;
let rescueLayer: L.FeatureGroup | null = null;
let monitorLayer: L.FeatureGroup | null = null;
let focusCircleLayer: Circle | null = null;
let hasFitOnce = false;
let resizeObserver: ResizeObserver | null = null;
const eventMarkers = new Map<string, L.CircleMarker>();
const monitorMarkers = new Map<string, L.CircleMarker>();

function formatCoord(value: number): string {
  return value.toFixed(6);
}

function renderMonitorPoints(items: readonly MonitorPoint[]) {
  if (!map || !monitorLayer) return;
  monitorLayer.clearLayers();
  monitorMarkers.clear();
  for (const point of items) {
    const marker = L.circleMarker([point.latitude, point.longitude], {
      radius: 7,
      color: MONITOR_COLOR,
      weight: 2,
      fillColor: MONITOR_COLOR,
      fillOpacity: 0.9,
    });
    marker.bindTooltip(
      `${escapeHtml(point.name)}<br /><span style="color:#666;font-variant-numeric:tabular-nums">lat ${formatCoord(point.latitude)}, lng ${formatCoord(point.longitude)}<br />radius ${point.radius} m</span>`,
      { direction: 'top', sticky: false },
    );
    marker.on('mouseover', () => emit('hover-monitor', point.id));
    marker.on('mouseout', () => emit('hover-monitor', null));
    monitorLayer.addLayer(marker);
    monitorMarkers.set(point.id, marker);
  }
  applyFocus();
}

function escapeHtml(value: unknown): string {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function buildPopupHtml(feature: RescueFeature, color: string): string {
  const p = feature.properties;
  const caseRows = (p.caseList ?? [])
    .map((c) => {
      const label = escapeHtml(c.startPointInfo ?? '(無代號)');
      const flag = c.path && c.path.length > 0 ? '' : ' <span style="color:#888">(無路徑)</span>';
      return `<li>${label}${flag}</li>`;
    })
    .join('');

  return `
    <div style="font-size:13px;line-height:1.5;min-width:220px">
      <div style="font-weight:600;color:${color};margin-bottom:4px">
        ${escapeHtml(p.fireType ?? p.title ?? '救援事件')}
      </div>
      <div><strong>地點:</strong> ${escapeHtml(p.endPointInfo ?? '-')}</div>
      <div><strong>事件編號:</strong> ${escapeHtml(p.featureId ?? '-')}</div>
      ${caseRows ? `<div style="margin-top:6px"><strong>出勤車輛:</strong></div><ul style="margin:4px 0 0 18px;padding:0">${caseRows}</ul>` : ''}
    </div>
  `;
}

function renderFeatures(fc: RescueFeatureCollection) {
  if (!map || !rescueLayer) return;
  rescueLayer.clearLayers();
  eventMarkers.clear();

  fc.features.forEach((feature, index) => {
    const color = colorFor(index);
    const [lng, lat] = feature.geometry.coordinates;
    const eventLatLng: LatLngTuple = [lat, lng];

    const eventMarker = L.circleMarker(eventLatLng, {
      radius: 9,
      color,
      weight: 2,
      fillColor: color,
      fillOpacity: 0.85,
    }).bindPopup(buildPopupHtml(feature, color));
    rescueLayer!.addLayer(eventMarker);
    eventMarkers.set(featureKey(feature, index), eventMarker);

    for (const item of feature.properties.caseList ?? []) {
      if (item.path && item.path.length > 0) {
        const line = L.polyline(item.path, {
          color,
          weight: 4,
          opacity: 0.75,
        });
        rescueLayer!.addLayer(line);
      }
      if (item.startPoint) {
        const startMarker = L.circleMarker(item.startPoint, {
          radius: 4,
          color,
          weight: 2,
          fillColor: '#ffffff',
          fillOpacity: 1,
        });
        if (item.startPointInfo) {
          startMarker.bindTooltip(item.startPointInfo, { direction: 'top' });
        }
        rescueLayer!.addLayer(startMarker);
      }
    }
  });

  if (!hasFitOnce && fc.features.length > 0) {
    const bounds = rescueLayer.getBounds();
    if (bounds.isValid()) {
      map.fitBounds(bounds, { padding: [40, 40], maxZoom: 15 });
      hasFitOnce = true;
    }
  }
}

function applyFocus() {
  if (!map) return;
  const id = props.focusedMonitorId;
  const point = id ? props.monitorPoints.find((p) => p.id === id) : null;
  if (!point || !props.showRangeCircle) {
    if (focusCircleLayer) {
      focusCircleLayer.remove();
      focusCircleLayer = null;
    }
    return;
  }
  const latlng: LatLngTuple = [point.latitude, point.longitude];
  if (!focusCircleLayer) {
    focusCircleLayer = L.circle(latlng, {
      radius: point.radius,
      color: MONITOR_COLOR,
      weight: 1,
      opacity: 0.4,
      fillColor: MONITOR_COLOR,
      fillOpacity: 0.08,
      interactive: false,
    }).addTo(map);
  } else {
    focusCircleLayer.setLatLng(latlng);
    focusCircleLayer.setRadius(point.radius);
    if (!map.hasLayer(focusCircleLayer)) {
      focusCircleLayer.addTo(map);
    }
  }
}

function handleResize() {
  map?.invalidateSize();
}

onMounted(() => {
  if (!mapContainer.value) return;

  map = L.map(mapContainer.value, {
    center: NEW_TAIPEI_CENTER,
    zoom: DEFAULT_ZOOM,
    preferCanvas: true,
  });

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  }).addTo(map);

  rescueLayer = L.featureGroup().addTo(map);
  monitorLayer = L.featureGroup().addTo(map);

  if (props.featureCollection) {
    renderFeatures(props.featureCollection);
  }
  renderMonitorPoints(props.monitorPoints);

  window.addEventListener('resize', handleResize);
  resizeObserver = new ResizeObserver(handleResize);
  resizeObserver.observe(mapContainer.value);
});

watch(
  () => props.featureCollection,
  (fc) => {
    if (fc) renderFeatures(fc);
    else rescueLayer?.clearLayers();
  },
);

watch(
  () => props.monitorPoints,
  (points) => {
    renderMonitorPoints(points ?? []);
  },
);

watch(
  () => props.focusedMonitorId,
  () => {
    applyFocus();
  },
);

watch(
  () => props.showRangeCircle,
  () => {
    applyFocus();
  },
);

function focusFeature(featureId: string) {
  const marker = eventMarkers.get(featureId);
  if (!marker || !map) return;
  const latlng = marker.getLatLng();
  const targetZoom = Math.max(map.getZoom(), 15);
  map.setView(latlng, targetZoom, { animate: true });
  marker.openPopup();
}

function focusMonitor(monitorId: string) {
  const point = props.monitorPoints.find((p) => p.id === monitorId);
  if (!point || !map) return;
  map.panTo([point.latitude, point.longitude], { animate: true });
}

defineExpose({ focusFeature, focusMonitor });

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleResize);
  resizeObserver?.disconnect();
  resizeObserver = null;
  eventMarkers.clear();
  monitorMarkers.clear();
  if (focusCircleLayer) {
    focusCircleLayer.remove();
    focusCircleLayer = null;
  }
  map?.remove();
  map = null;
  rescueLayer = null;
  monitorLayer = null;
});
</script>

<template>
  <div ref="mapContainer" class="rescue-map"></div>
</template>

<style scoped>
.rescue-map {
  width: 100%;
  height: 100%;
  min-height: 320px;
}
</style>
