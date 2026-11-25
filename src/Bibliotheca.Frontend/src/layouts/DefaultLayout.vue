<!-- src/layouts/DefaultLayout.vue -->
<template>
  <AppShell>
    <template #sidebar="{ onCloseSidebar }">
      <Sidebar @navigate="onCloseSidebar" />
    </template>

    <template #topbar="{ onToggleSidebar }">
      <Topbar @toggle-sidebar="onToggleSidebar" />
    </template>

    <router-view/>

    <ToastContainer />
  </AppShell>
</template>

<script setup>
import { onBeforeUnmount, onMounted } from 'vue'
import { recordImport, refreshImportHistory, resetImportHistory } from '@/composables/useImportHistory'
import AppShell from '@/components/AppShell.vue'
import Topbar from '@/components/Topbar.vue'
import Sidebar from '@/components/Sidebar.vue'
import ToastContainer from '@/components/ToastContainer.vue'
import { useBooksHub } from '@/composables/useBooksHub.js'
import { useToasts } from '@/composables/useToasts.js'

const { addToast } = useToasts()

function refreshImports() {
  refreshImportHistory().catch(() => {})
}

onMounted(() => window.addEventListener('online', refreshImports))
onBeforeUnmount(() => {
  window.removeEventListener('online', refreshImports)
  resetImportHistory()
})

useBooksHub(({ isbn, success, title, bookId, import: progress }) => {
  recordImport(progress)
  const summary = progress?.total > 1
    ? ` (${progress.succeeded + progress.failed} of ${progress.total} processed)`
    : ''
  addToast({
    header: success ? 'Book imported' : 'Book import failed',
    body: success
      ? `Book with ISBN ${isbn}, titled ${title} was imported.${summary}`
      : `Could not import the book with ISBN ${isbn}.${summary}`,
    links: success && bookId != null ? [{ label: 'Open book', href: `/books/${bookId}` }] : [],
  })
}, refreshImports)
</script>