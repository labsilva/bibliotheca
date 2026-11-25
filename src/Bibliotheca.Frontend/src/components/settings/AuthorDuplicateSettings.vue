<script setup>
import { computed, onBeforeUnmount, onMounted, shallowRef } from 'vue'
import api from '@/lib/http.js'
import AuthorDuplicateProfile from './AuthorDuplicateProfile.vue'
import NeonButton from './NeonButton.vue'
import SettingsSection from './SettingsSection.vue'

const overview = shallowRef(null)
const error = shallowRef('')
const notice = shallowRef('')
const denied = shallowRef(false)
const loading = shallowRef(false)
const submitting = shallowRef(false)
const deferred = shallowRef(false)
const after = shallowRef(0)
const keepAuthorId = shallowRef(null)
const confirming = shallowRef(false)
const lifecycle = new AbortController()
let timer

const scan = computed(() => overview.value?.latest)
const comparison = computed(() => overview.value?.comparison)
const queueCount = computed(() => deferred.value ? overview.value?.deferredCount : overview.value?.pendingCount)
const hasConflict = computed(() => {
  const { first, second } = comparison.value ?? {}
  return !!first?.sourceAuthorKey && !!second?.sourceAuthorKey && first.sourceAuthorKey !== second.sourceAuthorKey
})

function resetSelection() {
  keepAuthorId.value = null
  confirming.value = false
}

function failed(cause, fallback) {
  if (cause.response?.status === 403) {
    denied.value = true
    overview.value = null
    clearTimeout(timer)
  } else {
    error.value = cause.response?.data?.detail || fallback
  }
}

async function load() {
  clearTimeout(timer)
  loading.value = true
  error.value = ''
  try {
    const config = { params: { deferred: deferred.value, after: after.value }, signal: lifecycle.signal }
    let { data } = await api.get('/api/author-duplicates', config)
    if (!data.comparison && after.value && (deferred.value ? data.deferredCount : data.pendingCount)) {
      after.value = 0
      config.params.after = 0
      data = (await api.get('/api/author-duplicates', config)).data
    }
    if (lifecycle.signal.aborted) return
    if (comparison.value?.version !== data.comparison?.version) resetSelection()
    overview.value = data
  } catch (cause) {
    if (!lifecycle.signal.aborted) failed(cause, 'Could not load duplicate author suggestions.')
  } finally {
    loading.value = false
    if (!lifecycle.signal.aborted && !denied.value && scan.value?.active)
      timer = setTimeout(load, 3000)
  }
}

function switchQueue() {
  deferred.value = !deferred.value
  after.value = 0
  notice.value = ''
  resetSelection()
  return load()
}

function next() {
  after.value = comparison.value.id
  notice.value = ''
  resetSelection()
  return load()
}

