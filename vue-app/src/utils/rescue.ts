import type { RescueFeature } from '@/types/rescue';

export const PALETTE = ['#e63946', '#f4a261', '#2a9d8f', '#264653', '#6a4c93', '#1d3557'];

export function colorFor(index: number): string {
  return PALETTE[index % PALETTE.length] ?? '#666666';
}

export function featureKey(feature: RescueFeature, index: number): string {
  return feature.properties.featureId ?? `idx-${index}`;
}
