import { ref, onMounted } from 'vue'
import api from '@/lib/http.js'
import { useCatalogueDetail } from './useCatalogueDetail.js'

export function getAuthors({ list, page = 1, size = 12 }) {
  const authors = ref([])
  const loading = ref(false)
  const error = ref(null)

  let controller = null

  async function fetchAuthors() {
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
      const { data } = await api.get(`/api/authors?${params}`, {
        signal: request.signal,
        headers: { Accept: 'application/json' },
      })
      if (request.signal.aborted) return

      // Mapear para o formato do <BookCard/>
      authors.value = (data || []).map(it => ({
        id: it.authorId,
        name: it.authorName,
        photoUrl: it.photoUrl
      }))
    } catch (e) {
      if (!request.signal.aborted) error.value = e
    } finally {
      if (!request.signal.aborted) loading.value = false
    }
  }

  onMounted(fetchAuthors)

  return { authors, loading, error, refetch: fetchAuthors }
}


export function getAuthorById() {
  const { detail: author, ...state } = useCatalogueDetail(id => `/api/authors/${id}`, {
    mapDetail: ({ id, name, photoUrl, biography, biographySourceUrl }) =>
      ({ id, name, photoUrl, biography, biographySourceUrl }),
  })
  return { author, ...state }
}
