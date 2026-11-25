import { chromium } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'

// Explicitly invoked live diagnostic; it is not part of the fixture test suite.
const directory = process.argv[2] || 'test-results/wook-live'
await mkdir(directory, { recursive: true })
const headless = process.env.WOOK_PROBE_HEADLESS !== 'false'
const channel = process.env.WOOK_PROBE_CHANNEL || undefined
const browser = await chromium.launch({ headless, ...(channel && { channel }) })
console.log(JSON.stringify({ headless, channel: channel || 'bundled-chromium', version: browser.version() }))
try {
  for (const title of process.env.WOOK_PROBE_TITLE ? [process.env.WOOK_PROBE_TITLE] : ['Matilda', 'Ensaio sobre a Cegueira', 'Livro do Desassossego']) {
    const context = await browser.newContext()
    const page = await context.newPage()
    const url = 'https://www.wook.pt/pesquisa?keyword=' + encodeURIComponent(title)
    try {
      const response = await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 25000 })
      const html = await page.content()
      const filename = title.normalize('NFD').replace(/\p{Diacritic}/gu, '').replace(/\W+/g, '-').toLowerCase() + '.html'
      await writeFile(directory + '/' + filename, html)
      const links = await page.locator('a[href*="/livro/"]').evaluateAll(nodes => nodes.slice(0, 8).map(node => ({ title: node.textContent.trim(), href: node.href })))
      const rayId = response?.headers()['cf-ray']
      const userAgent = await page.evaluate(() => navigator.userAgent)
      console.log(JSON.stringify({ title, status: response?.status(), rayId, userAgent, url: page.url(), pageTitle: await page.title(), htmlFile: filename, links }))
      if (process.env.WOOK_PROBE_DETAILS === 'true' && links.length) {
        const detailResponse = await page.goto(links[0].href, { waitUntil: 'domcontentloaded', timeout: 25000 })
        await writeFile(directory + '/detail-' + filename, await page.content())
        console.log(JSON.stringify({ title, detailStatus: detailResponse?.status(), detailUrl: page.url(), detailTitle: await page.title(), htmlFile: 'detail-' + filename }))
      }
    } catch (error) {
      console.log(JSON.stringify({ title, url, error: error.message }))
    } finally { await context.close() }
  }
} finally { await browser.close() }
