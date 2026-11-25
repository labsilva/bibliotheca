<template>
  <ItemList
    :back-to="{ name: 'dashboard' }"
    header="Collections"
    route-name="collection"
    :items="collections"
    :loading="loadingCollections"
    :loading-error="errCollections"
    loading-text="Loading collections..."
    no-data-found-text="No collections found."
    search-placeholder="Search collections...">
    <template #emoji="{ isLoading }">
      <font-awesome-icon icon="fa-solid fa-table-list" />
    </template>
  </ItemList>
</template>
<script setup>
import ItemList from '@/components/ItemList.vue'
import { getCollections } from '@/composables/useCollections.js'
const { collections: collections, loading: loadingCollections, error: errCollections } =
  getCollections({ list: '', page: 0, size: 20 })
</script>