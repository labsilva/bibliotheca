import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import Authors from '@/pages/Authors.vue'
import AuthorDetails from '@/pages/AuthorDetails.vue'
import Collections from '@/pages/Collections.vue'
import CollectionDetails from '@/pages/CollectionDetails.vue'
import Series from '@/pages/Series.vue'
import SeriesDetails from '@/pages/SeriesDetails.vue'
import Genres from '@/pages/Genres.vue'
import GenreDetails from '@/pages/GenreDetails.vue'
import Tags from '@/pages/Tags.vue'
import TagsDetails from '@/pages/TagsDetails.vue'
import BookCarousel from '@/components/BookCarousel.vue'
import api from '@/lib/http.js'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
enableAutoUnmount(afterEach)

const cataloguePages = [
  { path: 'authors', route: 'author', record: { authorId: 1, authorName: 'Roald Dahl' }, title: 'Roald Dahl', index: Authors, details: AuthorDetails, heading: 'h1' },
  { path: 'collections', route: 'collection', record: { collectionId: 1, name: 'Classics' }, title: 'Classics', index: Collections, details: CollectionDetails },
  { path: 'series', route: 'series', record: { seriesId: 1, name: 'Adventure' }, title: 'Adventure', index: Series, details: SeriesDetails },
  { path: 'genres', route: 'genre', record: { bookGenreId: 1, genre: 'Fantasy', bookCount: 2 }, title: 'Fantasy', index: Genres, details: GenreDetails, countText: '2 books' },
  { path: 'tags', route: 'tag', record: { tagId: 1, tag: 'Favorites' }, title: 'Favorites', index: Tags, details: TagsDetails, suffix: '/books' },
]

it.each(cataloguePages)('opens $path and displays the selected entry and its books', async ({ path, route, record, title, index, details, suffix = '', heading = 'h2', countText }) => {
  api.get.mockResolvedValueOnce({ status: 200, data: [record] })
  api.get.mockResolvedValueOnce({ status: 200, data: {
    id: 1, name: title, booksTotal: 13,
    books: [{ id: 42, title: 'Matilda', cover: '/api/books/42/cover', status: 'Read' },
      ...Array.from({ length: 11 }, (_, index) => ({ id: index + 100 }))],
  } })
  api.get.mockResolvedValueOnce({ status: 200, data: {
    id: 1, name: title, booksTotal: 13, books: [{ id: 43, title: 'The BFG' }],
  } })
  api.get.mockResolvedValueOnce({ status: 200, data: { id: 2, name: 'Another entry', books: [] } })

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/dashboard', name: 'dashboard', component: { template: '<div />' } },
      { path: '/' + path, component: index },
      { path: '/' + path + '/:id', name: route, component: details },
    ],
  })
  await router.push('/' + path)
  const wrapper = mount(RouterView, {
    global: { plugins: [router], stubs: { BookCarousel: true, 'font-awesome-icon': true } },
  })
  await flushPromises()

  expect(wrapper.get('article').text()).toContain(title)
  if (countText) expect(wrapper.get('article').text()).toContain(countText)
  await wrapper.get('article a').trigger('click')
  await flushPromises()

  expect(router.currentRoute.value.params.id).toBe('1')
  expect(wrapper.get(heading).text()).toBe(title)
  const carousel = wrapper.getComponent(BookCarousel)
  expect(carousel.props('books')[0]).toEqual(
    expect.objectContaining({ id: 42, title: 'Matilda', cover: '/backend/api/books/42/cover', status: 'Read' }),
  )
  await carousel.props('loadMore')()
  await flushPromises()
  expect(api.get).toHaveBeenLastCalledWith('/api/' + path + '/1' + suffix, {
    signal: expect.any(AbortSignal), params: { list: undefined, page: 1, size: 12 },
  })
  expect(carousel.props('books').at(-1).title).toBe('The BFG')
  expect(carousel.props('hasMore')).toBe(false)

  await router.push({ name: route, params: { id: 2 } })
  await flushPromises()
  expect(api.get).toHaveBeenLastCalledWith('/api/' + path + '/2' + suffix, {
    signal: expect.any(AbortSignal), params: { list: undefined, page: 0, size: 12 },
  })
  expect(wrapper.get(heading).text()).toBe('Another entry')
  expect(wrapper.getComponent(BookCarousel).props('books')).toEqual([])
})

