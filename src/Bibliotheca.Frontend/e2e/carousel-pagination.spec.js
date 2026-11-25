import { expect, test } from '@playwright/test'
test('carousel loads the next page when the reader reaches its end', async ({ page, baseURL }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/__test__/blank')
  await page.evaluate(origin => sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
    access_token: 'token', token_type: 'Bearer', profile: { sub: 'reader', name: 'reader' },
    expires_at: Math.floor(Date.now() / 1000) + 3600,
  })), baseURL)
  await page.route('**/api/account/settings', route => route.fulfill({ json: {} }))
  let nextRequests = 0
  await page.route('**/api/books?**', route => {
    const url = new URL(route.request().url())
    if (url.searchParams.get('list') !== 'recent') return route.fulfill({ json: { total: 0, items: [] } })
    const next = url.searchParams.get('page') === '1'
    if (next) nextRequests++
    return route.fulfill({ json: { total: 21, items: next ? [{ id: 21, title: 'Last book' }] :
      Array.from({ length: 20 }, (_, i) => ({ id: i + 1, title: `Book ${i + 1}` })) } })
  })
  await page.goto('/books')
  const carousel = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Recently Added' }) })
  await expect(carousel.getByText('Book 20', { exact: true })).toBeAttached()
  await carousel.locator('.rail').evaluate(el => { el.scrollLeft = el.scrollWidth })
  await expect(carousel.getByText('Last book')).toBeAttached()
  expect(nextRequests).toBe(1)
})

async function signInFixture(page, baseURL) {
  await page.goto('/__test__/blank')
  await page.evaluate(origin => sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
    access_token: 'token', token_type: 'Bearer', profile: { sub: 'reader', name: 'reader' },
    expires_at: Math.floor(Date.now() / 1000) + 3600,
  })), baseURL)
  await page.route('**/api/account/settings', route => route.fulfill({ json: { library: {
    showReadingBooks: false, showUnreadBooks: false, showReadBooks: false, showShelves: true,
  } } }))
}

const firstPage = Array.from({ length: 12 }, (_, index) => ({
  id: index + 1, title: `First-page book ${index + 1}`,
}))

test('author books retain the first page after a failed request and retry on mobile', async ({ page, baseURL }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await signInFixture(page, baseURL)
  const pages = []
  await page.route(url => url.pathname === '/api/authors/7', route => {
    const url = new URL(route.request().url())
    const index = Number(url.searchParams.get('page'))
    pages.push(index)
    expect(url.searchParams.get('size')).toBe('12')
    if (pages.length === 2) return route.fulfill({ status: 503 })
    return route.fulfill({ json: {
      id: 7, name: 'An author', booksTotal: 13,
      books: index === 0 ? firstPage : [{ id: 13, title: 'Last author book' }],
    } })
  })

  await page.goto('/authors/7')
  await expect(page.getByRole('heading', { name: 'An author' })).toBeVisible()
  const lastLoaded = page.getByRole('link', { name: 'First-page book 12' })
  await lastLoaded.focus()
  await expect(page.getByRole('alert')).toContainText('Could not load more books')
  await expect(lastLoaded).toBeAttached()
  await page.getByRole('button', { name: 'Try again' }).press('Enter')
  await expect(page.getByText('Last author book')).toBeAttached()
  expect(pages).toEqual([0, 1, 1])
})

test('library shelves load independent pages on desktop', async ({ page, baseURL }) => {
  await signInFixture(page, baseURL)
  await page.route(url => url.pathname === '/api/library', route => route.fulfill({ json: { libraries: [] } }))
  await page.route(url => url.pathname === '/api/library/shelves', route => route.fulfill({ json: {
    shelves: [{ shelve: 'Classics' }, { shelve: 'Fiction' }],
  } }))
  const requests = []
  await page.route(url => /^\/api\/library\/shelves\/[^/]+\/books$/.test(url.pathname), route => {
    const url = new URL(route.request().url())
    const shelf = url.pathname.split('/')[4]
    const index = Number(url.searchParams.get('page'))
    requests.push([shelf, index])
    return route.fulfill({ json: {
      total: 13, items: index === 0 ? firstPage : [{ id: 13, title: `Last ${shelf} book` }],
    } })
  })
  await page.goto('/library')
  const classics = page.getByRole('region', { name: 'Classics', exact: true })
  const fiction = page.getByRole('region', { name: 'Fiction', exact: true })
  await expect(fiction.getByText('First-page book 12', { exact: true })).toBeAttached()
  await classics.getByRole('link', { name: 'First-page book 12' }).focus()
  await expect(classics.getByText('Last Classics book')).toBeAttached()
  expect(requests.filter(([, index]) => index > 0)).toEqual([['Classics', 1]])
  await fiction.getByRole('link', { name: 'First-page book 12' }).focus()
  await expect(fiction.getByText('Last Fiction book')).toBeAttached()
  expect(requests.filter(([, index]) => index > 0)).toEqual([['Classics', 1], ['Fiction', 1]])
})
