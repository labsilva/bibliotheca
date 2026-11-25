<script setup>
import { ref, watch } from 'vue'
const props = defineProps({ name: { type: String, required: true }, url: { type: String, default: null } })
const failed = ref(false)
watch(() => props.url, () => { failed.value = false })
</script>
<template>
  <img v-if="url && !failed" class="thumbnail" :src="url" :alt="`Portrait of ${name}`"
    loading="lazy" referrerpolicy="no-referrer" @error="failed = true" />
  <span v-else class="thumbnail placeholder" role="img" aria-label="No author photo available">
    <font-awesome-icon icon="fa-solid fa-feather-pointed" aria-hidden="true" />
  </span>
</template>
<style scoped>
.thumbnail { width: 48px; height: 48px; border-radius: 50%; object-fit: cover; display: block; }
.placeholder { display: grid; place-items: center; background: var(--panel); color: var(--sub); }
</style>
