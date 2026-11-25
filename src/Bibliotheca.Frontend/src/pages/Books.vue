<template>
  <header class="catalogue-heading">
    <h1>Books</h1>
    <div class="metrics" aria-label="Book counts">
      <span>Shared catalogue: {{ totalRecent ?? '—' }}</span>
      <span>My reading: {{ totalReading ?? '—' }}</span>
      <span>My read: {{ totalRead ?? '—' }}</span>
    </div>
    <div class="view-switch" role="group" aria-label="Books view">
      <button type="button" :aria-pressed="store.library.booksView !== 'list'" @click="store.library.booksView = 'carousel'">Carousels</button>
      <button type="button" :aria-pressed="store.library.booksView === 'list'" @click="store.library.booksView = 'list'">List</button>
    </div>
  </header>
  <BookListView v-if="store.loaded && store.library.booksView === 'list'" />
  <template v-else>
    <BookCarousel title="Recently Added" :books="recent" emptyMessage="No recent books, start by adding one" 
                  @book-menu="onMenu" :loading="loadingRecent" :error="errRecent" :retry="retryRecent" :load-more="moreRecent" :has-more="hasRecent" :loading-more="loadingMoreRecent" :more-error="moreErrorRecent" />
    <BookCarousel title="Reading" :books="reading" emptyMessage="No books currently being read" 
                  @book-menu="onMenu" :loading="loadingReading" :error="errReading" :retry="retryReading" :load-more="moreReading" :has-more="hasReading" :loading-more="loadingMoreReading" :more-error="moreErrorReading" />
    <BookCarousel title="Latest read" :books="read" emptyMessage="No books read recently" 
                  @book-menu="onMenu" :loading="loadingRead" :error="errRead" :retry="retryRead" :load-more="moreRead" :has-more="hasRead" :loading-more="loadingMoreRead" :more-error="moreErrorRead" />
    <BookCarousel id="unshelved-books" title="Unshelved books" :books="unshelved" emptyMessage="No books unshelved"
                  @book-menu="onMenu" :loading="loadingUnshelved" :error="errUnshelved" :retry="retryUnshelved" :load-more="moreUnshelved" :has-more="hasUnshelved" :loading-more="loadingMoreUnshelved" :more-error="moreErrorUnshelved" />
  </template>
</template>

<script setup>
import { onMounted } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore.js'
import BookListView from '@/components/BookListView.vue'
import BookCarousel from '@/components/BookCarousel.vue'
import { getBooks } from '@/composables/useBooks.js'

const store = useSettingsStore()
onMounted(() => store.initialize())

const { books: recent, total: totalRecent, loading: loadingRecent, error: errRecent, refetch: retryRecent, loadMore: moreRecent, hasMore: hasRecent, loadingMore: loadingMoreRecent, moreError: moreErrorRecent } =
  getBooks({ list: 'recent', page: 0, size: 20 })

const { books: reading, total: totalReading, loading: loadingReading, error: errReading, refetch: retryReading, loadMore: moreReading, hasMore: hasReading, loadingMore: loadingMoreReading, moreError: moreErrorReading } =
  getBooks({ list: 'reading', page: 0, size: 100 })

const { books: read, total: totalRead, loading: loadingRead, error: errRead, refetch: retryRead, loadMore: moreRead, hasMore: hasRead, loadingMore: loadingMoreRead, moreError: moreErrorRead } =
  getBooks({ list: 'read', page: 0, size: 10 })

const { books: unshelved, loading: loadingUnshelved, error: errUnshelved, refetch: retryUnshelved, loadMore: moreUnshelved, hasMore: hasUnshelved, loadingMore: loadingMoreUnshelved, moreError: moreErrorUnshelved } =
  getBooks({ list: 'unshelved', page: 0, size: 10 })

function onMenu(book){
  // Placeholder para menu de contexto
}
</script>

<style scoped>
.catalogue-heading { margin-bottom: 16px; }
.metrics, .view-switch { display: flex; flex-wrap: wrap; gap: 12px; margin: 12px 0; }
.view-switch button[aria-pressed="true"] { outline: 2px solid var(--brand); }
</style>
