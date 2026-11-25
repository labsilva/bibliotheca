<template>
  <div class="search-wrap" ref="wrapRef">
    <div class="search-input">
      <font-awesome-icon icon="fa-solid fa-search" class="icon" />
      <input
        v-model="q"
        type="text"
        role="combobox"
        aria-label="Search catalogue"
        placeholder="Title, ISBN, author, series, collection or shelf…"
        aria-autocomplete="list"
        :aria-expanded="open"
        :aria-controls="open ? listId : undefined"
        :aria-activedescendant="open && activeIndex >= 0 ? listId + '-' + activeIndex : undefined"
        @keydown="onKeydown"
        @focus="open = results.length > 0"
      />
      <span v-if="loading" class="spinner" aria-label="A procurar…"></span>
    </div>

    <div v-if="open" class="search-panel">
      <ul :id="listId" role="listbox" aria-label="Search results" :aria-busy="loading">
        <li
          v-for="(item, i) in results"
          :key="item.area + '-' + item.id"
          :id="listId + '-' + i"
          :class="['row', { active: i === activeIndex }]"
          role="option"
          :aria-selected="i === activeIndex"
          @mouseenter="activeIndex = i"
          @mousedown.prevent
          @click="goTo(item)"
        >
          <img v-if="item.imagePath" class="thumb" :src="item.imagePath" alt="" />
          <div class="meta">
            <div class="title">{{ item.title }}</div>
            <div class="line">
              <span class="pill" :class="toPillClass(item.area)">
                {{ item.area }}
              </span>
            </div>
          </div>
        </li>
      </ul>

      <div v-if="error" class="error" role="status">Erro a pesquisar</div>
      <div v-else-if="!results.length && !loading" class="empty" role="status">Sem resultados</div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, useId } from 'vue'
import { useRouter } from 'vue-router'
import { useSearch } from '@/composables/useSearch'

const router = useRouter()
const {
  q, results, loading, error, open, activeIndex, close, move, reset
} = useSearch()

const wrapRef = ref(null)
const listId = useId()

/* Mapear área → rota */
const areaRoute = {
  'Book':      (item) => ({ name: 'book',        params: { id: item.id } }),
  'Author':    (item) => ({ name: 'author',      params: { id: item.id } }),
  'Collection':(item) => ({ name: 'collection',  params: { id: item.id } }),
  'Series':(item) => ({ name: 'series',  params: { id: item.id } }),
  'Shelve':(item) => ({ name: 'user-library-shelve',  params: { name: item.id } }),
}

function goTo(item) {
  const fn = areaRoute[item.area]
  if (fn) {
    router.push(fn(item))
    reset()
  } 
  close()
}

function onKeydown(e) {
  if (!open.value && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
    if (results.value.length) open.value = true
  }
  switch (e.key) {
    case 'ArrowDown': e.preventDefault(); move(1); break
    case 'ArrowUp':   e.preventDefault(); move(-1); break
    case 'Enter':
      if (open.value && activeIndex.value >= 0) {
        e.preventDefault()
        goTo(results.value[activeIndex.value])
      }
      break
    case 'Escape': close(); break
  }
}

function onClickOutside(ev) {
  if (!wrapRef.value) return
  if (!wrapRef.value.contains(ev.target)) close()
}
onMounted(() => document.addEventListener('click', onClickOutside))
onBeforeUnmount(() => document.removeEventListener('click', onClickOutside))

function toPillClass(area) {
  switch (area) {
    case 'Book': return 'pill--book'
    case 'Author': return 'pill--author'
    case 'Collection': return 'pill--collection'
    case 'Series': return 'pill--series'
    case 'Shelve': return 'pill--shelve'
    default: return 'pill--other'
  }
}
</script>

<style scoped>
.search-wrap {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  max-width: 100%;
}

.search-input:hover { 
  box-shadow: 0 0 6px var(--brand-glow);
}

.search-input {
  position: relative;
  flex: 1;
  min-width: 0;
}

.search-input input {
  width: 100%;
  max-width: 100%;
  box-sizing: border-box;

  height: 38px;                 /* 👈 altura igual aos botões */
  line-height: 38px;            /* centra verticalmente o texto */
  padding-left: 40px;           /* espaço para o ícone */
  padding-right: 12px;          /* espaço no lado direito */
  border-radius: 12px;          /* igual aos botões */
}

.search-input .icon {
  position: absolute;
  left: 14px;
  top: 50%;
  transform: translateY(-50%);
  opacity: .6;
  width: 18px;           /* tamanho fixo */
  height: 18px;
  flex-shrink: 0;        /* não deixa o ícone encolher */
  pointer-events: none;  /* não intercepta cliques no input */
}

.search-input input:focus {
  border-color: var(--brand);
  box-shadow: 0 0 0 2px color-mix(in oklab, var(--brand) 40%, transparent);
}
.spinner {
  position: absolute; right: 10px; top: 50%; transform: translateY(-50%);
  width: 14px; height: 14px; border-radius: 50%;
  border: 2px solid color-mix(in oklab, var(--sub) 50%, transparent);
  border-top-color: var(--brand);
  animation: spin .8s linear infinite;
}
@keyframes spin { to { transform: translateY(-50%) rotate(360deg) } }

.search-panel {
  position: absolute;
  top: 46px; left: 0; right: 0;
  background: var(--panel);
  border: 1px solid var(--line);
  border-radius: 12px;
  padding: 6px;
  box-shadow: 0 12px 32px rgba(0,0,0,.35);
  z-index: 20;
}
.search-panel ul { list-style: none; margin: 0; padding: 0; max-height: 60vh; overflow: auto; }
.row {
  display: grid;
  grid-template-columns: 40px 1fr;
  gap: 10px;
  align-items: center;
  padding: 8px;
  border-radius: 10px;
  cursor: pointer;
}
.row:hover, .row.active {
  background: color-mix(in oklab, var(--brand) 18%, transparent);
}
.thumb {
  width: 40px; height: 56px; object-fit: cover; border-radius: 6px; background: #0b0e14;
  border: 1px solid var(--line);
}
.meta .title { font-weight: 600; color: var(--text); }
.meta .line { margin-top: 2px; }

.pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 12px;
  line-height: 1.5;
  border: 1px solid var(--line);
}

.pill--book {
  background: color-mix(in oklab, #3ddcff 22%, transparent);
  border-color: #2a9fd6;
  color: #9ad7ff;
}

.pill--series {
  background: color-mix(in oklab, #ca4224 22%, transparent);
  border-color: #ca4224;
  color: #ca4224;
}

.pill--author {
  background: color-mix(in oklab, var(--brand) 20%, transparent);
  border-color: #2aa86f;
  color: #9bf0c3;
}

.pill--shelve {
  background: color-mix(in oklab, #d4ff00 22%, transparent);
  border-color: #7a8200;
  color: #c3dd6b;
}

.pill--collection {
  background: color-mix(in oklab, #8b5cf6 22%, transparent);
  border-color: #6d28d9;
  color: #c4b5fd;
}
.pill--other {
  background: color-mix(in oklab, var(--sub) 20%, transparent);
  color: var(--sub);
}
.empty, .error {
  padding: 10px; text-align: center; color: var(--sub);
}

@media (max-width: 768px) {
  .search-wrap {
    position: relative;
    display: flex;
    align-items: center;
    gap: 12px;
    width: 100%;
    max-width: 100%;
  }
}

</style>
