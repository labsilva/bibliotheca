<script setup>
import { computed, onBeforeUnmount, onMounted, shallowRef } from 'vue'
import api from '@/lib/http.js'
import NeonButton from './NeonButton.vue'
import SettingsSection from './SettingsSection.vue'

const state = shallowRef(null)
const error = shallowRef('')
const denied = shallowRef(false)
const confirming = shallowRef(false)
const submitting = shallowRef(false)
const lifecycle = new AbortController()
let timer
let request

const latest = computed(() => state.value?.latest)
const active = computed(() => !!latest.value && !latest.value.completedAtUtc)
const catalogueActive = computed(() => !!state.value?.catalogueRefreshActive)
const busy = computed(() => active.value || catalogueActive.value)
const finished = computed(() => latest.value ? latest.value.updated + latest.value.unchanged + latest.value.failed : 0)

function denyAccess() {
  denied.value = true
  state.value = null
  confirming.value = false
  error.value = ''
  clearTimeout(timer)
}

async function load() {
  clearTimeout(timer)
  request?.abort()
  request = new AbortController()
  const signal = AbortSignal.any([lifecycle.signal, request.signal])
  try {
    const { data } = await api.get('/api/author-refresh', { signal })
    if (signal.aborted) return
    state.value = data
    error.value = ''
    if (busy.value) confirming.value = false
  } catch (cause) {
    if (signal.aborted) return
    if (cause.response?.status === 403) denyAccess()
    else error.value = 'Could not load author refresh.'
  } finally {
    if (!signal.aborted && !denied.value && busy.value) timer = setTimeout(load, 3000)
  }
}

async function start() {
  if (submitting.value || busy.value) return
  submitting.value = true
  error.value = ''
  try {
    const { data } = await api.post('/api/author-refresh', {}, { signal: lifecycle.signal })
    if (lifecycle.signal.aborted) return
    confirming.value = false
    state.value = { ...state.value, latest: { id: data.id, total: data.total, updated: 0, unchanged: 0, failed: 0 }, errors: [] }
    await load()
  } catch (cause) {
    if (lifecycle.signal.aborted) return
    if (cause.response?.status === 403) denyAccess()
    else if (cause.response?.status === 409) {
      confirming.value = false
      await load()
      if (!lifecycle.signal.aborted && !denied.value && !busy.value && !error.value) {
        error.value = 'Another refresh was already running. You can try again now.'
      }
    } else error.value = 'Could not start author refresh. Please try again.'
  } finally {
    submitting.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => { lifecycle.abort(); request?.abort(); clearTimeout(timer) })
</script>

<template>
  <SettingsSection title="Refresh author profiles" aria-label="Refresh author profiles" class="refresh">
    <p v-if="denied" role="alert">Only catalogue managers can refresh author profiles. Ask an administrator for access.</p>
    <template v-else>
      <p v-if="error" role="alert">{{ error }} <button type="button" @click="load">Reload status</button></p>
      <template v-if="state">
        <p>{{ state.authorCount }} {{ state.authorCount === 1 ? 'author' : 'authors' }} in the shared catalogue.</p>
        <p v-if="catalogueActive" role="status">A catalogue refresh is running. Author refresh will be available when it finishes.</p>
        <div v-if="confirming" class="refresh-confirm">
          <p>Refresh profiles for all {{ state.authorCount }} authors? This looks up photos and biographies for the shared catalogue.</p>
          <NeonButton :disabled="submitting || busy" @click="start">{{ submitting ? 'Starting…' : 'Confirm author refresh' }}</NeonButton>
          <NeonButton :disabled="submitting" @click="confirming = false">Cancel</NeonButton>
        </div>
        <NeonButton v-else :disabled="busy || submitting || !state.authorCount" @click="confirming = true">Refresh all authors</NeonButton>
        <div v-if="latest" role="status" aria-live="polite">
          <p>{{ active ? 'Processing' : 'Completed' }}: {{ finished }} / {{ latest.total }}</p>
          <p v-if="active && latest.current">Currently processing: <RouterLink :to="`/authors/${latest.current.authorId}`">{{ latest.current.authorName }}</RouterLink></p>
          <progress :value="finished" :max="latest.total || 1" aria-label="Author refresh progress" />
          <p>Updated: {{ latest.updated }} · No update available: {{ latest.unchanged }} · Failed: {{ latest.failed }}</p>
        </div>
        <ul v-if="state.errors?.length" class="refresh-errors">
          <li v-for="item in state.errors" :key="item.authorId">
            <RouterLink :to="`/authors/${item.authorId}`">{{ item.authorName }}</RouterLink> · {{ item.error }}
          </li>
        </ul>
      </template>
      <p v-else-if="!error" role="status">Loading author refresh…</p>
    </template>
  </SettingsSection>
</template>

<style scoped>
.refresh { margin-top: 1rem; }
.refresh-confirm { padding: 1rem; border: 1px solid var(--line); border-radius: 8px; }
.refresh-errors { overflow-wrap: anywhere; }
</style>
