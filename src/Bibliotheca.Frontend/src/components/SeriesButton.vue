<template>
  <button
    class="pill"
    :class="statusClasses"
    @click="goSelectSeries">{{ currentText }}
  </button>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'

const props = defineProps({
  bookId: { type: [String, Number], required: true },
  series: { type: [String], required: false }
})

const current = ref(props.series)
const router = useRouter()
const route = useRoute()

const statusClasses = computed(() => ({
  'status-empty': !current.value,
  'status-fill': !!current.value,
}))

const currentText = computed(() => 
  !current.value ? 'No series' : current.value
)

function goSelectSeries() {
  const id = props.bookId
  router.push({
    name: 'series-select',
    params: { id },
    query: { returnTo: route.fullPath }
  })
}
</script>

<style scoped>
.pill {
  padding: 6px 12px;
  border-radius: 8px;
  border: 1px solid rgba(255,255,255,0.1);
}
.status-empty  { background: #fff; box-shadow: 0 0 6px #fff, 0 0 10px #fff; color: #000; }
.status-fill  { background: #1d4665; box-shadow: 0 0 6px #1d4665, 0 0 10px #1d4665; color: #fff; }
</style>