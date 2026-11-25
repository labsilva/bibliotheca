import { expect, test } from '@playwright/test'

test('a direct visit to an unknown URL shows a public 404 page', async ({ page }) => {
  const unknownPath = '/missing/old-link?source=bookmark'
  await page.goto(unknownPath)

  await expect(page).toHaveURL(unknownPath)
  await expect(page.getByRole('heading', { name: '404 — Page not found' })).toBeVisible()
  await expect(page).toHaveTitle('Bibliotheca')
  await expect(page.getByRole('link', { name: 'Go to dashboard' })).toHaveAttribute('href', '/dashboard')
})

test('following an outdated return link in the app lets the reader recover to the dashboard', async ({ page }) => {
  await page.goto('/__test__/blank')
  await page.evaluate(() => sessionStorage.setItem('oidc.user:http://127.0.0.1:4180:bibliotheca-e2e', JSON.stringify({
    access_token: 'first-token',
    token_type: 'Bearer',
    profile: { sub: 'first', name: 'first' },
    expires_at: Math.floor(Date.now() / 1000) + 3600,
  })))
  await page.route('**/api/series?**', route => route.fulfill({ json: [] }))
  await page.goto('/books/42/select-series?returnTo=%2Fmissing%2Freturn-link')
  await expect(page.getByRole('heading', { name: 'Escolher série' })).toBeVisible()
  const documentStart = await page.evaluate(() => performance.timeOrigin)

  await page.getByRole('button', { name: '← Voltar' }).click()

  await expect(page).toHaveURL('/missing/return-link')
  await expect(page.getByRole('heading', { name: '404 — Page not found' })).toBeVisible()
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(documentStart)

  await page.getByRole('link', { name: 'Go to dashboard' }).click()

  await expect(page).toHaveURL('/dashboard')
  await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible()
})
