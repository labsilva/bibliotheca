import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import api from '@/lib/http.js'
import CatalogueRefreshSettings from '@/components/settings/CatalogueRefreshSettings.vue'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn() } }))
enableAutoUnmount(afterEach)
beforeEach(() => { vi.resetAllMocks(); vi.useFakeTimers() })
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers() })
const status = (latest = null, errors = []) => ({ data: { bookCount: 3, latest, errors } })
const mountPanel = () => mount(CatalogueRefreshSettings, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })

it('requires confirmation and cancel leaves the catalogue untouched', async () => {
  api.get.mockResolvedValue(status())
  const wrapper = mountPanel()
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text() === 'Refresh all books').trigger('click')
  expect(wrapper.text()).toContain('Refresh all 3 books')
  await wrapper.findAll('button').find(button => button.text() === 'Cancel').trigger('click')
  expect(api.post).not.toHaveBeenCalled()
  expect(wrapper.text()).not.toContain('Confirm refresh')
})

it('starts a confirmed batch, restores its progress and shows item failures', async () => {
  api.get.mockResolvedValueOnce(status()).mockResolvedValue(status({ id: 'batch', total: 3, updated: 1, unchanged: 1, failed: 1, completedAtUtc: '2026-11-21' }, [{ bookId: 7, title: 'Failed book', error: 'Provider request timed out.' }]))
  api.post.mockResolvedValue({ data: { id: 'batch' } })
  const wrapper = mountPanel()
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text() === 'Refresh all books').trigger('click')
  await wrapper.findAll('button').find(button => button.text() === 'Confirm refresh').trigger('click')
  await flushPromises()
  expect(api.post).toHaveBeenCalledWith('/api/catalogue-refresh', {}, expect.any(Object))
  expect(wrapper.text()).toContain('Completed: 3 / 3')
  expect(wrapper.text()).toContain('Failed book')
  wrapper.unmount()
  const restored = mountPanel()
  await flushPromises()
  expect(restored.text()).toContain('Completed: 3 / 3')
})

it('disables overlapping refresh and hides controls from unauthorized readers', async () => {
  api.get.mockResolvedValueOnce(status({ id: 'batch', total: 3, updated: 1, unchanged: 0, failed: 0, completedAtUtc: null, current: { bookId: 7, title: 'Current book' } }))
  const wrapper = mountPanel()
  await flushPromises()
  expect(wrapper.findAll('button').find(button => button.text() === 'Refresh all books').attributes('disabled')).toBeDefined()
  expect(wrapper.text()).toContain('Currently processing: Current book')
  wrapper.unmount()
  api.get.mockRejectedValueOnce({ response: { status: 403 } })
  const denied = mountPanel()
  await flushPromises()
  expect(denied.find('section').exists()).toBe(true)
  expect(denied.get('[role="alert"]').text()).toContain('Only catalogue managers')
  expect(denied.find('button').exists()).toBe(false)
  const calls = api.get.mock.calls.length
  await vi.advanceTimersByTimeAsync(5000)
  expect(api.get).toHaveBeenCalledTimes(calls)
})

it('explains access revoked when a confirmed refresh is rejected', async () => {
  api.get.mockResolvedValue(status())
  api.post.mockRejectedValue({ response: { status: 403 } })
  const wrapper = mountPanel()
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text() === 'Refresh all books').trigger('click')
  await wrapper.findAll('button').find(button => button.text() === 'Confirm refresh').trigger('click')
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Ask an administrator for access')
  expect(wrapper.find('button').exists()).toBe(false)
  expect(wrapper.text()).not.toContain('3 books')
})

it('reports loading errors and cancels scheduled polling on unmount', async () => {
  api.get.mockRejectedValue(new Error('Offline'))
  const wrapper = mountPanel()
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Could not load')
  const calls = api.get.mock.calls.length
  wrapper.unmount()
  await vi.advanceTimersByTimeAsync(5000)
  expect(api.get).toHaveBeenCalledTimes(calls)
})
