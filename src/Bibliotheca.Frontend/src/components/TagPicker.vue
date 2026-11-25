<!-- src/components/TagPicker.vue -->
<template>
  <div class="tag-picker" @keydown.stop>
    <p v-if="!hasTags" class="muted">The book doesn't have tags.</p>
    <ul v-else class="tags">
      <li v-for="t in tags" :key="t.tagId || t.tag">{{ t.tag }}</li>
    </ul>

    <div class="tag-input-wrap">
      <input
        v-model="tagQuery"
        class="tag-input"
        type="text"
        placeholder="Add a tag…"
        @input="onTagInput"
        @keydown="onTagKeydown"
        @focus="onTagInput"
        @blur="closeTagDropdown"
        :disabled="tagAdding"
        aria-haspopup="listbox"
        :aria-expanded="listVisible"
        aria-controls="tag-listbox"
      />

      <ul
        v-if="listVisible"
        id="tag-listbox"
        class="tag-suggest"
        role="listbox"
      >
        <li
          v-for="(s, i) in tagSuggestions"
          :key="`sugg-${s.tagId || s.tag}`"
          class="tag-suggest-item"
          :class="{ highlighted: i === tagHighlighted }"
          role="option"
          @mousedown.prevent="selectSuggestion(s)"
        >
          {{ s.tag }}
        </li>

        <li
          v-if="canCreateTag"
          key="create-new"
          class="tag-suggest-item create"
          :class="{ highlighted: tagHighlighted === tagSuggestions.length }"
          role="option"
          @mousedown.prevent="createAndAttachTag(tagQuery)"
        >
          + Create “{{ tagQuery }}”
        </li>
      </ul>

      <div class="tag-hints">
        <span v-if="tagAdding">Adding…</span>
        <span v-else-if="tagDuplicate" class="warn">Already added</span>
        <span v-else-if="tagError" class="warn">{{ tagError }}</span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onBeforeUnmount } from 'vue'
import { useTagSearch } from '@/composables/useTags.js'

const props = defineProps({
  tags: { type: Array, default: () => [] },
  tagAdding: Boolean,
})
const emit = defineEmits(['add'])
const hasTags = computed(() => props.tags.length > 0)

/** UI input/autocomplete */
const tagQuery = ref('')
const tagDropdownOpen = ref(false)
const tagHighlighted = ref(0)
const tagError = ref('')
const tagSuggestions = ref([])
const searchPending = ref(false)

const { searchTags } = useTagSearch()

const normalized = s => (s || '').trim().toLowerCase()
const existingTagNames = computed(() => props.tags.map(t => normalized(t.tag)))
const tagDuplicate = computed(() => existingTagNames.value.includes(normalized(tagQuery.value)))
const canCreateTag = computed(() => {
  const q = normalized(tagQuery.value)
  if (!q || searchPending.value) return false
  if (tagDuplicate.value) return false
  return !tagSuggestions.value.some(s => normalized(s.tag) === q)
})
const listVisible = computed(() => tagDropdownOpen.value && (tagSuggestions.value.length > 0 || canCreateTag.value))

let tagDebounce
let searchController
function invalidateSuggestions() {
  clearTimeout(tagDebounce)
  searchController?.abort()
  searchController = null
  searchPending.value = false
  tagSuggestions.value = []
  tagHighlighted.value = 0
  tagError.value = ''
}

function closeTagDropdown() {
  tagDropdownOpen.value = false
  invalidateSuggestions()
}

function onTagInput() {
  invalidateSuggestions()
  tagDropdownOpen.value = true
  const q = tagQuery.value.trim()
  if (q.length < 2) return
  searchPending.value = true
  searchController = new AbortController()
  const { signal } = searchController
  tagDebounce = setTimeout(async () => {
    try {
      const suggestions = await searchTags(q, signal)
      if (!signal.aborted) tagSuggestions.value = suggestions
    } catch {
      if (!signal.aborted) tagError.value = 'Could not search tags'
    } finally {
      if (!signal.aborted) searchPending.value = false
    }
  }, 250)
}

function onTagKeydown(e){
  if (e.key === 'Tab' || e.key === 'Escape') {
    closeTagDropdown()
    return
  }
  if (!tagDropdownOpen.value) return
  const hasItems = tagSuggestions.value.length + (canCreateTag.value ? 1 : 0)
  if ((e.key === 'Enter' || e.key === ',') && normalized(tagQuery.value)){
    e.preventDefault()
    const suggestion = tagSuggestions.value[tagHighlighted.value]
    if (suggestion) selectSuggestion(suggestion)
    else if (canCreateTag.value) createAndAttachTag(tagQuery.value)
    return
  }
  if (e.key === 'ArrowDown'){
    e.preventDefault()
    const total = Math.max(1, hasItems)
    tagHighlighted.value = (tagHighlighted.value + 1) % total
  } else if (e.key === 'ArrowUp'){
    e.preventDefault()
    const total = Math.max(1, hasItems)
    tagHighlighted.value = (tagHighlighted.value - 1 + total) % total
  }
}

function selectSuggestion(tag) {
  emit('add', tag)
  resetTagControls()
}

function createAndAttachTag(name) {
  const tag = name.trim()
  if (tag && !tagDuplicate.value) selectSuggestion({ tag })
}

function resetTagControls(){
  tagQuery.value = ''
  closeTagDropdown()
}
onBeforeUnmount(invalidateSuggestions)
</script>

<style scoped>
.muted { color: rgba(255,255,255,0.6); font-style: italic; }
.tags { list-style: none; padding: 0; margin: 0 0 8px; display: flex; flex-wrap: wrap; gap: 8px; }
.tags li { background: rgba(255,255,255,0.1); border-radius: 8px; padding: 4px 8px; font-size: 0.9rem; }

.tag-input-wrap { margin-top: 8px; position: relative; max-width: 93%; }
.tag-input { width: 100%; background: rgba(255,255,255,0.06); border: 1px solid rgba(255,255,255,0.12); color: #fff; padding: 10px 12px; border-radius: 10px; outline: none; }
.tag-input::placeholder { color: rgba(255,255,255,0.55); }
.tag-input:focus { border-color: rgba(59,130,246,0.5); box-shadow: 0 0 0 3px rgba(59,130,246,0.2); }

.tag-suggest { position: absolute; top: calc(100% + 6px); left: 0; right: 0; background: #11161d; border: 1px solid rgba(255,255,255,0.12); border-radius: 10px; padding: 6px 0; list-style: none; margin: 0; z-index: 30; box-shadow: 0 8px 20px rgba(0,0,0,0.5); max-height: 240px; overflow: auto; }
.tag-suggest-item { padding: 10px 12px; cursor: pointer; color: #e4e4e4; }
.tag-suggest-item.highlighted, .tag-suggest-item:hover { background: rgba(255,255,255,0.08); }
.tag-suggest-item.create { font-weight: 600; border-top: 1px dashed rgba(255,255,255,0.12); }
.tag-hints { margin-top: 6px; min-height: 18px; font-size: 0.85rem; color: rgba(255,255,255,0.7); }
.tag-hints .warn { color: #ffb4b4; }
</style>
