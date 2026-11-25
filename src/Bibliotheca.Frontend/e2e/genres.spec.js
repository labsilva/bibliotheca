import { expect, test } from '@playwright/test'

test('genre counts fit on a phone and stay visible when searching', async ({ page, baseURL }) => {
  const width = 390
  await page.setViewportSize({ width, height: 844 })
  await page.goto('/__test__/blank')
  await page.evaluate(origin => {
    sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
      access_token: 'first-token', token_type: 'Bearer', profile: { sub: 'first', name: 'first' },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    }))
  }, baseURL)
  await page.route(url => url.pathname === '/api/genres', route => route.fulfill({
    json: [
      { bookGenreId: 1, genre: 'Fantasy', bookCount: 2 },
      { bookGenreId: 2, genre: 'Historical fiction and alternate history', bookCount: 1 },
    ],
  }))

  await page.goto('/genres')
  const fantasy = page.getByRole('link', { name: 'Fantasy 2 books' })
  const historical = page.getByRole('link', { name: 'Historical fiction and alternate history 1 book' })
  await expect(fantasy).toBeVisible()
  await expect(historical).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)

  await page.getByPlaceholder('Search genres...').fill(' HISTORICAL ')
  await expect(fantasy).toHaveCount(0)
  await expect(historical).toBeVisible()
})
