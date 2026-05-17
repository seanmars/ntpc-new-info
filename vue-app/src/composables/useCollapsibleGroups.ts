import { reactive, watch } from 'vue';

export function useCollapsibleGroups<T extends string>(
  key: string,
  defaults: Record<T, boolean>,
) {
  const initial = readFromStorage(key, defaults);
  const groups = reactive({ ...defaults, ...initial }) as Record<T, boolean>;

  watch(
    () => ({ ...groups }) as Record<T, boolean>,
    (next) => {
      writeToStorage(key, next);
    },
    { deep: true },
  );

  function toggle(name: T) {
    groups[name] = !groups[name];
  }

  function setExpanded(name: T, expanded: boolean) {
    groups[name] = expanded;
  }

  return { groups, toggle, setExpanded };
}

function readFromStorage<T extends string>(
  key: string,
  defaults: Record<T, boolean>,
): Partial<Record<T, boolean>> {
  if (typeof window === 'undefined' || !window.localStorage) return {};
  try {
    const raw = window.localStorage.getItem(key);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object') return {};
    const out: Partial<Record<T, boolean>> = {};
    for (const k of Object.keys(defaults) as T[]) {
      const v = (parsed as Record<string, unknown>)[k];
      if (typeof v === 'boolean') out[k] = v;
    }
    return out;
  } catch {
    return {};
  }
}

function writeToStorage<T extends string>(key: string, value: Record<T, boolean>) {
  if (typeof window === 'undefined' || !window.localStorage) return;
  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Quota or privacy-mode failures are non-fatal; preferences just won't persist.
  }
}
