import { ref, toValue, watch } from 'vue'
import api from '@/lib/http.js'
import { getApiBase } from '@/lib/getApiBase'
import { catalogueAccount } from '@/auth/catalogueAccount.js'

// Each invocation owns its request and cursor; changing context cancels obsolete pages.
export function usePagedBooks(endpoint, {
  list, page = 0, size = 12, enabled = true, selectPage = data => data, onContextChange,
} = {}) {
  const books = ref([])
  const total = ref(0)
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = ref(null)
  const moreError = ref(null)
  const hasMore = ref(false)
  let controller
  let nextPage = 0
  let generation = 0

  async function fetchPage(append = false) {
    if (!toValue(enabled) || (append && (loading.value || loadingMore.value || !hasMore.value))) return
    controller?.abort()
    const request = new AbortController()
    controller = request
    const version = generation
    const path = toValue(endpoint)
    const account = catalogueAccount.value
    const isCurrent = () => !request.signal.aborted && version === generation
      && path === toValue(endpoint) && account === catalogueAccount.value
    const currentPage = append ? nextPage : Number(toValue(page))
    const pageSize = Number(toValue(size))
    if (append) { loadingMore.value = true; moreError.value = null }
    else { loading.value = true; error.value = null; moreError.value = null }
    try {
      const { data } = await api.get(path, { signal: request.signal,
        params: { list: toValue(list), page: currentPage, size: pageSize } })
      if (!isCurrent()) return
      const result = selectPage(data, append)
      const records = Array.isArray(result) ? result : (result?.items || [])
      const base = getApiBase()
      const mapped = records.map(it => ({ id: it.id, title: it.title, subtitle: it.subtitle,
        author: it.author, authorId: it.authorId, cover: base + (it.cover || `/api/books/${it.id}/cover`),
        series: it.series, seriesId: it.seriesId,
        progress: it.progress ?? null, status: it.status ?? '' }))
      if (append) {
        const unique = new Map(books.value.map(book => [book.id, book]))
        for (const book of mapped) {
          if (!unique.has(book.id)) unique.set(book.id, book)
        }
        books.value = [...unique.values()]
      } else books.value = mapped
      total.value = result?.total ?? mapped.length
      nextPage = currentPage + 1
      hasMore.value = !Array.isArray(result) && nextPage * pageSize < total.value && records.length > 0
    } catch (cause) {
      if (isCurrent()) {
        if (append) moreError.value = cause
        else error.value = cause
      }
    } finally {
      if (isCurrent()) { loading.value = false; loadingMore.value = false }
    }
  }
  function reset() {
    generation++
    controller?.abort()
    loading.value = false
    loadingMore.value = false
    hasMore.value = false
    error.value = null
    moreError.value = null
    return fetchPage()
  }

  watch(
    [() => toValue(endpoint), () => toValue(list), () => toValue(page),
      () => toValue(size), () => toValue(enabled), catalogueAccount],
    (_, __, onCleanup) => {
      books.value = []
      total.value = 0
      onContextChange?.()
      onCleanup(() => controller?.abort())
      void reset()
    },
    { immediate: true },
  )
  return { books, total, loading, loadingMore, error, moreError, hasMore, refetch: reset, loadMore: () => fetchPage(true) }
}
