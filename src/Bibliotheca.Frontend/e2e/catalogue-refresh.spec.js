import { expect, test } from '@playwright/test'

for (const width of [1280, 390]) {
  test(`catalogue refresh confirms, restores progress and fits ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 844 })
    await page.goto('/__test__/blank')
    await page.evaluate(() => sessionStorage.setItem('oidc.user:http://127.0.0.1:4180:bibliotheca-e2e', JSON.stringify({
      access_token: 'first-token', token_type: 'Bearer', profile: { sub: 'first', name: 'first' }, expires_at: Math.floor(Date.now() / 1000) + 3600,
    })))
    let started = false
    await page.route('**/api/catalogue-refresh', route => {
      if (route.request().method() === 'POST') {
        started = true
        return route.fulfill({ status: 202, json: { id: 'batch', total: 3 } })
      }
      return route.fulfill({ json: {
        bookCount: 3,
        latest: started ? { id: 'batch', total: 3, updated: 1, unchanged: 1, failed: 1, completedAtUtc: '2026-11-21T20:00:00Z' } : null,
        errors: started ? [{ bookId: 42, title: 'Matilda', error: 'Provider request timed out.' }] : [],
      } })
    })
    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Database' }).click()
    await page.getByRole('button', { name: 'Refresh all books', exact: true }).click()
    await expect(page.getByText('Refresh all 3 books using the configured providers?')).toBeVisible()
    await page.getByRole('button', { name: 'Cancel', exact: true }).click()
    expect(started).toBe(false)
    await page.getByRole('button', { name: 'Refresh all books', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm refresh', exact: true }).click()
    await expect(page.getByText('Completed: 3 / 3')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Matilda' })).toBeVisible()
    await page.reload()
    await page.getByRole('tab', { name: 'Database' }).click()
    await expect(page.getByText('Completed: 3 / 3')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
    await page.screenshot({ path: testInfo.outputPath(`catalogue-refresh-${width}.png`), fullPage: true })
  })
}
