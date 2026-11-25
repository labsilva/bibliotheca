<template>
    <ul class="group">
      <li class="label">Libraries</li>
      <router-link :to="{ name: 'user-library' }" v-for="(b,i) in libraries" :key="i">
      <li class="item">
        <font-awesome-icon icon="fa-regular fa-user" /> {{b.library}} <span class="pill">{{ b.bookCount }}</span>
      </li>
      </router-link>
    </ul>
</template>

<script setup>
import { useLibraries } from '@/composables/useLibraries';
const { libraries, loading, error } = useLibraries()
</script>

<style scoped>
.group{list-style:none; margin:8px 0; padding:8px 0; border-top:1px solid var(--line)}
.label{font-size:12px; color:var(--sub); text-transform:uppercase; letter-spacing:.08em; margin:8px 0}

/* Entire row is clickable */
.item{
  display:flex; align-items:center; gap:10px;
  padding:8px 10px; border-radius:8px; cursor:pointer; color:var(--brand);
  user-select:none;
}
.item:hover{
  color:var(--brand-glow);
  background: var(--brand-hover);
  box-shadow: 0 0 6px var(--brand-glow);
}
.item.active{background:#1b2330}

.icon{width:20px; text-align:center}
.pill{margin-left:auto; background:#1b2330; color:var(--text); font-size:12px; padding:2px 8px; border-radius:999px}
</style>