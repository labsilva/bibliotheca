import { expect, test } from '@playwright/test'
for (const width of [320, 1280]) {
  test(`book list and metrics fit at ${width}px`, async ({ page, baseURL }) => {
    await page.setViewportSize({ width, height: 800 })
    await page.goto('/__test__/blank')
    await page.evaluate(origin => sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
      access_token: 'token', token_type: 'Bearer', profile: { sub: 'reader', name: 'reader' },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    })), baseURL)
    await page.route('**/api/account/settings', route => route.fulfill({ json: { library: { booksView: 'list' } } }))
    await page.route('**/api/books?**', route => route.fulfill({ json: { total: 1,
      items: [{ id: 1, title: 'Matilda', author: 'Roald Dahl' }] } }))
    await page.goto('/books')
    await expect(page.getByRole('heading', { name: 'Books', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: /Matilda/ })).toBeVisible()
    await expect(page.getByText('Shared catalogue: 1')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
    await page.getByRole('button', { name: 'Carousels' }).click()
    await expect(page.getByRole('heading', { name: 'Recently Added' })).toBeVisible()
  })
}
