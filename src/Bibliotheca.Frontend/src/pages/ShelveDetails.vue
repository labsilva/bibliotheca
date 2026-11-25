<template>
    <BookItemList
        :back-to="{ name: 'user-library' }"
        :header="route.params.name"
        route-name="book"
        :items="books"
        :loading="loading"
        :loading-error="error"
        loading-text="Loading books..."
        no-data-found-text="No books found."
        search-placeholder="Search shelve...">
    </BookItemList>
</template>

<script setup>
import { useRoute } from 'vue-router'
import BookItemList from '@/components/BookItemList.vue'
import { fetchShelveById } from '@/composables/useLibraries.js'

const { shelve: shelve, books: books, loading: loading, error: error } = fetchShelveById()
var route = useRoute()

</script>