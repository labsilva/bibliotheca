<script setup>
import { computed, shallowRef, watch, onBeforeUnmount } from 'vue'
import api from '@/lib/http.js'
import BookCarousel from '@/components/BookCarousel.vue'
import { getAuthorById } from '@/composables/useAuthors.js'

const { author, carousel, refetch } = getAuthorById()
const refreshing = shallowRef(false)
const refreshError = shallowRef('')
let refreshRequest
onBeforeUnmount(() => refreshRequest?.abort())
const photoFailed = shallowRef(false)
const source = computed(() => {
  const url = author.value?.biographySourceUrl
  if (url?.startsWith('https://en.wikipedia.org/?curid=')) return 'Wikipedia'
  if (url?.startsWith('https://openlibrary.org/authors/')) return 'Open Library'
  if (url?.startsWith('https://www.wook.pt/autor/')) return 'Wook'
  return null
})

watch(author, () => { photoFailed.value = false })

async function refreshAuthor() {
  if (!author.value || refreshing.value) return
  const id = author.value.id
  const request = new AbortController()
  refreshRequest = request
  refreshing.value = true
  refreshError.value = ''
  try {
    const { data } = await api.post(`/api/author-refresh/authors/${id}`, {}, { signal: request.signal })
    let batch
    do {
      batch = (await api.get(`/api/author-refresh/${data.id}`, { signal: request.signal })).data
      if (!batch.completedAtUtc) await new Promise((resolve, reject) => {
        const onAbort = () => { clearTimeout(timer); reject(new Error('Cancelled')) }
        const timer = setTimeout(() => { request.signal.removeEventListener('abort', onAbort); resolve() }, 2000)
        request.signal.addEventListener('abort', onAbort, { once: true })
      })
    } while (!batch.completedAtUtc && author.value?.id === id)
    if (request.signal.aborted || author.value?.id !== id) return
    if (batch.items[0]?.status === 'failed') throw new Error('Refresh failed')
    await refetch()
  } catch (cause) {
    if (!request.signal.aborted && author.value?.id === id) refreshError.value = cause.response?.status === 409
      ? 'A metadata refresh is already running. Please try again later.' : 'Could not refresh author metadata.'
  } finally {
    if (refreshRequest === request) { refreshing.value = false; refreshRequest = null }
  }
}

</script>

<template>
  <article class="author-detail">
    <header v-if="author" class="author-heading">
      <img
        v-if="author.photoUrl && !photoFailed"
        :key="author.id"
        :src="author.photoUrl"
        :alt="`Portrait of ${author.name}`"
        class="author-portrait"
        referrerpolicy="no-referrer"
        @error="photoFailed = true"
      />
      <span v-else class="author-portrait portrait-placeholder" role="img" aria-label="No author photo available">
        <font-awesome-icon icon="fa-solid fa-feather-pointed" aria-hidden="true" />
      </span>
      <div>
        <h1>{{ author.name }}</h1>
        <button type="button" class="refresh-author" :disabled="refreshing" @click="refreshAuthor">{{ refreshing ? 'Refreshing…' : 'Refresh metadata' }}</button>
        <p v-if="refreshing" role="status">Refreshing author metadata…</p>
        <p v-if="refreshError" role="alert">{{ refreshError }}</p>
      </div>
      <section class="biography" aria-labelledby="biography-title">
        <h2 id="biography-title">Biography</h2>
        <template v-if="author.biography">
          <p class="summary">{{ author.biography }}</p>
          <p v-if="source" class="attribution">
            <template v-if="source === 'Wikipedia'">
              Excerpt from <a :href="author.biographySourceUrl">Wikipedia contributors</a>
              · <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC BY-SA 4.0</a>
            </template>
            <template v-else>From <a :href="author.biographySourceUrl">{{ source }}</a></template>
          </p>
        </template>
        <p v-else class="empty-biography">No biography available.</p>
      </section>
    </header>
    <BookCarousel title="Author books" v-bind="carousel"
      error-message="Could not load the author and their books." empty-message="No books for author" />
  </article>
</template>

<style scoped>
.author-detail {
  width: 100%;
  max-width: 1200px;
  margin: 0 auto;
}
.author-heading {
  display: grid;
  grid-template-columns: clamp(160px, 20vw, 232px) minmax(0, 1fr);
  grid-template-rows: auto 1fr;
  column-gap: clamp(20px, 4vw, 44px);
  row-gap: 16px;
  padding: clamp(16px, 3vw, 36px);
  margin-bottom: 16px;
  border: 1px solid var(--line);
  border-radius: 16px;
  background: linear-gradient(120deg, var(--muted), var(--panel) 65%);
}

.author-heading h1 {
  margin: 0;
  font-size: clamp(1.7rem, 4vw, 2.8rem);
  line-height: 1.1;
  overflow-wrap: anywhere;
}

.refresh-author { margin-top: .75rem; padding: .5rem .75rem; border-radius: 8px; cursor: pointer; }
.refresh-author:disabled { cursor: wait; opacity: .6; }
.author-portrait {
  grid-row: 1 / 3;
  width: 100%;
  aspect-ratio: 4 / 5;
  border: 1px solid var(--line);
  border-radius: 12px;
  box-sizing: border-box;
  box-shadow: 0 8px 24px #0005;
  background: var(--panel);
  object-fit: cover;
  object-position: center 30%;
}

.portrait-placeholder {
  display: grid;
  place-items: center;
  color: var(--sub);
  font-size: clamp(2.25rem, 5vw, 4rem);
}
.biography h2 { margin: 0; font-size: 1.12rem; }
.biography p { margin: .5rem 0 0; }
.summary { max-width: 70ch; line-height: 1.65; white-space: pre-line; overflow-wrap: anywhere; }
.empty-biography, .attribution { color: var(--sub); line-height: 1.5; }
.biography .attribution { margin-top: 1rem; font-size: .875rem; }
.attribution a { color: #93beff; text-decoration: underline; text-underline-offset: .2em; }
.attribution a:focus-visible { outline: 2px solid var(--brand-glow); outline-offset: 3px; }
@media (max-width: 650px) {
  .author-heading { grid-template-columns: minmax(84px, 37%) minmax(0, 1fr); gap: 20px 16px; }
  .author-heading h1 { align-self: center; }
  .author-portrait { grid-row: 1; }
  .biography { grid-column: 1 / -1; }
}
</style>
