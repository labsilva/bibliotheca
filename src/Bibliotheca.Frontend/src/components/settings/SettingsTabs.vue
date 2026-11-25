<template>
  <nav class="settings-tabs" role="tablist">
    <SettingsTab
      v-for="tab in tabs"
      :key="tab.id"
      :label="tab.label"
      :icon="tab.icon"
      :active="tab.id === modelValue"
      @click="selectTab(tab)"
    />
  </nav>
</template>

<script setup>
import SettingsTab from './SettingsTab.vue'

const props = defineProps({
  tabs: {
    type: Array,
    required: true // [{ id: 'reader', label: 'Reader' }, ...]
  },
  modelValue: {
    type: [String, Number],
    required: true
  }
})

const emit = defineEmits(['update:modelValue', 'change'])

function selectTab(tab) {
  if (tab.id === props.modelValue) return
  emit('update:modelValue', tab.id)
  emit('change', tab)
}
</script>

<style scoped>
.settings-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px 12px 4px;
  border-bottom: 1px solid rgba(15, 23, 42, 0.9);
  margin: -16px -16px 16px; /* “cola” às bordas da caixa principal */
}
</style>
