import { ref, onMounted } from 'vue'
import api from '@/lib/http.js'
import { useCatalogueDetail } from './useCatalogueDetail.js'

export function getGenres({ list, page = 1, size = 12 }) {
  const genres = ref([])
  const loading = ref(false)
  const error = ref(null)

  let controller = null

  async function fetchGenres() {
    loading.value = true
    error.value = null

    controller?.abort()
    const request = new AbortController()
    controller = request

    const params = new URLSearchParams({
      list,
      page: String(page),
      size: String(size)
    })

    try {
      const { data } = await api.get(`/api/genres?${params}`, {
        signal: request.signal,
        headers: { Accept: 'application/json' },
      })
      if (request.signal.aborted) return

      // Mapear para o formato do <BookCard/>
      genres.value = (data || []).map(it => ({
        id: it.bookGenreId,
        name: it.genre,
        bookCount: it.bookCount
      }))
    } catch (e) {
      if (!request.signal.aborted) error.value = e
    } finally {
      if (!request.signal.aborted) loading.value = false
    }
  }

  onMounted(fetchGenres)

  return { genres, loading, error, fetchGenres }
}

export function getGenreById() {
  const { detail: genre, ...state } = useCatalogueDetail(id => `/api/genres/${id}`, { fallbackError: 'Unknown error' })
  return { genre, ...state }
}
