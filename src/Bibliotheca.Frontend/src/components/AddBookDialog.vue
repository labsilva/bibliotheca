<template>
  <dialog
    ref="dialog"
    class="add-book"
    aria-labelledby="add-book-title"
    @cancel.self.prevent="onEscape"
    @click.self="closeOnBackdrop"
  >
    <div v-if="open" :inert="scanning ? '' : undefined">
      <header class="dialog-header">
        <h2 id="add-book-title">Add book</h2>
        <button type="button" class="button close-button" aria-label="Close" :disabled="submitting" @click="close">
          <font-awesome-icon icon="fa-solid fa-xmark" aria-hidden="true" />
        </button>
      </header>

      <div class="mode-switch" role="group" aria-label="Find a book by">
        <button type="button" class="button" :aria-pressed="mode === 'isbn'" :disabled="submitting" @click="mode = 'isbn'">
          ISBN / barcode
        </button>
        <button type="button" class="button" :aria-pressed="mode === 'title'" :disabled="submitting" @click="searchByTitle(null)">
          Search by title
        </button>
        <button type="button" class="button" :aria-pressed="mode === 'batch'" :disabled="submitting" @click="mode = 'batch'">
          Scan a batch
        </button>
        <button type="button" class="button" :aria-pressed="mode === 'history'" :disabled="submitting" @click="mode = 'history'">
          Import history
        </button>
      </div>

      <ExternalEditionSearch v-if="mode === 'title'" :initial-shelf-id="titleShelf" @busy="submitting = $event" @queued="complete"
        @open-book="close" />
      <BarcodeBatch v-else-if="mode === 'batch'" :progress="batchProgress"
        @batch-started="activeBatch = $event" @queued="recordImport" @finished="mode = 'history'" />
      <ImportHistory v-else-if="mode === 'history'" class="dialog-body" allow-title-search
        @search-by-title="searchByTitle" />
      <form v-else @submit.prevent="submit">
      <div class="dialog-body">
        <label for="add-book-isbn">ISBN</label>
        <div class="isbn-row">
          <input
            id="add-book-isbn"
            ref="input"
            v-model="isbn"
            placeholder="9780140328721"
            autocomplete="off"
            autocapitalize="off"
            spellcheck="false"
            :aria-describedby="error ? 'isbn-hint isbn-error' : 'isbn-hint'"
            :disabled="submitting"
            @input="error = ''"
          />
          <button ref="scanButton" type="button" class="button scan-button" :disabled="submitting" @click="startScan">
            <font-awesome-icon icon="fa-solid fa-barcode" aria-hidden="true" />
            Scan barcode
          </button>
        </div>
        <p id="isbn-hint" class="hint">Enter the ISBN or scan the book’s barcode.</p>
        <p v-if="error" id="isbn-error" class="error" role="alert">{{ error }}</p>
      </div>

      <footer class="dialog-actions">
        <button type="button" class="button cancel-button" :disabled="submitting" @click="close">
          <font-awesome-icon icon="fa-solid fa-arrow-left" aria-hidden="true" />
          Cancel
        </button>
        <button type="submit" class="button add-button" :disabled="!isbn.trim() || submitting">
          {{ submitting ? 'Adding…' : 'Add book' }}
        </button>
      </footer>
      </form>
    </div>

    <BarcodePhotoModal v-model:open="scanning" @decoded="isbn = $event" />
  </dialog>

  <div v-if="notification" class="notification" role="status">{{ notification }}</div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import BarcodeBatch from './BarcodeBatch.vue'
import ImportHistory from './ImportHistory.vue'
import { recordImport, importEntries } from '@/composables/useImportHistory'
import BarcodePhotoModal from './BarcodePhotoModal.vue'
import ExternalEditionSearch from './ExternalEditionSearch.vue'
import { importBook } from '@/composables/useBooks'

const open = defineModel('open', { type: Boolean, default: false })
const dialog = ref(null)
const input = ref(null)
const scanButton = ref(null)
const isbn = ref('')
const mode = ref('isbn')
const activeBatch = ref(null)
const titleShelf = ref(null)
const batchProgress = computed(() => importEntries.value.find(entry => entry.id === activeBatch.value && activeBatch.value))
const scanning = ref(false)
const submitting = ref(false)
const error = ref('')
const notification = ref('')
let opener
let notificationTimer
let disposed = false

function fitViewport() {
  const viewport = window.visualViewport
  if (!viewport || !dialog.value) return

  const bottom = Math.max(0, document.documentElement.clientHeight - viewport.height - viewport.offsetTop)
  dialog.value.style.setProperty('--visible-height', viewport.height + 'px')
  dialog.value.style.setProperty('--viewport-bottom', bottom + 'px')
}

onMounted(() => {
  window.visualViewport?.addEventListener('resize', fitViewport)
  window.visualViewport?.addEventListener('scroll', fitViewport)
})

watch(open, async (visible) => {
  if (visible) {
    await nextTick()
    if (!open.value || !dialog.value) return
    opener = document.activeElement
    fitViewport()
    dialog.value.showModal()
    if (!window.matchMedia('(max-width: 768px), (pointer: coarse)').matches) input.value?.focus()
  } else {
    dialog.value?.close()
    isbn.value = ''
    mode.value = 'isbn'
    error.value = ''
    scanning.value = false
    opener?.focus()
  }
}, { immediate: true })

