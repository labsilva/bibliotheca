import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import api from '@/lib/http.js'
import Dashboard from '@/pages/Dashboard.vue'
import Settings from '@/pages/Settings.vue'
import { useSettingsStore } from '@/stores/settingsStore.js'

vi.mock('@/auth/oidc', () => ({
  getUser: async () => ({ profile: { sub: 'reader' }, access_token: 'reader-token' }),
  oidc: {
    settings: { authority: 'https://identity.example' },
    events: { addUserLoaded: () => () => {}, addUserUnloaded: () => () => {} },
  },
}))

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
enableAutoUnmount(afterEach)

afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

it('renders the server statistics after loading the dashboard', async () => {
  const statistics = {
    currentYearReadBooks: { title: 'This year', count: 8 },
    readBooks: { title: 'Read', count: 12 },
    unreadBooks: { title: 'Unread', count: 15 },
    readingBooks: { title: 'Reading', count: 3 },
    totalPagesRead: { title: 'Pages read', count: 1200 },
  }
  let respond
  api.get.mockReturnValueOnce(new Promise(resolve => { respond = resolve }))
  const wrapper = mount(Dashboard)
  await nextTick()
  expect(wrapper.text()).toContain('Loading...')
  respond({ data: statistics })
  await flushPromises()

  expect(api.get).toHaveBeenCalledWith('/api/dashboard', expect.any(Object))
  expect(wrapper.get(".panel-title").text()).toBe("Read this year")
  expect(wrapper.get(".panel-value").text()).toBe("8")
  expect(wrapper.get(".chart").findAll("li")).toHaveLength(12)
  expect(wrapper.get(".totals").text()).toContain("pages read")
})

it('reports failure when personal statistics are unavailable', async () => {
  api.get.mockRejectedValueOnce(new Error('Offline'))
  const wrapper = mount(Dashboard)
  await flushPromises()
  expect(wrapper.text()).toContain('An error occurred')
  expect(wrapper.find('.single-stat-panel').exists()).toBe(false)
})

