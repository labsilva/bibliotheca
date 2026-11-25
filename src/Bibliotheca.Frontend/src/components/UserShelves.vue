<template>
    <ul v-if="!hasShelves" class="group">
      <li class="label">Shelves <button class="" title="Add shelve" @click="openAddShelve">+</button></li>
      <li class="item">
        <span class="icon">🗂️</span> No shelves available
      </li>
    </ul>
    <ul v-else class="group">
      <li class="label">Shelves <button class="" title="Add shelve" @click="openAddShelve">+</button></li>
      <router-link :to="{ name: 'user-library-shelve', params: { name: b.shelve } } "  v-for="(b,i) in shelves" :key="i">
        <li class="item" >
          <font-awesome-icon v-if="b.shelve === 'Favoritos' || b.shelve === 'Favourites' || b.shelve === 'Favorites'" 
            icon="fa-solid fa-heart" class="fa-shadow" style="color: red" />
          <font-awesome-icon v-else icon="fa-solid fa-swatchbook" /> 
            {{b.shelve}}
            <span class="pill">{{ b.bookCount }}</span>
        </li>
      </router-link>
    </ul>

  <div v-if="showAddShelve" class="overlay" @click.self="closeAddShelve">
    <div class="modal">
      <h3>Add shelve</h3>
      <input v-model="shelve" placeholder="Shelve name" @keydown.enter="submit" />
      <div class="modal-actions">
        <button class="ghost" @click="closeAddShelve">Cancel</button>
        <button class="btn" @click="submit">Add</button>
      </div>
    </div>
  </div>
    
</template>

<script setup>
import { ref, computed } from 'vue';
import { useShelves } from '@/composables/useLibraries.js';

const emit = defineEmits(['add-shelve'])

const { shelves, loading, error, addShelf } = useShelves()

const showAddShelve = ref(false);
const shelve = ref('')

const hasShelves = computed(() => Array.isArray(shelves.value) && shelves.value.length > 0)

function openAddShelve() {
  showAddShelve.value = true;
}

function closeAddShelve() {
  showAddShelve.value = false;
}

async function submit() {
  const value = (shelve.value || '').trim()
  if (!value) return

  emit('add-shelve', value)
  closeAddShelve()

  // chamada POST à API backend
  try {
    await addShelf({ name: value })  // cria e refresca
    // notificationText.value = 'O livro vai ser obtido e ficará pronto na área de staging.'
    
  } catch (err) {
    // notificationText.value = 'Erro ao iniciar obtenção do livro. Tente novamente.'
  }
}

</script>

<style scoped>
.group{list-style:none; margin:8px 0; padding:8px 0; border-top:1px solid var(--line)}
.label{font-size:12px; color:var(--sub); text-transform:uppercase; letter-spacing:.08em; margin:8px 0}

/* Entire row is clickable */
.item{
  display:flex; align-items:center; gap:10px;
  padding:8px 10px; border-radius:8px; cursor:pointer; 
  color:var(--brand);
  user-select:none;
}
.item:hover{
  color: var(--brand-glow);
  background: var(--brand-hover);
  box-shadow: 0 0 6px var(--brand-glow);
}
.item.active{background:#1b2330}

.icon{width:20px; text-align:center}
.pill{margin-left:auto; background:#1b2330; color:var(--text); font-size:12px; padding:2px 8px; border-radius:999px}

.overlay{
  position:fixed;
  inset:0;
  display:flex;
  align-items:center;
  justify-content:center;
  background: rgba(0,0,0,0.35);
  backdrop-filter: blur(6px);
  -webkit-backdrop-filter: blur(6px); /* Safari */
  z-index:1000;
}

.modal{
  background:var(--panel);
  color:var(--text);
  padding:18px;
  border-radius:12px;
  width:360px;
  max-width:92%;
  box-shadow:0 12px 40px rgba(0,0,0,0.45);
  border:1px solid var(--line);
}

.modal h3{margin:0 0 8px 0; font-size:16px}
.modal input{width:100%; padding:8px; border-radius:6px; border:1px solid var(--line); background:var(--bg); color:var(--text); margin-bottom:12px}
.modal-actions{display:flex; gap:8px; justify-content:flex-end}

.fa-shadow{filter:drop-shadow(0px 0px 6px #b0173a)}
</style>