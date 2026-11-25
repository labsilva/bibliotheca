<script setup>
import AuthorThumbnail from '@/components/AuthorThumbnail.vue'

defineProps({ profile: { type: Object, required: true } })
</script>

<template>
  <article class="profile">
    <header class="profile-heading">
      <AuthorThumbnail :url="profile.photoUrl" :name="profile.name" />
      <h3><RouterLink :to="`/authors/${profile.id}`">{{ profile.name }}</RouterLink></h3>
    </header>
    <p>Catalogue record #{{ profile.id }}</p>
    <p v-if="profile.sourceAuthorKey">External author ID {{ profile.sourceAuthorKey }}</p>
    <details v-if="profile.biography">
      <summary>Biography</summary>
      <p>{{ profile.biography }}</p>
      <p v-if="profile.biographySourceUrl">Source {{ profile.biographySourceUrl }}</p>
    </details>
    <p v-else>No biography</p>
    <details v-if="profile.books.length" open>
      <summary>{{ profile.books.length }} {{ profile.books.length === 1 ? 'linked book' : 'linked books' }}</summary>
      <ul class="profile-books">
        <li v-for="book in profile.books" :key="book.id">
          <RouterLink :to="`/books/${book.id}`">{{ book.title }}</RouterLink>
          <small v-if="book.isbn"> ({{ book.isbn }})</small>
        </li>
      </ul>
    </details>
    <p v-else>No linked books</p>
  </article>
</template>

<style scoped>
.profile {
  min-width: 0;
  overflow-wrap: anywhere;
  padding: 0.8rem;
  border: 1px solid var(--line);
  border-radius: 8px;
}
.profile-heading { display: flex; align-items: center; gap: 0.6rem; }
.profile-books { padding-inline-start: 1.25rem; }
</style>
