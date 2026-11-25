<!-- src/components/ConfirmModal.vue -->
<template>
  <teleport to="body">
    <transition name="cm-fade">
      <div
        v-if="modelValue"
        class="cm-overlay"
        @click.self="onCancel"
      >
        <div
          class="cm-modal"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          ref="modalEl"
        >
          <h3 :id="titleId" class="cm-title">{{ title }}</h3>

          <div class="cm-body">
            <slot>
              <p class="cm-text">{{ message }}</p>
            </slot>
          </div>

          <div class="cm-actions">
            <button
              class="cm-btn cm-btn--danger"
              @click="onConfirm"
              :disabled="loading"
            >
              {{ loading ? loadingText : confirmText }}
            </button>
            <button
              class="cm-btn cm-btn--secondary"
              @click="onCancel"
              :disabled="loading"
              ref="cancelBtn"
            >
              {{ cancelText }}
            </button>
          </div>
        </div>
      </div>
    </transition>
  </teleport>
</template>

<script setup>
import { ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  title: { type: String, default: 'Are you sure?' },
  message: { type: String, default: '' },
  confirmText: { type: String, default: 'Confirm' },
  cancelText: { type: String, default: 'Cancel' },
  loading: { type: Boolean, default: false },
  loadingText: { type: String, default: 'Working…' },
  closeOnEsc: { type: Boolean, default: true }
})

const emit = defineEmits(['update:modelValue', 'confirm', 'cancel'])

const modalEl = ref(null)
const cancelBtn = ref(null)
const prevOverflow = ref('')
const titleId = `cm-title-${Math.random().toString(36).slice(2, 8)}`

function openSideEffects () {
  prevOverflow.value = document.documentElement.style.overflow
  document.documentElement.style.overflow = 'hidden'
  nextTick(() => cancelBtn.value?.focus())
}
function closeSideEffects () {
  document.documentElement.style.overflow = prevOverflow.value || ''
}

function onConfirm () { emit('confirm') }
function onCancel () {
  if (!props.loading) {
    emit('cancel')
    emit('update:modelValue', false)
  }
}

function onKeydown (e) {
  if (e.key === 'Escape' && props.modelValue && props.closeOnEsc && !props.loading) {
    onCancel()
  }
}

watch(() => props.modelValue, (open) => {
  if (open) openSideEffects()
  else closeSideEffects()
})

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
  closeSideEffects()
})
</script>

<style scoped>
:root { --cm-neon: #1ec8ff; }

/* Full-viewport overlay, perfectly centered with blur */
.cm-overlay {
  position: fixed;
  inset: 0;                 /* top:0 right:0 bottom:0 left:0 */
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0,0,0,.45);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  z-index: 9999;            /* above everything */
}

/* Centered modal card with neon glow */
.cm-modal {
  width: min(520px, 90vw);
  background: #0F141B;
  color: #fff;
  border: 1px solid rgba(255,255,255,0.12);
  border-radius: 16px;
  padding: 24px;
  box-shadow:
    0 0 50px rgba(0,0,0,0.6),
    0 0 32px rgba(30, 200, 255, 0.25),
    0 0 120px rgba(30, 200, 255, 0.4),
    0 0 180px rgba(30, 200, 255, 0.25);
  /* glow layers ↑ for softness + depth */
  transform: scale(.97);
  animation: cm-pop .18s ease-out forwards;
}

.cm-title {
  margin: 0 0 12px;
  font-size: 1.25rem;
  font-weight: 700;
  text-align: center;
}
.cm-body { text-align: center; }
.cm-text { margin: 0; color: rgba(255,255,255,.85); }

.cm-actions {
  display: flex;
  justify-content: center;
  gap: 12px;
  margin-top: 20px;
}

.cm-btn {
  border: 1px solid rgba(255,255,255,.12);
  border-radius: 10px;
  padding: 10px 16px;
  min-width: 100px;
  font-weight: 600;
  color: #fff;
  cursor: pointer;
  transition: all .15s ease;
}

.cm-btn--danger {
  background: #b0173a;
  box-shadow: 0 0 8px rgba(176,23,58,.7);
}
.cm-btn--danger:hover {
  background: #c41c44;
  box-shadow: 0 0 10px rgba(176,23,58,.9);
}

.cm-btn--secondary { background: rgba(255,255,255,.08); }
.cm-btn--secondary:hover { background: rgba(255,255,255,.12); }
.cm-btn:disabled { opacity: .6; cursor: not-allowed; }

/* Transitions */
.cm-fade-enter-active, .cm-fade-leave-active { transition: opacity .15s; }
.cm-fade-enter-from, .cm-fade-leave-to { opacity: 0; }
@keyframes cm-pop { to { transform: scale(1) } }
</style>