watch(mode, () => { activeBatch.value = null })

watch(scanning, async (visible) => {
  if (visible) return
  await nextTick()
  if (open.value) scanButton.value?.focus()
})

function close() {
  if (!submitting.value) open.value = false
}

function searchByTitle(shelfId) {
  titleShelf.value = shelfId
  mode.value = 'title'
}

function startScan() {
  input.value?.blur()
  scanning.value = true
}

function onEscape() {
  if (scanning.value) scanning.value = false
  else close()
}

function closeOnBackdrop(event) {
  const bounds = dialog.value.getBoundingClientRect()
  if (event.clientX < bounds.left || event.clientX >= bounds.right ||
      event.clientY < bounds.top || event.clientY >= bounds.bottom) close()
}

async function submit() {
  const value = isbn.value.trim()
  if (!value || submitting.value) return

  submitting.value = true
  error.value = ''
  try {
    complete(await importBook(value))
  } catch (exception) {
    if (!disposed) error.value = exception.response?.status === 429
      ? 'The import queue is full. Try again later.'
      : 'Could not queue the book. Check your connection and try again.'
  } finally {
    submitting.value = false
  }
}

function complete(entry) {
  if (disposed) return
  recordImport(entry)
  open.value = false
  notification.value = 'Book queued for import.'
  clearTimeout(notificationTimer)
  notificationTimer = setTimeout(() => { notification.value = '' }, 5000)
}

onBeforeUnmount(() => {
  disposed = true
  clearTimeout(notificationTimer)
  window.visualViewport?.removeEventListener('resize', fitViewport)
  window.visualViewport?.removeEventListener('scroll', fitViewport)
})
</script>

<style scoped>
.add-book {
  position: fixed;
  box-sizing: border-box;
  width: min(540px, calc(100% - 32px));
  max-height: calc(var(--visible-height, 100dvh) - 32px);
  padding: 0;
  overflow: auto;
  overscroll-behavior: contain;
  border: 1px solid var(--line);
  border-radius: 16px;
  color: var(--text);
  background: var(--panel);
  box-shadow: 0 24px 64px rgb(0 0 0 / 48%);
}

.add-book::backdrop {
  background: rgb(0 0 0 / 58%);
}

.dialog-header, .dialog-body, .dialog-actions {
  padding-inline: 24px;
}

.dialog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding-top: 16px;
}

.dialog-header h2 {
  margin: 0;
  font-size: 1.35rem;
  line-height: 1.2;
}

.dialog-body {
  padding-block: 12px 20px;
}

.mode-switch {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  padding: 14px 24px 0;
}

.mode-switch [aria-pressed="true"] {
  border-color: var(--brand);
  background: var(--bg);
}

.dialog-body label {
  display: block;
  margin-bottom: 6px;
  font-weight: 600;
}

.isbn-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
}

.isbn-row input {
  box-sizing: border-box;
  width: 100%;
  min-height: 44px;
  padding: 9px 12px;
  border: 1px solid var(--line);
  border-radius: 9px;
  background: var(--bg);
  color: var(--text);
  font: inherit;
  font-size: max(16px, 1rem);
}

.hint, .error {
  margin: 8px 0 0;
  font-size: .875rem;
}

.hint { color: var(--sub, var(--text)); }
.error { color: #ff9cae; }

.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding-block: 16px;
  border-top: 1px solid var(--line);
}

.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 44px;
  padding: 8px 14px;
  border: 1px solid var(--line);
  border-radius: 9px;
  background: transparent;
  color: inherit;
  font: inherit;
  white-space: nowrap;
  cursor: pointer;
}

.button:disabled { opacity: .55; cursor: default; }
.button:focus-visible, .isbn-row input:focus-visible { outline: 2px solid var(--brand); outline-offset: 2px; }
.close-button { width: 44px; padding: 0; border-color: transparent; }
.close-button:hover:not(:disabled), .scan-button:hover:not(:disabled) { background: var(--bg); }
.add-button { background: var(--brand); color: #fff; font-weight: 600; }
.cancel-button {
  border-color: #b0173a;
  background: color-mix(in srgb, #b0173a 31%, var(--panel));
  color: #fff;
}
.cancel-button:hover:not(:disabled) { background: #b0173a; }

.notification {
  position: fixed;
  inset: auto 16px calc(16px + env(safe-area-inset-bottom)) auto;
  z-index: 1100;
  padding: 12px 16px;
  border: 1px solid var(--line);
  border-radius: 10px;
  background: var(--panel);
  color: var(--text);
  box-shadow: 0 8px 24px rgb(0 0 0 / 35%);
}

@media (max-width: 768px) {
  .add-book {
    inset: auto 0 var(--viewport-bottom, 0px);
    width: 100%;
    max-width: none;
    max-height: calc(var(--visible-height, 100dvh) - env(safe-area-inset-top));
    margin: 0 auto;
    border-radius: 20px 20px 0 0;
  }

  .dialog-header, .dialog-body, .dialog-actions {
    padding-left: max(16px, env(safe-area-inset-left));
    padding-right: max(16px, env(safe-area-inset-right));
  }

  .isbn-row, .dialog-actions { display: grid; grid-template-columns: minmax(0, 1fr); }
  .dialog-actions { padding-bottom: max(16px, env(safe-area-inset-bottom)); }
  .add-button { grid-row: 1; }
}
</style>
