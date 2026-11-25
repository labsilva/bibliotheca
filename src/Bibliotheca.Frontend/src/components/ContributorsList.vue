<!-- src/components/ContributorsList.vue -->
<template>
  <div>
    <div v-if="!hasContributors" class="muted">{{ emptyText }}</div>

    <div v-else class="contributors">
      <div
        v-for="c in contributors"
        :key="c[idKey] || c[nameKey] || JSON.stringify(c)"
        class="contributor"
      >
        <router-link
          v-if="routeName && c[idKey]"
          :to="{ name: routeName, params: { id: c[idKey] } }"
          class="contributor-link"
        >
          <AuthorThumbnail class="avatar" :name="c[nameKey] || ''" :url="c.photoUrl" />
          <div>
            <div class="name">{{ c[nameKey] }}</div>
            <div class="role">{{ c[roleKey] || defaultRole }}</div>
          </div>
        </router-link>

        <!-- Fallback sem router-link -->
        <div v-else class="contributor-link no-link">
          <AuthorThumbnail class="avatar" :name="c[nameKey] || ''" :url="c.photoUrl" />
          <div>
            <div class="name">{{ c[nameKey] }}</div>
            <div class="role">{{ c[roleKey] || defaultRole }}</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import AuthorThumbnail from '@/components/AuthorThumbnail.vue'

const props = defineProps({
  /** Array de objetos de contribuidores (ex.: [{ authorId, authorName, role }]) */
  contributors: { type: Array, default: () => [] },

  /** Nome da propriedade com o ID (para o router-link) */
  idKey: { type: String, default: 'authorId' },

  /** Nome da propriedade com o nome a mostrar */
  nameKey: { type: String, default: 'authorName' },

  /** Nome da propriedade com a função/“role” */
  roleKey: { type: String, default: 'role' },

  /** Nome da rota para o router-link; se vazio, não cria link */
  routeName: { type: String, default: 'author' },

  /** Texto a mostrar quando não há contribuidores */
  emptyText: { type: String, default: '—' },

  /** Role por omissão quando não há valor */
  defaultRole: { type: String, default: 'Author' }
})

const hasContributors = computed(() => Array.isArray(props.contributors) && props.contributors.length > 0)

</script>

<style scoped>
.muted { color: rgba(255,255,255,0.6); font-style: italic; }

.contributors {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.contributor { display: block; }

.contributor-link {
  display: flex;
  align-items: center;
  gap: 12px;
  text-decoration: none;
  color: inherit;
  transition: color 0.2s ease, background 0.2s ease;
  border-radius: 10px;
  padding: 4px 6px;
}

.contributor-link:hover {
  background: rgba(255, 255, 255, 0.05);
}

.contributor-link:hover .avatar {
  background: rgba(255, 255, 255, 0.2);
}

.contributor-link:hover .name {
  color: #ffffff;
}

.contributor-link.no-link { cursor: default; }

.avatar {
  width: 40px;
  height: 40px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: bold;
  color: rgba(255, 255, 255, 0.9);
  transition: background 0.2s ease;
}

.name {
  font-weight: 500;
  color: rgba(255, 255, 255, 0.9);
  transition: color 0.2s ease;
}

.role {
  color: rgba(255, 255, 255, 0.7);
  font-size: 0.9rem;
}
</style>
