import { expect, test } from '@playwright/test'

const sessionKey = 'oidc.user:http://127.0.0.1:4180:bibliotheca-e2e'

for (const rememberMe of [true, false]) {
  test(`reopening the app with Remember me ${rememberMe ? 'checked' : 'unchecked'}`, async ({ browser, page, context, baseURL }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto('/library?view=personal')
    const remember = page.getByRole('checkbox', { name: 'Manter sessão iniciada' })
    if (rememberMe) await remember.check()
    await page.getByRole('button', { name: 'Entrar' }).click()
    await expect(page).toHaveURL(baseURL + '/library?view=personal')
    await expect.poll(() => page.evaluate(key => JSON.parse(sessionStorage.getItem(key))?.profile.sub, sessionKey)).toBe('first')
    await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)

    // Advance time so the real client renews the token after an expired PWA session resumes.
    const renewal = page.waitForResponse(response => response.url().endsWith('/connect/token') &&
      new URLSearchParams(response.request().postData()).get('grant_type') === 'refresh_token')
    await page.clock.setFixedTime(new Date(Date.now() + 16 * 60_000))
    await page.reload()
    expect((await renewal).ok()).toBe(true)
    await expect(page).toHaveURL(baseURL + '/library?view=personal')

    const state = await context.storageState()
    expect(state.origins.some(origin => origin.localStorage.some(({ name }) => name === sessionKey))).toBe(false)
    expect(state.cookies.find(cookie => cookie.name === 'fixture_session').expires > 0).toBe(rememberMe)
    // Simulate closing the browser by transferring only cookies that survive its session.
    state.cookies = state.cookies.filter(cookie => cookie.expires > 0)
    const reopened = await browser.newContext({ storageState: state, viewport: { width: 390, height: 844 } })
    try {
      const launched = await reopened.newPage()
      await launched.goto('/library')
      if (!rememberMe) {
        await expect(launched.getByRole('button', { name: 'Entrar' })).toBeVisible()
        return
      }
      await expect(launched).toHaveURL(baseURL + '/library')
      await expect.poll(() => launched.evaluate(key => JSON.parse(sessionStorage.getItem(key))?.profile.sub, sessionKey)).toBe('first')
      await launched.getByRole('button', { name: 'Menu', exact: true }).click()
      await launched.getByRole('menuitem', { name: 'Logout' }).click()
      await expect(launched).toHaveURL(baseURL + '/logout-callback')
      expect(await launched.evaluate(key => sessionStorage.getItem(key), sessionKey)).toBeNull()
      expect((await reopened.cookies()).some(cookie => cookie.name === 'fixture_session')).toBe(false)
    } finally {
      await reopened.close()
    }
  })
}
