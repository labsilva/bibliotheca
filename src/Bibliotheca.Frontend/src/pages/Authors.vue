<template>
  <ItemList
    :back-to="{ name: 'dashboard' }"
    header="Authors"
    route-name="author"
    :items="authors"
    :loading="loadingAuthors"
    :loading-error="errAuthors"
    loading-text="Loading authors..."
    no-data-found-text="No authors found."
    search-placeholder="Search authors...">
    <template #emoji="{ item }">
      <AuthorThumbnail :name="item.name" :url="item.photoUrl" />
    </template>
  </ItemList>
</template>
<script setup>
import ItemList from '@/components/ItemList.vue'
import AuthorThumbnail from '@/components/AuthorThumbnail.vue'
import { getAuthors } from '@/composables/useAuthors.js'
const { authors: authors, loading: loadingAuthors, error: errAuthors } =
  getAuthors({ list: '', page: 0, size: 20 })
</script>