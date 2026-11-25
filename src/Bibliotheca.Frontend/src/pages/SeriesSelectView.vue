<template>
  <main class="page">
    <header class="topbar">
      <h1>Escolher série</h1>
      <button class="ghost" @click="associate('')">x Remover</button>
      <button class="ghost" @click="goBack">← Voltar</button>
    </header>

    <div class="toolbar">
      <input
        v-model="search"
        class="input"
        placeholder="Procurar série..."
        @input="onSearch"
      />
      <button class="btn" @click="onSearch">Procurar</button>
    </div>

    <div v-if="loadingSeries" class="muted">A carregar séries…</div>
    <div v-else-if="errSeries" class="error">Falha ao carregar séries.</div>

    <div class="list">
      <article
        v-for="s in filtered"
        :key="s.id"
        class="row"
      >
        <div class="row-main">
          <span class="emoji">
            <font-awesome-icon icon="fa-solid fa-list-ol" />
          </span>
          <div>
            <div class="row-title">{{ s.name }}</div>
            <div v-if="s.bookCount != null" class="muted text-sm">
              {{ s.bookCount }} livros
            </div>
          </div>
        </div>
        <button class="btn" :disabled="associating" @click="associate(s.name)">
          Associar
        </button>
      </article>

      <div v-if="!loadingSeries && filtered.length === 0" class="empty">
        Nenhuma série encontrada.
      </div>
    </div>

    <div class="divider"></div>

    <details class="create">
      <summary>Criar nova série</summary>
      <div class="create-form">
        <input v-model="newSeries" class="input" placeholder="Nome da série" />
        <button class="btn" :disabled="!newSeries || creating" @click="createAndAssociate">
          Criar e associar
        </button>
      </div>
    </details>
  </main>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { getSeries } from '@/composables/useSeries'
import api from '@/lib/http.js'

const router = useRouter()

const search = ref('')
const newSeries = ref('')
const associating = ref(false)
const creating = ref(false)

const { series, loading: loadingSeries, error: errSeries, addSeries } =
  getSeries({ list: '', page: 0, size: 200 })

// filtro no cliente (ajusta se o teu composable suportar filtro por nome)
const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return series.value || []
  return (series.value || []).filter(s => (s.name || '').toLowerCase().includes(q))
})

function onSearch() {
  // Mantido para futura integração de filtros no servidor.
}

function returnDestination(route) {
  return route.query.returnTo || `/books/${route.params.id}`
}

function captureBookRoute() {
  const route = router.currentRoute.value
  return { route, bookId: route.params.id, returnTo: returnDestination(route) }
}

function goBack() {
  router.push(returnDestination(router.currentRoute.value))
}

async function associate(seriesName, target = captureBookRoute()) {
  try {
    associating.value = true
    await api.put(`/api/books/${target.bookId}/series`, { series: seriesName }, {
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
      },
    })
    if (router.currentRoute.value === target.route) router.push(target.returnTo)
  } catch (e) {
    alert('Não foi possível associar a série ao livro.')
  } finally {
    associating.value = false
  }
}

async function createAndAssociate() {
  const seriesName = newSeries.value
  if (!seriesName) return
  const target = captureBookRoute()
  try {
    creating.value = true
    await addSeries({ name: seriesName })
    await associate(seriesName, target)
  } catch (e) {
    alert('Não foi possível criar a série.')
  } finally {
    creating.value = false
  }
}
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
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 18px;
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

.row-main {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.emoji { font-size: 18px; }
.row-title { font-weight: 600; }

/* Vazio e estados */
.muted { opacity: .75; }
.error { color: #ff7373; }
.empty { padding: 12px; opacity: .75; }

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
</style>
