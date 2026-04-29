/* eslint-env node */
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// ─────────────────────────────────────────────────────────────────────────────
// Ports from G4Composer.Server/Properties/launchSettings.json:
//   HTTPS → https://localhost:7112
//   HTTP  → http://localhost:5238
//
// We proxy to HTTP to avoid self-signed certificate issues in dev.
// ─────────────────────────────────────────────────────────────────────────────
const BACKEND_URL = 'http://localhost:5238'

export default defineConfig({
  plugins: [react()],

  server: {
    port: 5173,
    strictPort: true,
    open: false, // VS Studio / SpaProxy opens the browser — don't double-open

    proxy: {
      '/api': {
        target: BACKEND_URL,
        changeOrigin: true,
        secure: false,
      },
      '/scalar': {
        target: BACKEND_URL,
        changeOrigin: true,
        secure: false,
      },
      '/openapi': {
        target: BACKEND_URL,
        changeOrigin: true,
        secure: false,
      },
    },
  },

  build: {
    outDir: '../G4Composer.Server/wwwroot',
    emptyOutDir: true,
  },
})
