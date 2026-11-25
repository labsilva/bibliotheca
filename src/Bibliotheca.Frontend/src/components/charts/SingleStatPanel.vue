<template>
  <div v-if="loading">
    Loading...
  </div>
  <div v-else class="single-stat-panel" :class="{ featured }">
    <div class="panel-header">
      <span class="panel-title">{{ title }}</span>
    </div>

    <div class="panel-body">
      <span class="panel-value">{{ displayValue }}</span>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  title: {
    type: String,
    required: true
  },
  value: {
    type: [Number, String],
    required: true
  },
  // opcional: permite formatar o valor (ex: 1_234 -> "1 234")
  formatter: {
    type: Function,
    default: null
  },
  featured: Boolean,
  loading: { type: Boolean, required: false },
  error: { type: String, required: false },
})

const displayValue = computed(() =>
  props.formatter ? props.formatter(props.value) : props.value
)
</script>

<style scoped>
.single-stat-panel {
  display: grid;
  align-content: start;
  gap: .5rem;
  padding: .9rem 1rem;
  min-width: 0;
  min-height: 90px;
  color: var(--brand);
  background: transparent;
  border: 1px solid var(--brand);
  box-shadow: 0 0 12px rgb(0 200 255 / 35%);
  border-radius: 12px;
}

.panel-title {
  font-size: .9rem;
  font-weight: 600;
}

.panel-value {
  font-size: clamp(1.55rem, 5vw, 2rem);
  font-variant-numeric: tabular-nums;
  color: var(--brand-glow);
  text-shadow: 0 0 8px rgb(0 200 255 / 80%);
  font-weight: 700;
  line-height: 1.1;
}

.featured {
  padding: 1.125rem 1.25rem;
}

.featured .panel-value {
  font-size: clamp(2.4rem, 6vw, 3.4rem);
}
</style>
