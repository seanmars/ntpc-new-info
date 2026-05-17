import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue';
import { fetchLatestRescue } from '@/api/rescue';
import type { RescueFeatureCollection, RescueMeta } from '@/types/rescue';

const DEFAULT_INTERVAL_MS = 60_000;

function resolveIntervalMs(): number {
  const raw = import.meta.env.VITE_RESCUE_POLL_INTERVAL_MS;
  if (!raw) return DEFAULT_INTERVAL_MS;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_INTERVAL_MS;
}

export interface RescueErrorState {
  message: string;
  occurredAt: Date;
  // 'pending' = backend 503 (data not ready), 'failure' = anything else
  kind: 'pending' | 'failure';
}

export function useRescueData() {
  const featureCollection = shallowRef<RescueFeatureCollection | null>(null);
  const meta = shallowRef<RescueMeta | null>(null);
  const error = ref<RescueErrorState | null>(null);
  const isLoading = ref(false);
  const lastFetchedAt = ref<Date | null>(null);

  let timer: ReturnType<typeof setTimeout> | null = null;
  let controller: AbortController | null = null;
  let stopped = false;

  async function runFetch() {
    if (stopped) return;
    controller?.abort();
    const current = new AbortController();
    controller = current;
    isLoading.value = true;
    try {
      const result = await fetchLatestRescue(current.signal);
      if (stopped || controller !== current) return;

      if (result.status === 'ok') {
        // Atomic batch replace; preserves previous values if a later fetch fails.
        featureCollection.value = result.body.data;
        meta.value = result.body.meta;
        lastFetchedAt.value = new Date();
        error.value = null;
      } else if (result.status === 'pending') {
        error.value = {
          kind: 'pending',
          message: result.pending?.error ?? 'rescue data not yet available',
          occurredAt: new Date(),
        };
      } else {
        if (result.error === 'aborted') return;
        error.value = {
          kind: 'failure',
          message: result.error,
          occurredAt: new Date(),
        };
      }
    } finally {
      // Only clear the flag if this call is still the latest in-flight fetch;
      // otherwise an aborted older fetch would falsely clear loading while a
      // newer fetch is still pending.
      if (!stopped && controller === current) {
        isLoading.value = false;
      }
    }
  }

  function scheduleNext() {
    if (stopped) return;
    if (timer) clearTimeout(timer);
    timer = setTimeout(refresh, resolveIntervalMs());
  }

  async function refresh() {
    await runFetch();
    scheduleNext();
  }

  onMounted(() => {
    void refresh();
  });

  onBeforeUnmount(() => {
    stopped = true;
    if (timer) clearTimeout(timer);
    timer = null;
    controller?.abort();
    controller = null;
  });

  return {
    featureCollection,
    meta,
    error,
    isLoading,
    lastFetchedAt,
    refresh,
  };
}
