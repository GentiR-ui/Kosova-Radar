// Scratch: Tab through each page, record every focus stop and whether it has a visible indicator.
import { chromium } from '@playwright/test'
import fs from 'node:fs'
const data = JSON.parse(fs.readFileSync('test-results/qa/tour-data.json', 'utf8'))
const pages = ['/', '/events', `/events/${data.evId}`, '/deals', `/deals/${data.dealId}`, '/search?q=QA', '/admin', '/admin/review', `/admin/items/${data.pendingCand}`, '/admin/published', `/admin/published/deal/${data.dealId}`, '/admin/sources', '/admin/users']
const browser = await chromium.launch()
const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 }, baseURL: 'http://localhost:5173' })
const page = await ctx.newPage()
await page.request.post('/api/auth/login', { data: { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' } })
for (const path of pages) {
  await page.goto(path)
  await page.waitForLoadState('networkidle')
  const stops = []
  const bad = []
  for (let i = 0; i < 45; i++) {
    await page.keyboard.press('Tab')
    const f = await page.evaluate(() => {
      const el = document.activeElement
      if (!el || el === document.body) return null
      const s = getComputedStyle(el)
      const visible = (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || (s.boxShadow && s.boxShadow !== 'none')
      const r = el.getBoundingClientRect()
      const name = (el.getAttribute('aria-label') || el.labels?.[0]?.textContent || el.textContent || el.getAttribute('name') || '').trim().slice(0, 30)
      return { tag: el.tagName.toLowerCase(), name, visible, onscreen: r.width > 0 && r.height > 0, outline: `${s.outlineStyle} ${s.outlineWidth} ${s.outlineColor}` }
    })
    if (!f) { stops.push('<body>'); continue }
    stops.push(`${f.tag}:${f.name}`)
    if (!f.visible || !f.onscreen) bad.push(f)
  }
  console.log('==', path, '\n  ', stops.slice(0, 22).join(' | '))
  if (bad.length) console.log('  NO VISIBLE FOCUS:', JSON.stringify(bad.slice(0, 5)))
}
await browser.close()
