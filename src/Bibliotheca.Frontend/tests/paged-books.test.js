import { effectScope, nextTick, ref } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { usePagedBooks } from '@/composables/usePagedBooks'
import api from '@/lib/http.js'
import { catalogueAccount, catalogueAccountKey } from '@/auth/catalogueAccount.js'
vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '' }))
afterEach(() => vi.resetAllMocks())
it('loads further pages once and retains prior books on error and retry', async () => {
  api.get.mockResolvedValueOnce({ data: { total: 3, items: [{ id: 1 }, { id: 2 }] } })
    .mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValueOnce({ data: { total: 3, items: [{ id: 3 }] } })
  const scope = effectScope()
  const state = scope.run(() => usePagedBooks('/api/books', { list: 'recent', page: 0, size: 2 }))
  await flushPromises()
  expect(state.hasMore.value).toBe(true)
  await state.loadMore()
  expect(state.books.value.map(b => b.id)).toEqual([1, 2])
  expect(state.moreError.value).toBeTruthy()
  await state.loadMore()
  expect(state.books.value.map(b => b.id)).toEqual([1, 2, 3])
  expect(state.hasMore.value).toBe(false)
  await state.loadMore()
  expect(api.get).toHaveBeenCalledTimes(3)
  scope.stop()
})
it('ignores an obsolete page after its context changes', async () => {
  let finish
  api.get.mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
    .mockResolvedValueOnce({ data: { total: 1, items: [{ id: 2 }] } })
  const list = ref('reading')
  const scope = effectScope()
  const state = scope.run(() => usePagedBooks('/api/books', { list, page: 0, size: 2 }))
  list.value = 'read'
  await nextTick()
  await flushPromises()
  finish({ data: { total: 1, items: [{ id: 1 }] } })
  await flushPromises()
  expect(state.books.value.map(b => b.id)).toEqual([2])
  scope.stop()
})

it('ignores an in-flight shelf page when the account changes and coalesces concurrent requests', async () => {
  const pending = Promise.withResolvers()
  const accountAt = iss => catalogueAccountKey({ profile: { sub: 'reader', iss } })
  catalogueAccount.value = accountAt('https://first.example')
  api.get.mockResolvedValueOnce({ data: { total: 2, items: [{ id: 1 }] } })
    .mockReturnValueOnce(pending.promise)
    .mockResolvedValueOnce({ data: { total: 1, items: [{ id: 3 }] } })
  const scope = effectScope()
  try {
    const state = scope.run(() => usePagedBooks('/api/library/shelves/Fiction/books', { size: 1 }))
    await flushPromises()
    const append = state.loadMore()
    await state.loadMore()
    expect(api.get).toHaveBeenCalledTimes(2)
    const signal = api.get.mock.calls[1][1].signal

    catalogueAccount.value = accountAt('https://second.example')
    await nextTick()
    expect(signal.aborted).toBe(true)
    expect(state.books.value.every(book => book.id === 3)).toBe(true)
    pending.resolve({ data: { total: 2, items: [{ id: 2 }] } })
    await append
    await flushPromises()
    expect(state.books.value.map(book => book.id)).toEqual([3])
    expect(state.hasMore.value).toBe(false)
    expect(api.get.mock.calls[2][1].params.page).toBe(0)
  } finally {
    scope.stop()
    catalogueAccount.value = undefined
  }
})
