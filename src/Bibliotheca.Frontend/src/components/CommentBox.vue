<template>
  <div class="comment-box">
    <div class="comment-box__header" v-if="label || description">
      <span v-if="label" class="comment-box__label">{{ label }}</span>
      <span v-if="description" class="comment-box__description">
        {{ description }}
      </span>
    </div>

    <div
      class="comment-box__wrapper"
      :class="{ 'comment-box__wrapper--focused': isFocused }"
    >
        <textarea
        ref="textareaEl"
        class="comment-box__textarea"
        :placeholder="placeholder"
        :rows="rows"
        :disabled="disabled"
        :maxlength="maxLength || null"
        v-model="localValue"
        @focus="onFocus"
        @blur="onBlur"
        @input="onInput"
        ></textarea>
    </div>

    <div class="comment-box__footer">
      <span v-if="maxLength" class="comment-box__counter">
        {{ localValue.length }} / {{ maxLength }}
      </span>
      <slot name="footer" />
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onMounted } from 'vue'

const props = defineProps({
  modelValue: { type: String, default: '' },
  label: { type: String, default: '' },
  description: { type: String, default: '' },
  placeholder: { type: String, default: 'Add a comment…' },
  rows: { type: Number, default: 3 },
  maxLength: { type: Number, default: null },
  disabled: Boolean,
  autoResize: { type: Boolean, default: true }
})

const emit = defineEmits([
  'update:modelValue',
  'changed' // evento que queres apanhar no pai
])

const localValue = ref(props.modelValue ?? '')
const textareaEl = ref(null)
const isFocused = ref(false)

// valor quando o utilizador entrou no campo
const focusValue = ref(localValue.value ?? '')

watch(
  () => props.modelValue,
  (val) => {
    const safe = val ?? ''
    if (safe !== localValue.value) {
      localValue.value = safe
      if (props.autoResize) resize()
    }
  }
)

function resize() {
  if (!textareaEl.value || !props.autoResize) return
  const el = textareaEl.value
  el.style.height = 'auto'
  el.style.height = el.scrollHeight + 'px'
}

function onInput() {
  emit('update:modelValue', localValue.value)
  if (props.autoResize) resize()
}

function onFocus() {
  isFocused.value = true
  // snapshot do valor no momento em que o utilizador começou a editar
  focusValue.value = localValue.value ?? ''
}

function onBlur() {
  isFocused.value = false

  // só emite se o utilizador mudar algo entre focus e blur
  if ((localValue.value ?? '') !== (focusValue.value ?? '')) {
    emit('changed', localValue.value)
    // não atualizamos focusValue aqui — será atualizado no próximo focus
  }
}

onMounted(() => {
  if (props.autoResize) resize()
})
</script>

<style scoped>
.comment-box {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.comment-box__header {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.comment-box__label {
  font-size: 14px;
  font-weight: 500;
  color: #e5f3ff;
}

.comment-box__description {
  font-size: 12px;
  color: #8fa0bf;
}

/* wrapper com borda/glow */
.comment-box__wrapper {
  border-radius: 12px;
  border: 1px solid rgba(56, 189, 248, 0.35);
  background: radial-gradient(circle at 0 0, #07111d 0%, #020617 70%);
  box-shadow: 0 0 10px rgba(56, 189, 248, 0.15);
  padding: 8px 10px;
  transition:
    border-color 0.15s ease-out,
    box-shadow 0.15s ease-out,
    background 0.15s ease-out;
}

.comment-box__wrapper--focused {
  border-color: rgba(56, 189, 248, 0.9);
  box-shadow:
    0 0 14px rgba(56, 189, 248, 0.7),
    0 0 30px rgba(56, 189, 248, 0.35);
  background: radial-gradient(circle at 0 0, #0b2033 0%, #020617 75%);
}

.comment-box__textarea {
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  background: transparent;
  color: #e5f3ff;
  font-size: 14px;
  line-height: 1.4;
  font-family: inherit;
}

.comment-box__textarea::placeholder {
  color: rgba(148, 163, 184, 0.7);
}

.comment-box__footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  min-height: 18px;
}

.comment-box__counter {
  font-size: 11px;
  color: #9ca3af;
}

/* mobile */
@media (max-width: 768px) {
  .comment-box__wrapper {
    padding: 6px 8px;
  }

  .comment-box__textarea {
    font-size: 13px;
  }
}
</style>
