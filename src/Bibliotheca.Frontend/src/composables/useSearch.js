import { onScopeDispose, ref, watch } from 'vue'
import { getApiBase } from '@/lib/getApiBase'
import api from '@/lib/http.js'

export function useSearch() {
  const q = ref('')
  const results = ref([])
  const loading = ref(false)
  const error = ref(null)
  const open = ref(false)
  const activeIndex = ref(-1)
  let timer
  let controller

  function cancelPending() {
    clearTimeout(timer)
    controller?.abort()
    controller = undefined
  }

  async function fetchResults(text) {
    const request = new AbortController()
    controller = request

    try {
      const { data } = await api.get('/api/search', {
        params: { text },
        signal: request.signal,
        headers: { Accept: 'application/json' },
      })
      if (controller !== request) return
      const base = getApiBase()
      results.value = (Array.isArray(data) ? data : []).map(item => ({
        area: item.area,
        id: item.id,
        title: item.title,
        imagePath: item.imagePath ? base + item.imagePath : '',
      }))
    } catch (err) {
      if (controller === request) error.value = err
    } finally {
      if (controller === request) {
        loading.value = false
        controller = undefined
      }
    }
  }

  watch(q, value => {
    cancelPending()
    const text = value.trim()
    results.value = []
    error.value = null
    activeIndex.value = -1
    open.value = Boolean(text)
    loading.value = Boolean(text)
    if (text) timer = setTimeout(() => fetchResults(text), 250)
  }, { flush: 'sync' })

  onScopeDispose(cancelPending)

  function close() {
    open.value = false
    activeIndex.value = -1
  }

  function move(delta) {
    const count = results.value.length
    if (!open.value || count === 0) return
    const next = activeIndex.value + delta
    activeIndex.value = next < 0 ? count - 1 : next % count
  }

  function reset() {
    q.value = ''
  }

  return { q, results, loading, error, open, activeIndex, close, move, reset }
}
