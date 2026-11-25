import { beforeEach, expect, it, vi } from 'vitest'
import { installAuthGuard } from '@/auth/guard.js'
import { getAccessToken, getUser, handleSigninCallback, logout, recoverRejectedToken } from '@/auth/oidc.js'

const manager = vi.hoisted(() => ({
  getUser: vi.fn(),
  clearStaleState: vi.fn(),
  signinRedirect: vi.fn(),
  signinRedirectCallback: vi.fn(),
  signinSilent: vi.fn(),
  removeUser: vi.fn(),
  events: { addAccessTokenExpired: vi.fn(), addUserLoaded: vi.fn(), addUserUnloaded: vi.fn() },
  signoutRedirect: vi.fn(),
}))

vi.mock('oidc-client-ts', () => ({
  UserManager: class { constructor() { Object.assign(this, manager) } },
  WebStorageStateStore: class {},
  Log: { INFO: 1, setLogger: vi.fn(), setLevel: vi.fn() },
}))

beforeEach(() => vi.resetAllMocks())

function guard() {
  const router = { beforeEach: vi.fn() }
  installAuthGuard(router)
  return router.beforeEach.mock.calls[0][0]
}

it.each(['/oidc-callback', '/logout-callback', '/oidc-silent-renew', '/apple-icon-180.png', '/images/book.jpeg', '/theme.css'])(
  'lets %s load without contacting the identity provider',
  async (path) => {
    expect(await guard()({ path })).toBe(true)
    expect(manager.getUser).not.toHaveBeenCalled()
  }
)

it('allows an OIDC callback with code and state and an authenticated route', async () => {
  const navigate = guard()
  expect(await navigate({ path: '/dashboard', query: { code: 'authorization-code', state: 'state' } })).toBe(true)
  expect(manager.getUser).not.toHaveBeenCalled()
  manager.getUser.mockResolvedValueOnce({ expired: false })
  expect(await navigate({ path: '/dashboard', query: {} })).toBe(true)
  expect(manager.signinRedirect).not.toHaveBeenCalled()
})

it('coalesces simultaneous redirects and preserves the intended URL', async () => {
  let finishRedirect
  manager.getUser.mockResolvedValue({ expired: true })
  manager.signinRedirect.mockReturnValueOnce(new Promise(resolve => { finishRedirect = resolve }))
  const navigate = guard()

  const first = navigate({ path: '/books/42', fullPath: '/books/42?tab=notes' })
  await expect(navigate({ path: '/library', fullPath: '/library' })).resolves.toBe(false)
  expect(manager.clearStaleState).toHaveBeenCalledTimes(1)
  expect(manager.signinRedirect).toHaveBeenCalledExactlyOnceWith({ state: { target: '/books/42?tab=notes' } })
  finishRedirect()
  await expect(first).resolves.toBe(false)

  await expect(navigate({ path: '/library', fullPath: '/library' })).resolves.toBe(false)
  expect(manager.signinRedirect).toHaveBeenLastCalledWith({ state: { target: '/library' } })
})

it('restores the target after sign-in, defaults to the home route and exposes the token', async () => {
  manager.signinRedirectCallback.mockResolvedValueOnce({ state: { target: '/books/42' } }).mockResolvedValueOnce({})
  expect(await handleSigninCallback()).toBe('/books/42')
  expect(await handleSigninCallback()).toBe('/')
  manager.getUser.mockResolvedValueOnce({ access_token: 'test-token' })
  expect(await getAccessToken()).toBe('test-token')
  expect(await getAccessToken()).toBeUndefined()
})

it('delegates logout of an active user to OIDC', async () => {
  manager.getUser.mockResolvedValueOnce({ expired: false })
  await logout()
  expect(manager.signoutRedirect).toHaveBeenCalledTimes(1)
})

it('shares one refresh-token exchange after the app resumes', async () => {
  manager.getUser.mockResolvedValue({ expired: true, refresh_token: 'refresh' })
  manager.signinSilent.mockResolvedValue({ access_token: 'renewed' })
  const [user, token] = await Promise.all([getUser(), getAccessToken()])
  expect(user.access_token).toBe('renewed')
  expect(token).toBe('renewed')
  expect(manager.signinSilent).toHaveBeenCalledTimes(1)
})

it('retains the session after a network failure and clears it after revocation', async () => {
  manager.getUser.mockResolvedValue({ expired: true, refresh_token: 'refresh' })
  manager.signinSilent.mockRejectedValueOnce(Error('offline')).mockRejectedValueOnce({ error: 'invalid_grant' })
  await expect(getAccessToken()).rejects.toThrow('offline')
  expect(manager.removeUser).not.toHaveBeenCalled()
  expect(await getAccessToken()).toBeUndefined()
  expect(manager.removeUser).toHaveBeenCalledOnce()
  expect(manager.signinRedirect).toHaveBeenCalledOnce()
})

it('redirects a rejected refresh to the requested route during navigation', async () => {
  manager.getUser.mockResolvedValue({ expired: true, refresh_token: 'revoked' })
  manager.signinSilent.mockRejectedValue({ error: 'invalid_grant' })
  await expect(guard()({ path: '/books/42', fullPath: '/books/42#notes' })).resolves.toBe(false)
  expect(manager.signinRedirect).toHaveBeenCalledExactlyOnceWith({ state: { target: '/books/42#notes' } })
})

it('ignores a delayed unauthorized response belonging to the previous token', async () => {
  manager.getUser.mockResolvedValue({ access_token: 'current' })
  await recoverRejectedToken('Bearer previous')
  expect(manager.removeUser).not.toHaveBeenCalled()
  await recoverRejectedToken('Bearer current')
  expect(manager.removeUser).toHaveBeenCalledOnce()
  expect(manager.signinRedirect).toHaveBeenCalledOnce()
})