it('shows the author portrait and falls back for unavailable images', async () => {
  const placeholder = '[role="img"][aria-label="No author photo available"]'
  const photo = 'https://covers.openlibrary.org/a/id/123-M.jpg?default=false'
  const otherPhoto = 'https://covers.openlibrary.org/a/id/456-M.jpg?default=false'
  api.get
    .mockResolvedValueOnce({ data: { id: 1, name: 'Roald Dahl', photoUrl: photo, books: [] } })
    .mockResolvedValueOnce({ data: { id: 2, name: 'Eva Smith', photoUrl: otherPhoto, books: [] } })
    .mockResolvedValueOnce({ data: { id: 3, name: 'Sam Doe', photoUrl: null, books: [] } })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/authors/:id', component: AuthorDetails }],
  })
  await router.push('/authors/1')
  const wrapper = mount(RouterView, {
    global: { plugins: [router], stubs: { BookCarousel: true, 'font-awesome-icon': true } },
  })
  await flushPromises()

  const portrait = wrapper.get('img[alt="Portrait of Roald Dahl"]')
  expect(portrait.attributes('src')).toBe(photo)
  expect(wrapper.find(placeholder).exists()).toBe(false)
  await portrait.trigger('error')
  expect(wrapper.find('img').exists()).toBe(false)
  expect(wrapper.find(placeholder).exists()).toBe(true)

  await router.push('/authors/2')
  await flushPromises()
  expect(wrapper.get('img[alt="Portrait of Eva Smith"]').attributes('src')).toBe(otherPhoto)
  expect(wrapper.find(placeholder).exists()).toBe(false)

  await router.push('/authors/3')
  await flushPromises()
  expect(wrapper.get('h1').text()).toBe('Sam Doe')
  expect(wrapper.find('img').exists()).toBe(false)
  expect(wrapper.find(placeholder).exists()).toBe(true)
})

it.each(cataloguePages.filter(({ path }) => path !== 'tags'))('shows loading and HTTP failures for $path', async ({ path, index }) => {
  const request = Promise.withResolvers()
  api.get.mockReturnValueOnce(request.promise)
  const wrapper = mount(index, { global: { stubs: { RouterLink: true, 'font-awesome-icon': true } } })
  const paging = path === 'genres' ? 'page=1&size=12' : 'page=0&size=20'
  await flushPromises()

  expect(api.get).toHaveBeenCalledExactlyOnceWith(`/api/${path}?list=&${paging}`, {
    signal: expect.any(AbortSignal),
    headers: { Accept: 'application/json' },
  })
  expect(wrapper.text()).toContain(`Loading ${path}...`)
  expect(wrapper.find('[role="alert"]').exists()).toBe(false)

  request.reject(new Error('Request failed with status code 503'))
  await flushPromises()

  expect(wrapper.text()).not.toContain(`Loading ${path}...`)
  expect(wrapper.get('[role="alert"]').text()).toBe('Request failed with status code 503')
})
it('attributes a Wook biography to its author page', async () => {
  api.get.mockResolvedValueOnce({ data: { id: 7, name: 'Writer', biography: 'Biography',
    biographySourceUrl: 'https://www.wook.pt/autor/writer/123', books: [] } })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/authors/:id', component: AuthorDetails }] })
  await router.push('/authors/7')
  const wrapper = mount(RouterView, { global: { plugins: [router], stubs: { BookCarousel: true, 'font-awesome-icon': true } } })
  await flushPromises()
  const link = wrapper.get('.attribution a')
  expect(link.text()).toBe('Wook')
  expect(link.attributes('href')).toBe('https://www.wook.pt/autor/writer/123')
})

it('refreshes only the current author and reloads the profile', async () => {
  api.get.mockImplementation(async url => url.startsWith('/api/author-refresh/')
    ? { data: { completedAtUtc: '2026-01-01', items: [{ status: 'updated' }] } }
    : { data: { id: 7, name: 'Writer', biography: 'Updated', books: [] } })
  api.post.mockResolvedValueOnce({ data: { id: 'job' } })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/authors/:id', component: AuthorDetails }] })
  await router.push('/authors/7')
  const wrapper = mount(RouterView, { global: { plugins: [router], stubs: { BookCarousel: true, 'font-awesome-icon': true } } })
  await flushPromises()
  await wrapper.get('.refresh-author').trigger('click')
  await flushPromises()
  expect(api.post).toHaveBeenCalledWith('/api/author-refresh/authors/7', {}, expect.any(Object))
  expect(api.get).toHaveBeenCalledWith('/api/author-refresh/job', expect.any(Object))
  expect(api.get.mock.calls.filter(([url]) => url.startsWith('/api/authors/7'))).toHaveLength(2)
  expect(wrapper.get('.summary').text()).toBe('Updated')
})
