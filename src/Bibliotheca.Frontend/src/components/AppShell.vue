<template>
  <div class="app">
    <!-- Wrapper do sidebar para gerir o drawer + backdrop -->
    <aside class="sidebar-wrap" :class="{ open: isSidebarOpen }"  >
      <div class="sidebar">
        <slot name="sidebar" :onCloseSidebar="closeSidebar" />
      </div>

      <!-- Backdrop (fecha ao clicar fora) -->
      <button
        v-if="isSidebarOpen"
        class="backdrop"
        aria-label="Fechar menu lateral"
        @click="closeSidebar"
      />
    </aside>

    <main class="main">
      <header class="topbar">
        <!-- Slot prop para ligares o botão ☰ do Topbar -->
        <slot name="topbar" :onToggleSidebar="toggleSidebar" />
      </header>

      <section class="content">
        <slot />
      </section>
    </main>
  </div>
</template>

<script setup>
import { ref, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
watch(() => route.fullPath, () => { if (window.innerWidth <= 960) closeSidebar() })

const isSidebarOpen = ref(true) // começa aberto em desktop

function toggleSidebar() {
  isSidebarOpen.value = !isSidebarOpen.value
}
function closeSidebar() {
  isSidebarOpen.value = false
}

onMounted(() => {
  if (window.innerWidth <= 960) {
    isSidebarOpen.value = false   // fechado por defeito em mobile
  }
})
</script>

<style scoped>
:root { --bg:#0f1115; --panel:#12151c; --muted:#1c2029; --text:#e7e7ea; --sub:#aeb2c1; --brand:#3ddc84; --accent:#23c55e; --line:#262b36; }

.app{
  display:grid;
  /* era: grid-template-columns: 280px 1fr;  */
  grid-template-columns: auto 1fr; /* a largura do aside passa a ser controlada pelo width do wrapper */
  height:100dvh;
  background:var(--bg);
  color:var(--text);
}

/* Sidebar vive dentro de .sidebar-wrap */
.sidebar-wrap{
  position:relative;
  background:var(--panel);
  border-right:1px solid var(--line);
  width:280px;                 /* <= define a coluna */
  overflow:hidden;             /* evita scrolls laterais quando colapsa */
  transition: width .24s ease; /* anima a largura ao abrir/fechar */
}

/* Quando FECHADO em ecrãs largos, a coluna passa a 0 */
.sidebar-wrap:not(.open){
  width:0;
  border-right:none;
}

/* Já tinhas isto; mantém para esconder o painel em desktop quando fechado */
.sidebar-wrap:not(.open) .sidebar{
  transform: translateX(-100%);
  transition: transform .24s ease;
}


.main{
  display:grid;
  grid-template-rows: 64px 1fr;
  min-width:0;
}
.topbar{
  display:flex;
  align-items:center;
  gap:12px;
  padding:12px 16px;
  border-bottom:1px solid var(--line);
  background:var(--panel);
}
.content{
  padding:16px;
  overflow:auto;
}

@media (min-width: 960px){
    .sidebar {
    width: 280px;
    }
}

@media (max-width: 960px){
  .app{
    grid-template-columns: 1fr;
  }

  .sidebar-wrap{
    position: fixed;
    inset: 0;
    pointer-events: none;     /* só recebe cliques quando .open */
    z-index: 1000;
    border-right: none;
    background: transparent;
    width: auto;
  }

  .sidebar-wrap.open{
    pointer-events: auto;
  }

  .sidebar-wrap .sidebar{
    position: absolute;
    top: 0; bottom: 0; left: 0;
    width: 84vw;
    max-width: 320px;
    background: var(--panel);
    border-right: 1px solid var(--line);
    transform: translateX(-100%);
    transition: transform .24s ease;
    will-change: transform;
    z-index: 2;               /* sidebar acima do backdrop */
  }

  .sidebar-wrap.open .sidebar{
    transform: translateX(0);
  }

  /* 👇 ALTERAÇÃO IMPORTANTE AQUI */
  .sidebar-wrap .backdrop{
    position: fixed;          /* em vez de absolute */
    inset: 0;                 /* cobre o ecrã TODO */
    background: rgba(0,0,0,.35);
    border: 0;
    margin: 0;
    padding: 0;
    pointer-events: auto;
    z-index: 1;               /* por baixo da sidebar */
  }
}
</style>
