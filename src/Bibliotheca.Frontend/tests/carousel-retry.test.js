import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, disposePinia } from 'pinia'
import api from '@/lib/http.js'
import Books from '@/pages/Books.vue'
import LibraryDetails from '@/pages/LibraryDetails.vue'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
vi.mock('@/auth/oidc', () => ({
  getUser: async () => ({ profile: { sub: 'carousel-reader' }, access_token: 'test-token' }),
  oidc: {
    settings: { authority: 'https://identity.example' },
    events: { addUserLoaded: () => () => {}, addUserUnloaded: () => () => {} },
  },
}))
enableAutoUnmount(afterEach)
afterEach(() => vi.unstubAllGlobals())

const stubs = { RouterLink: RouterLinkStub, 'font-awesome-icon': true }

function section(wrapper, title) {
  return wrapper.findAll('section').find(item => item.get('h3').text() === title)
}

it('distinguishes catalogue failures from empty results and retries only the failed section', async () => {
  let attempts = 0
  let finishRetry
  api.get.mockImplementation((url, { params } = {}) => {
    if (url === '/api/account/settings') return Promise.resolve({ data: {} })
    if (params.list !== 'recent') return Promise.resolve({ data: [] })
    if (attempts++ === 0) return Promise.reject(new Error('Offline'))
    return new Promise(resolve => { finishRetry = resolve })
  })
  const pinia = createPinia()
  const wrapper = mount(Books, { global: { stubs, plugins: [pinia] } })
  await flushPromises()

  const recent = section(wrapper, 'Recently Added')
  expect(recent.get('[role="alert"]').text()).toBe('Could not load books.')
  expect(recent.text()).not.toContain('No recent books')
  expect(section(wrapper, 'Reading').text()).toContain('No books currently being read')
  expect(section(wrapper, 'Reading').find('.retry').exists()).toBe(false)

  await recent.get('.retry').trigger('click')
  expect(recent.get('[role="status"]').text()).toBe('Loading...')
  expect(recent.find('[role="alert"]').exists()).toBe(false)
  finishRetry({ data: [{ id: 42, title: 'Matilda' }] })
  await flushPromises()

  expect(recent.text()).toContain('Matilda')
  expect(recent.find('.retry').exists()).toBe(false)
  expect(api.get.mock.calls.filter(([url]) => url === '/api/books').map(([, options]) => options.params.list)).toEqual([
    'recent', 'reading', 'read', 'unshelved', 'recent',
  ])
})

it('shows and retries a failed shelf independently of a successful one', async () => {
  vi.stubGlobal('localStorage', { getItem: () => null, setItem: vi.fn() })
  let attempts = 0
  api.get.mockImplementation(async (url) => {
    if (url === '/api/account/settings') return { data: { library: {
      showReadingBooks: false, showUnreadBooks: false, showReadBooks: false,
    } } }
    if (url === '/api/library') return { data: { libraries: [] } }
    if (url === '/api/library/shelves') return { data: { shelves: [{ shelve: 'Read later' }, { shelve: 'Classics' }] } }
    if (url === '/api/library/shelves/Classics/books') return { data: [] }
    if (url === '/api/library/shelves/Read%20later/books') {
      if (attempts++ === 0) throw Error('Network unavailable')
      return { data: [{ id: 42, title: 'Matilda' }] }
    }
    throw Error('Unexpected API request ' + url)
  })
  const pinia = createPinia()
  const wrapper = mount(LibraryDetails, { global: { stubs, plugins: [pinia] } })
  try {
    await flushPromises()
    const failedShelf = section(wrapper, 'Read later')
    expect(failedShelf.get('[role="alert"]').text()).toBe('Could not load books.')
    expect(section(wrapper, 'Classics').text()).toContain('No books available')

    await failedShelf.get('.retry').trigger('click')
    await flushPromises()
    expect(failedShelf.text()).toContain('Matilda')
    expect(failedShelf.find('[role="alert"]').exists()).toBe(false)
    expect(api.get.mock.calls.filter(([path]) => path === '/api/library/shelves/Classics/books')).toHaveLength(1)
  } finally {
    wrapper.unmount()
    disposePinia(pinia)
  }
})
