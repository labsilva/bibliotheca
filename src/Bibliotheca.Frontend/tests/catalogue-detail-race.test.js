import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import { getAuthorById } from '@/composables/useAuthors.js'
import { getCollectionById } from '@/composables/useCollections.js'
import { getGenreById } from '@/composables/useGenres.js'
import { fetchShelveById } from '@/composables/useLibraries.js'
import { getSeriesById } from '@/composables/useSeries.js'
import { getTagById } from '@/composables/useTags.js'
import api from '@/lib/http.js'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
enableAutoUnmount(afterEach)

const authors = { key: 'author', load: getAuthorById, endpoint: '/api/authors' }

async function start({ load, key }) {
  const param = key === 'shelve' ? 'name' : 'id'
  const page = {
    setup() {
      const state = load()
      return { ...state, entry: state[key] }
    },
    template: `
      <main>
        <h1 v-if="entry">{{ entry.name }}</h1>
        <p v-if="loading" role="status">Loading</p>
        <p v-if="error" role="alert">{{ error }}</p>
        <article v-for="book in books" :key="book.id">{{ book.title }}</article>
      </main>
    `,
  }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<p>Home</p>' } },
      { path: `/detail/:${param}`, component: page },
    ],
  })
  await router.push('/detail/1')
  const wrapper = mount(RouterView, { global: { plugins: [router] } })
  return { router, wrapper }
}

function detailResponse(id, isShelf) {
  const books = [{ id, title: `Book ${id}`, cover: `/api/books/${id}/cover` }]
  return { status: 200, data: isShelf ? books : { name: `Entry ${id}`, books } }
}

it.each([
  authors,
  { key: 'collection', load: getCollectionById, endpoint: '/api/collections' },
  { key: 'genre', load: getGenreById, endpoint: '/api/genres' },
  { key: 'series', load: getSeriesById, endpoint: '/api/series' },
  { key: 'tag', load: getTagById, endpoint: '/api/tags', suffix: '/books' },
  { key: 'shelve', load: fetchShelveById, endpoint: '/api/library/shelves', suffix: '/books' },
])('keeps the current $key detail when an older request resolves last', async (detail) => {
  const earlier = Promise.withResolvers()
  const current = Promise.withResolvers()
  api.get.mockReturnValueOnce(earlier.promise).mockReturnValueOnce(current.promise)
  const { router, wrapper } = await start(detail)
  const isShelf = detail.key === 'shelve'
  const detailPath = id => `${detail.endpoint}/${id}${detail.suffix ?? ''}`

  await router.push('/detail/2')
  const [[earlierPath, earlierOptions], [currentPath, currentOptions]] = api.get.mock.calls
  expect(earlierPath).toBe(detailPath(1))
  expect(currentPath).toBe(detailPath(2))
  expect(earlierOptions.signal.aborted).toBe(true)
  expect(currentOptions.signal.aborted).toBe(false)

  current.resolve(detailResponse(2, isShelf))
  await flushPromises()
  earlier.resolve(detailResponse(1, isShelf))
  await flushPromises()

  expect(wrapper.get('article').text()).toBe('Book 2')
  if (!isShelf) expect(wrapper.get('h1').text()).toBe('Entry 2')
  expect(wrapper.find('[role="status"]').exists()).toBe(false)
  expect(wrapper.find('[role="alert"]').exists()).toBe(false)
})

it('keeps the current request loading when an older request fails', async () => {
  const earlier = Promise.withResolvers()
  const current = Promise.withResolvers()
  api.get.mockReturnValueOnce(earlier.promise).mockReturnValueOnce(current.promise)
  const { router, wrapper } = await start(authors)
  await router.push('/detail/2')

  earlier.reject(new Error('Old request failed'))
  await flushPromises()
  expect(wrapper.get('[role="status"]').text()).toBe('Loading')
  expect(wrapper.find('[role="alert"]').exists()).toBe(false)

  current.resolve(detailResponse(2, false))
  await flushPromises()
  expect(wrapper.get('article').text()).toBe('Book 2')
  expect(wrapper.find('[role="status"]').exists()).toBe(false)
})

it('aborts the pending detail request when the route unmounts', async () => {
  const pending = Promise.withResolvers()
  api.get.mockReturnValueOnce(pending.promise)
  const { router, wrapper } = await start(authors)
  const { signal } = api.get.mock.calls[0][1]

  await router.push('/')
  expect(signal.aborted).toBe(true)
  pending.resolve(detailResponse(1, false))
  await flushPromises()

  expect(wrapper.text()).toBe('Home')
})
