import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import BookListView from '@/components/BookListView.vue'
import api from '@/lib/http.js'
vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '' }))
beforeEach(() => { vi.useFakeTimers(); api.get.mockReset() })
afterEach(() => vi.useRealTimers())
it('searches the catalogue, pages results and links to book details', async () => {
  api.get.mockImplementation(async (_, { params }) => ({ data: { total: 21,
    items: [{ id: params.page + 1, title: 'Matilda', author: 'Roald Dahl' }] } }))
  const wrapper = mount(BookListView, { global: { stubs: { RouterLink: RouterLinkStub } } })
  await flushPromises()
  expect(wrapper.text()).toContain('Roald Dahl')
  expect(wrapper.getComponent(RouterLinkStub).props('to').params.id).toBe(1)
  await wrapper.get('nav button:last-child').trigger('click')
  await flushPromises()
  expect(api.get).toHaveBeenLastCalledWith('/api/books', expect.objectContaining({ params: expect.objectContaining({ page: 1 }) }))
  await wrapper.get('input').setValue('Dahl')
  await vi.advanceTimersByTimeAsync(300)
  await flushPromises()
  expect(api.get).toHaveBeenLastCalledWith('/api/books', expect.objectContaining({ params: expect.objectContaining({ page: 0, search: 'Dahl' }) }))
  wrapper.unmount()
})
