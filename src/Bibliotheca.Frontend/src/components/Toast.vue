<template>
  <transition name="toast-fade">
    <div v-if="show" class="toast">
      <div class="toast-header">
        {{ header }}
        <button class="close" @click="close">×</button>
      </div>

      <div class="toast-body">
        <p>{{ body }}</p>

        <ul v-if="links?.length" class="toast-links">
          <li v-for="(l, i) in links" :key="i">
            <a :href="l.href" target="_blank">{{ l.label }}</a>
          </li>
        </ul>
      </div>
    </div>
  </transition>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  header: { type: String, required: true },
  body: { type: String, required: true },
  links: { type: Array, default: () => [] },
  modelValue: { type: Boolean, default: false } // permite v-model
})

const emit = defineEmits(['update:modelValue'])

const show = computed({
  get: () => props.modelValue,
  set: v => emit('update:modelValue', v)
})

function close() {
  show.value = false
}
</script>

<style scoped>
.toast {
  position: fixed;          /* fixa ao viewport */
  z-index: 9999;            /* por cima do resto */
  right: 16px;              /* encostado à direita */
  top: 16px;             /* em baixo no desktop */

  max-width: 320px;
  background: #1f2937;
  color: white;
  padding: 12px 14px;
  border-radius: 8px;
  box-shadow: 0 4px 12px var(--brand-glow);
  animation: slideIn 0.25s ease;
}

/* Layout interno mantém-se igual */
.toast-header {
  font-weight: bold;
  margin-bottom: 6px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.toast-body p {
  margin: 6px 0 10px;
}

.toast-links {
  list-style: none;
  padding: 0;
  margin: 0;
}

.toast-links li a {
  color: #93c5fd;
  font-size: 0.9em;
  text-decoration: underline;
}

.close {
  background: none;
  border: none;
  color: white;
  font-size: 16px;
  cursor: pointer;
}

/* Animação / fade */
.toast-fade-enter-active,
.toast-fade-leave-active {
  transition: opacity .25s;
}

.toast-fade-enter-from,
.toast-fade-leave-to {
  opacity: 0;
}

@keyframes slideIn {
  from {
    transform: translateY(10px);
    opacity: 0;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

/* 📱 Em mobile: topo, centrado e “quase full width” */
@media (max-width: 640px) {
  .toast {
    top: 16px;
    bottom: auto;
    right: 50%;
    left: 50%;
    transform: translateX(-50%);
    width: calc(100% - 32px);  /* 16px de margem de cada lado */
    max-width: 480px;
  }
}
</style>