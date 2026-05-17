import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue';
import {
  createMonitorPoint,
  deleteMonitorPoint,
  fetchMonitorPoints,
  updateMonitorPoint,
} from '@/api/monitorPoints';
import {
  normalizeMonitorPoint,
  type MonitorPoint,
  type MonitorPointCreateInput,
} from '@/types/monitorPoint';

const DEFAULT_INTERVAL_MS = 60_000;

function resolveIntervalMs(): number {
  const raw = import.meta.env.VITE_RESCUE_POLL_INTERVAL_MS;
  if (!raw) return DEFAULT_INTERVAL_MS;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_INTERVAL_MS;
}

export interface UseMonitorPointsOptions {
  autoPoll?: boolean;
}

export type MutationResult =
  | { status: 'ok'; point: MonitorPoint }
  | { status: 'error'; error: string };

export type RemoveResult = { status: 'ok' } | { status: 'error'; error: string };

export function useMonitorPoints(options: UseMonitorPointsOptions = {}) {
  const autoPoll = options.autoPoll ?? false;

  const points = shallowRef<MonitorPoint[]>([]);
  const error = ref<string | null>(null);
  const isLoading = ref(false);

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
      const result = await fetchMonitorPoints(current.signal);
      if (stopped || controller !== current) return;
      if (result.status === 'ok') {
        points.value = result.body.map(normalizeMonitorPoint);
        error.value = null;
      } else {
        if (result.error === 'aborted') return;
        error.value = result.error;
      }
    } finally {
      if (!stopped && controller === current) {
        isLoading.value = false;
      }
    }
  }

  function scheduleNext() {
    if (stopped || !autoPoll) return;
    if (timer) clearTimeout(timer);
    timer = setTimeout(refresh, resolveIntervalMs());
  }

  async function refresh() {
    await runFetch();
    scheduleNext();
  }

  async function create(input: MonitorPointCreateInput): Promise<MutationResult> {
    const result = await createMonitorPoint(input);
    if (result.status === 'ok') {
      const normalized = normalizeMonitorPoint(result.body);
      points.value = [...points.value, normalized];
      error.value = null;
      return { status: 'ok', point: normalized };
    }
    error.value = result.error;
    return { status: 'error', error: result.error };
  }

  async function update(id: string, input: MonitorPointCreateInput): Promise<MutationResult> {
    const result = await updateMonitorPoint(id, input);
    if (result.status === 'ok') {
      const normalized = normalizeMonitorPoint(result.body);
      points.value = points.value.map((p) => (p.id === id ? normalized : p));
      error.value = null;
      return { status: 'ok', point: normalized };
    }
    error.value = result.error;
    return { status: 'error', error: result.error };
  }

  async function remove(id: string): Promise<RemoveResult> {
    const result = await deleteMonitorPoint(id);
    if (result.status === 'ok') {
      points.value = points.value.filter((p) => p.id !== id);
      error.value = null;
      return { status: 'ok' };
    }
    error.value = result.error;
    return { status: 'error', error: result.error };
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
    points,
    error,
    isLoading,
    refresh,
    create,
    update,
    remove,
  };
}
