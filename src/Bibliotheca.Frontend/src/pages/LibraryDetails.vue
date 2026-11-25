<template>
    <BookCarousel v-if="store.loaded && store.library.showReadingBooks" title="Reading books" :books="reading" empty-message="No books are being read" :loading="loadingReading" :error="errReading" :retry="retryReading" :load-more="moreReading" :has-more="hasReading" :loading-more="loadingMoreReading" :more-error="moreErrorReading" />
    <div id="unread-books">
      <BookCarousel v-if="showUnread" title="Unread books" :books="unread" empty-message="No unread books" :loading="loadingUnread" :error="errUnread" :retry="retryUnread" :load-more="moreUnread" :has-more="!showAllUnread && hasUnread" :loading-more="loadingMoreUnread" :more-error="moreErrorUnread" />
      <nav v-if="showAllUnread && unreadTotal > 20" aria-label="Unread book pages">
        <button type="button" :disabled="unreadPage === 0 || loadingUnread" @click="unreadPage--">Previous page</button>
        <span> Page {{ unreadPage + 1 }} </span>
        <button type="button" :disabled="(unreadPage + 1) * 20 >= unreadTotal || loadingUnread" @click="unreadPage++">Next page</button>
      </nav>
    </div>
    <BookCarousel v-if="store.loaded && store.library.showReadBooks" title="Read books" :books="read" empty-message="No read books" :loading="loadingRead" :error="errRead" :retry="retryRead" :load-more="moreRead" :has-more="hasRead" :loading-more="loadingMoreRead" :more-error="moreErrorRead" />
    <hr />
    <BookCarousel
      v-if="store.loaded && store.library.showShelves && (shelvesError || (loadingShelves && !shelves.length))"
      title="Shelves"
      error-message="Could not load shelves."
      :error="shelvesError"
      :loading="loadingShelves"
      :retry="retryShelves"
    />
    <ShelfBookCarousel v-for="shelf in visibleShelves" :key="shelf.shelve" :name="shelf.shelve" />
</template>

<script setup>
import { computed, ref, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useSettingsStore } from '@/stores/settingsStore.js'
import BookCarousel from '@/components/BookCarousel.vue'
import ShelfBookCarousel from '@/components/ShelfBookCarousel.vue'
import { useShelves, fetchBooks } from '@/composables/useLibraries.js'

const store = useSettingsStore()
const route = useRoute()
const showAllUnread = computed(() => route?.hash === '#unread-books')
const showUnread = computed(() => showAllUnread.value || (store.loaded && store.library.showUnreadBooks))
const unreadPage = ref(0)
watch(showAllUnread, () => { unreadPage.value = 0 })

onMounted(() => {
  store.initialize()
})

const { books: unread, total: unreadTotal, loading: loadingUnread, error: errUnread, refetch: retryUnread, loadMore: moreUnread, hasMore: hasUnread, loadingMore: loadingMoreUnread, moreError: moreErrorUnread } = fetchBooks({
  list: 'unread',
  page: () => showAllUnread.value ? unreadPage.value : 0,
  size: () => showAllUnread.value ? 20 : store.library.unreadBooksCount,
  enabled: () => showUnread.value,
})
const { books: read, loading: loadingRead, error: errRead, refetch: retryRead, loadMore: moreRead, hasMore: hasRead, loadingMore: loadingMoreRead, moreError: moreErrorRead } = fetchBooks({
  list: 'read',
  page: 0,
  size: () => store.library.readBooksCount,
  enabled: () => store.loaded && store.library.showReadBooks,
})
const { books: reading, loading: loadingReading, error: errReading, refetch: retryReading, loadMore: moreReading, hasMore: hasReading, loadingMore: loadingMoreReading, moreError: moreErrorReading } = fetchBooks({
  list: 'reading',
  page: 0,
  size: () => store.library.readingBooksCount,
  enabled: () => store.loaded && store.library.showReadingBooks,
})
const { shelves, loading: loadingShelves, error: shelvesError, refetch: retryShelves } = useShelves()

const visibleShelves = computed(() => store.loaded && store.library.showShelves ? shelves.value : [])
</script>

<style scoped>
hr {
  color: var(--brand-glow);
  box-shadow: 0 0 6px var(--brand-glow);
  margin-bottom: 16px;
}
</style>
