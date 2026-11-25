<script setup>
import { onBeforeUnmount, ref, watch } from 'vue'
import api from '@/lib/http.js'
import { getApiBase } from '@/lib/getApiBase'

const query = ref('')
const page = ref(0)
const items = ref([])
const total = ref(0)
const loading = ref(false)
const error = ref(false)
let request
let timer

async function load(onCleanup) {
  request?.abort()
  const controller = new AbortController()
  request = controller
  onCleanup?.(() => controller.abort())
  loading.value = true
  error.value = false
  try {
    const { data } = await api.get('/api/books', { signal: controller.signal,
      params: { list: 'recent', page: page.value, size: 20, search: query.value.trim() } })
    if (controller.signal.aborted) return
    items.value = data.items || []
    total.value = data.total || 0
  } catch {
    if (!controller.signal.aborted) error.value = true
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}
onBeforeUnmount(() => { clearTimeout(timer); request?.abort() })
watch(page, (_, __, cleanup) => load(cleanup), { immediate: true })
watch(query, (_, __, cleanup) => {
  clearTimeout(timer)
  request?.abort()
  timer = setTimeout(() => { if (page.value === 0) load(); else page.value = 0 }, 300)
  cleanup(() => clearTimeout(timer))
})
</script>

<template>
  <section class="book-list" aria-label="Shared catalogue books">
    <label>Search titles or authors <input v-model="query" type="search" /></label>
    <p v-if="loading" role="status">Loading books…</p>
    <p v-else-if="error" role="alert">Could not load books. <button type="button" @click="load()">Retry</button></p>
    <p v-else-if="!items.length">No books found.</p>
    <ul v-else>
      <li v-for="book in items" :key="book.id">
        <RouterLink :to="{ name: 'book', params: { id: book.id } }" class="book-row">
          <img :src="`${getApiBase()}/api/books/${book.id}/cover`" alt="" loading="lazy" />
          <span><strong>{{ book.title }}</strong><span v-if="book.subtitle">{{ book.subtitle }}</span>
            <span>{{ book.author || 'Unknown author' }}</span></span>
        </RouterLink>
      </li>
    </ul>
    <nav v-if="total > 20" aria-label="Book pages">
      <button type="button" :disabled="page === 0 || loading" @click="page--">Previous</button>
      <span> Page {{ page + 1 }} </span>
      <button type="button" :disabled="(page + 1) * 20 >= total || loading" @click="page++">Next</button>
    </nav>
  </section>
</template>
<style scoped>
.book-list { padding: 16px; background: var(--panel); border: 1px solid var(--line); border-radius: 14px; }
label { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
input { min-width: 0; max-width: 100%; }
ul { list-style: none; padding: 0; }
li + li { border-top: 1px solid var(--line); }
.book-row { display: flex; align-items: center; gap: 16px; padding: 10px 0; color: inherit; text-decoration: none; }
.book-row img { width: 54px; height: 78px; object-fit: cover; }
.book-row span span, .book-row strong { display: block; overflow-wrap: anywhere; }
</style>
