<template>
  <ItemList
    :back-to="{ name: 'dashboard' }"
    header="Tags"
    route-name="tag"
    :items="tags"
    :loading="loading"
    :loading-error="error"
    loading-text="Loading tags..."
    no-data-found-text="No tags found."
    search-placeholder="Search tags...">
    <template #emoji="{ isLoading }">
      <font-awesome-icon icon="fa-solid fa-tag" />
    </template>
  </ItemList>

    
</template>
<script setup>
import ItemList from '@/components/ItemList.vue'
import { getTags } from '@/composables/useTags.js'

const { tags: tags, loading: loading, error: error } = getTags()
</script>

<style scoped>
/* Divider */
.divider {
  border-top: 1px solid #2b2f36;
  margin: 22px 0;
}

/* Criar nova série */
.create {
  padding: 0;
  border-radius: 12px;
}

/* Remove o marcador/bolinha do summary (• / triângulo nativo) e alinha */
.create > summary {
  list-style: none;
  cursor: pointer;
  padding: 10px 0;
  font-weight: 600;
  outline: none;
}
.create > summary::-webkit-details-marker { display: none; }

.create-form {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

/* Reseta marcadores de qualquer UL/LI dentro desta página */
:where(ul) { list-style: none; padding: 0; margin: 0; }
:where(li)::marker { content: ""; }

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
</style>