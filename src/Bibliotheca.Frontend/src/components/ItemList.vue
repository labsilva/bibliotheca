<template>
  <CatalogueList>
    <template #actions="slotProps"><slot name="actions" v-bind="slotProps" /></template>
    <template #prelist="slotProps"><slot name="prelist" v-bind="slotProps" /></template>
    <template #row="{ item: s }">
      <RouterLink :to="{ name: routeName, params: { id: s.id } }">
        <div class="row-main">
          <span class="emoji"><slot name="emoji" :item="s">🗂️</slot></span>
          <div>
            <div class="row-title">{{ s.name }}</div>
            <div v-if="s.bookCount != null" class="book-count">
              {{ s.bookCount }} {{ s.bookCount === 1 ? 'book' : 'books' }}
            </div>
          </div>
        </div>
      </RouterLink>
    </template>
  </CatalogueList>
</template>

<script setup>
import CatalogueList from './CatalogueList.vue'

defineProps({ routeName: { type: String, required: true } })

</script>

<style scoped>
.row-main {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.emoji { font-size: 18px; }
.row-title { font-weight: 600; }

.book-count { font-size: .875rem; opacity: .75; }
</style>
