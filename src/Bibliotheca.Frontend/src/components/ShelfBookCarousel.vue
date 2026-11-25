<script setup>
import BookCarousel from './BookCarousel.vue'
import { useShelfBooks } from '@/composables/useLibraries.js'

const props = defineProps({ name: { type: String, required: true } })
const { books, loading, error, refetch, loadMore, hasMore, loadingMore, moreError } =
  useShelfBooks(() => props.name)
</script>

<template>
  <BookCarousel :title="name" :books="books" :loading="loading" :error="error" :retry="refetch"
    :load-more="loadMore" :has-more="hasMore" :loading-more="loadingMore" :more-error="moreError" />
</template>
