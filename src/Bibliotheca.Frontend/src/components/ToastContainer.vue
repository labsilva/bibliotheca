<template>
  <div class="toast-container">
    <Toast
      v-for="t in toasts"
      :key="t.id"
      :header="t.header"
      :body="t.body"
      :links="t.links"
      :model-value="true"
      @update:modelValue="val => { if (!val) onClose(t.id) }"
    />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import Toast from './Toast.vue'
import { useToasts } from '@/composables/useToasts.js'

const { state, removeToast } = useToasts()

const toasts = computed(() => state.toasts)

function onClose(id) {
  removeToast(id)
}
</script>

<style scoped>
.toast-container {
  position: fixed;
  right: 16px;
  bottom: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  z-index: 9999;
}

/* mobile: topo centrado */
@media (max-width: 640px) {
  .toast-container {
    top: 16px;
    bottom: auto;
    left: 50%;
    right: auto;
    transform: translateX(-50%);
    width: calc(100% - 32px);
    max-width: 480px;
    align-items: center;
  }
}
</style>
