import { expect, test } from '@playwright/test'

const sessionKey = 'oidc.user:http://127.0.0.1:4180:bibliotheca-e2e'

async function setSession(page, account) {
  await page.evaluate(({ key, account }) => {
    sessionStorage.setItem(key, JSON.stringify({
      access_token: account + '-token',
      token_type: 'Bearer',
      profile: { sub: account, name: account },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    }))
  }, { key: sessionKey, account })
}

test.beforeEach(async ({ page }) => {
  await page.goto('/__test__/blank')
  await setSession(page, 'first')
})

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`title search queues the selected edition at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await page.route('**/api/external-editions/search?**', route => {
      const query = new URL(route.request().url()).searchParams
      expect(query.get('title')).toBe('Matilda')
      expect(query.get('author')).toBe('Roald Dahl')
      return route.fulfill({ json: {
        failedProviders: ['googleBooks'],
        items: [
          {
            externalId: 'OL1M', title: 'Matilda', author: 'Roald Dahl', language: 'eng', publisher: 'Puffin', year: 1988,
            sources: [{ providerId: 'openLibrary', externalId: 'OL1M', name: 'Open Library', url: 'https://openlibrary.org/books/OL1M' }],
          },
          {
            externalId: 'OL2M', title: 'Matilda (English edition)', bookId: 42,
            sources: [{ providerId: 'openLibrary', externalId: 'OL2M', name: 'Open Library' }],
          },
        ],
      } })
    })
    await page.route('**/api/library/shelves', route => route.fulfill({ json: { shelves: [{ shelveId: 1, shelve: 'Favorites' }] } }))
    await page.route('**/api/external-editions/import', route => {
      expect(route.request().postDataJSON()).toEqual({ providerId: 'openLibrary', externalId: 'OL1M', shelfId: 1 })
      return route.fulfill({ status: 202, json: { status: 'queued' } })
    })
    await page.goto('/dashboard')
    const add = page.getByRole('button', { name: 'Add book', exact: true })
    await add.press('Enter')
    const dialog = page.getByRole('dialog', { name: 'Add book' })
    await dialog.getByRole('button', { name: 'Search by title' }).press('Enter')
    await dialog.getByLabel('Title', { exact: true }).fill('Matilda')
    await dialog.getByLabel('Author (optional)').fill('Roald Dahl')
    await dialog.getByLabel('Author (optional)').press('Enter')
    await expect(dialog.getByText('Some providers did not respond. Showing available results.')).toBeVisible()
    await expect(dialog.getByRole('link', { name: 'Open Library' })).toHaveAttribute('href', 'https://openlibrary.org/books/OL1M')
    await expect(dialog.getByRole('link', { name: 'Open book' })).toHaveAttribute('href', '/books/42')
    await dialog.getByRole('button', { name: 'Choose edition' }).press('Enter')
    await dialog.getByLabel('Destination shelf').selectOption('1')
    await dialog.getByRole('button', { name: 'Add edition' }).press('Enter')
    await expect(page.getByText('Book queued for import.')).toBeVisible()
    await expect(dialog).toHaveCount(0)
    await expect(add).toBeFocused()
  })
}

test('settings shows provider readiness in the mobile PWA and handles offline mode', async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.route('**/api/external-editions/providers', route => route.fulfill({ json: [
    { id: 'openLibrary', name: 'Open Library', state: 'active', available: true },
    { id: 'googleBooks', name: 'Google Books', state: 'requiresConfiguration', available: false },
    { id: 'fixture', name: 'Disabled fixture', state: 'disabled', available: false },
  ] }))
  await page.goto('/settings')
  await expect(page.getByRole('heading', { name: 'Title search providers' })).toBeVisible()
  await expect(page.getByText('Active · Available')).toBeVisible()
  await expect(page.getByText('Needs configuration')).toBeVisible()
  await expect(page.getByText('Disabled', { exact: true })).toBeVisible()
  await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)
  await page.unroute('**/api/external-editions/providers')
  await context.setOffline(true)
  await page.reload()
  await expect(page.getByText('Could not load search providers.')).toBeVisible()
})

test('the homepage connects personal reading, tasks and the reading year', async ({ page }) => {
  const stats = {
    currentYearReadBooks: { count: 2 }, readBooks: { count: 79 }, totalPagesRead: { count: 42715 },
    currentBook: { id: 42, title: 'Matilda', author: 'Roald Dahl' },
    nextBooks: [{ id: 43, title: 'Next book', shelf: 'Favorites' }],
    unshelvedCount: 2, activeImports: 1, recentFailedImports: 1,
    readByMonth: [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    activity: [{ id: 42, title: 'Matilda', date: '2026-01-12T12:00:00' }],
    shelves: [{ name: 'Favorites', count: 1 }],
  }
  await page.route('**/api/dashboard', route => route.fulfill({ json: stats }))
  await page.goto('/dashboard')
  await expect(page.getByRole('link', { name: 'Open book' })).toHaveAttribute('href', '/books/42')
  await expect(page.getByRole('link', { name: 'Organise' })).toHaveAttribute('href', '/books#unshelved-books')
  await expect(page.getByRole('link', { name: 'See my unread books' })).toHaveAttribute('href', '/library#unread-books')
  await expect(page.getByRole('list', { name: 'Books finished by month' }).getByRole('listitem')).toHaveCount(12)
  await expect(page.locator('.timeline time')).toHaveAttribute('datetime', stats.activity[0].date)
  await expect(page.locator('.shelves').getByRole('link', { name: 'Favorites (1)' })).toBeVisible()

  const screenshotDir = process.env.BIBLIOTHECA_SCREENSHOTS
  if (screenshotDir) await page.screenshot({ path: screenshotDir + '/homepage-desktop.png', fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.reload()
  await expect(page.getByRole('button', { name: 'Fechar menu lateral' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Open book' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  if (screenshotDir) await page.screenshot({ path: screenshotDir + '/homepage-mobile.png', fullPage: true })
})

test('rating preview, keyboard selection and Escape respect persisted reading data', async ({ page }) => {
  let rating = 2
  await page.route('**/api/books/42', route => route.fulfill({ json: {
    book: { bookId: 42, title: 'Matilda', authors: [] },
    userBook: { rating, readingStatus: 'Read', shelve: { shelveName: 'Favorites' } },
    shelves: [{ shelveName: 'Favorites' }],
  } }))
  await page.route('**/api/books/42/rating', route => {
    rating = route.request().postDataJSON().rating
    return route.fulfill({ json: {} })
  })
  await page.goto('/books/42')
  const stars = page.getByRole('radiogroup').getByRole('radio')
  await stars.nth(3).hover()
  await expect(page.locator('.star-btn.active')).toHaveCount(4)
  expect(rating).toBe(2)
  const reading = page.getByRole('button', { name: 'Read ▼' })
  await reading.hover()
  await expect(page.locator('.star-btn.active')).toHaveCount(2)
  await expect(stars.nth(1)).toHaveAttribute('tabindex', '0')
  await stars.nth(1).focus()
  await stars.nth(1).press('ArrowRight')
  await expect(stars.nth(2)).toBeFocused()
  await stars.nth(2).press('Enter')
  await expect(stars.nth(2)).toHaveAttribute('aria-checked', 'true')
  await page.reload()
  await expect(stars.nth(2)).toHaveAttribute('aria-checked', 'true')
  await reading.press('ArrowDown')
  await expect(page.getByRole('option', { name: 'Read', exact: true })).toBeFocused()
  await page.getByRole('option', { name: 'Read', exact: true }).press('Escape')
  await expect(page.getByRole('listbox', { name: 'Reading status' })).toHaveCount(0)
  await expect(reading).toBeFocused()
  await expect(reading).toHaveAttribute('aria-expanded', 'false')
})

test.describe('reading date', () => {
  test.use({ timezoneId: 'America/Los_Angeles' })

  test('defaults to the local day and keeps the chosen date after reload on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.clock.setFixedTime(new Date('2026-11-21T02:00:00Z'))
    let readingStatus = 'Unread'
    let readingDate = null
    await page.route('**/api/books/42', route => route.fulfill({ json: {
      book: { bookId: 42, title: 'Matilda', authors: [] },
      userBook: { readingStatus, shelve: { shelveName: 'Favorites' } },
      shelves: [{ shelveName: 'Favorites' }], readingDate,
    } }))
    await page.route('**/api/books/42/status', route => {
      const payload = route.request().postDataJSON()
      readingStatus = payload.status
      readingDate = payload.readingDate
      return route.fulfill({ status: 200 })
    })
    await page.goto('/books/42')

    const status = page.getByRole('button', { name: 'Unread ▼' })
    const date = page.getByLabel('Reading date')
    async function markRead() {
      await status.press('ArrowDown')
      await page.getByRole('option', { name: 'Read', exact: true }).press('Enter')
      await expect(date).toHaveValue('2026-11-20')
      await expect(date).toBeFocused()
    }

    await markRead()
    await date.fill('2023-04-10')
    await date.press('Escape')
    await expect(date).toHaveCount(0)
    await expect(status).toBeFocused()
    expect(readingStatus).toBe('Unread')
    expect(readingDate).toBeNull()

    await markRead()
    await date.fill('2024-02-29')
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.locator('time[datetime="2024-02-29"]')).toHaveText('2024-02-29')
    expect(readingStatus).toBe('Read')
    expect(readingDate).toBe('2024-02-29')
    await page.reload()
    await expect(page.locator('time[datetime="2024-02-29"]')).toHaveText('2024-02-29')
  })
})

test('registered user statistics fit the mobile settings page', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.route('**/api/users?**', route => route.fulfill({ json: {
    items: [{
      id: 1, username: 'reader', fullName: 'Example Reader',
      email: 'a.long.account.email.address@an-example-domain.invalid',
      bookCount: 4, read: 1, reading: 1, unread: 2,
    }], total: 1, page: 1, pageSize: 20,
  } }))
  await page.goto('/settings')
  await page.getByRole('tab', { name: 'Users', exact: true }).click()
  const directory = page.getByRole('region', { name: 'Registered users' })
  await expect(directory.getByText('Example Reader')).toBeVisible()
  await expect(directory.getByText('4 books', { exact: true })).toBeVisible()
  await expect(directory.getByText('Read: 1 · Reading: 1 · Unread: 2')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
})

test('the installed worker removes old private caches and does not reuse a previous account’s library offline', async ({ page, context }) => {
  await page.evaluate(async () => {
    const cache = await caches.open('api')
    await cache.put('/api/library/books?list=read&page=0&size=10', new Response(JSON.stringify({
      items: [{ id: 101, title: 'First reader’s private book' }],
    }), { headers: { 'content-type': 'application/json' } }))
  })
  await page.goto('/library')
  await expect(page.getByText('First reader’s private book')).toBeVisible()
  await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)
  await expect.poll(() => page.evaluate(async () => (await caches.keys()).includes('api'))).toBe(false)

  await page.reload()
  await expect(page.getByText('First reader’s private book')).toBeVisible()

  await context.setOffline(true)
  await setSession(page, 'second')
  await page.reload({ waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('heading', { name: 'Read books', exact: true })).toBeVisible()
  await expect(page.getByText('First reader’s private book')).toHaveCount(0)

  const result = await page.evaluate(async () => {
    const url = '/api/library/books?list=read&page=0&size=10'
    try {
      await fetch(url, { headers: { Authorization: 'Bearer second-token' } })
      return 'served'
    } catch {
      return 'offline'
    }
  })
  expect(result).toBe('offline')

  await context.setOffline(false)
  await page.reload()
  await expect(page.getByText('Second reader’s private book')).toBeVisible()
  await expect(page.getByText('First reader’s private book')).toHaveCount(0)
})

test('keyboard search highlights a result and opens its book', async ({ page }) => {
  await page.goto('/dashboard')
  const search = page.getByRole('combobox', { name: 'Search catalogue' })
  await search.fill('Matilda')
  const result = page.getByRole('option', { name: /Matilda/ })
  await expect(result).toBeVisible()
  await search.press('ArrowDown')
  await expect(result).toHaveAttribute('aria-selected', 'true')
  await search.press('Enter')
  await expect(page).toHaveURL(/\/books\/42$/)
  await expect(page.getByRole('heading', { name: 'Matilda' })).toBeVisible()
})

test('offline settings survive a reload, stay with their account and sync when back online', async ({ page, context }) => {
  const settingsLoaded = page.waitForResponse(response =>
    response.url().endsWith('/api/account/settings') && response.request().method() === 'GET',
  )
  await page.goto('/settings')
  await settingsLoaded
  const readCount = page.locator('.settings-number').filter({
    has: page.getByText('Read books count', { exact: true }),
  })
  const input = readCount.getByRole('spinbutton')
  await expect(input).toHaveValue('10')
  await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)

  await context.setOffline(true)
  await readCount.getByRole('button', { name: 'Aumentar' }).click()
  await expect(input).toHaveValue('11')
  await page.reload({ waitUntil: 'domcontentloaded' })
  await expect(input).toHaveValue('11')

  await setSession(page, 'second')
  await page.reload({ waitUntil: 'domcontentloaded' })
  await expect(input).toHaveValue('10')

  await setSession(page, 'first')
  await page.reload({ waitUntil: 'domcontentloaded' })
  await expect(input).toHaveValue('11')

  const synced = page.waitForResponse(response =>
    response.url().endsWith('/api/account/settings') && response.request().method() === 'PUT',
  )
  await context.setOffline(false)
  const response = await synced
  expect(response.ok()).toBe(true)
  expect(response.request().postDataJSON().library.readBooksCount).toBe(11)
  expect(response.request().headers().authorization).toBe('Bearer first-token')
})

test('a rejected ISBN import can be retried successfully', async ({ page }) => {
  await page.goto('/dashboard')
  const open = page.getByTitle('Add', { exact: true }).filter({ visible: true })
  const isbn = page.getByRole('textbox')
  const value = '9780140328721'
  await open.click()
  await isbn.fill(value)
  const failed = page.waitForResponse(response => response.url().includes('/api/books/fetch?'))
  await isbn.press('Enter')
  expect((await failed).status()).toBe(503)
  await expect(page.getByText(/Could not queue the book|Erro ao iniciar obtenção/)).toBeVisible()

  // Older builds close the dialog after submission; reopen it if necessary.
  if (!(await isbn.isVisible())) await open.click()
  await isbn.fill(value)
  const retried = page.waitForResponse(response => response.url().includes('/api/books/fetch?'))
  await isbn.press('Enter')
  expect((await retried).status()).toBe(200)
  await expect(page.getByText(/Book queued for import|O livro vai ser obtido/)).toBeVisible()
})

test('a barcode photo fills the ISBN field', async ({ page }) => {
  await page.goto('/dashboard')
  await page.getByTitle('Add', { exact: true }).filter({ visible: true }).click()
  await page.getByRole('button', { name: 'Scan barcode' }).click()

  // EAN-13 for 9780140328721, with white margins on both sides.
  const modules = '10101110110001001010011100110010011101000110101010100001011011001001000100010011011001100110101'
  const bars = Array.from(modules, (bit, index) =>
    bit === '1' ? `<rect x="${(index + 12) * 4}" y="10" width="4" height="140"/>` : '',
  ).join('')
  const barcode = `<svg xmlns="http://www.w3.org/2000/svg" width="476" height="160"><rect width="100%" height="100%" fill="white"/>${bars}</svg>`
  await page.locator('input[type="file"]').setInputFiles({
    name: 'isbn.svg',
    mimeType: 'image/svg+xml',
    buffer: Buffer.from(barcode),
  })

  await expect(page.getByRole('dialog', { name: 'Scan barcode' })).toHaveCount(0)
  await expect(page.getByRole('textbox')).toHaveValue('9780140328721')
})

test('a live scan queues a batch and reopens its persisted progress on mobile', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => {
    const canvas = document.createElement('canvas')
    canvas.width = 480
    canvas.height = 180
    const frame = canvas.getContext('2d')
    const stream = canvas.captureStream(15)
    Object.defineProperty(navigator.mediaDevices, 'getUserMedia', { value: async () => stream })
    window.showTestBarcode = isbn => {
      const left = ['0001101', '0011001', '0010011', '0111101', '0100011', '0110001', '0101111', '0111011', '0110111', '0001011']
      const invert = bits => [...bits].map(bit => bit === '1' ? '0' : '1').join('')
      const digits = [...isbn].map(Number)
      const leftCode = digits.slice(1, 7).map((digit, i) =>
        'LGGLGL'[i] === 'L' ? left[digit] : [...invert(left[digit])].reverse().join(''),
      ).join('')
      const rightCode = digits.slice(7).map(digit => invert(left[digit])).join('')
      const modules = '101' + leftCode + '01010' + rightCode + '101'
      frame.fillStyle = 'white'
      frame.fillRect(0, 0, canvas.width, canvas.height)
      frame.fillStyle = 'black'
      for (let i = 0; i < modules.length; i++) if (modules[i] === '1') frame.fillRect((i + 12) * 4, 10, 4, 155)
    }
  })
  const imports = []
  await page.route('**/api/library/shelves', route =>
    route.fulfill({ json: { shelves: [{ shelveId: 5, shelve: 'Fiction' }] } }))
  await page.route('**/api/imports', route => {
    if (!imports.length) return route.fulfill({ json: [] })
    const failed = imports.reduce((n, entry) => n + entry.failed, 0)
    return route.fulfill({ json: [{
      id: imports[0].batchId, shelfId: 5, createdAtUtc: imports[0].createdAtUtc,
      total: imports.length, succeeded: imports.length - failed, failed, pending: 0, isbnFailureCount: failed,
      status: failed ? 'completedWithErrors' : 'completed',
    }] })
  })
  await page.route('**/api/imports/*/failures?*', route => {
    const items = imports.flatMap(entry => entry.isbnFailures.map(failure => ({ ...failure, importId: entry.id })))
    return route.fulfill({ json: { page: 0, total: items.length, items } })
  })
  await page.route('**/api/fetch/isbn', route => {
    const { isbns, batchId, shelfId } = route.request().postDataJSON()
    expect(shelfId).toBe(5)
    expect(isbns).toHaveLength(1)
    expect(batchId).toBe(imports[0]?.batchId ?? batchId)
    const failure = isbns[0] === '9780140328752'
    const entry = {
      id: 'scan-' + imports.length, batchId, shelfId,
      createdAtUtc: new Date().toISOString(), total: 1, succeeded: 0, failed: 0, status: 'queued',
    }
    imports.unshift({
      ...entry, succeeded: failure ? 0 : 1, failed: failure ? 1 : 0,
      status: failure ? 'completedWithErrors' : 'completed',
      isbnFailures: failure ? [{ isbn: isbns[0], position: 0, reason: 'No metadata was found.' }] : [],
    })
    return route.fulfill({ status: 202, json: entry })
  })

  await page.goto('/dashboard')
  const open = page.getByRole('button', { name: 'Add book', exact: true })
  await open.click()
  const dialog = page.getByRole('dialog', { name: 'Add book' })
  await dialog.getByRole('button', { name: 'Scan a batch' }).click()
  await dialog.getByLabel('Destination shelf').selectOption('5')
  await dialog.getByRole('button', { name: 'Start camera' }).click()
  await expect(dialog.locator('video')).toBeVisible()
  await page.evaluate(() => window.showTestBarcode('9780140328721'))
  await expect(dialog.getByText('1 queued for import')).toBeVisible()
  await page.evaluate(() => window.showTestBarcode('9780140328752'))
  await expect(dialog.getByText('2 queued for import')).toBeVisible()
  await page.evaluate(() => window.showTestBarcode('9780140328721'))
  await expect(dialog.locator('video')).toBeVisible()
  await dialog.getByRole('button', { name: 'Finish batch' }).click()
  await expect(dialog.getByText('1 of 2 imported, 1 failed, 0 pending')).toBeVisible()
  await expect(dialog.locator('video')).toHaveCount(0)
  await page.reload()
  await open.click()
  await dialog.getByRole('button', { name: 'Import history' }).click()
  await expect(dialog.getByText('1 of 2 imported, 1 failed, 0 pending')).toBeVisible()
  await dialog.getByText('Failed ISBNs', { exact: true }).click()
  await expect(dialog.getByText('No metadata was found.')).toBeVisible()
  expect(imports).toHaveLength(2)
})
