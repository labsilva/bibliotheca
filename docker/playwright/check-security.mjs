import assert from 'node:assert/strict'
import { chromium } from 'playwright'

const discovery = await fetch('http://127.0.0.1:8080/endpoint').then(response => response.json())
const browser = await chromium.connect(`ws://127.0.0.1:${discovery.port}${discovery.wsPath}`)
try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true })
  const page = await context.newPage()
  for (const url of ['http://127.0.0.1:8080/endpoint', 'https://127.0.0.1/', 'https://169.254.169.254/', 'https://localhost/']) {
    try {
      const response = await page.goto(url, { timeout: 10000 })
      assert.equal(response?.status(), 403, `${url} must fail closed`)
    } catch (error) {
      assert.match(error.message, /net::ERR_(?:TUNNEL_CONNECTION_FAILED|PROXY_CONNECTION_FAILED|CONNECTION_CLOSED|EMPTY_RESPONSE|HTTP_RESPONSE_CODE_FAILURE)/)
    }
  }
  const sandbox = await context.newPage()
  await sandbox.goto('chrome://sandbox')
  const status = await sandbox.locator('body').innerText()
  assert.match(status, /Layer 1 Sandbox\s+Namespace/i)
  assert.match(status, /PID namespaces\s+Yes/i)
  assert.match(status, /Seccomp-BPF sandbox\s+Yes/i)
  console.log(JSON.stringify({ architecture: process.arch, sandbox: 'enabled', privateDestinations: 'blocked' }))
  await context.close()
} finally { await browser.close() }
