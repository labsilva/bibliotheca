import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import SeriesButton from '@/components/SeriesButton.vue'
import SeriesSelectView from '@/pages/SeriesSelectView.vue'
import api from '@/lib/http.js'

vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }))
enableAutoUnmount(afterEach)
beforeEach(() => vi.clearAllMocks())

async function start() {
  api.get.mockResolvedValue({ status: 200, data: [{ seriesId: 1, name: 'Classics' }, { seriesId: 2, name: 'Adventures' }] })
  api.put.mockResolvedValue({ status: 200 })
  api.post.mockResolvedValue({ status: 200 })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/books/:id', name: 'book', component: { components: { SeriesButton }, template: '<SeriesButton :book-id="42" />' } },
      { path: '/books/:id/series', name: 'series-select', component: SeriesSelectView },
    ],
  })
  await router.push('/books/42?tab=metadata')
  const wrapper = mount(RouterView, {
    global: { plugins: [router], stubs: { 'font-awesome-icon': true } },
  })
  await wrapper.get('button').trigger('click')
  await flushPromises()
  return { wrapper, router }
}

it('selects a matching series and returns to the book with the original query', async () => {
  const { wrapper, router } = await start()
  expect(router.currentRoute.value.name).toBe('series-select')
  await wrapper.get('[placeholder="Procurar série..."]').setValue(' advent ')
  const rows = wrapper.findAll('article')
  expect(rows).toHaveLength(1)
  expect(rows[0].text()).toContain('Adventures')
  await rows[0].get('button').trigger('click')
  await flushPromises()

  expect(api.put).toHaveBeenCalledWith('/api/books/42/series', { series: 'Adventures' }, expect.any(Object))
  expect(router.currentRoute.value.fullPath).toBe('/books/42?tab=metadata')
})

it('creates and associates a series, and can remove an existing association', async () => {
  const { wrapper, router } = await start()
  await wrapper.get('[placeholder="Nome da série"]').setValue('New series')
  await wrapper.get('.create button').trigger('click')
  await flushPromises()

  expect(api.post).toHaveBeenCalledWith('/api/series/', { name: 'New series' })
  expect(api.put).toHaveBeenCalledWith('/api/books/42/series', { series: 'New series' }, expect.any(Object))
  expect(router.currentRoute.value.name).toBe('book')

  await wrapper.get('button').trigger('click')
  await flushPromises()
  await wrapper.get('header button').trigger('click')
  await flushPromises()
  expect(api.put).toHaveBeenLastCalledWith('/api/books/42/series', { series: '' }, expect.any(Object))
  expect(router.currentRoute.value.name).toBe('book')
})

it('associates the submitted name when the draft changes during creation', async () => {
  const { wrapper, router } = await start()
  const creation = Promise.withResolvers()
  api.post.mockReturnValueOnce(creation.promise)
  const name = wrapper.get('[placeholder="Nome da série"]')

  await name.setValue('Submitted series')
  await wrapper.get('.create button').trigger('click')
  expect(api.post).toHaveBeenCalledExactlyOnceWith('/api/series/', { name: 'Submitted series' })
  expect(api.put).not.toHaveBeenCalled()
  await name.setValue('Edited draft')
  creation.resolve({ status: 200 })
  await flushPromises()

  expect(api.put).toHaveBeenCalledExactlyOnceWith('/api/books/42/series', { series: 'Submitted series' }, expect.any(Object))
  expect(router.currentRoute.value.fullPath).toBe('/books/42?tab=metadata')
})

it('associates the original book and stays on the new route when the book changes during creation', async () => {
  const { wrapper, router } = await start()
  const creation = Promise.withResolvers()
  api.post.mockReturnValueOnce(creation.promise)

  await wrapper.get('[placeholder="Nome da série"]').setValue('Submitted series')
  await wrapper.get('.create button').trigger('click')
  await router.push({ name: 'series-select', params: { id: '81' }, query: { returnTo: '/books/81?tab=reading' } })
  const nextRoute = router.currentRoute.value.fullPath
  creation.resolve({ status: 200 })
  await flushPromises()

  expect(api.put).toHaveBeenCalledExactlyOnceWith('/api/books/42/series', { series: 'Submitted series' }, expect.any(Object))
  expect(router.currentRoute.value.fullPath).toBe(nextRoute)
})

it('stays on a new visit to the same selector when the earlier association completes', async () => {
  const { wrapper, router } = await start()
  const association = Promise.withResolvers()
  api.put.mockReturnValueOnce(association.promise)
  const selectorPath = router.currentRoute.value.fullPath

  await wrapper.get('article button').trigger('click')
  expect(api.put).toHaveBeenCalledExactlyOnceWith('/api/books/42/series', { series: 'Classics' }, expect.any(Object))
  await router.push('/books/81')
  await router.push(selectorPath)
  association.resolve({ status: 200 })
  await flushPromises()

  expect(router.currentRoute.value.fullPath).toBe(selectorPath)
})

it('keeps the series refresh loading when the cancelled initial request fails', async () => {
  const initial = Promise.withResolvers()
  const refresh = Promise.withResolvers()
  api.get.mockReturnValueOnce(initial.promise).mockReturnValueOnce(refresh.promise)
  const { wrapper, router } = await start()

  await wrapper.get('[placeholder="Nome da série"]').setValue('New series')
  await wrapper.get('.create button').trigger('click')
  await flushPromises()
  expect(api.get.mock.calls[0][1].signal.aborted).toBe(true)

  initial.reject(new Error('The earlier request failed'))
  await flushPromises()
  expect(wrapper.text()).toContain('A carregar séries…')
  expect(wrapper.text()).not.toContain('Falha ao carregar séries.')

  refresh.resolve({ data: [{ seriesId: 1, name: 'New series' }] })
  await flushPromises()
  expect(api.put).toHaveBeenCalledExactlyOnceWith('/api/books/42/series', { series: 'New series' }, expect.any(Object))
  expect(router.currentRoute.value.fullPath).toBe('/books/42?tab=metadata')
})
