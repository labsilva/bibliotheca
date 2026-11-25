<template>
  <div class="topbar-wrap">
    <div class="topbar-left">
      <button class="ghost" @click="$emit('toggle-sidebar')" aria-label="Abrir menu">
        <font-awesome-icon icon="fa-solid fa-bars" />
      </button>
    </div>

    <div class="topbar-center">
      <SearchBox />
    </div>

    <div class="topbar-right">
      <div class="actions">
        <AddBookButton @click="showAdd = true" />
        <router-link :to="{ name: 'settings' }">
          <button class="icon" title="Settings"><font-awesome-icon icon="fa-solid fa-gear" /></button>
        </router-link>
        <ProfileMenu />
      </div>

      <div class="actions-mobile">
        <AddBookButton @click="showAdd = true" />
        <TopBarMobileMenu>
          <template #buttons="{ isLoading }">
            <button class="topbarmenu-item" role="menuitem" title="Settings">
              <router-link :to="{ name: 'settings' }">
              <font-awesome-icon icon="fa-solid fa-gear" /> Settings
              </router-link>
            </button>
            <a class="topbarmenu-item" role="menuitem" :href="scalarUrl" target="_blank">Scalar</a>
          </template>
        </TopBarMobileMenu>
      </div>  
    </div>
  </div>
  

  <AddBookDialog v-model:open="showAdd" />
</template>

<script setup>
import { ref } from 'vue'
import { getApiBase } from '@/lib/getApiBase'
import SearchBox from './SearchBox.vue'
import AddBookButton from './AddBookButton.vue'
import AddBookDialog from './AddBookDialog.vue'
import ProfileMenu from './ProfileMenu.vue'
import TopBarMobileMenu from './TopBarMobileMenu.vue'

defineEmits(['toggle-sidebar'])

const showAdd = ref(false)
const scalarUrl = getApiBase() + '/scalar'
</script>

<style scoped>

.ghost{border:1px solid var(--line); background:var(--panel); color:var(--text); border-radius:10px; padding:8px 10px}
.loupe{opacity:.7}

.icon{border:1px solid var(--line); background:var(--panel); color:var(--text); border-radius:10px; padding:8px; width:38px; height:38px; display:inline-flex; align-items:center; justify-content:center}
.icon.on{outline:2px solid #374151}
.icon.brand{outline-color: var(--brand)}

.profile-btn:hover, .ghost:hover, .icon:hover {
  color: var(--accent-color, #007bff);
}

a.topbarmenu-item {
  border:1px solid var(--line);
  color:var(--text);
  align-items:center;
  cursor:pointer;
}


.topbarmenu-item {
  /* width: 100%; */
  text-align: left;
  padding: .6rem .8rem;
  border-radius: .4rem;
  font-size: .95rem;
}

/* === LAYOUT DA TOPBAR === */

.topbar-wrap {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
}

/* lados ocupam só o que precisam */
.topbar-left,
.topbar-right {
  flex: 0 0 auto;
}

/* direita é um container flex para os botões */
.topbar-right {
  display: flex;
  align-items: center;
  justify-content: flex-end;
}

/* centro ocupa o resto e pode encolher */
.topbar-center {
  flex: 1 1 auto;
  min-width: 0; /* isto é crítico para não “empurrar” a direita */
}

/* o SearchBox preenche o espaço que o centro tiver */
.topbar-center > * {
  display: block;
  width: 100%;
  max-width: 100%;
}

/* actions desktop */
.actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* por omissão, versão mobile escondida */
.actions-mobile {
  display: none;
}

/* em mobile escondes os botões “grandes” e mostras o menu 3 pontinhos */
@media (max-width: 768px) {
  .actions {
    display: none;
  }

  .actions-mobile {
    display: flex;
    align-items: center;
    gap: 8px;
  }

}
</style>
