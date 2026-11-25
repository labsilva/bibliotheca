<template>
  <div class="settings-page">
    <div class="settings-page__card">
      <header class="settings-page__header">
        <div>
          <h1 class="settings-page__title">Settings</h1>
          <p class="settings-page__subtitle">
            Configure your Bibliotheca experience
          </p>
        </div>
      </header>

      <SettingsTabs v-model="activeTab" :tabs="tabs" />

      <main class="settings-page__content">
        
        <template v-if="activeTab === 'dashboard'">
          <SettingsSection
            title="Dashboard Preferences"
            subtitle="Configure user Dashboard"
          >
            <SettingsSection
              title="Dashboard Settings Scope"
              subtitle="Choose whether to apply the same reader settings globally or individually per book."
            >
              <div class="settings-page__subgroup">
                <div class="settings-page__subgroup-title">
                  Charts
                </div>
                <span class="settings-page__chips">Global / Individual</span>
              </div>

              <div class="settings-page__subgroup">
                <div class="settings-page__subgroup-title">
                  EPUB Reader Settings
                </div>
                <span class="settings-page__chips">Global / Individual</span>
              </div>
            </SettingsSection>
          </SettingsSection>

          <SettingsSection
            title="Search & Recommendations"
            subtitle="Control automatic metadata search and similar book suggestions."
          >
            <SettingsToggle
              v-model="autoSearch"
              label="Auto Book Search"
              description="Automatically attempts metadata matching when the book information panel is opened."
            />

            <SettingsToggle
              v-model="similarRecommendations"
              label="Similar Book Recommendation"
              description="Enables or disables similar book recommendations based on your library."
            />
          </SettingsSection>
        </template>

        <template v-else-if="activeTab === 'appearance'">
          <SettingsSection
            title="Appearance"
            subtitle="Themes, fonts e outras opções visuais.">
          </SettingsSection>
        </template>

        <template v-else-if="activeTab === 'library'">
          <SettingsSection
            title="Library"
            subtitle="Personal library related settings">
            <template #icon>
             <AppIcon icon="shelf-books" />
            </template>

            <SettingsToggle
              v-model="libraryShowReadingBooks"
              label="Show Reading Books"
              description="Shows books in your library that are currently being read."
            />
            <SettingsNumber
                v-model="libraryReadingBooksCount"
                label="Reading books count"
                description="How many reading books should be displayed."
                :min="4"
                :max="32"
                :step="1"
                suffix=""
            />
            <SettingsToggle
              v-model="libraryShowUnreadBooks"
              label="Show Unread Books"
              description="Shows books in your library that are currently unread."
            />
            <SettingsNumber
                v-model="libraryUnreadBooksCount"
                label="Unread books count"
                description="How many reading books should be displayed."
                :min="4"
                :max="32"
                :step="1"
                suffix=""
            />
            <SettingsToggle
              v-model="libraryShowReadBooks"
              label="Show Read Books"
              description="Shows books in your library that are already read."
            />
            <SettingsNumber
                v-model="libraryReadBooksCount"
                label="Read books count"
                description="How many reading books should be displayed."
                :min="4"
                :max="32"
                :step="1"
                suffix=""
            />
            <SettingsToggle
              v-model="libraryShowShelves"
              label="Show Shelves"
              description="Shows all shelves in your library."
            />
          </SettingsSection>
          <SettingsSection
            id="isbn-upload"
            title="ISBN File Upload"
            subtitle="Upload a file with ISBNs in the first column.">
            <template #icon>
              <font-awesome-icon icon="fa-solid fa-upload" />
            </template>
            <FileUpload endpoint="/api/library/_upload" accept=".txt, .csv, .tsv" @queued="recordImport" />
          </SettingsSection>
          <SettingsSection title="Title search providers">
            <ExternalProviderSettings />
          </SettingsSection>
        </template>

        <template v-else-if="activeTab === 'imports'">
          <SettingsSection id="isbn-imports" title="Import status" subtitle="Track your book imports and retry failures.">
            <ImportHistory />
          </SettingsSection>
        </template>

        <template v-else-if="activeTab === 'database'">
          <CatalogueRefreshSettings />
          <AuthorRefreshSettings />
          <AuthorDuplicateSettings />
          <SettingsSection
            title="Database"
            subtitle="Database related settings">
            <template #icon>
              <font-awesome-icon icon="fa-solid fa-database" />
            </template>

            <SettingsSection
              title="Import & Export"
              subtitle="Database related settings">
              <div>
                <NeonButton @click="save">
                  <template #icon>
                    <font-awesome-icon icon="fa-solid fa-download" />
                  </template>
                  Export
                </NeonButton>
                <NeonButton @click="restore">
                  <template #icon>
                    <font-awesome-icon icon="fa-solid fa-upload" />
                  </template>
                  Import
                </NeonButton>
              </div>
            </SettingsSection>
          </SettingsSection>
        </template>

        <template v-else-if="activeTab === 'users'">
          <UserDirectory />
          <SettingsSection
            title="Users"
            subtitle="User management settings">
            <template #icon>
              <font-awesome-icon icon="fa-solid fa-user" />
            </template>

            <SettingsSection
              v-if="canInvite"
              title="New user"
              subtitle="New user onboarding">
              <div>
                <NeonButton @click="createNewInviteCode">
                  <template #icon>
                    <font-awesome-icon icon="fa-solid fa-user-plus" />
                  </template>
                  New user
                </NeonButton>
                <div style="color: var(--brand-glow)" v-if="newInviteCode">New invite code generated, new user can use it at {{ newInviteCode }} </div>
              </div>
            </SettingsSection>
          </SettingsSection>
        </template>
      </main>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useSettingsStore } from '@/stores/settingsStore.js'
