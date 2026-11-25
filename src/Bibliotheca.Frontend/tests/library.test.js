import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import api from '@/lib/http.js'
import BookCarousel from '@/components/BookCarousel.vue'
import UserShelves from '@/components/UserShelves.vue'
import LibraryDetails from '@/pages/LibraryDetails.vue'
import ShelveDetails from '@/pages/ShelveDetails.vue'
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

const shelfName = 'Ficção ? #1'
const shelfPath = '/api/library/shelves/Fic%C3%A7%C3%A3o%20%3F%20%231'

it('loads personal reading lists and lets the reader open a shelf with a special name', async () => {
  const matilda = { id: 42, title: 'Matilda', author: 'Roald Dahl', status: 'Read', cover: '/api/books/42/cover' }
  api.get.mockImplementation(async (endpoint, options) => {
    switch (endpoint) {
      case '/api/library': return { data: { libraries: [] } }
      case '/api/account/settings': return { status: 200, data: {} }
      case '/api/library/shelves': return { data: { shelves: [{ shelve: shelfName, bookCount: 1 }] } }
      case '/api/library/books': return { data: options.params.list === 'read' ? [matilda] : [] }
      case `${shelfPath}/books`: return { status: 200, data: [matilda] }
      case '/api/library/shelves/Empty/books': return { status: 200, data: [] }
      default: throw Error('Unexpected API endpoint ' + endpoint)
    }
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/library', name: 'user-library', component: LibraryDetails },
      { path: '/shelves/:name', name: 'user-library-shelve', component: ShelveDetails },
      { path: '/books/:id', name: 'book', component: { template: '<div />' } },
    ],
  })
  await router.push('/library')
  const pinia = createPinia()
  const wrapper = mount(RouterView, {
    global: { plugins: [router, pinia], stubs: { BookCarousel: true } },
  })
  await flushPromises()
  const carousels = wrapper.findAllComponents(BookCarousel)
  const read = carousels.find(carousel => carousel.props('title') === 'Read books')
  const shelf = carousels.find(carousel => carousel.props('title') === shelfName)
  expect(read.props('books')).toEqual([expect.objectContaining({ id: 42, cover: '/backend/api/books/42/cover' })])
  expect(shelf.props('books')).toEqual([expect.objectContaining({ title: 'Matilda' })])
  expect(api.get).toHaveBeenCalledWith('/api/library/books', expect.objectContaining({
    params: { list: 'read', page: 0, size: 10 },
  }))

  const sidebar = mount(UserShelves, {
    global: { plugins: [router], stubs: { 'font-awesome-icon': true } },
  })
  await sidebar.get('a').trigger('click')
  await flushPromises()
  expect(router.currentRoute.value.params.name).toBe(shelfName)
  expect(api.get).toHaveBeenCalledWith(`${shelfPath}/books`, { signal: expect.any(AbortSignal) })
  expect(wrapper.get('h1').text()).toBe(shelfName)
  expect(wrapper.get('article').text()).toContain('Matilda')

  await router.push('/shelves/Empty')
  await flushPromises()
  expect(wrapper.get('h1').text()).toBe('Empty')
  expect(wrapper.text()).toContain('No books found.')

  await wrapper.get('header button').trigger('click')
  await flushPromises()
  expect(router.currentRoute.value.name).toBe('user-library')
})

it('ignores an empty shelf name and refreshes the sidebar after creating a shelf with a special name', async () => {
  api.post.mockResolvedValueOnce({ status: 200 })
  api.get.mockResolvedValueOnce({ data: { shelves: [{ shelve: shelfName, bookCount: 0 }] } })
  const wrapper = mount(UserShelves, { global: { stubs: { RouterLink: true, 'font-awesome-icon': true } } })

  await wrapper.get('[title="Add shelve"]').trigger('click')
  const input = wrapper.get('input')
  await input.setValue(' ')
  await input.trigger('keydown', { key: 'Enter' })
  expect(api.post).not.toHaveBeenCalled()

  await input.setValue(`  ${shelfName}  `)
  await input.trigger('keydown', { key: 'Enter' })
  await flushPromises()

  expect(api.post).toHaveBeenCalledExactlyOnceWith(shelfPath)
  expect(api.get).toHaveBeenCalledWith('/api/library/shelves', expect.any(Object))
  expect(wrapper.emitted('add-shelve')).toEqual([[shelfName]])
  expect(wrapper.find('input').exists()).toBe(false)
})

it('waits for server preferences and refetches only the section whose limit changes', async () => {
  let resolveSettings
  const settingsResponse = new Promise(resolve => { resolveSettings = resolve })
  api.get.mockImplementation(async (endpoint) => {
    if (endpoint === '/api/account/settings') return settingsResponse
    if (endpoint === '/api/library/books') return { data: { items: [] } }
    if (endpoint === '/api/library') return { data: { libraries: [] } }
    if (endpoint === '/api/library/shelves') return { data: { shelves: [] } }
    throw Error('Unexpected API endpoint ' + endpoint)
  })
  api.put.mockResolvedValue({ status: 200 })
  const pinia = createPinia()
  const wrapper = mount(LibraryDetails, {
    global: { plugins: [pinia], stubs: { BookCarousel: true } },
  })
  const bookQueries = () => api.get.mock.calls
    .filter(([endpoint]) => endpoint === '/api/library/books')
    .map(([, options]) => options.params)
  expect(bookQueries()).toEqual([])

  resolveSettings({ status: 200, data: JSON.stringify({
    library: { showReadingBooks: false, showUnreadBooks: false, showShelves: false, readBooksCount: 6 },
  }) })
  await flushPromises()
  expect(bookQueries()).toEqual([{ list: 'read', page: 0, size: 6 }])
  expect(wrapper.findAllComponents(BookCarousel).map(carousel => carousel.props('title'))).toEqual(['Read books'])

  useSettingsStore(pinia).library.readBooksCount = 12
  await flushPromises()
  expect(bookQueries()).toEqual([
    { list: 'read', page: 0, size: 6 },
    { list: 'read', page: 0, size: 12 },
  ])
})
