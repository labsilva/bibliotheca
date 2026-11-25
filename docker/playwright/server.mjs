import { createServer } from 'node:http'
import { chromium } from 'playwright'
import { startEgress } from './egress.mjs'

// The browser uses a virtual display, never headless mode or a personal profile.
// Keep Playwright's random websocket path; discovery is private to the Compose network.
const host = process.env.PLAYWRIGHT_HOST || '0.0.0.0'
const localEgress = process.env.PLAYWRIGHT_EGRESS_PROXY ? null : await startEgress()
const browser = await chromium.launchServer({ headless: false, host, port: 8081,
  chromiumSandbox: true,
  proxy: { server: process.env.PLAYWRIGHT_EGRESS_PROXY || 'http://127.0.0.1:8082' },
  args: ['--proxy-bypass-list=<-loopback>', '--disable-quic', '--force-webrtc-ip-handling-policy=disable_non_proxied_udp'] })
const endpoint = new URL(browser.wsEndpoint())
let stopping = false
const discovery = createServer((request, response) => {
  response.setHeader('Cache-Control', 'no-store')
  response.setHeader('Content-Type', 'application/json')
  if (request.method !== 'GET' || !['/endpoint', '/health'].includes(request.url)) {
    response.writeHead(404).end('{}')
    return
  }
  response.end(JSON.stringify(request.url === '/health'
    ? { ready: true }
    : { wsPath: endpoint.pathname, port: Number(endpoint.port) }))
})
await new Promise((resolve, reject) => {
  discovery.once('error', reject)
  discovery.listen(8080, host, resolve)
})
console.log(JSON.stringify({ ready: true, architecture: process.arch, headless: false, browser: 'chromium' }))
browser.on('close', () => {
  discovery.close()
  localEgress?.close()
  if (!stopping) process.exit(1)
})
async function stop() {
  if (stopping) return
  stopping = true
  discovery.close()
  await browser.close()
  process.exit(0)
}
process.on('SIGTERM', stop)
process.on('SIGINT', stop)
