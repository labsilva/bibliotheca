<template>
  <div class="upload-card">
    <h2 v-if="props.title" class="upload-title">
      <font-awesome-icon icon="fa-solid fa-upload" class="title-icon" />
      {{ props.title }}
    </h2>

    <form @submit.prevent="onSubmit" class="upload-form">
      <label class="file-input-label">
        <span class="file-label-text">
          {{ fileName || 'Choose a file…' }}
        </span>

        <font-awesome-icon icon="fa-solid fa-folder-open" class="file-icon" />

        <input
          type="file"
          @change="onFileChange"
          :disabled="loading"
          :accept="accept"  
        />
      </label>

      <button
        class="upload-button"
        type="submit"
        :disabled="!file || loading"
      >
        <font-awesome-icon icon="fa-solid fa-cloud-arrow-up" class="upload-icon" />
        <span v-if="!loading">Upload</span>
        <span v-else>Uploading…</span>
      </button>
    </form>

    <p v-if="success" class="status status--success">
      <font-awesome-icon icon="fa-solid fa-circle-check" />
      File queued for import.
    </p>

    <p v-if="errorMessage" class="status status--error">
      <font-awesome-icon icon="fa-solid fa-triangle-exclamation" />
      {{ errorMessage }}
    </p>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useFileUpload } from '@/composables/useFileUpload.js'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'

const props = defineProps({
  endpoint: {
    type: String,
    required: true,
  },
  accept: { type: String, default: "" },
  title: {
    type: String,
    required: false
  }
})

const emit = defineEmits(['queued'])
const { uploadFile, loading, success, errorMessage } = useFileUpload(props.endpoint)

const file = ref(null)
const fileName = ref('')

function onFileChange(event) {
  const selected = event.target.files?.[0] ?? null
  file.value = selected
  fileName.value = selected ? selected.name : ''
  success.value = false
  errorMessage.value = ''
}

async function onSubmit() {
  if (!file.value) return
  try {
    emit('queued', await uploadFile(file.value))
  } catch {
    // The upload composable exposes the error for display.
  }
}
</script>

<style scoped>
:root {
  --upload-bg: #050813;
  --upload-surface: #070b1c;
  --upload-border: rgba(31, 59, 255, 0.35);
  --upload-neon: #35a3ff;
  --upload-text: #e6f1ff;
  --upload-text-muted: #7f8db3;
  --upload-error: #ff4b6e;
  --upload-success: #4bffb3;
}

/* CARD */
.upload-card {
  width: 100%;
  background:
    radial-gradient(circle at top left, rgba(53, 163, 255, 0.18), transparent 55%),
    radial-gradient(circle at bottom right, rgba(31, 59, 255, 0.3), transparent 60%),
    var(--upload-surface);
  border-radius: 12px;
  padding: 1.25rem 1.5rem;
  border: 1px solid var(--upload-border);
  box-shadow:
    0 0 18px rgba(5, 12, 48, 0.85),
    0 0 26px rgba(31, 59, 255, 0.3);
  color: var(--upload-text);
  box-sizing: border-box;
  overflow: hidden; /* mantém os glows dentro da caixa */
}

/* TITLE */
.upload-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0 0 1rem;
  font-size: 0.95rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--upload-neon);
  text-shadow: 0 0 6px rgba(53, 163, 255, 0.7);
}

.title-icon {
  color: var(--upload-neon);
}

/* FORM – input e botão na MESMA linha */
.upload-form {
  display: flex;
  flex-direction: row;
  align-items: stretch;
  gap: 0.8rem;
  width: 100%;
  /* sem flex-wrap: ficam sempre na mesma linha */
}

/* FILE INPUT VISUAL */
.file-input-label {
  position: relative;
  flex: 1 1 auto;
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.55rem 1rem;
  border-radius: 999px;
  background: rgba(3, 8, 30, 0.98);
  border: 1px solid var(--upload-border);
  cursor: pointer;
  transition:
    border-color 0.2s ease,
    box-shadow 0.2s ease,
    background 0.2s ease,
    transform 0.1s ease;
  box-shadow:
    inset 0 0 0 1px rgba(3, 10, 40, 0.9);
}

.file-input-label:hover {
  border-color: var(--upload-neon);
  box-shadow:
    0 0 10px rgba(53, 163, 255, 0.7),
    0 0 20px rgba(53, 163, 255, 0.45);
  background: rgba(5, 12, 40, 1);
  transform: translateY(-0.5px);
}

/* LABEL TEXT */
.file-label-text {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--upload-text-muted);
  font-size: 0.85rem;
}

/* FOLDER ICON */
.file-icon {
  font-size: 1rem;
  color: var(--upload-neon);
}

/* HIDE REAL INPUT */
.file-input-label input[type="file"] {
  position: absolute;
  inset: 0;
  opacity: 0;
  cursor: pointer;
}

/* === NEW UPLOAD BUTTON (styled like the Export / Import buttons) === */
.upload-button {
  flex: 0 0 auto;
  min-width: 150px;
  height: 40px;

  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.45rem;

  padding: 0 1.2rem;
  border-radius: 12px;

  background: linear-gradient(
    180deg,
    rgba(10, 20, 40, 0.75) 0%,
    rgba(5, 10, 25, 0.9) 100%
  );

  border: 1px solid rgba(56, 189, 248, 0.35);
  box-shadow:
    0 0 6px rgba(53, 163, 255, 0.25),
    inset 0 0 8px rgba(53, 163, 255, 0.12);

  color: var(--upload-text);
  font-size: 0.85rem;
  font-weight: 500;
  letter-spacing: 0.03em;
  text-transform: none;
  text-shadow: 0 0 6px rgba(0, 180, 255, 0.4);
  cursor: pointer;
  transition:
    box-shadow 0.2s ease,
    background 0.2s ease,
    border-color 0.2s ease,
    transform 0.12s ease;
}

.upload-button:hover:not(:disabled) {
  border-color: rgba(53, 163, 255, 0.9);
  background: linear-gradient(
    180deg,
    rgba(15, 35, 70, 0.85) 0%,
    rgba(8, 15, 35, 0.95) 100%
  );

  box-shadow:
    0 0 12px rgba(53, 163, 255, 0.45),
    inset 0 0 10px rgba(53, 163, 255, 0.2);

  transform: translateY(-1px);
}

.upload-button:active:not(:disabled) {
  transform: translateY(0);
  box-shadow:
    0 0 6px rgba(53, 163, 255, 0.35),
    inset 0 0 6px rgba(53, 163, 255, 0.15);
}

.upload-button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
  box-shadow: none;
  border-color: rgba(53, 163, 255, 0.4);
}

/* Button icon color */
.upload-icon {
  color: var(--upload-neon);
  font-size: 1rem;
}

/* STATUS */
.status {
  margin-top: 0.85rem;
  font-size: 0.8rem;
  display: flex;
  gap: 0.4rem;
  align-items: center;
}

.status--success {
  color: var(--upload-success);
  text-shadow: 0 0 6px rgba(75, 255, 179, 0.8);
}

.status--error {
  color: var(--upload-error);
  text-shadow: 0 0 6px rgba(255, 75, 110, 0.8);
}
@media (max-width: 640px) {
  .upload-card { padding: 12px; }
  .upload-form { flex-direction: column; }
  .file-input-label { min-width: 0; }
  .upload-button { min-width: 0; width: 100%; }
}
</style>
