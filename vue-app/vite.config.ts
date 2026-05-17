import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : 5173,
    proxy: {
      '/api': {
        target: process.env.services__webapi__http__0 ?? 'http://localhost:5129',
        changeOrigin: true,
      },
    },
  },
})
