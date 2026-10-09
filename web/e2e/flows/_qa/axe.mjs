// Scratch: axe-core WCAG 2.2 AA audit of every screen (desktop + 360). Not a test.
import { chromium } from '@playwright/test'
import fs from 'node:fs'
const data = JSON.parse(fs.readFileSync('test-results/qa/tour-data.json', 'utf8'))
const AXE = 'C:/tmp/qa-axe/node_modules/axe-core/axe.min.js'
const pages = [
  ['/', null], ['/events', null], [`/events/${data.evId}`, null], ['/deals', null], [`/deals/${data.dealId}`, null],
  ['/search?q=QA', null], ['/events?search=zzzz', null], ['/admin/login', 'anon'],
  ['/admin', 'admin'], ['/admin/review', 'admin'], [`/admin/items/${data.pendingCand}`, 'admin'], ['/admin/published', 'admin'],
  [`/admin/published/event/${data.evId}`, 'admin'], ['/admin/sources', 'admin'], ['/admin/users', 'admin'],
]
const browser = await chromium.launch()
const summary = {}
for (const width of [1280, 360]) {
  const ctx = await browser.newContext({ viewport: { width, height: 800 }, baseURL: 'http://localhost:5173' })
  const page = await ctx.newPage()
  let signedIn = false
  for (const [path, who] of pages) {
    if (who === 'admin' && !signedIn) { await page.request.post('/api/auth/login', { data: { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' } }); signedIn = true }
    await page.goto(path)
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(300)
    await page.addScriptTag({ path: AXE })
    const res = await page.evaluate(async () => {
      // eslint-disable-next-line no-undef
      const r = await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] } })
      return r.violations.map((v) => ({ id: v.id, impact: v.impact, n: v.nodes.length, help: v.help, sample: v.nodes.slice(0, 3).map((n) => n.target.join(' ') + ' | ' + (n.any[0]?.message ?? n.all[0]?.message ?? '').slice(0, 160)) }))
    })
    summary[`${width} ${path}`] = res
  }
  await ctx.close()
}
await browser.close()
fs.writeFileSync('test-results/qa/axe-report.json', JSON.stringify(summary, null, 1))
for (const [k, v] of Object.entries(summary)) { console.log('==', k); for (const x of v) console.log(`  ${x.impact} ${x.id} (${x.n}) ${x.help}\n    ${x.sample.join('\n    ')}`) }