import SettingsTabs from '@/components/settings/SettingsTabs.vue'
import SettingsSection from '@/components/settings/SettingsSection.vue'
import SettingsToggle from '@/components/settings/SettingsToggle.vue'
import SettingsNumber from '@/components/settings/SettingsNumber.vue'
import SettingsLabel from '@/components/settings/SettingsLabel.vue'
import NeonButton from '@/components/settings/NeonButton.vue'
import AppIcon from '@/components/AppIcon.vue'
import { getApiBase } from '@/lib/getApiBase'
import { getInviteCode } from '@/composables/useInviteCodes'
import api from '@/lib/http.js'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import FileUpload from '@/components/FileUpload.vue'
import { recordImport } from '@/composables/useImportHistory'
import ImportHistory from '@/components/ImportHistory.vue'
import ExternalProviderSettings from '@/components/settings/ExternalProviderSettings.vue'
import CatalogueRefreshSettings from '@/components/settings/CatalogueRefreshSettings.vue'
import AuthorRefreshSettings from '@/components/settings/AuthorRefreshSettings.vue'
import AuthorDuplicateSettings from '@/components/settings/AuthorDuplicateSettings.vue'
import UserDirectory from '@/components/settings/UserDirectory.vue'

const tabs = [
    { id: 'library', label: 'Library', icon: {
      component: AppIcon,
      props: { icon: 'shelf-books', size: '1.4rem' }
    } },
    { id: 'imports', label: 'Imports', icon: 'fa-solid fa-upload' },
    { id: 'database', label: 'Database', icon: {
      component: FontAwesomeIcon,
      props: { icon: 'fa-solid fa-database' }
    } },
    { id: 'users', label: 'Users', icon: 'fa-solid fa-users' },
    // { id: 'dashboard', label: 'Dashboard' },
    // { id: 'appearance', label: 'Appearance' },
]

const store = useSettingsStore()
const canInvite = ref(false)

onMounted(async () => {
  store.initialize()
  try {
    canInvite.value = (await api.get('/api/account/permissions')).data?.canInvite === true
  } catch {
    canInvite.value = false
  }
})

const { inviteCode, loading: inviteCodeLoading, error: inviteCodeError, fetchInviteCode } = getInviteCode();
const newInviteCode = ref(null)
async function createNewInviteCode() {
  newInviteCode.value = await getNewInviteCode();
}

async function getNewInviteCode() {
  await fetchInviteCode()
  if (!inviteCode.value?.inviteCode) return null
  var base = getApiBase();
  return base + "/account/register#inviteCode=" + encodeURIComponent(inviteCode.value.inviteCode);
}

const loaded = computed(() => store.loaded)
const syncing = computed(() => store.syncing)
const dirty = computed(() => store.dirty)
const error = computed(() => store.error)

const route = useRoute()
const activeTab = ref('library')
watch(() => route?.hash, hash => { if (hash === '#isbn-imports') activeTab.value = 'imports' }, { immediate: true })

const libraryShowReadingBooks = computed({
  get: () => store.library.showReadingBooks,
  set: v => (store.library.showReadingBooks = v)
})

const libraryReadingBooksCount = computed({
  get: () => store.library.readingBooksCount,
  set: v => (store.library.readingBooksCount = v)
})

const libraryShowUnreadBooks = computed({
  get: () => store.library.showUnreadBooks,
  set: v => (store.library.showUnreadBooks = v)
})

const libraryUnreadBooksCount = computed({
  get: () => store.library.unreadBooksCount,
  set: v => (store.library.unreadBooksCount = v)
})

const libraryShowReadBooks = computed({
  get: () => store.library.showReadBooks,
  set: v => (store.library.showReadBooks = v)
})

const libraryReadBooksCount = computed({
  get: () => store.library.readBooksCount,
  set: v => (store.library.readBooksCount = v)
})

const libraryShowShelves = computed({
  get: () => store.library.showShelves,
  set: v => (store.library.showShelves = v)
})

</script>

<style scoped>
.settings-page {
  box-sizing: border-box;
  min-width: 0;
  min-height: 100%;
  padding: 24px;
  background: radial-gradient(circle at top, #020617 0, #02010a 45%, #000 100%);
}

.settings-page__card {
  box-sizing: border-box;
  min-width: 0;
  max-width: 1000px;
  margin: 0 auto;
  border-radius: 20px;
  border: 1px solid rgba(0, 229, 255, 0.25);
  background: radial-gradient(circle at top left, #020617 0, #020617 40%, #02010a 100%);
  box-shadow:
    0 0 40px rgba(0, 229, 255, 0.12),
    0 0 120px rgba(56, 189, 248, 0.08);
  padding: 16px 16px 20px;
  color: #e5f3ff;
}

.settings-page__header {
  padding: 8px 4px 8px 4px;
}

.settings-page__title {
  font-size: 22px;
  font-weight: 600;
}

.settings-page__subtitle {
  margin-top: 4px;
  font-size: 13px;
  color: #9ca3af;
}

.settings-page__content {
  margin-top: 8px;
}

.settings-page__subgroup {
  padding: 10px 0 4px;
  border-top: 1px solid rgba(15, 23, 42, 0.9);
}

.settings-page__subgroup:first-of-type {
  border-top: 0;
}

.settings-page__subgroup-title {
  font-size: 14px;
  font-weight: 500;
  color: #dbeafe;
  margin-bottom: 4px;
}

.settings-page__chips {
  font-size: 12px;
  color: #9ca3af;
}
@media (max-width: 640px) {
  .settings-page { padding: 8px; }
  .settings-page__card { padding: 12px; }
  .settings-page__content { min-width: 0; overflow-wrap: anywhere; }
}
</style>
