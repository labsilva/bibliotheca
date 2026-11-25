import { onScopeDispose, reactive, ref, watch, toValue } from 'vue'
import { deleteBook, getBookById, updateComments, updateRating, updateReadingStatus } from './useBooks'
import { useLibraries, useShelves } from './useLibraries'
import { useTagsApi } from './useTags'
import api from '@/lib/http.js'

export function useBookDetail(id) {
  const book = ref(null)
  const loading = ref(false)
  const error = ref('')
  const actionError = ref('')
  const saving = reactive({})
  let pending = Promise.resolve()
  let active = true

  const lifecycle = new AbortController()
  onScopeDispose(() => { active = false; lifecycle.abort() })
  const { addBookToShelf, removeBookFromShelf, refetch: refetchShelves } = useShelves()
  const { refetch: refetchLibraries } = useLibraries()
  const { createTag, addTagToBook } = useTagsApi()

  watch(() => toValue(id), async (bookId, _, onCleanup) => {
    const request = new AbortController()
    onCleanup(() => request.abort())
    book.value = null
    error.value = ''
    actionError.value = ''
    loading.value = true

    try {
      const loaded = await getBookById(bookId, request.signal)
      if (!request.signal.aborted) book.value = loaded
    } catch (err) {
      if (!request.signal.aborted) error.value = err.message
    } finally {
      if (!request.signal.aborted) loading.value = false
    }
  }, { immediate: true })

  async function save(key, message, action) {
    const current = book.value
    if (!current || saving[key]) return false

    saving[key] = true
    const operation = pending.then(async () => {
      if (!active || book.value !== current) return
      actionError.value = ''
      await action(current, current.book.bookId)
    })
    pending = operation.catch(() => {})
    try {
      await operation
      return active && book.value === current
    } catch {
      if (active && book.value === current) actionError.value = message
      return false
    } finally {
      saving[key] = false
    }
  }

  function saveReadingStatus(status, readingDate) {
    return save('status', 'Could not update reading status.', async (current, bookId) => {
      await updateReadingStatus(bookId, status, readingDate)
      if (current.userBook && (status !== 'Read' || readingDate)) {
        current.userBook.readingStatus = status
        current.readingDate = status === 'Read' ? readingDate : null
      } else {
        const updated = await getBookById(bookId)
        current.userBook = updated.userBook
        current.readingDate = updated.readingDate
      }
    })
  }

  function saveShelf(name) {
    return save('shelf', 'Could not update shelf.', async (current, bookId) => {
      if (name === 'Unshelved') {
        await removeBookFromShelf(current.userBook.shelve.shelveName, bookId)
        current.userBook = null
        current.readingDate = null
      } else {
        await addBookToShelf(name, bookId)
        current.userBook = {
          readingStatus: 'Unread',
          ...current.userBook,
          shelve: current.shelves.find(shelf => shelf.shelveName === name),
        }
      }
    })
  }

  function saveRating(rating) {
    return save('rating', 'Could not save rating.', async (current, bookId) => {
      await updateRating(bookId, rating)
      current.userBook.rating = rating
    })
  }

  function saveComments(comments) {
    return save('comments', 'Could not save comment. Edit the text to retry.', async (current, bookId) => {
      await updateComments(bookId, comments)
      if (current.userBook) current.userBook.comments = comments
    })
  }

  function saveTag(tag) {
    return save('tag', 'Failed to add tag.', async (current, bookId) => {
      const attached = tag.tagId ? tag : await createTag(tag.tag)
      await addTagToBook(bookId, attached.tagId)
      current.book.tags = [...(current.book.tags ?? []), attached]
    })
  }

  function refreshMetadata() {
    return save('refresh', 'Could not refresh book metadata. Please try again.', async (current, bookId) => {
      const { data } = await api.post(`/api/catalogue-refresh/books/${bookId}`, {}, { signal: lifecycle.signal })
      let batch
      do {
        const response = await api.get(`/api/catalogue-refresh/${data.id}`, { signal: lifecycle.signal })
        batch = response.data
        if (!batch.completedAtUtc) {
          await new Promise((resolve, reject) => {
            const onAbort = () => { clearTimeout(timer); reject(new Error('Cancelled')) }
            const timer = setTimeout(() => { lifecycle.signal.removeEventListener('abort', onAbort); resolve() }, 2000)
            lifecycle.signal.addEventListener('abort', onAbort, { once: true })
          })
        }
      } while (!batch.completedAtUtc && active && book.value === current)
      if (!active || book.value !== current) return
      if (batch.items[0]?.status === 'failed') throw new Error('Refresh failed')
      const updated = await getBookById(bookId, lifecycle.signal)
      Object.assign(current, updated)
    })
  }

  function removeBook() {
    return save('delete', 'Failed to delete book.', async (_, bookId) => {
      await deleteBook(bookId)
      await Promise.all([refetchShelves(), refetchLibraries()])
    })
  }

  return {
    book, loading, error, actionError, saving,
    saveReadingStatus, saveShelf, saveRating, saveComments, saveTag, removeBook, refreshMetadata,
  }
}
