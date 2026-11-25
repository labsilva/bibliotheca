import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  build: {
    // Keep the browser baseline used by the Vite 7 build for existing PWA installs.
    target: ['chrome107', 'edge107', 'firefox104', 'safari16'],
  },
  plugins: [
    vue(),
    vueDevTools(),
    VitePWA({
      registerType: 'autoUpdate',       // atualiza SW automaticamente
      includeAssets: ['favicon.svg'],    // extra (se quiser)
      manifest: {
        name: 'Bibliotheca',
        short_name: 'Bibliotheca',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        background_color: '#1b0303ff',
        theme_color: '#0ea5e9',
        orientation: 'portrait',
        icons: [
          { src: '/pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: '/pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          // importante para Android/iOS
          { src: '/pwa-maskable-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
        ]
      },
      workbox: {
        globIgnores: ['**/config.js'],
        // cache dos ficheiros gerados pelo build (por defeito)
        maximumFileSizeToCacheInBytes: 5 * 1024 ** 2, // 5 MB or set to something else
        importScripts: ['remove-legacy-caches.js'],
        runtimeCaching: [],
        navigateFallbackDenylist: [/^\/(?:api|account|connect|\.well-known|openapi|scalar)(?:\/|$)/],
        navigateFallback: '/index.html'
      }
    })
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  }
})
