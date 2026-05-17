// Coordinate as stored inside caseList: [lat, lng]
export type LatLngTuple = [number, number];

// Coordinate as stored inside GeoJSON geometry.coordinates: [lng, lat]
export type LngLatTuple = [number, number];

export interface RescueCase {
  path: LatLngTuple[] | null;
  startPoint: LatLngTuple | null;
  startPointInfo: string | null;
  startPointKey: number | string | null;
}

export interface RescueFeatureProperties {
  lng: number;
  lat: number;
  distance?: number;
  fireType?: string;
  endPointInfo?: string;
  layerName?: string;
  type?: string;
  title?: string;
  dataSource?: string;
  featureId?: string;
  caseList?: RescueCase[];
  [key: string]: unknown;
}

export interface RescuePointGeometry {
  type: 'Point';
  coordinates: LngLatTuple;
}

export interface RescueFeature {
  type: 'Feature';
  properties: RescueFeatureProperties;
  geometry: RescuePointGeometry;
}

export interface RescueFeatureCollection {
  type: 'FeatureCollection';
  features: RescueFeature[];
}

export interface RescueMeta {
  fetchedAt: string;
  source: string;
  lastError: string | null;
  lastErrorAt: string | null;
}

export interface RescueResponse {
  data: RescueFeatureCollection;
  meta: RescueMeta;
}

export interface RescuePendingMeta {
  error?: string;
  lastError?: string | null;
  lastErrorAt?: string | null;
}
