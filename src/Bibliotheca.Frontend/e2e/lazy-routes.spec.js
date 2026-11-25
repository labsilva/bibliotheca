import { expect, test } from '@playwright/test'

test('catalogue routes open by direct link and from an installed offline PWA', async ({ page, context, baseURL }) => {
  await page.goto('/__test__/blank')
  await page.evaluate(origin => {
    sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
      access_token: 'first-token',
      token_type: 'Bearer',
      profile: { sub: 'first', name: 'first' },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    }))
  }, baseURL)

  await page.goto('/books/42')
  await expect(page.getByRole('heading', { name: 'Matilda' })).toBeVisible()
  await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)
  await context.setOffline(true)

  await page.locator('nav a[href="/collections"]').click()
  await expect(page).toHaveURL(/\/collections$/)
  await expect(page.getByRole('heading', { name: 'Collections', exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toContainText('Network Error')

  await page.goto('/authors')
  await expect(page.getByRole('heading', { name: 'Authors', exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toContainText('Network Error')
})
