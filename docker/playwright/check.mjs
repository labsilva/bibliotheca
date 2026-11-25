import assert from 'node:assert/strict'
import { chromium } from 'playwright'

const discovery = await fetch('http://127.0.0.1:8080/endpoint').then(response => {
  assert.equal(response.status, 200)
  return response.json()
})
for (let attempt = 0; attempt < 2; attempt++) {
  const browser = await chromium.connect(`ws://127.0.0.1:${discovery.port}${discovery.wsPath}`)
  try {
    assert.equal(browser.contexts().length, 0, 'A new client must not inherit another client’s contexts')
    const context = await browser.newContext()
    const page = await context.newPage()
    await page.route('https://fixture.invalid/**', route => route.fulfill({ contentType: 'text/html', body: '<title>Browser fixture</title><h1>Ready</h1>' }))
    await page.goto('https://fixture.invalid/')
    assert.equal(await page.title(), 'Browser fixture')
    const userAgent = await page.evaluate(() => navigator.userAgent)
    assert.ok(!userAgent.includes('HeadlessChrome'), 'Expected Chromium running under Xvfb, not headless')
    console.log(JSON.stringify({ attempt: attempt + 1, architecture: process.arch, userAgent, renderer: 'passed' }))
    await context.close()
  } finally { await browser.close() }
}
assert.equal((await fetch('http://127.0.0.1:8080/health')).status, 200)
