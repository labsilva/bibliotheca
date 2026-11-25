<script setup>
import BookCarousel from '@/components/BookCarousel.vue'
import { getTagById } from '@/composables/useTags.js'

const { tag, carousel } = getTagById()
</script>

<template>
  <div>
    <h2 v-if="tag?.name">{{ tag.name }}</h2>
    <BookCarousel title="Tag books" v-bind="carousel" empty-message="No books for tag" />
  </div>
</template>
