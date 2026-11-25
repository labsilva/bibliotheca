import { chromium } from 'playwright'

const endpoint = await fetch('http://127.0.0.1:8080/endpoint').then(response => response.json())
const browser = await chromium.connect(`ws://127.0.0.1:${endpoint.port}${endpoint.wsPath}`)
try {
  for (const title of ['Matilda', 'Ensaio sobre a Cegueira', 'Livro do Desassossego']) {
    const context = await browser.newContext()
    try {
      const page = await context.newPage()
      const response = await page.goto('https://www.wook.pt/pesquisa?keyword=' + encodeURIComponent(title), { waitUntil: 'domcontentloaded', timeout: 25000 })
      const links = await page.locator('a[href*="/livro/"]').evaluateAll(nodes => [...new Set(nodes.map(node => node.href))])
      console.log(JSON.stringify({ title, searchStatus: response?.status(), pageTitle: await page.title(), productCount: links.length, architecture: process.arch }))
      if (response?.ok() && links.length) {
        const detail = await page.goto(links[0], { waitUntil: 'domcontentloaded', timeout: 25000 })
        console.log(JSON.stringify({ title, detailStatus: detail?.status(), detailTitle: await page.title() }))
        if (!detail?.ok()) process.exitCode = 1
      } else process.exitCode = 1
    } catch (error) {
      console.log(JSON.stringify({ title, error: error.message }))
      process.exitCode = 1
    } finally { await context.close() }
  }
} finally { await browser.close() }
