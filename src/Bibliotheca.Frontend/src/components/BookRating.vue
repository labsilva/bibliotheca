<template>
  <div class="rating" role="radiogroup" aria-label="Classificação do livro"
    @pointerleave="preview = null" @focusout="resetFocus">
    <button
      v-for="star in 5"
      :key="star"
      class="star-btn"
      type="button"
      :class="{ active: star <= (preview ?? rating) }"
      :tabindex="star === (focused ?? (rating || 1)) ? 0 : -1"
      :aria-label="`${star} estrela${star > 1 ? 's' : ''}`"
      :disabled="disabled"
      :aria-checked="star === rating"
      role="radio"
      @click="onClick(star)"
      @pointerenter="!disabled && (preview = star)"
      @focus="focused = star"
      @keydown="navigate($event, star)"
    >
      ★
    </button>
  </div>
</template>

<script setup>
import { ref } from 'vue'
const props = defineProps({
  // Pontuação atual do livro (0–5)
  rating: {
    type: Number,
    default: 0,
  },
  // Caso queiras bloquear interação (ex: enquanto está a gravar)
  disabled: {
    type: Boolean,
    default: false,
  },
})

const emit = defineEmits(['rate'])
const preview = ref(null)
const focused = ref(null)

function resetFocus(event) {
  if (!event.currentTarget.contains(event.relatedTarget)) focused.value = null
}

function navigate(event, star) {
  const destinations = { ArrowLeft: (star + 3) % 5 + 1, ArrowUp: (star + 3) % 5 + 1,
    ArrowRight: star % 5 + 1, ArrowDown: star % 5 + 1, Home: 1, End: 5 }
  const target = destinations[event.key]
  if (!target || props.disabled) return
  event.preventDefault()
  event.currentTarget.parentElement.children[target - 1].focus()
}

function onClick(star) {
  if (props.disabled) return
  preview.value = null
  emit('rate', star)
}
</script>

<style scoped>
.rating {
  display: inline-flex;
  gap: 0.35rem;
  align-items: center;
}

/* Botão base */
.star-btn {
  background: transparent;
  border: none;
  padding: 0;
  margin: 0;
  cursor: pointer;
  font-size: 1.8rem;
  line-height: 1;
  color: rgba(255, 255, 255, 0.2); /* branco pálido */
  text-shadow: 0 0 4px rgba(255, 255, 255, 0.1);
  transition:
    color 0.15s ease-out,
    text-shadow 0.15s ease-out,
    transform 0.1s ease-out;
}

/* Estado ativo (estrela selecionada) – dourado neon */
.star-btn.active {
  color: #ffd700; /* dourado */
  text-shadow:
    0 0 6px rgba(255, 215, 0, 0.8),
    0 0 14px rgba(255, 215, 0, 0.6);
}

/* Hover para dar feedback */
.star-btn:focus-visible {
  outline: 2px solid var(--brand-glow);
  outline-offset: 3px;
}

.star-btn.active:hover {
  transform: scale(1.05);
  color: #ffe76b;
  text-shadow:
    0 0 8px rgba(255, 231, 107, 0.9),
    0 0 18px rgba(255, 231, 107, 0.7);
}

/* Se quiseres desativar interação visualmente */
.star-btn:disabled,
.rating[aria-disabled="true"] .star-btn {
  cursor: default;
  opacity: 0.4;
}

/* --- MOBILE --- */
@media (max-width: 768px) {
  .rating {
    gap: 3px;
  }

  .star-btn {
    font-size: 1.3rem;   /* estrelas um pouco mais pequenas */
    padding: 0 1px;      /* menos padding para caber melhor na row */
  }
}

</style>
