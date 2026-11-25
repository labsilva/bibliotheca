<template>
  <div v-if="book && !loading" class="p-6">
    <BookDetailView
      v-model:confirm-open="confirmOpen"
      :book="book"
      :saving="saving"
      :action-error="actionError"
      @status="saveReadingStatus"
      @shelf="saveShelf"
      @rate="saveRating"
      @comment="saveComments"
      @tag="saveTag"
      @refresh="refreshMetadata"
      @delete="onDelete"
    />
  </div>
  <div v-else-if="loading" class="p-6 text-white/70">A carregar livro…</div>
  <div v-else-if="error" class="p-6 text-red-400">{{ error }}</div>
</template>

<script setup>
import { ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import BookDetailView from '@/components/BookDetailView.vue'
import { useBookDetail } from '@/composables/useBookDetail'

const route = useRoute()
const router = useRouter()
const {
  book, loading, error, actionError, saving,
  saveReadingStatus, saveShelf, saveRating, saveComments, saveTag, removeBook, refreshMetadata,
} = useBookDetail(() => route.params.id)
const confirmOpen = ref(false)

watch(() => route.params.id, () => { confirmOpen.value = false })

async function onDelete() {
  const deleted = await removeBook()
  confirmOpen.value = false
  if (deleted) await router.push({ name: 'dashboard' })
}
</script>
