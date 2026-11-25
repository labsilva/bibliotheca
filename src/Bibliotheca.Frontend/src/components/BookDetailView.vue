<!-- src/components/BookDetailView.vue -->
<template>
  <div class="book-detail">
    <div class="header">
      <img class="banner" :src="book.coverUrl" alt="" />
      <div class="header-content">
        <div class="cover-wrapper">
          <img :src="book.coverUrl" alt="Capa" class="cover" />
          <img v-if="flagUrl" :src="flagUrl" alt="Bandeira" class="flag" />
        </div>
        <div class="info">
          <h1 class="title">{{ book.book.title }} {{ book.book.subtitle != null && book.book.subtitle != '' ? " - " + book.book.subtitle : "" }}</h1>
          <div class="tabs">
            <!-- <button class="tab active">Information</button> -->
            <ReadingStatusDropdown
              :model-value="book.userBook?.readingStatus ?? 'Unread'"
              :saving="saving.status"
              @select="(status, readingDate) => emit('status', status, readingDate)"
            />
            <SeriesButton 
              :book-id="book.book.bookId"
              :series="book.book.series?.name" />
            <ShelvesDropdown
              :model-value="currentShelf"
              :options="shelvesOptions"
              :saving="saving.shelf"
              @select="emit('shelf', $event)"
            />
            <BookRating
              v-if="currentShelf !== 'Unshelved'"
              class="book-rating"
              :rating="book.userBook?.rating ?? 0"
              :disabled="saving.rating"
              @rate="emit('rate', $event)"
            />
          </div>
          <div class="tabs">
            <ContributorsList :contributors="book.book.authors" />
          </div>
        </div>
        <div class="actions">
          <button disabled title="Editing is unavailable">✎</button>
          <button :disabled="saving.refresh" title="Refresh metadata" @click="emit('refresh')">
            <font-awesome-icon icon="fa-solid fa-refresh" />
            {{ saving.refresh ? '…' : '' }}
          </button>
          <button class="delete-action" @click="confirmOpen = true" :disabled="saving.delete" title="Delete">
            <font-awesome-icon icon="fa-solid fa-trash" />
            {{ saving.delete ? '…' : '' }}
          </button>
          <ConfirmModal
            v-model="confirmOpen"
            title="Delete this book? This cannot be undone."
            message="This removes the book from the shared catalogue and all personal libraries."
            confirm-text="Delete"
            cancel-text="Cancel"
            :loading="saving.delete"
            @confirm="emit('delete')"
          />
          <button @click="$router.go(-1)">←</button>
        </div>
      </div>
    </div>

    <p v-if="saving.refresh" role="status">Refreshing book metadata…</p>
    <p v-if="actionError" role="alert">{{ actionError }}</p>
    <div class="body">
      <div class="left">
        <div class="card">
          <div class="field"><span>Language</span><strong>{{ book.book.additionalData?.language || '—' }}</strong></div>
          <div class="field"><span>ISBN-13</span><strong>{{ book.book.isbn || '—' }}</strong></div>
          <div class="field"><span>Published on</span><strong>{{ $formatDate(book.book.publishedOn) || '—' }}</strong></div>
          <div v-if="book.readingDate" class="field">
            <span>Read on</span><strong><time :datetime="book.readingDate">{{ book.readingDate }}</time></strong>
          </div>
          <div class="field"><span>Publisher</span><strong>{{ book.book.publisher?.name || '—' }}</strong></div>
          <div class="field"><span>Collection</span><strong>{{ book.book.collection?.name }}</strong></div>
          <div class="field"><span>Pages</span><strong>{{ book.book.additionalData?.pages }}</strong></div>
        </div>
        <div class="card">
          <h3>Genres</h3>
          <div v-if="!book.book.genres || !book.book.genres.length" class="muted">—</div>
          <div v-else>
              <RouterLink v-for="c in book.book.genres" :key="c.bookGenreId" class="tab" :to="{ name: 'genre', params: { id: c.bookGenreId } }">{{ c.genre }}</RouterLink>
          </div>
        </div>
        <div class="card">
          <h3>Tags</h3>
            <TagPicker
              :tags="book.book.tags ?? []"
              :tag-adding="saving.tag"
              @add="emit('tag', $event)"
            />
        </div>
      </div>
      <div class="right">
        <div class="top-cards">
          <div class="card">
            <h3>Synopsis</h3>
            <p v-if="!book.book.additionalData?.synopsis" class="muted">The book doesn't have a synopsis.</p>
            <p v-else>{{ book.book.additionalData?.synopsis }}</p>
          </div>
        </div>
        <div class="card" v-if="book.userBook">
          <h3>Comments</h3>
          <CommentBox
            :model-value="book.userBook.comments ?? ''"
            :disabled="saving.comments"
            placeholder="Write your thoughts about this book…"
            :max-length="500"
            @changed="emit('comment', $event)" />
        </div>
        <div class="card">
          <h3>External Links</h3>
          <div v-if="!book.book.externalLinks || !book.book.externalLinks.length" class="muted">—</div>
          <div v-else class="contributors">
            <div v-for="c in book.book.externalLinks" :key="c.externalLinkId" class="contributor">
              <a :href="c.url" target="_blank">{{ c.url }}</a>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import ConfirmModal from '@/components/ConfirmModal.vue'
