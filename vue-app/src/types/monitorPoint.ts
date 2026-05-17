export interface MonitorPoint {
  id: string;
  name: string;
  latitude: number;
  longitude: number;
  radius: number;
  createdAt: string;
}

export interface MonitorPointCreateInput {
  name: string;
  latitude: number;
  longitude: number;
  radius: number;
}

export interface GeocodeResult {
  displayName: string;
  latitude: number;
  longitude: number;
}

export const DEFAULT_MONITOR_POINT_RADIUS_METERS = 1000;
export const MIN_MONITOR_POINT_RADIUS_METERS = 50;
export const MAX_MONITOR_POINT_RADIUS_METERS = 50000;

export function normalizeMonitorPoint(input: unknown): MonitorPoint {
  const raw = input as Partial<MonitorPoint> & { radius?: unknown };
  const rawRadius = typeof raw.radius === 'number' && Number.isFinite(raw.radius) ? raw.radius : DEFAULT_MONITOR_POINT_RADIUS_METERS;
  return {
    id: String(raw.id ?? ''),
    name: String(raw.name ?? ''),
    latitude: Number(raw.latitude ?? 0),
    longitude: Number(raw.longitude ?? 0),
    radius: Math.round(rawRadius),
    createdAt: String(raw.createdAt ?? ''),
  };
}
