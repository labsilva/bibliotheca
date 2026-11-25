<template>
  <CatalogueList search-field="title">
    <template #actions="slotProps"><slot name="actions" v-bind="slotProps" /></template>
    <template #prelist="slotProps"><slot name="prelist" v-bind="slotProps" /></template>
    <template #row="{ item: s }">
      <div class="row-main">
        <RouterLink :to="{ name: routeName, params: { id: s.id } }">
          <img :src="s.cover" width="45" :alt="getBookTitle(s)" />
        </RouterLink>
        <div>
          <div class="row-title">
            <RouterLink :to="{ name: routeName, params: { id: s.id } }">{{ getBookTitle(s) }}</RouterLink>
            <div class="pills">
              <span class="tab status" :class="statusClasses(s.status)">{{ toTitleCase(s.status ?? '') }}</span>
              <RouterLink v-if="s.series && s.seriesId" class="tab series" :to="{ name: 'series', params: { id: s.seriesId } }">
                {{ s.series }}
              </RouterLink>
            </div>
          </div>
          <div class="row-authors">
            <RouterLink v-if="s.authorId" :to="{ name: 'author', params: { id: s.authorId } }">{{ s.author }}</RouterLink>
            <span v-else>{{ s.author }}</span>
          </div>
        </div>
      </div>
    </template>
  </CatalogueList>
</template>

<script setup>
import CatalogueList from './CatalogueList.vue'

defineProps({ routeName: { type: String, required: true } })

const statusClasses = current => ({
  'status-unread': current === 'Unread',
  'status-reading': current === 'Reading',
  'status-read': current === 'Read'
})

function getBookTitle(book) {
  if(book.subtitle != null) {
    return book.title + ' - ' + book.subtitle
  }
  else {
    return book.title
  }
}

function toTitleCase(str) {
  return str.replace(
    /\w\S*/g,
    text => text.charAt(0).toUpperCase() + text.substring(1).toLowerCase()
  );
}

</script>

<style scoped>
.row-main {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.row-title { font-weight: 600; }
.row-authors { font-size: small }

.tab {
  font-size: small;
  border: 0;
  display: inline-flex;
  align-items: center;   /* centra verticalmente */
  justify-content: center; /* centra horizontalmente */
  padding: 0 10px;       /* opcional */
  height: 21px;          /* opcional se quiseres altura fixa */
  margin-left: 5px;
}

.status-unread  { background: #b0173a; box-shadow: 0 0 6px #b0173a, 0 0 10px #b0173a; color: #fff; }
.status-reading { background: #bba40f; box-shadow: 0 0 6px #bba40f, 0 0 10px #bba40f; color: #fff; }
.status-read    { background: #0f9f4a; box-shadow: 0 0 6px #0f9f4a, 0 0 10px #0f9f4a; color: #fff; }

.tab.series {
  background-color: transparent;
  box-shadow: 0 0 6px var(--brand-glow);
  color: #fff;
}

@media (max-width: 768px) {
  div.pills { display: block; margin-top: 10px; margin-bottom: 10px; }
  .row-authors { margin-top: 10px; }
}

</style>
