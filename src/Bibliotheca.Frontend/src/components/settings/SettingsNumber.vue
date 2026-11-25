<template>
  <label class="settings-number">
    <div class="settings-number__text">
      <span class="settings-number__label">{{ label }}</span>
      <span v-if="description" class="settings-number__description">
        {{ description }}
      </span>
    </div>

    <div class="settings-number__control">
      <button
        type="button"
        class="settings-number__btn"
        @click="decrement"
        aria-label="Diminuir"
      >
        –
      </button>

      <input
        class="settings-number__input"
        type="number"
        inputmode="decimal"
        :min="min"
        :max="max"
        :step="step"
        v-model.number="localValue"
        @change="normalize"
      />

      <button
        type="button"
        class="settings-number__btn"
        @click="increment"
        aria-label="Aumentar"
      >
        +
      </button>

      <span v-if="suffix" class="settings-number__suffix">
        {{ suffix }}
      </span>
    </div>
  </label>
</template>

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  modelValue: { type: Number, required: true },
  label: { type: String, required: true },
  description: { type: String, default: '' },
  min: { type: Number, default: Number.NEGATIVE_INFINITY },
  max: { type: Number, default: Number.POSITIVE_INFINITY },
  step: { type: Number, default: 1 },
  suffix: { type: String, default: '' } // ex: "px", "s", "%"
})

const emit = defineEmits(['update:modelValue'])

const localValue = ref(props.modelValue)

watch(
  () => props.modelValue,
  (val) => {
    if (val !== localValue.value) localValue.value = val
  }
)

function clamp (v) {
  let value = isNaN(v) ? props.min || 0 : v
  if (value < props.min) value = props.min
  if (value > props.max) value = props.max
  return value
}

function normalize () {
  const value = clamp(localValue.value)
  localValue.value = value
  emit('update:modelValue', value)
}

function increment () {
  const value = clamp((localValue.value ?? 0) + props.step)
  localValue.value = value
  emit('update:modelValue', value)
}

function decrement () {
  const value = clamp((localValue.value ?? 0) - props.step)
  localValue.value = value
  emit('update:modelValue', value)
}
</script>

<style scoped>
.settings-number {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
}

@media (max-width: 640px) {
  .settings-number {
    flex-direction: column;
    align-items: stretch;
  }

  .settings-number__control {
    box-sizing: border-box;
    max-width: 100%;
  }
}

.settings-number__text {
  display: flex;
  flex-direction: column;
}

.settings-number__label {
  font-size: 15px;
  font-weight: 500;
  color: #e5f3ff;
}

.settings-number__description {
  margin-top: 2px;
  font-size: 13px;
  color: #8fa0bf;
}

.settings-number__control {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px;
  border-radius: 999px;
  background: radial-gradient(circle at 50% 0, #020617 0, #02010a 70%);
  border: 1px solid rgba(56, 189, 248, 0.4);
  box-shadow: 0 0 18px rgba(56, 189, 248, 0.55);
}

/* botões +/- */
.settings-number__btn {
  width: 32px;
  height: 32px;
  border-radius: 999px;
  border: 0;
  background: rgba(10, 20, 40, 0.9);
  color: #e5f3ff;
  font-size: 18px;
  line-height: 1;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.settings-number__btn:active {
  transform: scale(0.96);
}

/* INPUT – retirar spinners nativos e alinhar com o tema */
.settings-number__input {
  width: 70px;
  max-width: 90px;
  border: 0;
  outline: none;
  text-align: center;
  font-size: 15px;
  padding: 4px 6px;
  border-radius: 8px;
  background: #020617;
  color: #e5f3ff;

  /* esconde spinners no Chrome / Edge / Safari */
  -webkit-appearance: textfield;
}

/* esconde spinners no Firefox */
.settings-number__input[type='number'] {
  -moz-appearance: textfield;
}

/* redundante para alguns browsers WebKit */
.settings-number__input::-webkit-outer-spin-button,
.settings-number__input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

@media (max-width: 640px) {
  .settings-number__input {
    width: 0;
    flex: 1 1 auto;
    min-width: 0;
    max-width: 100%;
  }
}

.settings-number__suffix {
  font-size: 13px;
  color: #9ca3af;
  padding-right: 4px;
}
</style>
