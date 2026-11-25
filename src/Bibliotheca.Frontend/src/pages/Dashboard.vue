<template>
  <main class="dashboard" aria-labelledby="dashboard-title">
    <h1 id="dashboard-title">Dashboard</h1>
    <p v-if="loading" role="status">Loading...</p>
    <p v-else-if="error" role="alert">An error occurred.</p>
    <template v-else-if="data">
      <section class="featured-book" aria-labelledby="featured-title">
        <span v-if="featured" :key="featured.id" class="cover featured-cover" aria-hidden="true">
          <font-awesome-icon icon="fa-solid fa-book" />
          <img :src="cover(featured.id)" alt="" width="104" height="156" @error="hideCover" />
        </span>
        <div>
          <h2 id="featured-title">{{ data.currentBook ? 'Reading now' : 'From your shelves' }}</h2>
          <template v-if="featured">
            <p class="book-title">{{ featured.title }}</p>
            <p v-if="featured.author" class="muted">{{ featured.author }}</p>
            <RouterLink :to="bookLink(featured.id)" class="open-book">Open book</RouterLink>
          </template>
          <p v-else class="muted">No books currently in progress or waiting to be read.</p>
        </div>
      </section>

      <aside v-if="data.unshelvedCount || data.activeImports || data.recentFailedImports" class="work" aria-label="Library tasks">
        <div v-if="data.unshelvedCount">
          <span>{{ data.unshelvedCount }} {{ data.unshelvedCount === 1 ? 'book to shelve' : 'books to shelve' }}</span>
          <RouterLink :to="{ name: 'all-books', hash: '#unshelved-books' }">Organise</RouterLink>
        </div>
        <div v-if="data.activeImports || data.recentFailedImports">
          <span v-if="data.activeImports">Imports in progress: {{ data.activeImports }}.</span>
          <span v-if="data.recentFailedImports">Imports with errors in the last 7 days: {{ data.recentFailedImports }}.</span>
          <RouterLink :to="{ name: 'settings', hash: '#isbn-imports' }">Check imports</RouterLink>
        </div>
      </aside>

      <div class="columns">
        <section class="section" aria-labelledby="unread-title">
          <h2 id="unread-title">To read</h2>
          <ul v-if="data.nextBooks?.length" class="book-list">
            <li v-for="book in data.nextBooks" :key="book.id">
              <RouterLink :to="bookLink(book.id)">
                <span class="cover thumbnail-cover" aria-hidden="true">
                  <font-awesome-icon icon="fa-solid fa-book" />
                  <img :src="cover(book.id)" alt="" width="36" height="54" loading="lazy" @error="hideCover" />
                </span>
                <span><strong>{{ book.title }}</strong><small v-if="book.shelf">{{ book.shelf }}</small></span>
              </RouterLink>
            </li>
          </ul>
          <p v-else class="muted">No unread books in your library.</p>
          <RouterLink :to="{ name: 'user-library', hash: '#unread-books' }">See my unread books</RouterLink>
        </section>

        <section class="section" aria-labelledby="year-title">
          <h2 id="year-title">My reading year</h2>
          <div class="year">
            <SingleStatPanel featured title="Read this year" :value="data.currentYearReadBooks.count" :formatter="format" />
            <ol class="chart" aria-label="Books finished by month">
              <li v-for="(month, index) in months" :key="month" :aria-label="month + ': ' + monthly[index]"
                :title="month + ': ' + monthly[index]">
                <span class="bar-track" aria-hidden="true"><span class="bar" :style="{ '--height': (100 * monthly[index] / maximum) + '%' }"></span></span>
                <span aria-hidden="true">{{ month }}</span>
              </li>
            </ol>
          </div>
          <p class="muted totals">{{ format(data.readBooks.count) }} total read · {{ format(data.totalPagesRead.count) }} pages read</p>
        </section>

        <section class="section" aria-labelledby="activity-title">
          <h2 id="activity-title">Recent reading</h2>
          <ol v-if="data.activity?.length" class="timeline">
            <li v-for="entry in data.activity" :key="entry.id + entry.kind">
              <time :datetime="entry.date">{{ formatDate(entry.date) }}</time>
              <span class="muted">{{ entry.kind === 'favorite' ? 'Added to favourites' : 'Finished' }} · </span><RouterLink :to="bookLink(entry.id)">{{ entry.title }}</RouterLink>
            </li>
          </ol>
          <p v-else class="muted">No dated reading activity yet.</p>
        </section>

        <section class="section" aria-labelledby="shelf-title">
          <h2 id="shelf-title">My shelves</h2>
          <ul v-if="data.shelves?.length" class="shelves">
            <li v-for="shelf in data.shelves" :key="shelf.name">
              <RouterLink :to="{ name: 'user-library-shelve', params: { name: shelf.name } }">
                <font-awesome-icon :icon="/^favou?rites$|^favoritos$/i.test(shelf.name) ? 'fa-solid fa-heart' : 'fa-solid fa-book'" aria-hidden="true" />
                {{ shelf.name }} <span class="muted">({{ shelf.count }})</span>
              </RouterLink>
            </li>
          </ul>
          <p v-else class="muted">No shelves yet.</p>
        </section>
      </div>
    </template>
  </main>
