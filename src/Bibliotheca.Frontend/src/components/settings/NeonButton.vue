<template>
  <button
    class="neon-btn"
    :class="[`neon-btn--${variant}`, { 'neon-btn--full': fullWidth }]"
    :disabled="disabled"
    @click="$emit('click')"
  >
    <span v-if="$slots.icon" class="neon-btn__icon">
      <slot name="icon" />
    </span>

    <span class="neon-btn__label">
      <slot />
    </span>
  </button>
</template>

<script setup>
defineProps({
  variant: {
    type: String,
    default: 'primary', // primary | success | danger | soft
  },
  fullWidth: {
    type: Boolean,
    default: false
  },
  disabled: {
    type: Boolean,
    default: false
  }
})

defineEmits(['click'])
</script>

<style scoped>
.neon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;

  padding: 10px 18px;
  border-radius: 12px;
  border: 1px solid rgba(56, 189, 248, 0.35);
  background: radial-gradient(circle at 50% 0, #07111d 0%, #020617 80%);
  color: #e6faff;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
  transition:
    background 0.2s ease-out,
    box-shadow 0.2s ease-out,
    transform 0.15s ease-out;
  text-shadow: 0 0 6px rgba(0, 180, 255, 0.4);
  user-select: none;

  /* impede distorções em flex containers */
  max-width: 100%;
  white-space: normal;
  overflow-wrap: anywhere;
}

.neon-btn:hover:not(:disabled) {
  box-shadow:
    0 0 12px rgba(56, 189, 248, 0.45),
    0 0 20px rgba(56, 189, 248, 0.25);
  transform: translateY(-1px);
}

.neon-btn:active:not(:disabled) {
  transform: translateY(0);
  box-shadow: 0 0 6px rgba(56, 189, 248, 0.4);
}

.neon-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  box-shadow: none;
}

.neon-btn--full {
  width: 100%;
}

/* Variantes */
.neon-btn--primary {
  border-color: rgba(56, 189, 248, 0.45);
}

.neon-btn--success {
  border-color: rgba(16, 185, 129, 0.45);
  text-shadow: 0 0 6px rgba(16, 185, 129, 0.5);
}

.neon-btn--danger {
  border-color: rgba(239, 68, 68, 0.45);
  text-shadow: 0 0 6px rgba(239, 68, 68, 0.5);
}

.neon-btn--soft {
  border-color: rgba(148, 163, 184, 0.25);
  text-shadow: none;
}

/* Ícone */
.neon-btn__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 1.1rem;
}

/* MOBILE */
@media (max-width: 768px) {
  .neon-btn {
    padding: 8px 14px;
    font-size: 14px;
    border-radius: 10px;
  }
}
</style>
