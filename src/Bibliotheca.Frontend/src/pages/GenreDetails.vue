<script setup>
import BookCarousel from '@/components/BookCarousel.vue'
import { getGenreById } from '@/composables/useGenres.js'

const { genre, carousel } = getGenreById()
</script>

<template>
  <div>
    <h2 v-if="genre?.name">{{ genre.name }}</h2>
    <BookCarousel title="Genre books" v-bind="carousel" empty-message="No books on genre" />
  </div>
</template>
