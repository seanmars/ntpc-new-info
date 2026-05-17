/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_RESCUE_POLL_INTERVAL_MS?: string
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
