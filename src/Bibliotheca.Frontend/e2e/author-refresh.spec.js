import { expect, test } from '@playwright/test'

for (const width of [1280, 390]) {
  test(`author refresh confirms, restores its report and fits ${width}px`, async ({ page, baseURL }) => {
    await page.setViewportSize({ width, height: 844 })
    await page.goto('/__test__/blank')
    await page.evaluate(origin => sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
      access_token: 'first-token', token_type: 'Bearer', profile: { sub: 'first', name: 'first' },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    })), baseURL)
    await page.route('**/api/catalogue-refresh', route => route.fulfill({ json: { bookCount: 3, latest: null, errors: [] } }))
    let started = false
    await page.route('**/api/author-refresh', route => {
      if (route.request().method() === 'POST') {
        started = true
        return route.fulfill({ status: 202, json: { id: 'authors', total: 3 } })
      }
      return route.fulfill({ json: {
        authorCount: 3, catalogueRefreshActive: false,
        latest: started ? { id: 'authors', total: 3, updated: 1, unchanged: 1, failed: 1, completedAtUtc: '2026-11-22T09:00:00Z' } : null,
        errors: started ? [{ authorId: 42, authorName: 'Roald Dahl', error: 'Provider request timed out.' }] : [],
      } })
    })

    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Database' }).click()
    const authorPanel = page.getByRole('region', { name: 'Refresh author profiles' })
    await authorPanel.getByRole('button', { name: 'Refresh all authors' }).click()
    await expect(authorPanel.getByText('Refresh profiles for all 3 authors?', { exact: false })).toBeVisible()
    await authorPanel.getByRole('button', { name: 'Cancel', exact: true }).click()
    expect(started).toBe(false)
    await authorPanel.getByRole('button', { name: 'Refresh all authors' }).click()
    await authorPanel.getByRole('button', { name: 'Confirm author refresh' }).click()
    await expect(authorPanel.getByText('Completed: 3 / 3')).toBeVisible()
    await expect(authorPanel.getByRole('link', { name: 'Roald Dahl' })).toHaveAttribute('href', '/authors/42')
    await page.reload()
    await page.getByRole('tab', { name: 'Database' }).click()
    await expect(authorPanel.getByText('Completed: 3 / 3')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  })
}
