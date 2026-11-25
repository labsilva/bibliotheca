<template>
  <main class="page">
    <header class="topbar">
      <h1>{{ header }}</h1>
      <div class="topbar-actions">
        <button class="ghost" @click="$router.push(backTo)">← Go back</button>
        <slot name="actions" :isLoading="loading" />
      </div>
    </header>

    <form class="toolbar" @submit.prevent>
      <input v-model="search" class="input" :placeholder="searchPlaceholder" />
      <button class="btn" type="submit">Search</button>
    </form>

    <div v-if="loading" class="muted">{{ loadingText }}</div>
    <div v-else-if="loadingError" class="error" role="alert">{{ loadingError.message || loadingError }}</div>
    <slot name="prelist" :isLoading="loading" />
    <div class="list">
      <article v-for="item in filtered" :key="item.id" class="row" :class="{ 'row--book': searchField === 'title' }">
        <slot name="row" :item="item" />
      </article>
      <div v-if="!loading && !loadingError && !filtered.length" class="empty">
        {{ noDataFoundText }}
      </div>
    </div>
    <div class="divider"></div>
  </main>
</template>

<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  header: { type: String, required: true },
  backTo: { type: [String, Object], required: true },
  searchPlaceholder: { type: String, default: 'Search...' },
  searchField: { type: String, default: 'name' },
  items: { type: Array, default: () => [] },
  loading: Boolean,
  loadingError: { type: [String, Object], default: null },
  loadingText: { type: String, default: 'Loading...' },
  noDataFoundText: { type: String, default: 'No items found.' },
})

const search = ref('')
const filtered = computed(() => {
  const query = search.value.trim().toLowerCase()
  return query
    ? props.items.filter(item => (item[props.searchField] ?? '').toLowerCase().includes(query))
    : props.items
})
</script>

<style scoped>
/* Layout base */
.page {
  max-width: 920px;
  margin: 0 auto;
  padding: 24px;
  color: var(--fg, #e8e8e8);
}

.topbar {
  display: flex;
  align-items: end;
  justify-content: space-between; /* volta a ativar esta linha */
  gap: 12px;
  margin-bottom: 18px;
}

.topbar-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.topbar h1 {
  font-size: 28px;
  font-weight: 700;
  margin: 0;
}

/* Controles */
.toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}

.input {
  flex: 1;
  padding: 10px 12px;
  border: 1px solid #2b2f36;
  background: #0f1217;
  color: inherit;
  border-radius: 10px;
  outline: none;
}

.input:focus {
  border-color: #3a3f48;
}

.btn {
  padding: 10px 14px;
  border-radius: 12px;
  border: 1px solid #2b2f36;
  background: #1a1f27;
  color: inherit;
  cursor: pointer;
}

.btn:hover { filter: brightness(1.05); }
.btn:disabled { opacity: .6; cursor: not-allowed; }

.ghost {
  padding: 8px 10px;
  border-radius: 10px;
  border: 1px solid #2b2f36;
  background: transparent;
  color: inherit;
  cursor: pointer;
}

/* Lista */
.list {
  display: grid;
  gap: 10px;
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid #2b2f36;
  background: #0f1217;
  border-radius: 14px;
}

.row:hover { background: #11151b; }
.row--book:hover {
  background: #0f1217;
  box-shadow: 0 0 6px var(--brand-glow);
}

/* Vazio e estados */
.muted { opacity: .75; }
.error { color: #ff7373; }
.empty { padding: 12px; opacity: .75; }

/* Divider */
.divider {
  border-top: 1px solid #2b2f36;
  margin: 22px 0;
}

</style>
