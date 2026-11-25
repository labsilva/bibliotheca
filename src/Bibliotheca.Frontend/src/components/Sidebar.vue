<template>
  <nav class="nav" @click="onNavClick">
    <router-link to="/" class="brand">
    <div class="brand">
      <span class="logo"></span>
      <strong>Bibliotheca</strong>
    </div>
    </router-link>

    <ul class="group">
      <li class="label">Home</li>

      <!-- Dashboard -->
      <router-link :to="{ name: 'dashboard' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-house" /><span>Dashboard</span>
        </li>
      </router-link>

      <!-- Books -->
      <router-link :to="{ name: 'all-books' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-book" /><span>Books</span>
        </li>
      </router-link>

      <!-- Authors -->
      <router-link :to="{ name: 'all-authors' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-people-group" /><span>Authors</span>
        </li>
      </router-link>

      <!-- Collections -->
      <router-link :to="{ name: 'all-collections' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-list" /><span>Collections</span>
        </li>
      </router-link>

      <!-- Genres -->
      <router-link :to="{ name: 'all-genres' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-icons" /><span>Genres</span>
        </li>
      </router-link>

      <!-- Series -->
      <router-link :to="{ name: 'all-series' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-sitemap" /><span>Series</span>
        </li>
      </router-link>

      <!-- Tags -->
      <router-link :to="{ name: 'all-tags' }">
        <li class="item" :class="{ active: isExactActive }" role="link">
          <font-awesome-icon icon="fa-solid fa-tags" /><span>Tags</span>
        </li>
      </router-link>
    </ul>
    <UserLibraries />
    <UserShelves />

    <ul class="group">

    </ul>
    <footer class="foot">v0.0.1</footer>
  </nav>
</template>

<script setup>
import { ref } from 'vue'
import UserLibraries from './UserLibraries.vue'
import UserShelves from './UserShelves.vue'

const emit = defineEmits(['navigate'])

const isExactActive = ref(false)

function onNavClick(event) {
  const link = event.target.closest('a[href]')
  if (link && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && window.innerWidth <= 960) {
    emit('navigate')
  }
}

</script>

<style scoped>
.nav{padding:16px; display:flex; flex-direction:column; gap:16px}
.brand{display:flex; align-items:center; gap:10px; font-size:18px}
.logo{display:inline-grid; place-items:center; width:48px; height:48px; border-radius:6px; background:var(--brand); color:#0a0a0a; content:url("/src/assets/images/Bibliotheca-Small-Icon.png")}
.group{list-style:none; margin:8px 0; padding:8px 0; border-top:1px solid var(--line)}
.label{font-size:12px; color:var(--sub); text-transform:uppercase; letter-spacing:.08em; margin:8px 0}

/* Entire row is clickable */
.item{
  display:flex; 
  align-items:center; 
  gap:10px;
  padding:8px 10px; 
  border-radius:8px; 
  cursor:pointer; 
  color:var(--brand);
  user-select:none;
  text-decoration: none;
}
.item:hover{
  color:var(--brand-glow);
  background: #1b2330;
  box-shadow: 0 0 6px var(--brand-glow);
}
.item.active{background:#1b2330}

.icon{width:20px; text-align:center}
.pill{margin-left:auto; background:#1b2330; color:var(--text); font-size:12px; padding:2px 8px; border-radius:999px}
.foot{margin-top:auto; color:var(--sub); font-size:12px}
</style>
