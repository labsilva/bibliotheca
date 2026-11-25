import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import api from '@/lib/http.js'
import AuthorRefreshSettings from '@/components/settings/AuthorRefreshSettings.vue'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn() } }))
enableAutoUnmount(afterEach)
beforeEach(() => { vi.resetAllMocks(); vi.useFakeTimers() })
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers() })

const status = (values = {}) => ({ data: { authorCount: 3, latest: null, errors: [], ...values } })
const mountPanel = () => mount(AuthorRefreshSettings, {
  global: { stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } } }
})
const button = (wrapper, label) => wrapper.findAll('button').find(item => item.text() === label)

it('confirms the author count and polls a started job until its report is complete', async () => {
  const active = { id: 'batch', total: 3, completedAtUtc: null, updated: 1, unchanged: 0, failed: 0, current: { authorId: 7, authorName: 'Current author' } }
  const completed = status({
    latest: { ...active, completedAtUtc: '2026-11-22', unchanged: 1, failed: 1 },
    errors: [{ authorId: 42, authorName: 'Failed author', error: 'Provider request timed out.' }]
  })
  api.get.mockResolvedValueOnce(status()).mockResolvedValueOnce(status({ latest: active })).mockResolvedValue(completed)
  api.post.mockResolvedValue({ data: { id: 'batch', total: 3 } })
  const wrapper = mountPanel()
  await flushPromises()
  await button(wrapper, 'Refresh all authors').trigger('click')
  expect(wrapper.text()).toContain('Refresh profiles for all 3 authors?')
  await button(wrapper, 'Cancel').trigger('click')
  expect(api.post).not.toHaveBeenCalled()
  await button(wrapper, 'Refresh all authors').trigger('click')
  await button(wrapper, 'Confirm author refresh').trigger('click')
  await flushPromises()
  expect(api.post).toHaveBeenCalledWith('/api/author-refresh', {}, expect.any(Object))
  expect(wrapper.get('progress').element.value).toBe(1)
  expect(wrapper.get('a').attributes('href')).toBe('/authors/7')
  expect(wrapper.text()).toContain('Currently processing: Current author')
  expect(button(wrapper, 'Refresh all authors').element.disabled).toBe(true)
  await vi.advanceTimersByTimeAsync(3000)
  expect(wrapper.text()).toContain('Completed: 3 / 3')
  expect(wrapper.text()).not.toContain('Currently processing:')
  expect(wrapper.get('a').attributes('href')).toBe('/authors/42')
  wrapper.unmount()
  const reopened = mountPanel()
  await flushPromises()
  expect(reopened.text()).toContain('Completed: 3 / 3')
})

it('blocks author refresh during a catalogue job and enables it when that job finishes', async () => {
  api.get.mockResolvedValueOnce(status({ catalogueRefreshActive: true })).mockResolvedValue(status())
  const wrapper = mountPanel()
  await flushPromises()
  expect(wrapper.text()).toContain('A catalogue refresh is running.')
  expect(button(wrapper, 'Refresh all authors').element.disabled).toBe(true)
  await vi.advanceTimersByTimeAsync(3000)
  expect(button(wrapper, 'Refresh all authors').element.disabled).toBe(false)
})

it('explains manager access and hides refresh controls after a forbidden response', async () => {
  api.get.mockRejectedValue({ response: { status: 403 } })
  const wrapper = mountPanel()
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Only catalogue managers')
  expect(wrapper.find('button').exists()).toBe(false)
})
