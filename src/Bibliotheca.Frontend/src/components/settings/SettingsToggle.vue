<template>
  <label class="settings-toggle">
    <div class="settings-toggle__text">
      <span class="settings-toggle__label">{{ label }}</span>
      <span v-if="description" class="settings-toggle__description">
        {{ description }}
      </span>
    </div>

    <div class="settings-toggle__control">
      <span class="settings-toggle__state">
        {{ modelValue ? yesLabel : noLabel }}
      </span>
      <button
        type="button"
        class="settings-toggle__switch"
        :class="{ 'settings-toggle__switch--on': modelValue }"
        role="switch"
        :aria-checked="modelValue ? 'true' : 'false'"
        @click="toggle"
      >
        <span class="settings-toggle__thumb"></span>
      </button>
    </div>
  </label>
</template>

<script setup>
const props = defineProps({
  modelValue: { type: Boolean, required: true },
  label: { type: String, required: true },
  description: { type: String, default: '' },
  yesLabel: { type: String, default: 'On' },
  noLabel: { type: String, default: 'Off' }
})

const emit = defineEmits(['update:modelValue'])

function toggle() {
  emit('update:modelValue', !props.modelValue)
}
</script>

<style scoped>
.settings-toggle {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
}

.settings-toggle__text {
  display: flex;
  flex-direction: column;
}

.settings-toggle__label {
  font-size: 15px;
  font-weight: 500;
  color: #e5f3ff;
}

.settings-toggle__description {
  margin-top: 2px;
  font-size: 13px;
  color: #8fa0bf;
}

.settings-toggle__control {
  display: flex;
  align-items: center;
  gap: 10px;
}

.settings-toggle__state {
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: #7dd3fc;
}

.settings-toggle__switch {
  width: 44px;
  height: 24px;
  border-radius: 999px;
  border: 1px solid rgba(148, 163, 184, 0.5);
  background: radial-gradient(circle at 0 50%, #1f2933 0, #020617 80%);
  display: inline-flex;
  align-items: center;
  padding: 2px;
  cursor: pointer;
  position: relative;
  transition: all 0.15s ease-out;
}

.settings-toggle__switch--on {
  border-color: rgba(56, 189, 248, 1);
  box-shadow: 0 0 12px rgba(56, 189, 248, 0.8);
  background: radial-gradient(circle at 100% 50%, #38bdf8 0, #020617 70%);
}

.settings-toggle__thumb {
  width: 18px;
  height: 18px;
  border-radius: 999px;
  background: #e5f3ff;
  box-shadow: 0 0 6px rgba(15, 23, 42, 0.9);
  transform: translateX(0);
  transition: transform 0.15s ease-out;
}

.settings-toggle__switch--on .settings-toggle__thumb {
  transform: translateX(18px);
}
</style>
