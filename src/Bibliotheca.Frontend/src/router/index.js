// src/router/index.js
import { createRouter, createWebHistory } from 'vue-router'

// Layout
import DefaultLayout from '@/layouts/DefaultLayout.vue'

// Páginas
import DashboardPage from '@/pages/Dashboard.vue'

// Autenticação
import OidcCallback from '@/views/OidcCallback.vue'
import LogoutCallback from '@/views/LogoutCallback.vue'

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior(to) {
    if (to.hash) return { el: to.hash, top: 80 }
  },
  routes: [
    { path: '/oidc-callback', component: OidcCallback, meta: { public: true } },
    { path: '/logout-callback', component: LogoutCallback, meta: { public: true } },
    {
      path: '/',
      component: DefaultLayout,
      children: [
        { path: '', redirect: { name: 'dashboard' } },

        { path: 'dashboard',
          name: 'dashboard',
          component: DashboardPage,
          meta: { title: 'Dashboard' } },

        { path: 'settings',
          name: 'settings',
          component: () => import('@/pages/Settings.vue'),
          meta: { title: 'Settings' } 
        },

        { path: 'library',
          name: 'user-library',
          component: () => import('@/pages/LibraryDetails.vue'),
          meta: { title: 'Library' } 
        },

        { path: 'library/shelve/:name',
          name: 'user-library-shelve',
          component: () => import('@/pages/ShelveDetails.vue'),
          meta: { title: 'Shelve' } 
        },

        { path: 'books',
          name: 'all-books',
          component: () => import('@/pages/Books.vue'),
          meta: { title: 'Books' } 
        },

        { path: 'books/:id',
          name: 'book',
          component: () => import('@/pages/BookPage.vue'),
          props: true,
          meta: { title: 'Book Details' } 
        },

        {
          path: '/books/:id/select-series',
          name: 'series-select',
          component: () => import('@/pages/SeriesSelectView.vue'),
          props: true,
          meta: { title: 'Book series' } 
        },

        { path: 'authors',
          name: 'all-authors',
          component: () => import('@/pages/Authors.vue'),
          meta: { title: 'Authors' } },

        { path: 'authors/:id',
          name: 'author',
          component: () => import('@/pages/AuthorDetails.vue'),
          props: true,
          meta: { title: 'Author Details' } },

        { path: 'collections',
          name: 'all-collections',
          component: () => import('@/pages/Collections.vue'),
          meta: { title: 'Collections' } 
        },

        { path: 'collections/:id',
          name: 'collection',
          component: () => import('@/pages/CollectionDetails.vue'),
          props: true,
          meta: { title: 'Collection Details' } 
        },

        { path: 'genres',
          name: 'all-genres',
          component: () => import('@/pages/Genres.vue'),
          meta: { title: 'Genres' } 
        },

        { path: 'genres/:id',
          name: 'genre',
          component: () => import('@/pages/GenreDetails.vue'),
          props: true,
          meta: { title: 'Genre Details' } 
        },

        { path: 'series',
          name: 'all-series',
          component: () => import('@/pages/Series.vue'),
          meta: { title: 'Series' } 
        },

        { path: 'series/:id',
          name: 'series',
          component: () => import('@/pages/SeriesDetails.vue'),
          props: true,
          meta: { title: 'Series Details' } 
        },
        
        { path: 'tags',
          name: 'all-tags',
          component: () => import('@/pages/Tags.vue'),
          meta: { title: 'Tags' } 
        },

        { path: 'tags/:id',
          name: 'tag',
          component: () => import('@/pages/TagsDetails.vue'),
          props: true,
          meta: { title: 'Tag Details' } 
        },
      ],
    },

    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/pages/NotFound.vue'),
      meta: { public: true },
    },
  ],
})

// Título da página
router.afterEach((to) => {
  document.title = to.meta?.title || 'Bibliotheca'
})

export default router
