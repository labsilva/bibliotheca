<template>
  <section class="batch">
    <label for="batch-shelf">Destination shelf</label>
    <select id="batch-shelf" v-model="shelfId" :disabled="loadingShelves || batchId !== null">
      <option :value="null">Leave unshelved</option>
      <option v-for="shelf in shelves" :key="shelf.shelveId" :value="shelf.shelveId">{{ shelf.shelve }}</option>
    </select>
    <p v-if="shelfError" role="status">Could not load shelves. <button type="button" @click="loadShelves">Retry</button></p>
    <video v-if="cameraActive" :key="cameraAttempt" ref="preview" autoplay muted playsinline></video>
    <p v-if="cameraActive">Point the camera at each book’s ISBN barcode.</p>
    <p v-if="cameraError" role="alert">{{ cameraError }}</p>
    <p v-if="feedback" role="status">{{ feedback }}</p>
    <div role="status">
      <p>{{ Math.max(progress?.total ?? 0, queued) }} queued for import</p>
      <p v-if="progress">{{ progress.succeeded }} imported, {{ progress.failed }} failed, {{ progress.pending }} pending</p>
    </div>
    <button v-if="failed.size" type="button" @click="failed.clear()">Allow failed submissions to scan again</button>
    <button v-if="cameraActive" type="button" @click="stopCamera">Pause camera</button>
    <button v-else type="button" :disabled="loadingShelves" @click="startCamera">
      {{ batchId ? 'Resume camera' : 'Start camera' }}
    </button>
    <button v-if="batchId" type="button" @click="emit('finished')">Finish batch</button>
  </section>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { BrowserMultiFormatReader } from '@zxing/browser'
import api from '@/lib/http.js'

defineProps({ progress: Object })
const emit = defineEmits(['batch-started', 'queued', 'finished'])
const shelfId = ref(null)
const shelves = ref([])
const loadingShelves = ref(false)
const shelfError = ref(false)
const preview = ref(null)
const cameraActive = ref(false)
const cameraError = ref('')
const feedback = ref('')
const queued = ref(0)
const batchId = ref(null)
const scans = new Set()
const failed = ref(new Set())
const lifecycle = new AbortController()
let stream
let controls
let cameraAttempt = 0
let audio

async function loadShelves() {
  loadingShelves.value = true
  shelfError.value = false
  try {
    const { data } = await api.get('/api/library/shelves', { signal: lifecycle.signal })
    shelves.value = data.shelves || []
  } catch {
    shelfError.value = !lifecycle.signal.aborted
  } finally {
    loadingShelves.value = false
  }
}

async function startCamera() {
  const attempt = ++cameraAttempt
  cameraActive.value = true
  cameraError.value = ''
  try {
    if (!audio && window.AudioContext) {
      audio = new window.AudioContext()
      audio.resume().catch(() => {})
    }
  } catch {
    // Camera scanning also works on devices without audio feedback.
  }

  try {
    const media = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: { ideal: 'environment' } },
    })
    if (attempt !== cameraAttempt) {
      media.getTracks().forEach(track => track.stop())
      return
    }

    stream = media
    await nextTick()
    if (attempt !== cameraAttempt) return
    if (!batchId.value) {
      batchId.value = crypto.randomUUID()
      emit('batch-started', batchId.value)
    }
    const reader = new BrowserMultiFormatReader()
    const scanner = await reader.decodeFromStream(media, preview.value, result => {
      if (result && attempt === cameraAttempt) queueBarcode(result.getText())
    })
    if (attempt === cameraAttempt) controls = scanner
    else scanner.stop()
  } catch {
    if (attempt !== cameraAttempt) return
    stopCamera()
    cameraError.value = 'Could not start the camera. Check camera access and try again.'
  }
}

function stopCamera() {
  cameraAttempt++
  controls?.stop()
  controls = undefined
  stream?.getTracks().forEach(track => track.stop())
  stream = undefined
  cameraActive.value = false
}

async function queueBarcode(isbn) {
  if (!/^97[89]\d{10}$/.test(isbn)) return
  const checksum = [...isbn].reduce((sum, digit, index) => sum + Number(digit) * (index % 2 ? 3 : 1), 0)
  if (checksum % 10 || scans.has(isbn) || failed.value.has(isbn)) return

  scans.add(isbn)
  try {
    const { data } = await api.put('/api/fetch/isbn', {
      isbns: [isbn], shelfId: shelfId.value, batchId: batchId.value,
    })
    if (lifecycle.signal.aborted) return
    queued.value++
    emit('queued', data)
    feedback.value = 'Queued ' + isbn
    acknowledgeScan()
  } catch (exception) {
    if (lifecycle.signal.aborted) return
    scans.delete(isbn)
    failed.value.add(isbn)
    feedback.value = exception.response?.status === 429
      ? 'The import queue is full. Try the scan again when it has room.'
      : 'Could not queue ' + isbn + '. Check your connection and destination shelf.'
  }
}

function acknowledgeScan() {
  try {
    if (navigator.vibrate?.(80) || audio?.state !== 'running') return
    const tone = audio.createOscillator()
    const volume = audio.createGain()
    volume.gain.value = 0.1
    tone.frequency.value = 700
    tone.connect(volume).connect(audio.destination)
    tone.start()
    tone.stop(audio.currentTime + 0.12)
  } catch {
    // The on-screen confirmation remains available.
  }
}

onMounted(loadShelves)
onBeforeUnmount(() => {
  lifecycle.abort()
  stopCamera()
  audio?.close().catch(() => {})
})
</script>

<style scoped>
.batch {
  display: grid;
  gap: 8px;
  padding: 16px 24px;
}
video {
  width: 100%;
  max-height: 40dvh;
  border-radius: 8px;
  background: #000;
}
select, button {
  min-height: 44px;
  font: inherit;
}
p { margin: 0; }
</style>
