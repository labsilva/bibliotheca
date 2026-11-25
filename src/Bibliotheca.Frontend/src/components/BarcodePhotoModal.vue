<template>
  <div v-if="open" class="backdrop" @click.self="close">
    <div class="dialog" role="dialog" aria-labelledby="barcode-title">
      <header class="header">
        <h3 id="barcode-title">Scan barcode</h3>
        <button ref="closeButton" type="button" class="icon-btn" @click="close" aria-label="Close scanner">✕</button>
      </header>

      <div class="body">
        <p class="hint">Use a clear photo of the book’s barcode.</p>

        <div class="actions">
          <button type="button" class="btn" @click="triggerPick" :disabled="loading">
            <font-awesome-icon icon="fa-solid fa-camera" aria-hidden="true" />
            {{ loading ? 'Reading…' : 'Take or choose photo' }}
          </button>
          <input
            ref="fileInput"
            type="file"
            accept="image/*"
            capture="environment"
            class="hidden"
            @change="onPick"
            @cancel="onPickerCancel"
          />
        </div>

        <p v-if="error" class="error" role="status">{{ error }}</p>

        <figure v-if="previewUrl" class="preview">
          <img :src="previewUrl" alt="Preview" />
          <figcaption>Preview</figcaption>
        </figure>
      </div>

      <footer class="footer">
        <button type="button" class="btn" @click="close">
          <font-awesome-icon icon="fa-solid fa-arrow-left" aria-hidden="true" />
          Enter ISBN manually
        </button>
      </footer>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onBeforeUnmount } from 'vue'
import { BrowserMultiFormatReader } from '@zxing/browser'

const props = defineProps({
  open: { type: Boolean, default: false }
})
const emit = defineEmits(['update:open', 'decoded'])

const fileInput = ref(null)
const closeButton = ref(null)
const error = ref('')
const loading = ref(false)
const previewUrl = ref('')
let scanRequest = 0

function triggerPick () {
  error.value = ''
  if (!fileInput.value) return
  fileInput.value.value = ''
  try {
    fileInput.value.click()
  } catch {
    error.value = 'Could not open the camera or photo picker. You can enter the ISBN manually.'
  }
}

function onPickerCancel() {
  error.value = 'No photo selected. You can enter the ISBN manually.'
}

async function onPick (e) {
  error.value = ''
  const file = e.target.files?.[0]
  if (!file) return
  const request = ++scanRequest
  loading.value = true

  // cria URL para pré-visualização
  cleanupPreview()
  previewUrl.value = URL.createObjectURL(file)

  try {
    // Decodificar a partir do URL da imagem
    const reader = new BrowserMultiFormatReader()
    const result = await reader.decodeFromImageUrl(previewUrl.value)
    if (request !== scanRequest) return
    const code = String(result.getText() || '').trim()

    if (!code) throw new Error('Sem resultado')
    // Emitir e fechar
    emit('decoded', code)
    emit('update:open', false)
  } catch {
    if (request === scanRequest) error.value = 'Could not read the barcode. Try a clearer photo or enter the ISBN manually.'
  } finally {
    if (request === scanRequest) loading.value = false
  }
}

function close () {
  emit('update:open', false)
}

function cleanupPreview () {
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = ''
  }
}

watch(() => props.open, (isOpen) => {
  if (isOpen) {
    closeButton.value?.focus()
    return
  }
  scanRequest++
  error.value = ''
  loading.value = false
  cleanupPreview()
}, { flush: 'post' })

onBeforeUnmount(() => {
  scanRequest++
  cleanupPreview()
})
</script>

<style scoped>
.backdrop {
  position: fixed; inset: 0; background: rgba(0,0,0,.6);
  display: grid; place-items: center; z-index: 9999;
  padding: 16px;
  box-sizing: border-box;
}
.dialog {
  width: min(92vw, 520px); 
  background:var(--panel);
  border-radius: 14px;
  max-height: 100%;
  overflow: auto; box-shadow: 0 20px 50px rgba(0,0,0,.3);
  display: grid; grid-template-rows: auto 1fr auto;
}

.header, .footer {
  padding: 12px 16px; background: var(--panel); border-bottom: 1px solid var(--line);
  display: flex; align-items: center; justify-content: space-between; gap: 8px;
}
.header h3 { margin: 0; font-size: 1.05rem; }
.body { padding: 14px; }
.icon-btn { min-width: 44px; min-height: 44px; border:1px solid var(--line); background: var(--panel); color: var(--text); font-size: 18px; cursor: pointer; }
.btn { display: inline-flex; align-items: center; justify-content: center; gap: 8px; min-height: 44px; padding: 10px 14px; border-radius: 10px; border:1px solid var(--line); background: var(--panel); color: var(--text); cursor: pointer; }
.btn:hover { outline-color: var(--brand) }
.error { color: #ff9cae; margin-top: 10px; }
.hint { color: #fff; margin: 0 0 10px 0; }
.actions { display: flex; gap: 10px; align-items: center; }
.hidden { display: none; }
.preview { margin: 14px 0 0; }
.preview img { width: 100%; border-radius: 10px; background: #000; }
.preview figcaption { font-size: .85rem; color: #666; margin-top: 4px; }
</style>
