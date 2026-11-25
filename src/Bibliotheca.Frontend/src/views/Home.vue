<template>
  <div class="page">
    <header class="top">
      <h1>Bibliotheca</h1>
      <div class="spacer"></div>
      <button @click="callApi">Chamar API</button>
      <button @click="doLogout">Terminar sessão</button>
    </header>

    <pre v-if="api">{{ api }}</pre>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { apiGet } from '../services/api'
import { logout } from '../auth/oidc'

const api = ref<string>('')

async function callApi() {
  const r = await apiGet('/api/hello')
  api.value = JSON.stringify(r, null, 2)
}

async function doLogout() {
  await logout()
}
</script>

<style scoped>
.page { padding: 16px; }
.top { display:flex; gap:12px; align-items:center; }
.spacer { flex:1; }
</style>
