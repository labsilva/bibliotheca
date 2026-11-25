import api from '@/lib/http.js'
import { getApiBase } from '@/lib/getApiBase'
import { usePagedBooks } from './usePagedBooks.js'

export function getBooks(options) {
  return usePagedBooks('/api/books', options)
}

export async function getBookById(id, signal) {
  const { data } = await api.get('/api/books/' + id, { signal })
  return {
    book: data.book,
    coverUrl: getApiBase() + '/api/books/' + data.book.bookId + '/cover',
    userBook: data.userBook ?? null,
    readingDate: data.readingDate ?? null,
    shelves: data.shelves ?? [],
  }
}

export async function updateReadingStatus(bookId, status, readingDate) {
  await api.post('/api/books/' + bookId + '/status', { status, ...(readingDate && { readingDate }) })
}

export async function updateRating(bookId, rating) {
  await api.put('/api/books/' + bookId + '/rating', { rating })
}

export async function updateComments(bookId, comments) {
  await api.put('/api/books/' + bookId + '/comments', { comments })
}

export async function deleteBook(id) {
  await api.delete('/api/books/' + id)
}

export async function importBook(isbn) {
  const { data } = await api.post('/api/books/fetch', null, { params: { isbn } })
  return data
}