async function submit(action) {
  if (submitting.value || loading.value) return
  clearTimeout(timer)
  submitting.value = true
  notice.value = ''
  error.value = ''
  try {
    if (action === 'scan') {
      const { data } = await api.post('/api/author-duplicates/scan', {}, { signal: lifecycle.signal })
      overview.value = { ...overview.value, latest: data }
    } else {
      const { id, version } = comparison.value
      await api.post(`/api/author-duplicates/${id}/review`, {
        decision: action,
        version,
        keepAuthorId: action === 'merge' ? keepAuthorId.value : null,
      }, { signal: lifecycle.signal })
      notice.value = { merge: 'Authors merged.', reject: 'Suggestion rejected.', defer: 'Suggestion deferred.' }[action]
      resetSelection()
    }
    await load()
  } catch (cause) {
    if (lifecycle.signal.aborted) return
    if (cause.response?.status === 409) {
      resetSelection()
      await load()
    }
    failed(cause, 'Could not save this action. Please try again.')
  } finally {
    submitting.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => {
  lifecycle.abort()
  clearTimeout(timer)
})
</script>

<template>
  <SettingsSection title="Duplicate authors" aria-label="Duplicate authors" class="duplicate-settings">
    <p v-if="denied">Only catalogue managers can scan for and review duplicate authors.</p>
    <template v-else>
      <p v-if="error" role="alert">{{ error }} <button type="button" :disabled="loading || submitting" @click="load">Reload suggestions</button></p>
      <p v-if="notice" role="status">{{ notice }}</p>
      <p v-if="!overview && !error" role="status">Loading duplicate author suggestions…</p>
      <template v-if="overview">
        <div class="review-actions">
          <NeonButton type="button" :disabled="scan?.active || loading || submitting" @click="submit('scan')">Scan for duplicate authors</NeonButton>
          <NeonButton type="button" variant="soft" :disabled="loading || submitting" @click="switchQueue">
            {{ deferred ? `Show pending (${overview.pendingCount})` : `Show deferred (${overview.deferredCount})` }}
          </NeonButton>
        </div>
        <div v-if="scan" role="status">
          <p>{{ scan.active ? 'Scanning' : 'Scan complete' }} · {{ scan.processed }} / {{ scan.total }} authors · {{ scan.newSuggestions }} new suggestions</p>
          <progress v-if="scan.active" :value="scan.processed" :max="scan.total || 1" aria-label="Duplicate author scan progress" />
        </div>
        <p v-if="!comparison && !loading">No {{ deferred ? 'deferred' : 'pending' }} suggestions.</p>
        <template v-if="comparison">
          <p>{{ queueCount }} {{ deferred ? 'deferred' : 'pending' }} {{ queueCount === 1 ? 'suggestion' : 'suggestions' }}</p>
          <p>Both names match as <strong>{{ comparison.normalizedName }}</strong> after ignoring case, spaces and punctuation.
            They share {{ comparison.sharedBookCount }} {{ comparison.sharedBookCount === 1 ? 'book' : 'books' }}.</p>
          <p v-if="hasConflict">These profiles have different external author IDs. Check whether they refer to the same person.</p>
          <div class="profile-comparison">
            <AuthorDuplicateProfile :key="comparison.first.id" :profile="comparison.first" />
            <AuthorDuplicateProfile :key="comparison.second.id" :profile="comparison.second" />
          </div>
          <fieldset class="record-selection" :disabled="loading || submitting || scan?.active">
            <legend>Author record to keep</legend>
            <label v-for="profile in [comparison.first, comparison.second]" :key="profile.id" class="record-choice">
              <input v-model="keepAuthorId" type="radio" name="keep-author" :value="profile.id" @change="confirming = false" />
              {{ profile.name }} (#{{ profile.id }})
            </label>
          </fieldset>
          <div v-if="confirming" class="merge-confirmation">
            <p>Move all book credits to the selected record and delete the other record? Profile conflicts favor the selected record. Merges can be recovered from a database backup.</p>
            <div class="review-actions">
              <NeonButton type="button" variant="danger" :disabled="scan?.active || loading || submitting" @click="submit('merge')">Confirm merge</NeonButton>
              <NeonButton type="button" variant="soft" :disabled="loading || submitting" @click="confirming = false">Cancel</NeonButton>
            </div>
          </div>
          <div v-else class="review-actions">
            <NeonButton type="button" :disabled="!keepAuthorId || scan?.active || loading || submitting" @click="confirming = true">Merge authors</NeonButton>
            <NeonButton v-if="!deferred" type="button" variant="soft" :disabled="loading || submitting" @click="submit('defer')">Defer</NeonButton>
            <NeonButton type="button" variant="soft" :disabled="loading || submitting" @click="submit('reject')">Reject suggestion</NeonButton>
            <NeonButton v-if="queueCount > 1" type="button" variant="soft" :disabled="loading || submitting" @click="next">Next suggestion</NeonButton>
          </div>
        </template>
      </template>
    </template>
  </SettingsSection>
</template>

<style scoped>
.duplicate-settings { margin-top: 1rem; overflow-wrap: anywhere; }
.profile-comparison { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; }
.review-actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
.record-selection { min-width: 0; margin: 0.75rem 0; border: 1px solid var(--line); border-radius: 8px; }
.record-choice { display: block; padding: 0.25rem; }
.merge-confirmation { padding: 0.75rem; border: 1px solid var(--line); border-radius: 8px; }
@media (max-width: 640px) {
  .profile-comparison { grid-template-columns: minmax(0, 1fr); }
}
</style>