import TagPicker from '@/components/TagPicker.vue'
import ContributorsList from '@/components/ContributorsList.vue'
import ReadingStatusDropdown from '@/components/ReadingStatusDropdown.vue'
import ShelvesDropdown from '@/components/ShelvesDropdown.vue'
import SeriesButton from '@/components/SeriesButton.vue';
import BookRating from '@/components/BookRating.vue'
import CommentBox from '@/components/CommentBox.vue'

const props = defineProps({
  book: { type: Object, required: true },
  saving: { type: Object, required: true },
  actionError: { type: String, default: '' },
})
const emit = defineEmits(['status', 'shelf', 'rate', 'comment', 'tag', 'delete', 'refresh'])
const confirmOpen = defineModel('confirmOpen', { type: Boolean, default: false })

const flagUrl = computed(() => props.book.flagUrl)
const currentShelf = computed(() => props.book.userBook?.shelve?.shelveName ?? 'Unshelved')
const shelvesOptions = computed(() => [
  'Unshelved',
  ...props.book.shelves.map(shelf => shelf.shelveName),
])
</script>

<style scoped>
.actions button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.book-detail {
  color: #fff;
  background-color: #0B0F14;
  font-family: sans-serif;
  padding: 1.5rem;
  max-width: 1200px;
  margin: 0 auto;
}

.header {
  position: relative;
  border-radius: 16px;
  overflow: hidden;
  margin-bottom: 24px;
}
.banner {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center;
  filter: blur(24px) saturate(120%);
  transform: scale(1.1);
}
.header-content {
  position: relative;
  display: flex;
  gap: 24px;
  background: rgba(0, 0, 0, 0.4);
  backdrop-filter: blur(12px);
  padding: 32px;
  align-items: flex-start;
}
.cover-wrapper { position: relative; }
.cover {
  width: 180px;
  border-radius: 16px;
  box-shadow: 0 8px 24px rgba(0,0,0,0.6);
}
.flag {
  position: absolute;
  bottom: 8px;
  right: 8px;
  width: 24px;
  height: 16px;
  border-radius: 4px;
}
.info { flex: 1; min-width: 0; }
.title {
  font-size: 2rem;
  font-weight: 600;
  margin: 0;
}
.author { margin: 8px 0 16px; color: rgba(255,255,255,0.8); }
.tabs { display: flex; gap: 8px; padding-top: 10px }
.tab {
  padding: 6px 12px;
  border-radius: 8px;
  border: 1px solid rgba(255,255,255,0.1);
  background: transparent;
  color: rgba(255,255,255,0.8);
  text-decoration: none;
}
.tab.active {
  background: #1d4665;
  color: #fff;
  border-color: rgba(255,255,255,0.2);
}

.actions {
  display: flex;
  gap: 8px;
}
.actions button {
  background: rgba(255,255,255,0.1);
  border: none;
  border-radius: 10px;
  color: white;
  padding: 8px 10px;
  font-size: 18px;
  cursor: pointer;
}
.actions button:hover { background: rgba(255,255,255,0.15); }
.actions button.delete-action:not(:disabled):hover { background: #b42318; }

.body {
  display: grid;
  grid-template-columns: 1fr 2fr;
  gap: 24px;
}
.card {
  background: #0F141B;
  border: 1px solid rgba(255,255,255,0.1);
  border-radius: 16px;
  padding: 20px;
  margin-bottom: 24px;
}
.card h3 { margin-top: 0; font-size: 1.1rem; margin-bottom: 12px; }
.field {
  display: flex;
  justify-content: space-between;
  border-bottom: 1px solid rgba(255,255,255,0.1);
  padding: 8px 0;
}
.field span { color: rgba(255,255,255,0.6); font-size: 0.9rem; }
.field strong { font-size: 1rem; }
.muted { color: rgba(255,255,255,0.6); font-style: italic; }

.tabs {
  display: flex;
  gap: 8px;
  padding-top: 10px;
  /* importante para permitir que o rating passe para baixo quando não houver espaço */
  flex-wrap: wrap;
}

/* em desktop: rating fica como estava, só garantimos que não encolhe demais */
.book-rating {
  flex-shrink: 0;
}

/* --- MOBILE --- */
@media (max-width: 768px) {
  .tabs {
    /* os outros botões ficam na primeira linha */
    justify-content: flex-start;
  }

  .book-rating {
    /* passa para a linha de baixo e ocupa a largura toda */
    width: 100%;
    order: 99;                /* garante que vem depois dos outros */
    display: flex;
    justify-content: center;  /* centra as estrelas */
    margin-top: 6px;
  }
}

@media (max-width: 768px) {
  .book-detail {
    padding: 1rem 0.75rem 1.5rem;
  }

  .header {
    margin-bottom: 16px;
  }

  .header-content {
    flex-direction: column;      /* capa em cima, info por baixo */
    align-items: center;
    text-align: center;
    padding: 20px;
    gap: 16px;
  }

  .cover {
    width: 140px;               /* capa um pouco mais pequena em mobile */
  }

  .info {
    width: 100%;
  }

  .title {
    font-size: 1.4rem;
  }

  .author {
    margin-top: 4px;
    margin-bottom: 12px;
  }

  .actions {
    margin-left: 0;
    justify-content: center;
    flex-wrap: wrap;
    gap: 8px;
  }

  .actions button {
    font-size: 0.9rem;
    padding: 6px 10px;
  }

  .body {
    grid-template-columns: 1fr; /* 🔑 passa a 1 coluna em mobile */
    gap: 16px;
  }

  .left,
  .right {
    min-width: 0;
  }

  .card {
    margin-bottom: 16px;
  }
}



</style>
