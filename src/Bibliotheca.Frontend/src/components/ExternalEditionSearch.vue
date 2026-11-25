<template>
  <section class="edition-search">
    <form class="search-form" @submit.prevent="search">
      <label for="external-edition-title">Title</label>
      <input id="external-edition-title" v-model="title" maxlength="200" required :disabled="importing" />
      <label for="external-edition-author">Author (optional)</label>
      <input id="external-edition-author" v-model="author" maxlength="200" :disabled="importing" />
      <button type="submit" :disabled="!title.trim() || loading || importing">Search</button>
    </form>

    <p v-if="searchError" role="alert">{{ searchError }}</p>
    <div role="status">
      <p v-if="loading">Searching for editions…</p>
      <template v-else-if="response">
        <p v-if="response.failedProviders.length">
          {{ response.items.length ? 'Some providers did not respond. Showing available results.' : 'Some providers did not respond.' }}
        </p>
        <p v-if="!response.items.length">No editions found.</p>
      </template>
    </div>

    <ul v-if="response?.items.length" class="editions">
      <li v-for="edition in response.items" :key="editionKey(edition)" class="edition">
        <img v-if="webUrl(edition.coverUrl)" :src="webUrl(edition.coverUrl)" alt="" loading="lazy" referrerpolicy="no-referrer" />
        <div class="edition-details">
          <h3>{{ edition.title }}</h3>
          <p v-if="edition.author">{{ edition.author }}</p>
          <p v-if="details(edition)">{{ details(edition) }}</p>
          <ul class="sources" aria-label="Sources">
            <li v-for="source in edition.sources" :key="JSON.stringify([source.providerId, source.externalId])">
              <a v-if="webUrl(source.url)" :href="webUrl(source.url)" target="_blank" rel="noopener noreferrer">{{ source.name }}</a>
              <span v-else>{{ source.name }}</span>
            </li>
          </ul>
          <template v-if="edition.bookId && initialShelfId == null">
            <p>Already in catalogue</p>
            <RouterLink :to="bookRoute(edition.bookId)" @click="emit('open-book')">Open book</RouterLink>
          </template>
          <template v-else>
            <button type="button" :aria-expanded="selected === edition" :disabled="importing" @click="select(edition)">
              Choose edition
            </button>
            <form v-if="selected === edition" class="import-form" @submit.prevent="addEdition">
              <label for="external-edition-shelf">Destination shelf</label>
              <select id="external-edition-shelf" v-model="shelfId" :disabled="importing || loadingShelves">
                <option :value="null">Leave unshelved</option>
                <option v-for="shelf in shelves" :key="shelf.shelveId" :value="shelf.shelveId">{{ shelf.shelve }}</option>
              </select>
              <p v-if="loadingShelves" role="status">Loading shelves…</p>
              <p v-if="shelfError" role="alert">Could not load shelves. You can import this edition unshelved.</p>
              <button type="submit" :disabled="importing || loadingShelves">{{ importing ? 'Adding…' : 'Add edition' }}</button>
              <p v-if="importError" role="alert">{{ importError }}</p>
            </form>
          </template>
        </div>
      </li>
    </ul>
  </section>
</template>

<script setup>
import { onBeforeUnmount, ref, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import api from '@/lib/http.js'

const props = defineProps({ initialShelfId: Number })
const emit = defineEmits(['busy', 'queued', 'open-book'])
const router = useRouter()
const title = ref('')
const author = ref('')
const response = shallowRef(null)
const selected = shallowRef(null)
const shelves = ref([])
const shelfId = ref(null)
const loading = ref(false)
const loadingShelves = ref(false)
const importing = ref(false)
const searchError = ref('')
const shelfError = ref(false)
const importError = ref('')
const lifecycle = new AbortController()
let shelvesLoaded = false

function editionKey(edition) {
  return edition.isbn || JSON.stringify([edition.sources[0].providerId, edition.externalId])
}

function bookRoute(id) {
  return { name: 'book', params: { id } }
}

function details(edition) {
  return [edition.language, edition.publisher, edition.year, edition.format, edition.isbn].filter(Boolean).join(' · ')
}

function webUrl(value) {
  if (!value) return null
  try {
    const url = new URL(value)
    return ['https:', 'http:'].includes(url.protocol) ? url.href : null
  } catch {
    return null
  }
}

async function search() {
  if (!title.value.trim() || loading.value || importing.value) return
  loading.value = true
  response.value = null
  selected.value = null
  searchError.value = ''
  try {
    const { data } = await api.get('/api/external-editions/search', {
      params: { title: title.value.trim(), author: author.value.trim() || undefined },
      signal: lifecycle.signal,
    })
    response.value = data
  } catch {
    if (!lifecycle.signal.aborted) searchError.value = 'Could not search for editions. Try again.'
  } finally {
    loading.value = false
  }
}

async function select(edition) {
  const previous = selected.value
  selected.value = previous === edition ? null : edition
  shelfId.value = props.initialShelfId ?? null
  importError.value = ''
  if (!selected.value || shelvesLoaded || loadingShelves.value) return

  loadingShelves.value = true
  shelfError.value = false
  try {
    const { data } = await api.get('/api/library/shelves', { signal: lifecycle.signal })
    shelves.value = data.shelves || []
    shelvesLoaded = true
  } catch {
    if (!lifecycle.signal.aborted) shelfError.value = true
  } finally {
    loadingShelves.value = false
  }
}

async function addEdition() {
  if (!selected.value || importing.value) return

  const source = selected.value.sources[0]
  importing.value = true
  importError.value = ''
  emit('busy', true)
  try {
    const { data, status } = await api.post('/api/external-editions/import', {
      providerId: source.providerId, externalId: source.externalId, shelfId: shelfId.value,
    }, { signal: lifecycle.signal })
    if (lifecycle.signal.aborted) return
    emit('busy', false)
    if (status === 200 && data.bookId) {
      await router.push(bookRoute(data.bookId))
      emit('open-book')
    } else {
      emit('queued', data)
    }
  } catch (exception) {
    if (!lifecycle.signal.aborted) importError.value = exception.response?.status === 429
      ? 'The import queue is full. Try again later.'
      : 'Could not add this edition. Try again.'
  } finally {
    importing.value = false
    if (!lifecycle.signal.aborted) emit('busy', false)
  }
}

onBeforeUnmount(() => lifecycle.abort())
</script>

<style scoped>
.edition-search {
  padding: 16px 24px 24px;
}

.search-form, .import-form {
  display: grid;
  gap: 8px;
}

.edition-search :is(input, select, button) {
  box-sizing: border-box;
  min-height: 44px;
  border: 1px solid var(--line);
  border-radius: 9px;
  padding: 8px 12px;
  background: var(--bg);
  color: inherit;
  font: inherit;
  font-size: max(16px, 1rem);
}

.edition-search button {
  cursor: pointer;
  justify-self: start;
}

.edition-search button:disabled { opacity: .55; cursor: default; }
.edition-search :is(input, select, button, a):focus-visible {
  outline: 2px solid var(--brand);
  outline-offset: 2px;
}

.editions, .sources {
  list-style: none;
  padding: 0;
}

.edition {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding-block: 12px;
  border-top: 1px solid var(--line);
}

.edition img { width: 56px; max-height: 90px; object-fit: contain; }
.edition-details { min-width: 0; overflow-wrap: anywhere; }
.edition h3, .edition p { margin: 0 0 6px; }
.sources { display: flex; flex-wrap: wrap; gap: 0 12px; margin: 6px 0 10px; }
.import-form { margin-top: 8px; }
</style>
