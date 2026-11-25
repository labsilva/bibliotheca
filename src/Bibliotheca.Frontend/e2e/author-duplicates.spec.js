import { expect, test } from '@playwright/test'

for (const width of [1280, 390]) {
  test(`review duplicate authors at ${width}px`, async ({ page, baseURL }) => {
    await page.setViewportSize({ width, height: 844 })
    await page.goto('/__test__/blank')
    await page.evaluate(origin => sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
      access_token: 'first-token', token_type: 'Bearer', profile: { sub: 'first', name: 'first' },
      expires_at: Math.floor(Date.now() / 1000) + 3600,
    })), baseURL)
    await page.route('**/api/catalogue-refresh', route => route.fulfill({ json: { bookCount: 3, latest: null, errors: [] } }))
    await page.route('**/api/author-refresh', route => route.fulfill({ json: { authorCount: 3, latest: null, errors: [] } }))

    const comparison = {
      id: 1, version: 'fresh', normalizedName: 'GEORGERRMARTIN', sharedBookCount: 0,
      first: { id: 41, name: 'George R.R. Martin', sourceAuthorKey: '/authors/OL1A', books: [{ id: 10, title: 'A Game of Thrones' }] },
      second: { id: 42, name: 'George R. R. Martin', sourceAuthorKey: '/authors/OL2A', books: [{ id: 20, title: 'A Clash of Kings' }] },
    }
    let phase = 'idle'
    const reviews = []
    let reads = 0
    await page.route(/\/api\/author-duplicates(?:\/|\?|$)/, route => {
      const url = new URL(route.request().url())
      if (url.pathname.endsWith('/scan')) {
        phase = 'pending'
        reads = 0
        return route.fulfill({ status: 202, json: { id: 'scan', active: true, processed: 0, total: 2, newSuggestions: 0 } })
      }
      if (url.pathname.endsWith('/review')) {
        const decision = route.request().postDataJSON()
        reviews.push(decision)
        phase = decision.decision === 'defer' ? 'deferred' : 'resolved'
        return route.fulfill({ status: 204 })
      }

      const showDeferred = url.searchParams.get('deferred') === 'true'
      const isPending = phase === 'pending'
      const isDeferred = phase === 'deferred'
      const scanning = isPending && ++reads === 1
      return route.fulfill({ json: {
        latest: phase === 'idle' ? null : {
          id: 'scan', active: scanning, processed: scanning ? 1 : 2, total: 2, newSuggestions: 1,
        },
        comparison: (isPending && !showDeferred) || (isDeferred && showDeferred) ? comparison : null,
        pendingCount: isPending ? 1 : 0,
        deferredCount: isDeferred ? 1 : 0,
      } })
    })

    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Database' }).click()
    const panel = page.getByRole('region', { name: 'Duplicate authors' })
    await expect(panel.getByText('No pending suggestions.')).toBeVisible()
    await panel.getByRole('button', { name: 'Scan for duplicate authors' }).click()
    await expect(panel.getByRole('progressbar', { name: 'Duplicate author scan progress' })).toHaveAttribute('value', '1')
    await expect(panel.getByText('Scan complete', { exact: false })).toBeVisible()
    await expect(panel.getByRole('link', { name: 'A Game of Thrones' })).toHaveAttribute('href', '/books/10')
    await expect(panel.getByRole('link', { name: 'A Clash of Kings' })).toHaveAttribute('href', '/books/20')
    await expect(panel.getByText('These profiles have different external author IDs.', { exact: false })).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Merge authors' })).toBeDisabled()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)

    await panel.getByRole('button', { name: 'Defer', exact: true }).click()
    await expect(panel.getByText('Suggestion deferred.')).toBeVisible()
    await panel.getByRole('button', { name: 'Show deferred (1)' }).click()
    await panel.getByRole('radio', { name: 'George R. R. Martin (#42)' }).check()
    await panel.getByRole('button', { name: 'Merge authors' }).click()
    await panel.getByRole('button', { name: 'Cancel', exact: true }).click()
    expect(reviews).toEqual([{ decision: 'defer', version: 'fresh', keepAuthorId: null }])
    await panel.getByRole('button', { name: 'Merge authors' }).click()
    await panel.getByRole('button', { name: 'Confirm merge', exact: true }).click()
    await expect(panel.getByText('Authors merged.')).toBeVisible()
    await expect(panel.getByText('No deferred suggestions.')).toBeVisible()
    expect(reviews.at(-1)).toEqual({ decision: 'merge', version: 'fresh', keepAuthorId: 42 })
    await page.reload()
    await page.getByRole('tab', { name: 'Database' }).click()
    await expect(panel.getByText('No pending suggestions.')).toBeVisible()
  })
}
