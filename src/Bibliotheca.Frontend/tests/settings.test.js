import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, disposePinia } from 'pinia'
import api from '@/lib/http.js'
import { getUser, oidc } from '@/auth/oidc'
import { useSettingsStore } from '@/stores/settingsStore.js'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), put: vi.fn() } }))
vi.mock('@/auth/oidc', () => ({
  getUser: vi.fn(),
  oidc: {
    settings: { authority: 'https://identity.example' },
    events: { addUserLoaded: vi.fn(() => () => {}), addUserUnloaded: vi.fn(() => () => {}) },
  },
}))

const account = sub => ({ profile: { sub }, access_token: sub + '-token' })
const storageKey = sub => 'bibliotheca.settings.v2:https%3A%2F%2Fidentity.example:' + sub
const onlineGet = { status: 200, data: {} }

describe('library settings', () => {
  let pinia
  let connection

  beforeEach(() => {
    vi.useFakeTimers()
    pinia = createPinia()
    getUser.mockResolvedValue(account('alice'))
    api.get.mockReset().mockResolvedValue(onlineGet)
    api.put.mockReset().mockResolvedValue({ status: 200 })
    connection = { onLine: true }
    vi.stubGlobal('navigator', connection)
    const storage = new Map()
    vi.stubGlobal('localStorage', {
      getItem: key => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, value),
    })
  })

  afterEach(() => {
    disposePinia(pinia)
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('merges the signed-in account’s cache with server values and defaults without issuing a write', async () => {
    localStorage.setItem(storageKey('alice'), JSON.stringify({ library: { showShelves: false, readBooksCount: 4 } }))
    localStorage.setItem('bibliotheca.settings.v1', JSON.stringify({ library: { showUnreadBooks: false } }))
    api.get.mockResolvedValueOnce({ status: 200, data: JSON.stringify({ library: { readBooksCount: 7 } }) })
    const store = useSettingsStore(pinia)

    await store.initialize()

    expect(api.get).toHaveBeenCalledExactlyOnceWith('/api/account/settings', expect.objectContaining({
      headers: { Authorization: 'Bearer alice-token' },
    }))
    expect(store.loaded).toBe(true)
    expect(store.library).toMatchObject({ showShelves: false, readBooksCount: 7, showUnreadBooks: true })
    expect(JSON.parse(localStorage.getItem(storageKey('alice'))).library).toEqual(store.library)
    await vi.advanceTimersByTimeAsync(800)
    expect(api.put).not.toHaveBeenCalled()
  })

  it('saves changes locally and batches nearby edits into one request', async () => {
    const store = useSettingsStore(pinia)
    await store.initialize()

    store.library.readingBooksCount = 12
    await vi.advanceTimersByTimeAsync(500)
    store.library.readingBooksCount = 15

    expect(JSON.parse(localStorage.getItem(storageKey('alice'))).pending).toEqual({ readingBooksCount: 15 })
    await vi.advanceTimersByTimeAsync(799)
    expect(api.put).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)
    expect(api.put).toHaveBeenCalledExactlyOnceWith('/api/account/settings', {
      library: expect.objectContaining({ readingBooksCount: 15 }),
    }, expect.objectContaining({ headers: { Authorization: 'Bearer alice-token' } }))
    expect(store.dirty).toBe(false)
  })

  it('restores pending changes after a reload and merges them with the server when online', async () => {
    connection.onLine = false
    const beforeReload = useSettingsStore(pinia)
    await beforeReload.initialize()
    beforeReload.library.readBooksCount = 15
    disposePinia(pinia)
    pinia = createPinia()

    const store = useSettingsStore(pinia)
    await store.initialize()
    expect(store.library.readBooksCount).toBe(15)
    expect(store.dirty).toBe(true)
    expect(api.put).not.toHaveBeenCalled()

    api.get.mockResolvedValueOnce({ data: { library: { readBooksCount: 8, showShelves: false } } })
    connection.onLine = true
    window.dispatchEvent(new Event('online'))
    await vi.advanceTimersByTimeAsync(0)

    expect(api.put).toHaveBeenCalledWith('/api/account/settings', {
      library: expect.objectContaining({ readBooksCount: 15, showShelves: false }),
    }, expect.any(Object))
    expect(JSON.parse(localStorage.getItem(storageKey('alice'))).pending).toEqual({})
  })

  it('isolates account changes and leaves the previous reader’s pending edits queued', async () => {
    connection.onLine = false
    const store = useSettingsStore(pinia)
    await store.initialize()
    const changeAccount = oidc.events.addUserLoaded.mock.calls[0][0]
    store.library.readBooksCount = 15

    changeAccount(account('bob'))
    expect(store.library.readBooksCount).toBe(10)
    expect(store.dirty).toBe(false)
    connection.onLine = true
    window.dispatchEvent(new Event('online'))
    await vi.advanceTimersByTimeAsync(800)

    expect(api.put).not.toHaveBeenCalled()
    expect(JSON.parse(localStorage.getItem(storageKey('alice'))).pending).toEqual({ readBooksCount: 15 })
    expect(localStorage.getItem(storageKey('bob'))).toBeNull()
  })

  it('ignores a stale write when the reader switches accounts', async () => {
    const store = useSettingsStore(pinia)
    await store.initialize()
    let completeWrite
    api.put.mockReturnValueOnce(new Promise(resolve => { completeWrite = resolve }))
    store.library.readBooksCount = 15
    await vi.advanceTimersByTimeAsync(800)
    const request = api.put.mock.calls[0][2]

    const changeAccount = oidc.events.addUserLoaded.mock.calls[0][0]
    api.get.mockResolvedValueOnce({ data: { library: { readBooksCount: 9 } } })
    changeAccount(account('bob'))
    await vi.advanceTimersByTimeAsync(0)
    expect(request.signal.aborted).toBe(true)
    expect(store.library.readBooksCount).toBe(9)

    completeWrite({ status: 200 })
    await vi.advanceTimersByTimeAsync(0)
    expect(store.library.readBooksCount).toBe(9)
    expect(JSON.parse(localStorage.getItem(storageKey('alice'))).pending).toEqual({ readBooksCount: 15 })
  })
  it('persists the selected books view for the current account', async () => {
    const store = useSettingsStore(pinia)
    await store.initialize()
    store.library.booksView = 'list'
    await vi.advanceTimersByTimeAsync(800)
    expect(api.put).toHaveBeenCalledWith('/api/account/settings',
      expect.objectContaining({ library: expect.objectContaining({ booksView: 'list' }) }), expect.any(Object))
    expect(JSON.parse(localStorage.getItem(storageKey('alice'))).library.booksView).toBe('list')
    const changeAccount = oidc.events.addUserLoaded.mock.calls[0][0]
    changeAccount(account('bob'))
    await vi.advanceTimersByTimeAsync(0)
    expect(store.library.booksView).toBe('carousel')
  })

})
