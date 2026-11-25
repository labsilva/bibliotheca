import { ref, onMounted, watchEffect } from 'vue'
import api from '@/lib/http.js'
import { getApiBase } from '@/lib/getApiBase'
import { useCatalogueDetail } from './useCatalogueDetail.js'

export function useTagSearch() {
  async function searchTags(query, signal) {
    const url = `/api/tags/search?tag=${encodeURIComponent(query)}`
    const { data } = await api.get(url, { signal })
    return Array.isArray(data) ? data : []
  }

  return { searchTags }
}

export function getTags() {
  const tags = ref([])
  const loading = ref(false)
  const error = ref(null)

  let controller = null

  async function fetchTags() {
    loading.value = true
    error.value = null

    // cancela pedido anterior (se ainda estiver em voo)
    if (controller) controller.abort()
    controller = new AbortController()

    try {
      const res = await api.get(`/api/tags`, {
          signal: controller.signal,
          headers: { 'Accept': 'application/json' }
      })
      if (!res.status == 200) throw new Error(`HTTP ${res.status}`)
      const data = await res.data

      // Mapear para o formato do <BookCard/>
      tags.value = (data || []).map(it => ({
        id: it.tagId,
        name: it.tag
      }))
    } catch (e) {
      if (e.name !== 'AbortError') error.value = e
    } finally {
      loading.value = false
    }
  }
  onMounted(fetchTags)
  return { tags, loading, error, refetch: fetchTags }
}

export function getTagById() {
  const { detail: tag, ...state } = useCatalogueDetail(id => `/api/tags/${id}/books`)
  return { tag, ...state }
}

export function useTagsApi() {
    async function createTag(tag, bookId) {
      const base = getApiBase();
      const res = await api.post(`${base}/api/tags/`, { tag })
      if (!res.status == 200) throw new Error(`Create tag failed: ${res.status}`)
      const data = await res.data
      // Expecting { tagId, tag }
      return data
  }

    async function addTagToBook(bookId, tagId) {
      const res = await api.put(`/api/books/${bookId}/tag`, { tagId })
      if (!res.status == 200) throw new Error(`Attach tag failed: ${res.status}`)
      return true
  }

  return { createTag, addTagToBook }
}
