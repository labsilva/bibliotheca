// src/composables/useToasts.js
import { reactive } from 'vue'

const state = reactive({
  toasts: []
})

function genId() {
  if (crypto?.randomUUID) return crypto.randomUUID()
  return String(Date.now()) + Math.random().toString(16).slice(2)
}

export function useToasts() {
  function addToast({ header, body, links = [] }) {
    const id = genId()
    state.toasts.push({ id, header, body, links })
    // opcional: auto-remover ao fim de 5s
    setTimeout(() => removeToast(id), 5000)
    return id
  }

  function removeToast(id) {
    const idx = state.toasts.findIndex(t => t.id === id)
    if (idx !== -1) state.toasts.splice(idx, 1)
  }

  return { state, addToast, removeToast }
}
