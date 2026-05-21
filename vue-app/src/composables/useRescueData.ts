import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue';
import { fetchLatestRescue, forceRefreshRescue } from '@/api/rescue';
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
  const isForcing = ref(false);
  const cooldownRemainingSeconds = ref(0);
  const lastFetchedAt = ref<Date | null>(null);

  let timer: ReturnType<typeof setTimeout> | null = null;
  let cooldownTimer: ReturnType<typeof setInterval> | null = null;
  let controller: AbortController | null = null;
  let forceController: AbortController | null = null;
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
        applySuccess(result.body.data, result.body.meta);
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

  function applySuccess(data: RescueFeatureCollection, m: RescueMeta) {
    // Atomic batch replace; preserves previous values if a later fetch fails.
    featureCollection.value = data;
    meta.value = m;
    lastFetchedAt.value = new Date();
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

  function startCooldown(seconds: number) {
    cooldownRemainingSeconds.value = Math.max(0, Math.ceil(seconds));
    if (cooldownTimer) clearInterval(cooldownTimer);
    if (cooldownRemainingSeconds.value <= 0) return;
    cooldownTimer = setInterval(() => {
      cooldownRemainingSeconds.value = Math.max(0, cooldownRemainingSeconds.value - 1);
      if (cooldownRemainingSeconds.value === 0 && cooldownTimer) {
        clearInterval(cooldownTimer);
        cooldownTimer = null;
      }
    }, 1000);
  }

  async function forceRefresh() {
    if (stopped) return;
    if (isForcing.value || cooldownRemainingSeconds.value > 0) return;

    forceController?.abort();
    const current = new AbortController();
    forceController = current;
    isForcing.value = true;
    try {
      const result = await forceRefreshRescue(current.signal);
      if (stopped || forceController !== current) return;

      if (result.status === 'ok') {
        applySuccess(result.body.data, result.body.meta);
        error.value = null;
        // Reset the automatic polling timer so we do not fire again immediately.
        scheduleNext();
      } else if (result.status === 'throttled') {
        // Throttled is not an error — show countdown in dedicated UI, leave error state alone.
        startCooldown(result.retryAfterSeconds);
      } else {
        if (result.error === 'aborted') return;
        error.value = {
          kind: 'failure',
          message: `強制更新失敗: ${result.error}`,
          occurredAt: new Date(),
        };
      }
    } finally {
      if (!stopped && forceController === current) {
        isForcing.value = false;
      }
    }
  }

  onMounted(() => {
    void refresh();
  });

  onBeforeUnmount(() => {
    stopped = true;
    if (timer) clearTimeout(timer);
    timer = null;
    if (cooldownTimer) clearInterval(cooldownTimer);
    cooldownTimer = null;
    controller?.abort();
    controller = null;
    forceController?.abort();
    forceController = null;
  });

  return {
    featureCollection,
    meta,
    error,
    isLoading,
    isForcing,
    cooldownRemainingSeconds,
    lastFetchedAt,
    refresh,
    forceRefresh,
  };
}
