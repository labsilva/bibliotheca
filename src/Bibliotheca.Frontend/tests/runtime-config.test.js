import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { setting } from '@/lib/runtimeConfig.js'

const capture = vi.hoisted(() => ({ options: null }))
vi.mock('oidc-client-ts', () => ({
  UserManager: class {
    constructor(options) {
      capture.options = options
      this.events = { addAccessTokenExpired() {}, addUserLoaded() {}, addUserUnloaded() {} }
    }
  },
  WebStorageStateStore: class {},
  Log: { INFO: 1, setLogger() {}, setLevel() {} },
}))

beforeEach(() => { vi.resetModules(); delete window.__APP_CONFIG__ })
afterEach(() => { vi.unstubAllEnvs(); delete window.__APP_CONFIG__ })

it('ignores unexpanded placeholders and preserves an explicitly relative API', () => {
  window.__APP_CONFIG__ = { API_URL: '__API_URL__' }
  expect(setting('API_URL', 'fallback')).toBe('fallback')
  window.__APP_CONFIG__.API_URL = ''
  expect(setting('API_URL', 'fallback')).toBe('')
})

it('uses deployment API settings ahead of compiled settings in production', async () => {
  vi.stubEnv('DEV', false)
  vi.stubEnv('VITE_API_URL', 'https://build.example.org')
  window.__APP_CONFIG__ = { API_URL: 'https://deployment.example.org/' }
  const { getApiBase } = await import('@/lib/getApiBase.js')
  expect(getApiBase()).toBe('https://deployment.example.org')
})

it('configures the OIDC client from deployment values without rebuilding', async () => {
  window.__APP_CONFIG__ = {
    OIDC_AUTHORITY: 'https://deployment.example.org', OIDC_CLIENT_ID: 'bibliotheca-web',
    OIDC_SCOPE: 'openid profile email', OIDC_REDIRECT_URI: 'https://deployment.example.org/oidc-callback',
    OIDC_POST_LOGOUT_REDIRECT_URI: 'https://deployment.example.org/',
  }
  await import('@/auth/oidc.js')
  expect(capture.options).toMatchObject({
    authority: 'https://deployment.example.org', client_id: 'bibliotheca-web',
    redirect_uri: 'https://deployment.example.org/oidc-callback',
    post_logout_redirect_uri: 'https://deployment.example.org/',
  })
  expect(capture.options.scope).toContain('offline_access')
})
