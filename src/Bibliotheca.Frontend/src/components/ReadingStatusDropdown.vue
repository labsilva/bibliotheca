<template>
  <div class="dropdown" @keydown.esc="closeWithEscape" @focusout="closeOnBlur">
    <button
      class="tab dropdown-toggle"
      :class="statusClasses"
      @click="toggle"
      @keydown.down.prevent="openOptions"
      ref="trigger"
      type="button"
      aria-haspopup="listbox"
      :aria-expanded="open"
      :disabled="saving"
    >
      {{ current }}
      <span class="arrow">▼</span>
    </button>

    <ul v-if="open" ref="list" class="dropdown-menu" role="listbox" aria-label="Reading status">
      <li
        v-for="status in options"
        :key="status"
        :class="{ active: status === current }"
        role="option"
        tabindex="-1"
        :aria-selected="status === current"
        @click="select(status)"
        @keydown.enter.prevent="select(status)"
        @keydown.space.prevent="select(status)"
        @keydown.down.prevent="move($event, 1)"
        @keydown.up.prevent="move($event, -1)"
      >
        {{ status }}
      </li>
    </ul>

    <form v-if="dateDraft !== null" class="reading-date-form" @submit.prevent="confirmRead">
      <label class="reading-date-label">
        Reading date
        <input ref="dateInput" v-model="dateDraft" class="reading-date-control" type="date" required />
      </label>
      <button class="reading-date-control" type="submit" :disabled="!dateDraft || saving">Save</button>
      <button class="reading-date-control" type="button" @click="cancelRead">Cancel</button>
    </form>
  </div>
</template>

<script setup>
import { computed, nextTick, ref, shallowRef, useTemplateRef } from 'vue'

const props = defineProps({
  modelValue: { type: String, default: 'Unread' },
  options: { type: Array, default: () => ['Unread', 'Reading', 'Read'] },
  saving: Boolean,
})
const emit = defineEmits(['select'])

const open = ref(false)
const trigger = ref(null)
const list = ref(null)
const current = computed(() => props.modelValue)
const dateDraft = shallowRef(null)
const dateInput = useTemplateRef('dateInput')

const statusClasses = computed(() => ({
  'status-unread': current.value === 'Unread',
  'status-reading': current.value === 'Reading',
  'status-read': current.value === 'Read'
}))

function toggle() {
  if (open.value) open.value = false
  else openOptions()
}

async function openOptions() {
  open.value = true
  await nextTick()
  list.value?.children[Math.max(0, props.options.indexOf(current.value))]?.focus()
}

function move(event, direction) {
  const items = [...list.value.children]
  items[(items.indexOf(event.currentTarget) + direction + items.length) % items.length]?.focus()
}

function closeWithEscape(event) {
  if (!open.value && dateDraft.value === null) return
  event.stopPropagation()
  if (open.value) {
    open.value = false
    trigger.value?.focus()
  } else {
    cancelRead()
  }
}

function closeOnBlur(event) {
  if (!event.currentTarget.contains(event.relatedTarget)) open.value = false
}

async function select(status) {
  open.value = false
  trigger.value?.focus()
  if (props.saving || status === current.value) return

  if (status === 'Read') {
    const today = new Date()
    const month = String(today.getMonth() + 1).padStart(2, '0')
    const day = String(today.getDate()).padStart(2, '0')
    dateDraft.value = `${today.getFullYear()}-${month}-${day}`
    await nextTick()
    dateInput.value?.focus()
  } else {
    dateDraft.value = null
    emit('select', status)
  }
}

function confirmRead() {
  if (!dateDraft.value || props.saving) return
  const readingDate = dateDraft.value
  cancelRead()
  emit('select', 'Read', readingDate)
}

function cancelRead() {
  dateDraft.value = null
  trigger.value?.focus()
}
</script>

<style scoped>
/* reuse your existing visual language */
.dropdown { position: relative; display: inline-block; }

.dropdown-toggle {
  display: flex; align-items: center; gap: 6px;
  cursor: pointer; user-select: none;
  padding: 6px 12px; border-radius: 8px;
  border: 1px solid rgba(255,255,255,0.1);
  background: transparent; color: rgba(255,255,255,0.8);
}
.dropdown-toggle:disabled { opacity: .6; cursor: not-allowed; }
.dropdown-toggle .arrow { font-size: .8em; opacity: .8; }

.dropdown-menu {
  position: absolute; top: 115%; left: 0;
  background: #11161d; border: 1px solid rgba(255,255,255,0.12);
  border-radius: 8px; padding: 4px 0; margin: 0;
  list-style: none; z-index: 20; box-shadow: 0 6px 16px rgba(0,0,0,0.45);
  min-width: 130px;
}

.dropdown-menu li {
  padding: 8px 14px; color: #e4e4e4; cursor: pointer; transition: background .15s;
}
.dropdown-menu li:hover { background: rgba(255,255,255,0.08); }
.dropdown-menu li.active { background: rgba(255,255,255,0.12); font-weight: 600; }

.reading-date-form { display: flex; flex-wrap: wrap; align-items: end; gap: 6px; margin-top: 8px; }
.reading-date-label { display: grid; gap: 4px; font-size: .9rem; text-align: left; }
.reading-date-control {
  padding: 6px 12px;
  border: 1px solid rgba(255,255,255,0.15);
  border-radius: 8px;
  background: #11161d;
  color: #fff;
  color-scheme: dark;
}
.reading-date-control:disabled { opacity: .6; cursor: not-allowed; }

/* same neon classes you already use */
.status-unread  { background: #b0173a; box-shadow: 0 0 6px #b0173a, 0 0 10px #b0173a; color: #fff; }
.status-reading { background: #bba40f; box-shadow: 0 0 6px #bba40f, 0 0 10px #bba40f; color: #fff; }
.status-read    { background: #0f9f4a; box-shadow: 0 0 6px #0f9f4a, 0 0 10px #0f9f4a; color: #fff; }
</style>
