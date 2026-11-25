<template>
  <button
    type="button"
    class="settings-tab"
    :class="{ 'settings-tab--active': active }"
    role="tab"
    @click="$emit('click')"
  >
    <span v-if="icon" class="settings-tab__icon">
      <font-awesome-icon v-if="typeof icon === 'string'" :icon="icon" />
      <!-- fallback: se no futuro for um componente -->
      <component v-else :is="icon.component" v-bind="icon.props" />
    </span>
    <span class="settings-tab__label">{{ label }}</span>
  </button>
</template>

<script setup>
defineProps({
  label: { type: String, required: true },
  icon: { type: [String, Object, Function], default: null },
  active: { type: Boolean, default: false }
})

defineEmits(['click'])
</script>

<style scoped>
.settings-tab {
  position: relative;
  border: 0;
  background: transparent;
  padding: 10px 16px;
  border-radius: 999px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  color: #9ca3af;
  cursor: pointer;
  transition: background 0.15s, color 0.15s, box-shadow 0.15s;
}

.settings-tab__icon {
  display: inline-flex;
  font-size: 16px;
}

.settings-tab--active {
  color: #e5f3ff;
  background: radial-gradient(circle at 50% 0, rgba(56, 189, 248, 0.26), rgba(15, 23, 42, 0.6));
  box-shadow: 0 0 16px rgba(56, 189, 248, 0.45);
}

.settings-tab:not(.settings-tab--active):hover {
  background: rgba(15, 23, 42, 0.6);
  color: #d1d5db;
}
</style>
