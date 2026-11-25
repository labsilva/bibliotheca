<template>
  <ItemList
    :back-to="{ name: 'dashboard' }"
    header="Series"
    route-name="series"
    :items="series"
    :loading="loadingSeries"
    :loading-error="errSeries"
    loading-text="Loading series..."
    no-data-found-text="No series found."
    search-placeholder="Search series...">
    <template #actions="{ isLoading }">
      <button class="btn" title="Add series" @click="toggleCreate()">+ Add series</button>
    </template>
    <template #emoji="{ isLoading }">
      <font-awesome-icon icon="fa-solid fa-list-ol" />
    </template>
    <template #prelist="{ isLoading }">
      <div v-show="showCreate" ref="createDetails">
        <div class="divider"></div>

        <div class="create-form">
          <input v-model="newSeries" class="input" placeholder="Nome da série" />
          <button class="btn" :disabled="!newSeries || creating" @click="create">
            Create
          </button>
        </div>

        <div class="divider"></div>
      </div>
    </template>
  </ItemList>

    
</template>
<script setup>
import { ref } from 'vue'
import ItemList from '@/components/ItemList.vue'
import { getSeries } from '@/composables/useSeries.js'

const { series: series, loading: loadingSeries, error: errSeries, addSeries } =
  getSeries({ list: '', page: 0, size: 20 })

const showCreate = ref(false)
const createDetails = ref(null)

function toggleCreate(){
  showCreate.value = !showCreate.value
}

const newSeries = ref('')

async function create() {
  if (!newSeries.value) return
  try {
    const created = await addSeries({ name: newSeries.value })
    showCreate.value = false
    newSeries.value = ''
  } catch (e) {
    alert('Não foi possível criar a série.')
  } finally {
    
  }
}
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