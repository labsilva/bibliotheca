<script setup>
import { onMounted, onBeforeUnmount, shallowRef } from 'vue'
import NeonButton from '@/components/settings/NeonButton.vue'
import SettingsSection from '@/components/settings/SettingsSection.vue'
import api from '@/lib/http.js'

const search = shallowRef('')
const sort = shallowRef('name')
const page = shallowRef(1)
const result = shallowRef(null)
const loading = shallowRef(false)
const error = shallowRef('')
const allowed = shallowRef(true)
const pageSize = 20
let request
let appliedSearch = ''

async function load() {
  request?.abort()
  const current = new AbortController()
  request = current
  loading.value = true
  error.value = ''
  try {
    const { data } = await api.get('/api/users', {
      params: { search: appliedSearch, sort: sort.value, page: page.value, pageSize },
      signal: current.signal,
    })
    if (!current.signal.aborted) result.value = data
  } catch (cause) {
    if (current.signal.aborted) return
    if (cause.response?.status === 403) allowed.value = false
    else error.value = 'Could not load users. Please retry.'
  } finally {
    if (!current.signal.aborted) loading.value = false
  }
}

function applyFilters() {
  page.value = 1
  appliedSearch = search.value.trim()
  load()
}

function move(delta) {
  page.value += delta
  load()
}

onMounted(load)
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <SettingsSection v-if="allowed" title="Registered users" aria-label="Registered users">
    <form class="directory-filters" @submit.prevent="applyFilters">
      <label>Username or full name <input v-model="search" class="directory-control" type="search" /></label>
      <label>
        Sort by
        <select v-model="sort" class="directory-control" @change="applyFilters">
          <option value="name">Username</option>
          <option value="library">Library size</option>
        </select>
      </label>
      <NeonButton type="submit">Search</NeonButton>
    </form>
    <p v-if="loading" role="status">Loading users…</p>
    <p v-if="error" role="alert">{{ error }} <NeonButton type="button" @click="load">Retry</NeonButton></p>
    <template v-if="result && !loading && !error">
      <p v-if="!result.items.length">No users found.</p>
      <ul v-else class="directory-list">
        <li v-for="user in result.items" :key="user.id" class="directory-user">
          <strong>{{ user.fullName || user.username }}</strong>
          <span>{{ user.username }} · {{ user.email }}</span>
          <span>{{ user.bookCount }} books</span>
          <span>Read: {{ user.read }} · Reading: {{ user.reading }} · Unread: {{ user.unread }}</span>
        </li>
      </ul>
      <nav v-if="result.total > result.pageSize || page > 1" class="directory-pages" aria-label="User pages">
        <NeonButton type="button" :disabled="page === 1" @click="move(-1)">Previous</NeonButton>
        <span>Page {{ page }} · {{ result.total }} users</span>
        <NeonButton type="button" :disabled="page * result.pageSize >= result.total" @click="move(1)">Next</NeonButton>
      </nav>
    </template>
  </SettingsSection>
</template>

<style scoped>
.directory-filters, .directory-pages { display: flex; flex-wrap: wrap; gap: .75rem; align-items: end; }
.directory-filters label { display: grid; gap: .25rem; max-width: 100%; }
.directory-control {
  max-width: 100%;
  padding: .55rem .7rem;
  border: 1px solid rgba(147,164,196,.4);
  border-radius: 8px;
  background: #0b1422;
  color: #e5f3ff;
  color-scheme: dark;
}
.directory-list { list-style: none; padding: 0; }
.directory-user { display: grid; gap: .3rem; padding: .9rem 0; border-bottom: 1px solid var(--line); overflow-wrap: anywhere; }
</style>
