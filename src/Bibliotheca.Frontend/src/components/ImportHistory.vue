<template>
  <div class="history">
    <NeonButton :disabled="loading" @click="refresh">Check import status</NeonButton>
    <p v-if="error" role="alert">{{ error }}</p>
    <div aria-live="polite">
      <p v-if="loading && !imports.length">Loading imports…</p>
      <p v-else-if="!imports.length && !error">No imports yet.</p>
      <ul v-else>
        <li v-for="entry in imports" :key="entry.id">
          <time :datetime="entry.createdAtUtc">{{ new Date(entry.createdAtUtc).toLocaleString() }}</time>
          <strong>{{ statusLabels[entry.status] }}</strong>
          <span>{{ entry.succeeded }} of {{ entry.total }} imported, {{ entry.failed }} failed, {{ entry.pending }} pending</span>
          <ImportFailures v-if="entry.isbnFailureCount" :entry="entry" :allow-title-search="allowTitleSearch"
            @search-by-title="$emit('search-by-title', $event)" />
        </li>
      </ul>
    </div>
  </div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue'
import ImportFailures from './ImportFailures.vue'
import NeonButton from '@/components/settings/NeonButton.vue'
import { importEntries as imports, refreshImportHistory } from '@/composables/useImportHistory'

defineProps({ allowTitleSearch: Boolean })
defineEmits(['search-by-title'])
const loading = ref(false)
const error = ref('')
const lifecycle = new AbortController()
const statusLabels = {
  queued: 'Queued',
  processing: 'Processing',
  completed: 'Completed',
  completedWithErrors: 'Completed with errors',
}

async function refresh() {
  if (loading.value) return
  loading.value = true
  error.value = ''
  try {
    await refreshImportHistory()
  } catch {
    if (!lifecycle.signal.aborted) error.value = 'Could not load import status. Try again.'
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
onBeforeUnmount(() => lifecycle.abort())
</script>

<style scoped>
.history { margin-top: 1rem; }
ul { padding-inline-start: 1.25rem; }
li > * { display: block; }
li + li { margin-top: .75rem; }
</style>
