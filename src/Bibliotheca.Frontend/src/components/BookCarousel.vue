<template>
  <section class="section" :aria-label="title">
    <header class="section-hd">
      <h3>{{ title }}</h3>
      <div class="spacer" />
      <template v-if="!loading && !error && books.length">
        <button class="nav" type="button" @click="scrollBy(-1)" aria-label="scroll left">‹</button>
        <button class="nav" type="button" @click="scrollBy(1)" aria-label="scroll right">›</button>
      </template>
    </header>

    <p v-if="loading" class="section-state" role="status">Loading...</p>
    <div v-else-if="error" class="section-state">
      <p role="alert">{{ errorMessage }}</p>
      <button v-if="retry" class="retry" type="button" @click="retry()">Try again</button>
    </div>
    <p v-else-if="books.length === 0" class="section-state muted">{{ emptyMessage }}</p>
    <div v-else class="rail" ref="rail" @scroll.passive="onScroll" @focusin="onScroll">
      <router-link
        v-for="b in books"
        :key="b.id || b.isbn"
        :to="{ name: 'book', params: { id: b.id || b.isbn } }"
        :aria-label="b.title"
        class="link-wrapper"
      >
        <BookCard v-bind="b" @menu.stop.prevent="$emit('book-menu', b)" />
      </router-link>
    </div>
    <p v-if="loadingMore" role="status">Loading more books…</p>
    <p v-if="moreError" role="alert">Could not load more books. <button type="button" @click="loadMore?.()">Try again</button></p>
  </section>
</template>

<script setup>
import { ref } from 'vue'
import BookCard from './BookCard.vue'

const props = defineProps({
  title: { type: String, required: true },
  books: { type: Array, default: () => [] },
  emptyMessage: { type: String, default: 'No books available' },
  loading: { type: Boolean, required: false },
  error: { type: [String, Object], default: null },
  errorMessage: { type: String, default: 'Could not load books.' },
  retry: { type: Function, default: null },
  loadMore: { type: Function, default: null },
  hasMore: Boolean,
  loadingMore: Boolean,
  moreError: { type: [String, Object], default: null },
})

const rail = ref(null)
function onScroll() {
  const el = rail.value
  if (!el || !props.hasMore || props.loadingMore || props.moreError) return
  if (el.scrollLeft + el.clientWidth >= el.scrollWidth - el.clientWidth * .5) props.loadMore?.()
}
const scrollBy = (dir)=>{
  const el = rail.value
  if(!el) return
  const w = el.clientWidth
  el.scrollBy({ left: dir * (w*0.9), behavior: 'smooth' })
  if (dir > 0 && el.scrollLeft + 2 * w >= el.scrollWidth) onScroll()
}
</script>

<style scoped>
.section {
  background: var(--panel);
  border: 1px solid var(--line);
  border-radius: 14px;
  padding: 14px;
  margin-bottom: 16px;
}
.section-hd {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 2px 6px;
}
.section-hd h3 {
  font-size: 20px;
}
.section-state { margin: 10px; }
.section-state p { margin: 0 0 8px; }
.retry {
  min-height: 44px;
  padding: 6px 12px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--bg);
  color: var(--text);
  font: inherit;
  cursor: pointer;
}
.retry:focus-visible { outline: 2px solid var(--brand); outline-offset: 2px; }
.spacer {
  flex: 1;
}
.nav {
  border: 1px solid var(--line);
  background: var(--bg);
  color: var(--text);
  border-radius: 8px;
  padding: 6px 10px;
}
.rail {
  display: grid;
  grid-auto-flow: column;
  grid-auto-columns: max-content;
  gap: 12px;
  overflow: auto;
  padding: 8px;
}
.rail::-webkit-scrollbar {
  height: 10px;
}
.rail::-webkit-scrollbar-thumb {
  background: #1e2633;
  border-radius: 999px;
}

/* o wrapper transforma todo o card em link */
.link-wrapper {
  display: block;
  text-decoration: none;
  color: inherit;
  cursor: pointer;
}
</style>
