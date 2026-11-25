<script setup>
import BookCarousel from '@/components/BookCarousel.vue'
import { getCollectionById } from '@/composables/useCollections.js'

const { collection, carousel } = getCollectionById()
</script>

<template>
  <div>
    <h2 v-if="collection?.name">{{ collection.name }}</h2>
    <BookCarousel title="Collection books" v-bind="carousel" empty-message="No books on collection" />
  </div>
</template>
