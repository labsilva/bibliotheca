import { expect, it, vi } from 'vitest'
import { getAccessToken } from '@/auth/oidc'
import api from '@/lib/http.js'

vi.mock('@/auth/oidc', () => ({ getAccessToken: vi.fn() }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))

it('uses the current session for ordinary requests and preserves an account-bound token', async () => {
  getAccessToken.mockResolvedValue('current-token')
  const adapter = vi.fn(async config => ({ status: 200, data: {}, headers: {}, config }))

  await api.get('/api/library', { adapter })
  await api.put('/api/account/settings', {}, { adapter, headers: { Authorization: 'Bearer original-token' } })

  expect(adapter.mock.calls[0][0].headers.get('Authorization')).toBe('Bearer current-token')
  expect(adapter.mock.calls[1][0].headers.get('Authorization')).toBe('Bearer original-token')
  expect(getAccessToken).toHaveBeenCalledTimes(1)
})
