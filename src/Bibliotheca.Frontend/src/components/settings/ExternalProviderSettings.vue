<template>
  <p>When you search by title, Bibliotheca sends the title and optional author to the active providers.</p>
  <p v-if="loading" role="status">Loading search providers…</p>
  <div v-else-if="error" role="alert">
    <p>Could not load search providers.</p>
    <NeonButton @click="load">Retry</NeonButton>
  </div>
  <p v-else-if="!providers.length">No external search providers are installed on this server.</p>
  <ul v-else class="providers">
    <li v-for="provider in providers" :key="provider.id">
      <strong>{{ provider.name }}</strong>
      <span>{{ label(provider) }}</span>
      <p v-if="provider.state === 'requiresConfiguration'">An administrator needs to configure this provider on the server.</p>
    </li>
  </ul>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue'
import api from '@/lib/http.js'
import NeonButton from './NeonButton.vue'

const providers = ref([])
const loading = ref(true)
const error = ref(false)
const lifecycle = new AbortController()

function label(provider) {
  if (provider.state === 'requiresConfiguration') return 'Needs configuration'
  if (provider.state === 'disabled') return 'Disabled'
  return provider.available ? 'Active · Available' : 'Unavailable'
}

async function load() {
  loading.value = true
  error.value = false
  try {
    const { data } = await api.get('/api/external-editions/providers', { signal: lifecycle.signal })
    providers.value = data
  } catch {
    if (!lifecycle.signal.aborted) error.value = true
  } finally {
    loading.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => lifecycle.abort())
</script>

<style scoped>
.providers {
  list-style: none;
  padding: 0;
}

.providers li {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 12px;
  padding-block: 9px;
  border-top: 1px solid var(--line);
}

.providers li p {
  flex-basis: 100%;
  margin: 0;
}
</style>
