import { shallowRef } from 'vue'
import api from '@/lib/http.js'

export const importEntries = shallowRef([])
let session = { request: null, refreshAgain: false }

export function resetImportHistory() {
  session = { request: null, refreshAgain: false }
  importEntries.value = []
}

export function refreshImportHistory() {
  const current = session
  if (current.request) {
    current.refreshAgain = true
    return current.request
  }
  current.request = (async () => {
    do {
      current.refreshAgain = false
      const { data } = await api.get('/api/imports')
      if (current !== session) return
      importEntries.value = data
    } while (current.refreshAgain)
  })().finally(() => { current.request = null })
  return current.request
}

// Submissions and SignalR carry individual import progress. Re-read the compact
// batch summaries to reconcile duplicate or out-of-order events with stored scans.
export function recordImport(entry) {
  if (entry?.id) refreshImportHistory().catch(() => {})
}
