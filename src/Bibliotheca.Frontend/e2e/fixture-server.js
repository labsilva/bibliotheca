import { preview } from 'vite'
import { handleIdentityProvider } from './identity-provider-fixture.js'

const baseURL = 'http://127.0.0.1:4180'
const importAttempts = new Map()
const stats = Object.fromEntries(
  ['currentYearReadBooks', 'readBooks', 'unreadBooks', 'readingBooks', 'totalPagesRead']
    .map(key => [key, { title: key, count: 0 }]),
)

function json(res, body, status = 200) {
  res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' })
  res.end(JSON.stringify(body))
}

await preview({
  configFile: false,
  build: { outDir: 'dist-e2e' },
  preview: { host: '127.0.0.1', port: 4180, strictPort: true },
  plugins: [{
    name: 'bibliotheca-browser-fixtures',
    configurePreviewServer(server) {
      server.middlewares.use((req, res, next) => {
        const url = new URL(req.url, baseURL)
        const path = url.pathname
        if (path === '/.well-known/openid-configuration' || path === '/account/login' || path.startsWith('/connect/'))
          return handleIdentityProvider(req, res, url, json).catch(next)
        if (path === '/__test__/ready') return json(res, { ready: true })
        if (path === '/__test__/blank') {
          res.writeHead(200, { 'content-type': 'text/html' })
          return res.end('<!doctype html><title>Browser test fixture</title>')
        }
        if (path === '/config.js') {
          res.writeHead(200, { 'content-type': 'text/javascript' })
          return res.end('window.__APP_CONFIG__ = { API_URL: ' + JSON.stringify(baseURL) + ' }')
        }
        if (path === '/api/dashboard') return json(res, stats)
        if (path === '/api/library') return json(res, { libraries: [] })
        if (path === '/api/library/shelves') return json(res, { shelves: [] })
        if (path === '/api/account/settings') {
          return json(res, { library: { showReadingBooks: false, showUnreadBooks: false } })
        }
        if (path === '/api/library/books') {
          const records = {
            'Bearer first-token': { id: 101, title: 'First reader’s private book' },
            'Bearer second-token': { id: 102, title: 'Second reader’s private book' },
          }
          const record = records[req.headers.authorization]
          return json(res, { items: record ? [{ ...record, status: 'Read' }] : [] })
        }
        if (path === '/api/search') {
          return json(res, url.searchParams.get('text') === 'Matilda'
            ? [{ area: 'Book', id: '42', title: 'Matilda' }]
            : [])
        }
        if (path === '/api/books/42') {
          return json(res, { book: { bookId: 42, title: 'Matilda', authors: [] }, shelves: [] })
        }
        if (path === '/api/books/fetch' && req.method === 'POST') {
          const isbn = url.searchParams.get('isbn')
          const attempts = importAttempts.get(isbn) ?? 0
          importAttempts.set(isbn, attempts + 1)
          return json(res, {}, attempts === 0 ? 503 : 200)
        }
        if (path.startsWith('/api/')) return json(res, {}, 404)
        next()
      })
    },
  }],
})