</template>

<script setup>
import { computed } from 'vue'
import SingleStatPanel from '@/components/charts/SingleStatPanel.vue'
import { getDashboardData } from '@/composables/useDashboard.js'
import { getApiBase } from '@/lib/getApiBase'

const { data, loading, error } = getDashboardData()
const featured = computed(() => data.value?.currentBook ?? data.value?.nextBooks?.[0])
const numberFormat = new Intl.NumberFormat('pt-PT', { useGrouping: 'always' })
const format = value => numberFormat.format(value)
const formatDate = value => new Intl.DateTimeFormat('en', { dateStyle: 'medium' }).format(new Date(value))
const bookLink = id => ({ name: 'book', params: { id } })
const cover = id => getApiBase() + '/api/books/' + id + '/cover'
const hideCover = event => { event.currentTarget.hidden = true }
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const monthly = computed(() => data.value?.readByMonth ?? Array(12).fill(0))
const maximum = computed(() => Math.max(1, ...monthly.value))
</script>

<style scoped>
.dashboard { max-width: 1120px; margin: auto; padding: clamp(1rem, 4vw, 2rem); }
h1 { margin: 0 0 1.25rem; font-size: clamp(1.65rem, 4vw, 2rem); }
h2 { margin: 0 0 .75rem; font-size: 1.15rem; }
a { color: var(--brand-glow); }
.muted, small { color: var(--sub, #aebbd0); }
.featured-book { display: flex; align-items: center; gap: 1.25rem; border: 1px solid var(--line); border-radius: 14px; background: var(--panel); padding: 1rem; }
.cover { position: relative; display: grid; place-items: center; flex: none; overflow: hidden; border-radius: 6px; background: var(--line); color: var(--sub); }
.featured-cover { width: 104px; height: 156px; }
.thumbnail-cover { width: 36px; height: 54px; }
.cover img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
.featured-book p { margin: .35rem 0; }
.book-title { font-size: clamp(1.05rem, 3vw, 1.5rem); font-weight: 700; overflow-wrap: anywhere; }
.open-book { display: inline-block; margin-top: .55rem; }
.work { padding: .65rem .85rem; margin: 1rem 0; border-inline-start: 3px solid var(--brand); background: var(--panel); }
.work div { display: flex; flex-wrap: wrap; gap: .45rem 1rem; padding: .25rem 0; }
.columns { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.25rem 2rem; margin-top: 1.25rem; }
.section { min-width: 0; border-top: 1px solid var(--line); padding-top: .9rem; }
ul, ol { margin: 0 0 .7rem; padding: 0; list-style: none; }
.book-list li + li { border-top: 1px solid var(--line); }
.book-list a { display: flex; align-items: center; gap: .7rem; padding: .4rem 0; }
.book-list small { display: block; }
.year { display: grid; grid-template-columns: minmax(105px, 1fr) minmax(0, 2fr); gap: .85rem; align-items: stretch; }
.chart { display: grid; grid-template-columns: repeat(12, minmax(0, 1fr)); gap: 3px; margin: 0; }
.chart li { display: grid; align-content: end; gap: 4px; text-align: center; font-size: clamp(.5rem, 1.15vw, .68rem); }
.bar-track { display: flex; align-items: end; height: 70px; background: var(--panel); border-bottom: 1px solid var(--line); }
.bar { width: 100%; height: var(--height); background: var(--brand); border-radius: 3px 3px 0 0; }
.totals { font-size: .85rem; }
.timeline { border-inline-start: 1px solid var(--line); padding-inline-start: 1rem; }
.timeline li { padding: .3rem 0; }
.timeline time { display: block; font-size: .8rem; color: var(--sub, #aebbd0); }
.shelves { display: flex; flex-wrap: wrap; gap: .55rem 1rem; }
@media (max-width: 768px) {
  .columns { grid-template-columns: minmax(0, 1fr); }
  .featured-book { gap: .85rem; }
  .featured-cover { width: 64px; height: 96px; }
}
</style>
