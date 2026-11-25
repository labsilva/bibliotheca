import { ref, onMounted } from 'vue'
import api from '@/lib/http.js'
import { useCatalogueDetail } from './useCatalogueDetail.js'

export function getCollections({ list, page = 1, size = 12 }) {
  const collections = ref([])
  const loading = ref(false)
  const error = ref(null)

  let controller = null

  async function fetchCollections() {
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
      const { data } = await api.get(`/api/collections?${params}`, {
        signal: request.signal,
        headers: { Accept: 'application/json' },
      })
      if (request.signal.aborted) return

      // Mapear para o formato do <BookCard/>
      collections.value = (data || []).map(it => ({
        id: it.collectionId,
        name: it.name
      }))
    } catch (e) {
      if (!request.signal.aborted) error.value = e
    } finally {
      if (!request.signal.aborted) loading.value = false
    }
  }

  onMounted(fetchCollections)

  return { collections, loading, error, refetch: fetchCollections }
}

export function getCollectionById() {
  const { detail: collection, ...state } = useCatalogueDetail(id => `/api/collections/${id}`, { idField: 'collectionId' })
  return { collection, ...state }
}
