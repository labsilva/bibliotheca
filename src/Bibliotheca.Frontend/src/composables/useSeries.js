import { ref, onMounted } from 'vue'
import api from '@/lib/http.js'
import { useCatalogueDetail } from './useCatalogueDetail.js'

export function getSeries({ list, page = 1, size = 12 }) {
  const series = ref([])
  const loading = ref(false)
  const error = ref(null)

  let controller = null

  async function fetchSeries() {
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
      const { data } = await api.get(`/api/series?${params}`, {
        signal: request.signal,
        headers: { Accept: 'application/json' },
      })
      if (request.signal.aborted) return

      // Mapear para o formato do <BookCard/>
      series.value = (data || []).map(it => ({
        id: it.seriesId,
        name: it.name
      }))
    } catch (e) {
      if (!request.signal.aborted) error.value = e
    } finally {
      if (!request.signal.aborted) loading.value = false
    }
  }

  async function addSeries(payload) {
    // payload: { name: 'Nova prateleira' } (ou o formato que o teu backend espera)
    await api.post(`/api/series/`, payload);
    await fetchSeries() // refresca imediatamente após criar
  }

  onMounted(fetchSeries)

  return { series, loading, error, refetch: fetchSeries, addSeries }
}

export function getSeriesById() {
  const { detail: series, ...state } = useCatalogueDetail(id => `/api/series/${id}`, {
    idField: 'seriesId',
    emptyDetail: {},
  })
  return { series, ...state }
}
