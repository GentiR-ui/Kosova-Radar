// Scratch probe: viewport screenshot + widest elements for a page at a width. Usage: node probe.mjs <path> <width> <name> [scrollY]
import { chromium } from '@playwright/test'
const [path, width = '360', name = 'probe', scrollY = '0'] = process.argv.slice(2)
const browser = await chromium.launch()
const ctx = await browser.newContext({ viewport: { width: Number(width), height: 800 }, deviceScaleFactor: 1, baseURL: 'http://localhost:5173' })
const page = await ctx.newPage()
await page.request.post('/api/auth/login', { data: { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' } })
await page.goto(path)
await page.waitForLoadState('networkidle')
await page.waitForTimeout(500)
const info = await page.evaluate(() => {
  const w = document.documentElement.clientWidth
  const out = []
  for (const el of document.querySelectorAll('body *')) {
    const r = el.getBoundingClientRect()
    if (r.right > w + 1) out.push({ tag: el.tagName, cls: String(el.className).slice(0, 60), right: Math.round(r.right), text: (el.textContent || '').slice(0, 40) })
  }
  return { sw: document.documentElement.scrollWidth, w, out: out.slice(0, 12) }
})
console.log(JSON.stringify(info, null, 1))
await page.evaluate((y) => window.scrollTo(0, y), Number(scrollY))
await page.screenshot({ path: `test-results/qa/${name}.png` })
await browser.close()
