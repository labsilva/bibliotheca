<template>
  <div class="dropdown">
    <button
      class="tab dropdown-toggle"
      :class="statusClasses"
      @click="toggle"
      :disabled="saving"
    >
      {{ current }}
      <span class="arrow">▼</span>
    </button>

    <ul v-if="open" class="dropdown-menu" role="listbox">
      <li
        v-for="status in options"
        :key="status"
        :class="{ active: status === current }"
        role="option"
        @mousedown.prevent="select(status)"
      >
        {{ status }}
      </li>
    </ul>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  modelValue: { type: String, default: 'Unshelved' },
  options: { type: Array, default: () => [] },
  saving: Boolean,
})
const emit = defineEmits(['select'])

const open = ref(false)
const current = computed(() => props.modelValue)

const statusClasses = computed(() => ({
  'status-empty': current.value === 'Unshelved',
  'status-set': current.value !== 'Unshelved'
}))

function toggle() {
  open.value = !open.value
}

function select(status) {
  open.value = false
  if (status !== current.value && !props.saving) emit('select', status)
}
</script>

<style scoped>
/* reuse your existing visual language */
.dropdown { position: relative; display: inline-block; }

.dropdown-toggle {
  display: flex; align-items: center; gap: 6px;
  cursor: pointer; user-select: none;
  padding: 6px 12px; border-radius: 8px;
  border: 1px solid rgba(255,255,255,0.1);
  background: transparent; color: rgba(255,255,255,0.8);
}
.dropdown-toggle:disabled { opacity: .6; cursor: not-allowed; }
.dropdown-toggle .arrow { font-size: .8em; opacity: .8; }

.dropdown-menu {
  position: absolute; top: 115%; left: 0;
  background: #11161d; border: 1px solid rgba(255,255,255,0.12);
  border-radius: 8px; padding: 4px 0; margin: 0;
  list-style: none; z-index: 20; box-shadow: 0 6px 16px rgba(0,0,0,0.45);
  min-width: 130px;
}

.dropdown-menu li {
  padding: 8px 14px; color: #e4e4e4; cursor: pointer; transition: background .15s;
}
.dropdown-menu li:hover { background: rgba(255,255,255,0.08); }
.dropdown-menu li.active { background: rgba(255,255,255,0.12); font-weight: 600; }

/* same neon classes you already use */
.status-empty { background: #bba40f; box-shadow: 0 0 6px #bba40f, 0 0 10px #bba40f; color: #fff; }
.status-set   { background: #0f9f4a; box-shadow: 0 0 6px #0f9f4a, 0 0 10px #0f9f4a; color: #fff; }
</style>
