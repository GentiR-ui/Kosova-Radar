// Scratch (LIVE sites, run manually only): "Merr tani" on one seeded HTML source through the UI.
// Usage: node live-fetch.mjs "<source name>" <shot-suffix>
import { chromium } from '@playwright/test'
const [name, suffix] = process.argv.slice(2)
const browser = await chromium.launch()
const page = await (await browser.newContext({ viewport: { width: 1280, height: 800 }, baseURL: 'http://localhost:5173' })).newPage()
const errs = []
page.on('console', (m) => m.type() === 'error' && errs.push(m.text()))
await page.request.post('/api/auth/login', { data: { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' } })
await page.goto('/admin/sources')
const btn = page.getByRole('button', { name: `Merr tani: ${name}`, exact: true })
await btn.scrollIntoViewIfNeeded()
const t0 = Date.now()
await btn.click()
const status = page.getByRole('status').filter({ hasText: /Marrja|Një marrje/ })
await status.first().waitFor({ timeout: 300_000 })
const row = page.getByRole('row').filter({ hasText: name })
console.log('message:', (await status.first().textContent())?.trim(), `(${Math.round((Date.now() - t0) / 1000)} s)`)
console.log('row:', (await row.first().innerText()).replace(/\s+/g, ' '))
await row.first().scrollIntoViewIfNeeded()
await page.screenshot({ path: `test-results/qa/desktop-sources-live-${suffix}.png` })
console.log('console errors:', errs)
await browser.close()
