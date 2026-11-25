import { defineConfig, devices } from '@playwright/test'

const baseURL = 'http://127.0.0.1:4180'

export default defineConfig({
  testDir: './e2e',
  use: { baseURL, ...devices['Desktop Chrome'] },
  webServer: {
    command: 'npm run build -- --outDir dist-e2e && node e2e/fixture-server.js',
    url: baseURL + '/__test__/ready',
    timeout: 120_000,
    env: {
      VITE_API_BASE_URL: baseURL,
      VITE_OIDC_AUTHORITY: baseURL,
      VITE_OIDC_CLIENT_ID: 'bibliotheca-e2e',
      VITE_OIDC_REDIRECT_URI: baseURL + '/oidc-callback',
      VITE_OIDC_POST_LOGOUT_REDIRECT_URI: baseURL + '/logout-callback',
    },
  },
})
