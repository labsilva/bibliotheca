<script setup>
import BookCarousel from '@/components/BookCarousel.vue'
import { getSeriesById } from '@/composables/useSeries.js'

const { series, carousel } = getSeriesById()
</script>

<template>
  <div>
    <h2 v-if="series?.name">{{ series.name }}</h2>
    <BookCarousel title="Series books" v-bind="carousel" empty-message="No books on series" />
  </div>
</template>
