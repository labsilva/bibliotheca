import { expect, test } from '@playwright/test'

for (const size of [{ width: 320, height: 700 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
  test(`settings controls fit ${size.width}x${size.height}`, async ({ page, baseURL }) => {
    await page.setViewportSize(size)
    await page.goto('/__test__/blank')
    await page.evaluate(origin => sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
      access_token: 'token', token_type: 'Bearer', profile: { sub: 'first', name: 'first' },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    })), baseURL)
    await page.route('**/api/catalogue-refresh', route => route.fulfill({ json: { bookCount: 3, latest: null, errors: [] } }))
    await page.route('**/api/author-refresh', route => route.fulfill({ json: { authorCount: 3, latest: null, errors: [] } }))
    await page.goto('/settings')
    for (const tab of ['Library', 'Database']) {
      await page.getByRole('tab', { name: tab }).click()
      const panel = page.locator('.settings-page')
      await expect.poll(() => panel.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true)
      for (const button of await panel.getByRole('button').all()) {
        if (await button.isVisible()) {
          const box = await button.boundingBox()
          expect(box.x).toBeGreaterThanOrEqual(0)
          expect(box.x + box.width, await button.textContent()).toBeLessThanOrEqual(size.width + 1)
        }
      }
    }
  })
}

