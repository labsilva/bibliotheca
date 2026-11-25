<script setup>
import { onBeforeUnmount, reactive, ref, watch } from 'vue'
import api from '@/lib/http.js'
import { recordImport } from '@/composables/useImportHistory'

const props = defineProps({ entry: { type: Object, required: true }, allowTitleSearch: Boolean })
defineEmits(['search-by-title'])

const size = 20
const open = ref(false)
const page = ref(0)
const items = ref([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const retryMessage = ref('')
const retrying = ref(false)
const corrections = reactive({})
const lifecycle = new AbortController()

function key(failure) { return failure.importId + ':' + failure.position }

watch([open, page, () => props.entry.isbnFailureCount], async ([expanded, index], _, onCleanup) => {
  if (!expanded) return
  const request = new AbortController()
  onCleanup(() => request.abort())
  loading.value = true
  items.value = []
  error.value = ''
  try {
    const { data } = await api.get('/api/imports/' + props.entry.id + '/failures', {
      params: { page: index, size }, signal: request.signal,
    })
    if (request.signal.aborted) return
    items.value = data.items
    total.value = data.total
    for (const failure of data.items) corrections[key(failure)] ??= failure.isbn
  } catch {
    if (!request.signal.aborted) error.value = 'Could not load failed ISBNs. Close and reopen to try again.'
  } finally {
    if (!request.signal.aborted) loading.value = false
  }
})

async function retry(failures) {
  retrying.value = true
  error.value = ''
  retryMessage.value = ''
  try {
    const items = failures?.map(failure => ({
      importId: failure.importId, position: failure.position, isbn: corrections[key(failure)].trim(),
    }))
    const { data } = await api.post('/api/imports/' + props.entry.id + '/retry', { items }, { signal: lifecycle.signal })
    if (lifecycle.signal.aborted) return
    recordImport(data)
    retryMessage.value = 'Failed ISBNs queued for retry.'
  } catch (exception) {
    if (!lifecycle.signal.aborted) error.value = exception.response?.data?.detail || 'Could not retry the failed ISBNs.'
  } finally {
    retrying.value = false
  }
}

onBeforeUnmount(() => lifecycle.abort())
</script>

<template>
  <details @toggle="open = $event.target.open">
    <summary>Failed ISBNs</summary>
    <p v-if="loading" role="status">Loading failed ISBNs…</p>
    <form v-for="failure in items" :key="key(failure)" @submit.prevent="retry([failure])">
      <label>ISBN {{ failure.isbn }}
        <input v-model="corrections[key(failure)]" autocomplete="off" required>
      </label>
      <p>{{ failure.reason }}</p>
      <button :disabled="retrying" type="submit">Retry ISBN</button>
    </form>
    <div v-if="total > size">
      <button type="button" :disabled="loading || page === 0" @click="page--">Previous failures</button>
      <span>Page {{ page + 1 }} of {{ Math.ceil(total / size) }}</span>
      <button type="button" :disabled="loading || (page + 1) * size >= total" @click="page++">Next failures</button>
    </div>
    <button v-if="items.length" :disabled="retrying" type="button" @click="retry(items)">
      {{ total > size ? 'Retry ISBNs on this page' : 'Retry failed ISBNs' }}
    </button>
    <button v-if="total > size" :disabled="retrying" type="button" @click="retry()">Retry all original failed ISBNs</button>
    <button v-if="allowTitleSearch" type="button" @click="$emit('search-by-title', entry.shelfId)">Search by title and author</button>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="retryMessage" role="status">{{ retryMessage }}</p>
  </details>
</template>

<style scoped>
form { margin-top: .75rem; }
form input { display: block; width: min(100%, 18rem); }
button { min-height: 44px; margin: 4px; }
</style>