it('edits library preferences, uploads an ISBN file and generates an invite', async () => {
  vi.stubGlobal('localStorage', { getItem: () => null, setItem: vi.fn() })
  vi.useFakeTimers()
  api.get.mockImplementation(async (endpoint) => {
    if (endpoint === '/api/account/permissions') return { status: 200, data: { canInvite: true } }
    if (endpoint === '/api/account/settings') return { status: 200, data: {} }
    if (endpoint === '/api/imports') return { status: 200, data: [{
      id: 'test-import', status: 'completedWithErrors', createdAtUtc: '2026-11-19T12:00:00Z',
      succeeded: 1, total: 2, failed: 1,
    }] }
    if (endpoint === '/api/users') return { status: 200, data: { items: [{
      id: 1, username: 'reader', fullName: 'Test Reader', email: 'reader@example.invalid',
      bookCount: 2, shelfCount: 1, read: 1, reading: 1, unread: 0,
    }], total: 1, pageSize: 20 } }
    throw Error('Unexpected API request ' + endpoint)
  })
  api.post.mockResolvedValueOnce({ status: 202, data: { id: 'test-import', status: 'queued' } })
    .mockResolvedValueOnce({ status: 200, data: { inviteCode: 'test-invite' } })
  api.put.mockResolvedValueOnce({ status: 200 })
  const pinia = createPinia()
  const wrapper = mount(Settings, {
    global: { plugins: [pinia], stubs: { 'font-awesome-icon': true, FontAwesomeIcon: true } },
  })
  await vi.advanceTimersByTimeAsync(0)
  const store = useSettingsStore(pinia)
  expect(store.loaded).toBe(true)

  const [reading, unread, read, shelves] = wrapper.findAll('[role="switch"]')
  for (const control of [reading, unread, read, shelves]) {
    await control.trigger('click')
    expect(control.attributes('aria-checked')).toBe('false')
  }

  const [readingCount, unreadCount, readCount] = wrapper.findAll('.settings-number')
  await readingCount.get('[aria-label="Aumentar"]').trigger('click')
  await unreadCount.get('[aria-label="Diminuir"]').trigger('click')
  await readCount.get('input').setValue('200')
  await readCount.get('input').trigger('change')
  expect(store.library).toMatchObject({
    showReadingBooks: false, showUnreadBooks: false, showReadBooks: false, showShelves: false,
    readingBooksCount: 11, unreadBooksCount: 9, readBooksCount: 32,
  })
  await vi.advanceTimersByTimeAsync(800)
  expect(api.put).toHaveBeenCalledWith('/api/account/settings', expect.objectContaining({
    library: expect.objectContaining({ readBooksCount: 32, showShelves: false }),
  }), expect.objectContaining({ headers: { Authorization: 'Bearer reader-token' } }))

  const fileInput = wrapper.get('input[type="file"]')
  const file = new File(['9780140328721'], 'books.txt')
  expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  Object.defineProperty(fileInput.element, 'files', { value: [file] })
  await fileInput.trigger('change')
  await wrapper.get('form').trigger('submit')
  await vi.advanceTimersByTimeAsync(0)
  expect(api.post).toHaveBeenCalledWith('/api/library/_upload', expect.any(FormData), expect.any(Object))
  expect(wrapper.text()).toContain('File queued for import.')
  await wrapper.findAll('button').find(button => button.text().trim() === 'Imports').trigger('click')
  await vi.advanceTimersByTimeAsync(0)
  expect(wrapper.find('input[type="file"]').exists()).toBe(false)
  await wrapper.findAll('button').find(button => button.text() === 'Check import status').trigger('click')
  await vi.advanceTimersByTimeAsync(0)
  expect(wrapper.text()).toContain('Completed with errors')
  expect(wrapper.text()).toContain('1 of 2 imported, 1 failed')

  const users = wrapper.findAll('button').find(button => button.text().trim() === 'Users')
  await users.trigger('click')
  await vi.advanceTimersByTimeAsync(0)
  expect(wrapper.get('[aria-label="Registered users"]').text()).toContain('Test Reader')
  const addUser = wrapper.findAll('button').find(button => button.text().trim() === 'New user')
  await addUser.trigger('click')
  await vi.advanceTimersByTimeAsync(0)
  expect(api.post).toHaveBeenCalledWith('/api/account/invite-code')
  expect(wrapper.text()).toContain('/backend/account/register#inviteCode=test-invite')
})

it.each([false, 'unavailable'])('hides invitation creation when administrator permission is %s', async permission => {
  api.get.mockImplementation(async endpoint => {
    if (endpoint === '/api/account/permissions') {
      if (permission === 'unavailable') throw new Error('Offline')
      return { data: { canInvite: false } }
    }
    if (endpoint === '/api/account/settings') return { status: 200, data: {} }
    if (endpoint === '/api/users') return { data: { items: [], total: 0, pageSize: 20 } }
    return { data: [] }
  })
  const wrapper = mount(Settings, {
    global: { plugins: [createPinia()], stubs: { 'font-awesome-icon': true, FontAwesomeIcon: true } },
  })
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text().trim() === 'Users').trigger('click')
  await flushPromises()
  expect(wrapper.findAll('button').some(button => button.text().trim() === 'New user')).toBe(false)
})

it('opens import status from the dashboard link', async () => {
  api.get.mockImplementation(async endpoint => ({ data: endpoint === '/api/imports' ? [] : {} }))
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/settings', component: Settings }] })
  await router.push('/settings#isbn-imports')
  const wrapper = mount(Settings, { global: { plugins: [createPinia(), router], stubs: { 'font-awesome-icon': true, FontAwesomeIcon: true } } })
  await flushPromises()
  expect(wrapper.text()).toContain('Import status')
  expect(wrapper.get('#isbn-imports').text()).toContain('Check import status')
  expect(wrapper.find('input[type="file"]').exists()).toBe(false)
})
