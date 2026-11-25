import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { effectScope, ref } from 'vue'
import api from '@/lib/http.js'
import { useBookDetail } from '@/composables/useBookDetail.js'
import { getBooks } from '@/composables/useBooks.js'

vi.mock('@/lib/http.js', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

function bookResponse(id) {
  return {
    data: {
      book: { bookId: id, tags: [] },
      userBook: { rating: 2, comments: '', readingStatus: 'Unread', shelve: { shelveName: 'Main shelf' } },
      shelves: [{ shelveName: 'Main shelf' }, { shelveName: 'Second shelf' }],
    },
  }
}

describe('book details and catalogue queries', () => {
  let scope

  beforeEach(() => {
    scope = effectScope()
    vi.resetAllMocks()
    api.get.mockImplementation(async (url) => {
      if (url.startsWith('/api/books/')) return bookResponse(Number(url.split('/').at(-1)))
      return { data: { libraries: [], shelves: [] } }
    })
    api.put.mockResolvedValue({ status: 200 })
    api.post.mockResolvedValue({ status: 200 })
    api.delete.mockResolvedValue({ status: 204 })
  })

  afterEach(() => scope.stop())

  it('refreshes the selected book and reloads its details after completion', async () => {
    const detail = scope.run(() => useBookDetail(2))
    await flushPromises()
    api.post.mockResolvedValueOnce({ data: { id: 'batch' } })
    const get = api.get.getMockImplementation()
    api.get.mockImplementation(url => url === '/api/catalogue-refresh/batch'
      ? Promise.resolve({ data: { completedAtUtc: '2026-01-01', items: [{ status: 'updated' }] } })
      : get(url))
    expect(await detail.refreshMetadata()).toBe(true)
    expect(api.post).toHaveBeenCalledWith('/api/catalogue-refresh/books/2', {}, expect.any(Object))
    expect(api.get).toHaveBeenCalledWith('/api/catalogue-refresh/batch', expect.any(Object))
    expect(api.get.mock.calls.filter(([url]) => url === '/api/books/2')).toHaveLength(2)
  })

  it('reports a failed refresh without discarding the book', async () => {
    const detail = scope.run(() => useBookDetail(2))
    await flushPromises()
    api.post.mockRejectedValueOnce(new Error('Unavailable'))
    expect(await detail.refreshMetadata()).toBe(false)
    expect(detail.book.value.book.bookId).toBe(2)
    expect(detail.actionError.value).toMatch(/Could not refresh/)
  })

  it('keeps the selected book when an earlier request completes late', async () => {
    let resolveEarlier
    const get = api.get.getMockImplementation()
    api.get.mockImplementation((url) => {
      if (url === '/api/books/1') return new Promise(resolve => { resolveEarlier = resolve })
      return get(url)
    })
    const id = ref(1)
    const detail = scope.run(() => useBookDetail(id))
    await flushPromises()

    id.value = 2
    await flushPromises()
    resolveEarlier(bookResponse(1))
    await flushPromises()

    expect(detail.book.value.book.bookId).toBe(2)
    expect(detail.loading.value).toBe(false)
    expect(detail.error.value).toBe('')
  })

  it('keeps the saved rating on failure and shows the result of a retry', async () => {
    const detail = scope.run(() => useBookDetail(2))
    await flushPromises()
    api.put.mockRejectedValueOnce(new Error('Network error'))

    expect(await detail.saveRating(5)).toBe(false)
    expect(detail.book.value.userBook.rating).toBe(2)
    expect(detail.actionError.value).toMatch(/Could not save rating/)

    expect(await detail.saveRating(4)).toBe(true)
    expect(api.put).toHaveBeenLastCalledWith('/api/books/2/rating', { rating: 4 })
    expect(detail.book.value.userBook.rating).toBe(4)
    expect(detail.actionError.value).toBe('')
  })

  it('updates only the relevant state after changing status, comments and tags', async () => {
    const detail = scope.run(() => useBookDetail(2))
    await flushPromises()
    api.post.mockResolvedValueOnce({ status: 200 })
    api.post.mockResolvedValueOnce({ status: 200, data: { tagId: 7, tag: 'Fantasy' } })

    expect(await detail.saveReadingStatus('Read', '2024-02-29')).toBe(true)
    expect(await detail.saveComments('Finished it')).toBe(true)
    expect(await detail.saveTag({ tag: 'Fantasy' })).toBe(true)

    expect(api.post).toHaveBeenCalledWith('/api/books/2/status', { status: 'Read', readingDate: '2024-02-29' })
    expect(api.put).toHaveBeenCalledWith('/api/books/2/comments', { comments: 'Finished it' })
    expect(api.put).toHaveBeenCalledWith('/api/books/2/tag', { tagId: 7 })
    expect(detail.book.value.userBook).toMatchObject({ readingStatus: 'Read', comments: 'Finished it' })
    expect(detail.book.value.readingDate).toBe('2024-02-29')
    expect(detail.book.value.book.tags).toEqual([{ tagId: 7, tag: 'Fantasy' }])
  })

  it('moves and removes a book using the current shelf', async () => {
    const detail = scope.run(() => useBookDetail(2))
    await flushPromises()

    expect(await detail.saveShelf('Second shelf')).toBe(true)
    expect(api.put).toHaveBeenCalledWith('/api/library/shelves/Second%20shelf/books/2')
    expect(detail.book.value.userBook.shelve.shelveName).toBe('Second shelf')

    expect(await detail.saveShelf('Unshelved')).toBe(true)
    expect(api.delete).toHaveBeenCalledWith('/api/library/shelves/Second%20shelf/books/2')
    expect(detail.book.value.userBook).toBeNull()
  })

  it('reloads the catalogue when the page changes', async () => {
    api.get.mockImplementation(async (_, { params }) => ({
      data: [{ id: params.page, title: 'Book' }],
    }))
    const page = ref(1)
    const catalogue = scope.run(() => getBooks({ list: 'recent', page, size: 10 }))
    await flushPromises()
    expect(catalogue.books.value[0].id).toBe(1)

    page.value = 2
    await flushPromises()

    expect(api.get).toHaveBeenLastCalledWith('/api/books', expect.objectContaining({
      params: { list: 'recent', page: 2, size: 10 },
    }))
    expect(catalogue.books.value[0].id).toBe(2)
  })
})
