<script setup>
import { computed, onMounted, onBeforeUnmount, shallowRef } from 'vue'
import api from '@/lib/http.js'
import NeonButton from './NeonButton.vue'
import SettingsSection from './SettingsSection.vue'

const state = shallowRef(null)
const error = shallowRef('')
const allowed = shallowRef(true)
const confirming = shallowRef(false)
const submitting = shallowRef(false)
const lifecycle = new AbortController()
let timer
let request
let sequence = 0
const active = computed(() => state.value?.latest && !state.value.latest.completedAtUtc)
const finished = computed(() => state.value ? (state.value.latest?.updated ?? 0) + (state.value.latest?.unchanged ?? 0) + (state.value.latest?.failed ?? 0) : 0)

function denyAccess() {
  allowed.value = false
  state.value = null
  confirming.value = false
  error.value = ''
  clearTimeout(timer)
}

async function load() {
  clearTimeout(timer)
  request?.abort()
  const current = ++sequence
  request = new AbortController()
  const signal = AbortSignal.any([lifecycle.signal, request.signal])
  try {
    const { data } = await api.get('/api/catalogue-refresh', { signal })
    if (current !== sequence || signal.aborted) return
    state.value = data
    error.value = ''
  } catch (cause) {
    if (current !== sequence || signal.aborted) return
    if (cause.response?.status === 403) denyAccess()
    else error.value = 'Could not load catalogue refresh. Please retry.'
  } finally {
    if (current === sequence && !signal.aborted && allowed.value && active.value) timer = setTimeout(load, 3000)
  }
}
async function start() {
  submitting.value = true
  error.value = ''
  try {
    await api.post('/api/catalogue-refresh', {}, { signal: lifecycle.signal })
    confirming.value = false
    await load()
  } catch (cause) {
    if (!lifecycle.signal.aborted) {
      if (cause.response?.status === 403) denyAccess()
      else error.value = cause.response?.status === 409
        ? (cause.response.data?.detail || 'A catalogue refresh is already running. Refresh the status.') : 'Could not start catalogue refresh.'
    }
  } finally { submitting.value = false }
}
onMounted(load)
onBeforeUnmount(() => { lifecycle.abort(); request?.abort(); clearTimeout(timer) })
</script>

<template>
  <SettingsSection title="Refresh catalogue metadata" aria-label="Refresh catalogue metadata" class="refresh">
    <p v-if="!allowed" role="alert">Only catalogue managers can refresh the shared catalogue. Ask an administrator for access.</p>
    <template v-else>
    <p>This updates the shared catalogue for everyone. Shelves, reading dates, ratings and other personal data are preserved.</p>
    <p v-if="error" role="alert">{{ error }} <button type="button" @click="load">Retry</button></p>
    <template v-if="state">
      <p>{{ state.bookCount }} books in the shared catalogue.</p>
      <div v-if="confirming && !active" class="refresh-confirm">
        <p>Refresh all {{ state.bookCount }} books using the configured providers? Valid existing metadata is kept when a provider returns incomplete data.</p>
        <NeonButton :disabled="submitting" @click="start">{{ submitting ? 'Starting…' : 'Confirm refresh' }}</NeonButton>
        <NeonButton :disabled="submitting" @click="confirming = false">Cancel</NeonButton>
      </div>
      <NeonButton v-else :disabled="!!active || !state.bookCount" @click="confirming = true">Refresh all books</NeonButton>
      <div v-if="state.latest" role="status" aria-live="polite">
        <p>{{ active ? 'Processing' : 'Completed' }}: {{ finished }} / {{ state.latest.total }}</p>
        <p v-if="active && state.latest.current">Currently processing: <RouterLink :to="`/books/${state.latest.current.bookId}`">{{ state.latest.current.title }}</RouterLink></p>
        <progress :value="finished" :max="state.latest.total || 1" aria-label="Catalogue refresh progress" />
        <p>Updated: {{ state.latest.updated }} · No update available: {{ state.latest.unchanged }} · Failed: {{ state.latest.failed }}</p>
      </div>
      <ul v-if="state.errors.length" class="refresh-errors">
        <li v-for="item in state.errors" :key="item.bookId"><RouterLink :to="`/books/${item.bookId}`">{{ item.title }}</RouterLink>: {{ item.error }}</li>
      </ul>
    </template>
    <p v-else-if="!error" role="status">Loading catalogue refresh…</p>
    </template>
  </SettingsSection>
</template>

<style scoped>
.refresh { margin-top: 1rem; }
.refresh-confirm { padding: 1rem; border: 1px solid var(--line); border-radius: 8px; }
.refresh-errors { overflow-wrap: anywhere; }
</style>
