import { ref, toValue, watch } from 'vue'
import api from '@/lib/http.js'
import { catalogueAccount } from '@/auth/catalogueAccount.js'
import { useCatalogueDetail } from './useCatalogueDetail.js'
import { usePagedBooks } from './usePagedBooks.js'

const libraries = ref([])
const loadingLibraries = ref(false)
const errorLibraries = ref(null)
let librariesLoadedOnce = false

async function fetchLibraries() {
    loadingLibraries.value = true
    errorLibraries.value = null

    let controller = null;
    // cancela pedido anterior (se ainda estiver em voo)
    if (controller) controller.abort()
    controller = new AbortController()

    try {
        const res = await api.get(`/api/library`, {
            signal: controller.signal,
            headers: { 'Accept': 'application/json' }
      })
    const data = res.data   // não faças `await` aqui
    libraries.value = data.libraries || []
    librariesLoadedOnce = true
  } catch (e) {
    errorLibraries.value = e
  } finally {
    loadingLibraries.value = false
  }
}

export function fetchBooks(options) {
  return usePagedBooks('/api/library/books', options)
}

export function useLibraries() {
  if (!librariesLoadedOnce && !loadingLibraries.value) {
    fetchLibraries()
  }

  return {
    libraries,
    loading: loadingLibraries,
    error: errorLibraries,
    refetch: fetchLibraries
  }
}

const shelves = ref([])
const loadingShelves = ref(false)
const errorShelves = ref(null)
let shelvesLoadedOnce = false

let shelfRequest

async function fetchShelves() {
  shelfRequest?.abort()
  const request = new AbortController()
  shelfRequest = request
  const account = catalogueAccount.value
  const isCurrent = () => !request.signal.aborted && account === catalogueAccount.value
  loadingShelves.value = true
  errorShelves.value = null

  try {
    const { data } = await api.get('/api/library/shelves', {
      signal: request.signal,
      headers: { Accept: 'application/json' },
    })
    if (!isCurrent()) return
    shelves.value = data.shelves || []
    shelvesLoadedOnce = true
  } catch (error) {
    if (isCurrent()) errorShelves.value = error
  } finally {
    if (isCurrent()) loadingShelves.value = false
  }
}

watch(catalogueAccount, account => {
  const wasRequested = Boolean(shelfRequest)
  shelfRequest?.abort()
  shelfRequest = undefined
  shelves.value = []
  shelvesLoadedOnce = false
  loadingShelves.value = false
  errorShelves.value = null
  if (wasRequested && account) void fetchShelves()
}, { flush: 'sync' })

function shelfBooksPath(name) {
  return `/api/library/shelves/${encodeURIComponent(name)}/books`
}

export function useShelfBooks(name) {
  return usePagedBooks(() => shelfBooksPath(toValue(name)))
}

export function useShelves() {
  // ⚠️ importante: obter o refetch das libraries aqui
  const { refetch: refetchLibraries } = useLibraries()

  if (!shelvesLoadedOnce && !loadingShelves.value) {
    fetchShelves()
  }

  async function addShelf(payload) {
    await api.post(`/api/library/shelves/${encodeURIComponent(payload.name)}`)
    await fetchShelves()
  }

  async function addBookToShelf(shelfName, bookId) {
    await api.put(`/api/library/shelves/${encodeURIComponent(shelfName)}/books/${bookId}`)
    await fetchShelves()
    await refetchLibraries() 
  }

  async function removeBookFromShelf(shelfName, bookId) {
    await api.delete(`/api/library/shelves/${encodeURIComponent(shelfName)}/books/${bookId}`)
    await fetchShelves()
    await refetchLibraries() 
  }

  return {
    shelves,
    loading: loadingShelves,
    error: errorShelves,
    refetch: fetchShelves,
    addShelf,
    addBookToShelf,
    removeBookFromShelf,
  }
}

export function fetchShelveById() {
  const { detail: shelve, ...state } = useCatalogueDetail(
    shelfBooksPath,
    { param: 'name', idField: null },
  )
  return { shelve, ...state }
}
