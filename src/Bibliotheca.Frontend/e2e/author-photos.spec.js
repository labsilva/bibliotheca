import { expect, test } from '@playwright/test'

const photoUrl = 'https://covers.openlibrary.org/a/id/123-M.jpg?default=false'
const testImage = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==', 'base64')

for (const width of [1280, 390]) {
  test(`author details fit ${width}px with a portrait and with the fallback`, async ({ page, baseURL }) => {
    await page.setViewportSize({ width, height: 844 })
    await page.goto('/__test__/blank')
    await page.evaluate(origin => {
      sessionStorage.setItem(`oidc.user:${origin}:bibliotheca-e2e`, JSON.stringify({
        access_token: 'first-token', token_type: 'Bearer', profile: { sub: 'first', name: 'first' },
        expires_at: Math.floor(Date.now() / 1000) + 3600,
      }))
    }, baseURL)
    await page.route('**/api/authors/*', route => route.fulfill({
      json: { id: 42, name: 'Roald Dahl', photoUrl, biography: 'A biography of Roald Dahl.', biographySourceUrl: 'https://en.wikipedia.org/?curid=42', books: [] },
    }))
    let photoAvailable = true
    await page.route('https://covers.openlibrary.org/a/id/*', route => photoAvailable
      ? route.fulfill({ contentType: 'image/png', body: testImage })
      : route.fulfill({ status: 404 }))

    await page.goto('/authors/42')
    const portrait = page.getByRole('img', { name: 'Portrait of Roald Dahl' })
    await expect(portrait).toBeVisible()
    await expect.poll(() => portrait.evaluate(image => image.naturalWidth)).toBeGreaterThan(0)
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
    const loadedSize = await portrait.boundingBox()
    expect(loadedSize.height / loadedSize.width).toBeCloseTo(1.25, 1)
    await expect(page.getByText('A biography of Roald Dahl.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Wikipedia contributors' })).toHaveAttribute('href', 'https://en.wikipedia.org/?curid=42')

    photoAvailable = false
    await page.reload()
    const fallback = page.getByRole('img', { name: 'No author photo available' })
    await expect(fallback).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Roald Dahl' })).toBeVisible()
    const fallbackSize = await fallback.boundingBox()
    expect(fallbackSize.width).toBe(loadedSize.width)
    expect(fallbackSize.height).toBe(loadedSize.height)
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  })
}
