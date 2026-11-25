import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import BookPage from '@/pages/BookPage.vue'
import api from '@/lib/http.js'

vi.mock('@/lib/http.js', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
enableAutoUnmount(afterEach)

beforeEach(() => vi.resetAllMocks())
afterEach(() => vi.useRealTimers())

async function mountBookPage() {
  let readingStatus = 'Unread'
  let readingDate = null
  api.get.mockImplementation(async (path) => {
    if (path === '/api/books/42') return { data: {
      book: {
        bookId: 42, title: 'Matilda', isbn: '9780140328721', authors: [{ authorId: 1, authorName: 'Roald Dahl', photoUrl: 'https://example.invalid/author.jpg' }],
        genres: [{ bookGenreId: 9, genre: 'Fantasy' }], additionalData: { synopsis: 'A gifted reader.' },
        externalLinks: [{ externalLinkId: 1, url: 'https://example.invalid/matilda' }],
      },
      userBook: { readingStatus, rating: 2, comments: '', shelve: { shelveName: 'Favorites' } },
      readingDate,
      shelves: [{ shelveName: 'Favorites' }],
    } }
    return { data: { libraries: [], shelves: [] } }
  })
  api.put.mockResolvedValue({ status: 200 })
  api.post.mockImplementation(async (_, update) => {
    readingStatus = update.status
    readingDate = update.readingDate ?? null
    return { status: 200 }
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/books/:id', name: 'book', component: BookPage },
      { path: '/dashboard', name: 'dashboard', component: { template: '<p>Dashboard</p>' } },
      { path: '/authors/:id', name: 'author', component: { template: '<div />' } },
      { path: '/genres/:id', name: 'genre', component: { template: '<p>Genre detail</p>' } },
    ],
  })
  await router.push('/books/42')
  const wrapper = mount(RouterView, {
    global: {
      plugins: [router], mocks: { $formatDate: () => '' },
      stubs: { 'font-awesome-icon': true, teleport: true },
    },
  })

  await flushPromises()
  return { wrapper, router }
}

it('cancels a draft date, saves a past date, and renders that day again after reload', async () => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date(2026, 10, 21, 0, 15))
  const { wrapper, router } = await mountBookPage()

  async function selectStatus(status) {
    await wrapper.get('[aria-haspopup="listbox"]').trigger('click')
    await wrapper.findAll('[aria-label="Reading status"] [role="option"]')
      .find(option => option.text() === status).trigger('click')
  }

  await selectStatus('Read')
  const input = () => wrapper.get('input[type="date"]')
  expect(input().element.value).toBe('2026-11-21')
  await input().setValue('2023-09-17')
  await wrapper.get('form button[type="button"]').trigger('click')
  expect(wrapper.find('input[type="date"]').exists()).toBe(false)
  expect(wrapper.get('[aria-haspopup="listbox"]').text()).toContain('Unread')
  expect(wrapper.find('time').exists()).toBe(false)
  expect(api.post).not.toHaveBeenCalled()

  await selectStatus('Read')
  expect(input().element.value).toBe('2026-11-21')
  await input().setValue('2024-02-29')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(api.post).toHaveBeenCalledWith('/api/books/42/status', { status: 'Read', readingDate: '2024-02-29' })
  expect(wrapper.get('[aria-haspopup="listbox"]').text()).toContain('Read')
  expect(wrapper.get('time[datetime="2024-02-29"]').text()).toBe('2024-02-29')

  await router.push('/dashboard')
  await router.push('/books/42')
  await flushPromises()
  expect(wrapper.get('time[datetime="2024-02-29"]').text()).toBe('2024-02-29')

  await selectStatus('Reading')
  await flushPromises()
  expect(api.post).toHaveBeenLastCalledWith('/api/books/42/status', { status: 'Reading' })
  expect(wrapper.find('time').exists()).toBe(false)
})

it('renders personal and catalogue data, then cancels, retries and confirms deletion', async () => {
  const { wrapper, router } = await mountBookPage()
  api.delete.mockRejectedValueOnce(new Error('Network failure')).mockResolvedValueOnce({ status: 204 })

  expect(wrapper.get('h1').text()).toBe('Matilda')
  expect(wrapper.text()).toContain('Fantasy')
  expect(wrapper.text()).toContain('A gifted reader.')
  expect(wrapper.get('img[alt="Capa"]').attributes('src')).toBe('/backend/api/books/42/cover')

  await wrapper.findAll('[role="radio"]')[3].trigger('click')
  await flushPromises()
  expect(api.put).toHaveBeenCalledWith('/api/books/42/rating', { rating: 4 })
  expect(wrapper.findAll('[role="radio"]')[3].attributes('aria-checked')).toBe('true')

  const comment = wrapper.get('textarea')
  await comment.trigger('focus')
  await comment.setValue('Finished it')
  await comment.trigger('blur')
  await flushPromises()
  expect(api.put).toHaveBeenCalledWith('/api/books/42/comments', { comments: 'Finished it' })

  const remove = wrapper.get('[title="Delete"]')
  await remove.trigger('click')
  expect(wrapper.get('[role="dialog"]').text()).toContain('shared catalogue')
  await wrapper.get('.cm-btn--secondary').trigger('click')
  expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  expect(api.delete).not.toHaveBeenCalled()

  await remove.trigger('click')
  await wrapper.get('.cm-btn--danger').trigger('click')
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Failed to delete')
  expect(router.currentRoute.value.name).toBe('book')

  await remove.trigger('click')
  await wrapper.get('.cm-btn--danger').trigger('click')
  await flushPromises()
  expect(api.delete).toHaveBeenCalledTimes(2)
  expect(api.delete).toHaveBeenLastCalledWith('/api/books/42')
  expect(router.currentRoute.value.name).toBe('dashboard')
})

it('marks only the delete action for its destructive hover treatment', async () => {
  const { wrapper } = await mountBookPage()
  expect(wrapper.get('button[title="Delete"]').classes()).toContain('delete-action')
  expect(wrapper.get('button[title="Refresh metadata"]').classes()).not.toContain('delete-action')
})

it('shows the author photo in the link to the author and falls back when it fails', async () => {
  const { wrapper } = await mountBookPage()
  const link = wrapper.get('a[href="/authors/1"]')
  const photo = link.get('img[alt="Portrait of Roald Dahl"]')
  expect(photo.attributes('src')).toBe('https://example.invalid/author.jpg')
  await photo.trigger('error')
  expect(link.get('[aria-label="No author photo available"]').exists()).toBe(true)
})

it('opens the selected genre from book details', async () => {
  const { wrapper, router } = await mountBookPage()
  await wrapper.get('a[href="/genres/9"]').trigger('click')
  await flushPromises()
  expect(router.currentRoute.value.name).toBe('genre')
  expect(router.currentRoute.value.params.id).toBe('9')
})
