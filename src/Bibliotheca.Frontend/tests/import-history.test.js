import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import ImportHistory from '@/components/ImportHistory.vue'
import { importEntries, recordImport, refreshImportHistory, resetImportHistory } from '@/composables/useImportHistory'
import api from '@/lib/http.js'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn() } }))
enableAutoUnmount(afterEach)
afterEach(() => { resetImportHistory(); vi.resetAllMocks() })

const summary = {
  id: 'batch', status: 'completedWithErrors', total: 28, failed: 21, succeeded: 7,
  pending: 0, isbnFailureCount: 21, createdAtUtc: '2026-11-21T12:00:00Z',
}

it('fetches failed ISBNs only when expanded, pages them, and retries the selected position', async () => {
  api.get.mockImplementation(async (url, options) => {
    if (url === '/api/imports') return { data: [summary] }
    expect(url).toBe('/api/imports/batch/failures')
    const { page, size } = options.params
    expect(size).toBe(20)
    const items = Array.from({ length: page ? 1 : 20 }, (_, i) => ({
      importId: 'import-' + (page * size + i), position: 2, isbn: '123', reason: 'Missing ' + (page * size + i),
    }))
    return { data: { page, total: 21, items } }
  })
  api.post.mockResolvedValue({ data: { id: 'retry' } })
  const wrapper = mount(ImportHistory)
  await flushPromises()
  expect(api.get).toHaveBeenCalledTimes(1)
  expect(wrapper.text()).toContain('7 of 28 imported, 21 failed, 0 pending')
  const details = wrapper.get('details')
  details.element.open = true
  await details.trigger('toggle')
  await flushPromises()
  expect(wrapper.findAll('form')).toHaveLength(20)
  await wrapper.findAll('button').find(b => b.text() === 'Next failures').trigger('click')
  await flushPromises()
  expect(wrapper.findAll('form')).toHaveLength(1)
  expect(wrapper.get('form').text()).toContain('Missing 20')
  await wrapper.get('input').setValue('9780140328721')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(api.post).toHaveBeenCalledWith('/api/imports/batch/retry', {
    items: [{ importId: 'import-20', position: 2, isbn: '9780140328721' }],
  }, expect.anything())
  await wrapper.findAll('button').find(b => b.text() === 'Previous failures').trigger('click')
  await flushPromises()
  expect(wrapper.findAll('form')).toHaveLength(20)
})

it('reconciles an in-flight summary with SignalR and discards requests from an old session', async () => {
  let respond
  api.get.mockImplementationOnce(() => new Promise(resolve => { respond = resolve }))
    .mockResolvedValueOnce({ data: [summary] })
  const loading = refreshImportHistory()
  recordImport({ id: 'last-scan' })
  respond({ data: [] })
  await loading
  expect(importEntries.value).toEqual([summary])
  api.get.mockImplementationOnce(() => new Promise(resolve => { respond = resolve }))
  const old = refreshImportHistory()
  resetImportHistory()
  respond({ data: [summary] })
  await old
  expect(importEntries.value).toEqual([])
})
