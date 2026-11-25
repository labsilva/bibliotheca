import { ref, reactive, watch } from 'vue'
import { useRoute } from 'vue-router'
import api from '@/lib/http.js'
import { getApiBase } from '@/lib/getApiBase'
import { usePagedBooks } from './usePagedBooks.js'
import { catalogueAccount } from '@/auth/catalogueAccount.js'

export function useCatalogueDetail(pathFor, {
  param = 'id',
  idField = 'id',
  mapDetail = data => ({ id: data[idField], name: data.name }),
  emptyDetail = null,
  fallbackError = 'Erro desconhecido',
} = {}) {
  const route = useRoute()
  const detail = ref(emptyDetail)
  const hasDetail = idField !== null
  if (hasDetail) {
    const state = usePagedBooks(
      () => pathFor(route.params[param]),
      {
        enabled: () => Boolean(route.params[param]),
        onContextChange: () => { detail.value = emptyDetail },
        selectPage(data, append) {
          if (!append) detail.value = mapDetail(data)
          return { items: data.books, total: data.booksTotal }
        },
      },
    )
    const { books, loading, error, refetch, loadMore, hasMore, loadingMore, moreError } = state
    const carousel = reactive({
      books, loading, error, retry: refetch, loadMore, hasMore, loadingMore, moreError,
    })
    return { detail, carousel, ...state }
  }

  const books = ref([])
  const loading = ref(false)
  const error = ref(null)
  watch([() => route.params[param], catalogueAccount], async ([value, account], _, onCleanup) => {
    detail.value = emptyDetail
    books.value = []
    error.value = null
    loading.value = Boolean(value)
    if (!value) return

    const request = new AbortController()
    onCleanup(() => request.abort())
    const isCurrent = () => !request.signal.aborted && route.params[param] === value
      && catalogueAccount.value === account

    try {
      const { data } = await api.get(pathFor(value), { signal: request.signal })
      if (!isCurrent()) return

      const base = getApiBase()
      books.value = (data || []).map(book => ({
        id: book.id,
        title: book.title,
        subtitle: book.subtitle,
        author: book.author,
        cover: base + book.cover,
        progress: book.progress ?? null,
        status: book.status ?? '',
        authorId: book.authorId,
        series: book.series,
        seriesId: book.seriesId,
      }))
    } catch (cause) {
      if (isCurrent()) {
        error.value = cause?.message || fallbackError
      }
    } finally {
      if (isCurrent()) loading.value = false
    }
  }, { immediate: true })

  return { detail, books, loading, error }
}
