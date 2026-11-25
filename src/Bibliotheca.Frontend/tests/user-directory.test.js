import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import UserDirectory from '@/components/settings/UserDirectory.vue'
import api from '@/lib/http.js'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn() } }))
enableAutoUnmount(afterEach)
beforeEach(() => vi.resetAllMocks())

const alice = {
  id: 1, username: 'alice', fullName: 'Alice Reader', email: 'alice@example.invalid',
  bookCount: 4, shelfCount: 2, read: 2, reading: 1, unread: 1,
}
const bob = {
  id: 2, username: 'bob', fullName: 'Bob Reader', email: 'bob@example.invalid',
  bookCount: 0, shelfCount: 0, read: 0, reading: 0, unread: 0,
}

it('loads account data and keeps the submitted search when paging', async () => {
  const response = params => ({ data: {
    items: params.search === 'missing' ? [] : [params.page === 1 ? alice : bob],
    total: params.search === 'missing' ? 0 : 21,
    pageSize: params.pageSize,
  } })
  let finishLoading
  api.get.mockImplementationOnce((_, { params }) => new Promise(resolve => {
    finishLoading = () => resolve(response(params))
  })).mockImplementation(async (_, { params }) => response(params))
  const wrapper = mount(UserDirectory)
  await nextTick()
  expect(wrapper.get('[role="status"]').text()).toContain('Loading users')
  finishLoading()
  await flushPromises()

  const search = wrapper.get('input[type="search"]')
  const controls = () => wrapper.get('nav[aria-label="User pages"]').findAll('button')
  expect(wrapper.get('li').text()).toContain('Alice Reader')
  expect(wrapper.get('li').text()).toContain('alice@example.invalid')
  expect(wrapper.get('li').text()).toContain('4 books')
  expect(wrapper.get('li').text()).toContain('Read: 2 · Reading: 1 · Unread: 1')
  expect(controls()[0].element.disabled).toBe(true)

  await search.setValue(' reader ')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  await search.setValue('unsubmitted text')
  await controls()[1].trigger('click')
  await flushPromises()
  expect(api.get).toHaveBeenLastCalledWith('/api/users', expect.objectContaining({
    params: { search: 'reader', sort: 'name', page: 2, pageSize: 20 },
  }))
  expect(wrapper.get('li').text()).toContain('Bob Reader')
  expect(wrapper.get('li').text()).toContain('0 books')
  expect(controls()[1].element.disabled).toBe(true)

  await search.setValue('reader')
  await wrapper.get('select').setValue('library')
  await flushPromises()
  expect(api.get).toHaveBeenLastCalledWith('/api/users', expect.objectContaining({
    params: { search: 'reader', sort: 'library', page: 1, pageSize: 20 },
  }))

  await search.setValue('missing')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(wrapper.text()).toContain('No users found.')
  expect(wrapper.find('li').exists()).toBe(false)
  expect(wrapper.find('nav').exists()).toBe(false)
})

it('shows a retry after failure and hides directory data when access is forbidden', async () => {
  api.get.mockRejectedValueOnce(new Error('Network failure'))
    .mockRejectedValueOnce({ response: { status: 403 } })
  const wrapper = mount(UserDirectory)
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Could not load users')

  await wrapper.get('[role="alert"] button').trigger('click')
  await flushPromises()
  expect(api.get).toHaveBeenCalledTimes(2)
  expect(wrapper.find('[aria-label="Registered users"]').exists()).toBe(false)
  expect(wrapper.find('[role="alert"]').exists()).toBe(false)
})
