// Scratch QA tour: screenshots of every screen at 3 viewports, overflow + console errors. Not a test.
import { chromium, devices, request } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import fs from 'node:fs'

const BASE = 'http://localhost:5173'
const OUT = 'test-results/qa'
const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }
fs.mkdirSync(OUT, { recursive: true })

const iso = (d) => d.toISOString().replace(/\.\d{3}Z$/, '+00:00')
const inDays = (n, h = 19) => {
  const d = new Date(Date.now() + n * 86400000)
  d.setUTCHours(h, 0, 0, 0)
  return d
}

// ---- seed QA data through the API ----
const api = await request.newContext({ baseURL: BASE })
await api.post('/api/auth/login', { data: ADMIN })
const tag = randomUUID().slice(0, 6)
const longTitle = `QA ${tag} Festivali Ndërkombëtar i Filmit Dokumentar dhe të Shkurtër DokuFest Edicioni i Njëzet e Katërt në Prizren me shfaqje çdo natë`
const longWord = `QA${tag}-Supercalifragilisticexpialidocious_${'x'.repeat(80)}`
const items = [
  {
    externalId: 'qa-' + randomUUID(), kind: 'event', title: longTitle,
    url: `https://example.com/events/${randomUUID()}`, city: 'Prizren', startsAt: iso(inDays(3)),
    venue: 'Kalaja e Prizrenit, skena kryesore pranë lumit Lumbardhi', priceText: '5 € në hyrje, 3 € për studentë',
    description: 'Përshkrim i gjatë. '.repeat(60),
  },
  { externalId: 'qa-' + randomUUID(), kind: 'event', title: longWord, url: `https://example.com/events/${randomUUID()}`, city: 'Pejë', startsAt: iso(inDays(4)) },
  { externalId: 'qa-' + randomUUID(), kind: 'event', title: `QA ${tag} pa qytet`, url: `https://example.com/events/${randomUUID()}`, startsAt: iso(inDays(5)) },
  {
    externalId: 'qa-' + randomUUID(), kind: 'deal', title: `QA ${tag} Zbritje e madhe në laptopë gaming me ekran 17 inç dhe RTX`,
    url: `https://example.com/deals/${randomUUID()}`, city: 'Prishtinë', merchant: 'Dyqani Teknologjik i Madh sh.p.k.',
    oldPrice: 1299.99, newPrice: 999.99, currency: 'EUR', validUntil: iso(inDays(9)),
  },
]
const src = await (await api.post('/api/admin/sources', {
  data: { name: `QA tour ${tag}`, type: 'fixture', baseUrl: null, fixturePayload: JSON.stringify({ items }), defaultKind: 'event', pollIntervalMinutes: 1440, isActive: false, isUnreliable: false },
})).json()
await api.post(`/api/admin/sources/${src.id}/fetch`)
const cands = (await (await api.get(`/api/admin/candidates?sourceId=${src.id}&pageSize=50`)).json()).items
const byTitle = Object.fromEntries(cands.map((c) => [c.title, c]))
const evId = (await (await api.post(`/api/admin/candidates/${byTitle[longTitle].id}/approve`)).json()).itemId
const dealCand = cands.find((c) => c.kind === 'deal')
const dealId = (await (await api.post(`/api/admin/candidates/${dealCand.id}/approve`)).json()).itemId
const pendingCand = byTitle[`QA ${tag} pa qytet`] ?? cands.find((c) => c.status === 'pending')
const longWordCand = byTitle[longWord]
await api.post(`/api/admin/candidates/${longWordCand.id}/approve`)
fs.writeFileSync(`${OUT}/tour-data.json`, JSON.stringify({ tag, src: src.id, evId, dealId, pendingCand: pendingCand.id }, null, 1))

const publicPages = [
  ['home', '/'],
  ['home-evente-tab', '/', async (p) => p.getByRole('tab', { name: 'Evente' }).click()],
  ['home-oferta-tab', '/', async (p) => p.getByRole('tab', { name: 'Oferta' }).click()],
  ['events', '/events'],
  ['events-empty', '/events?search=zzzznothing'],
  ['events-prizren', '/events?city=prizren'],
  ['event-detail', `/events/${evId}`],
  ['deals', '/deals'],
  ['deal-detail', `/deals/${dealId}`],
  ['search-empty', '/search'],
  ['search-results', `/search?q=QA%20${tag}`],
  ['notfound', '/nope'],
  ['event-404', `/events/${randomUUID()}`],
]
const adminPages = [
  ['admin-dashboard', '/admin'],
  ['admin-review', '/admin/review'],
  ['admin-review-qa', `/admin/review?sourceId=${src.id}&status=all`],
  ['admin-candidate', `/admin/items/${pendingCand.id}`],
  ['admin-candidate-published', `/admin/items/${byTitle[longTitle].id}`],
  ['admin-published', '/admin/published'],
  ['admin-published-edit', `/admin/published/event/${evId}`],
  ['admin-published-edit-deal', `/admin/published/deal/${dealId}`],
  ['admin-sources', '/admin/sources'],
  ['admin-sources-form', '/admin/sources', async (p) => p.getByRole('button', { name: 'Shto burim' }).click()],
  ['admin-users', '/admin/users'],
]

const viewports = {
  desktop: { viewport: { width: 1280, height: 800 } },
  pixel7: devices['Pixel 7'],
  w360: { viewport: { width: 360, height: 780 }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 },
}

const report = []
const browser = await chromium.launch()
for (const [vname, opts] of Object.entries(viewports)) {
  const ctx = await browser.newContext({ ...opts, baseURL: BASE })
  const page = await ctx.newPage()
  const errors = []
  page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errors.push(`${m.type()}: ${m.text()}`) })
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message))
  const shoot = async (name, path, action) => {
    errors.length = 0
    await page.goto(path)
    await page.waitForLoadState('networkidle')
    if (action) { await action(page); await page.waitForLoadState('networkidle') }
    await page.waitForTimeout(300)
    const ov = await page.evaluate(() => {
      const w = document.documentElement.clientWidth
      const offenders = []
      for (const el of document.querySelectorAll('body *')) {
        const r = el.getBoundingClientRect()
        if (r.right > w + 1 && r.width > 0) {
          // skip descendants of a horizontally scrollable container
          let p = el.parentElement, scrolled = false
          while (p) { const s = getComputedStyle(p); if (s.overflowX === 'auto' || s.overflowX === 'scroll' || s.overflowX === 'hidden') { scrolled = true; break } p = p.parentElement }
          if (!scrolled) offenders.push(`${el.tagName.toLowerCase()}.${(el.className || '').toString().slice(0, 40)} right=${Math.round(r.right)}`)
        }
      }
      return { scrollWidth: document.documentElement.scrollWidth, clientWidth: w, offenders: offenders.slice(0, 5) }
    })
    await page.screenshot({ path: `${OUT}/${vname}-${name}.png`, fullPage: true })
    report.push({ vname, name, overflow: ov.scrollWidth > ov.clientWidth ? ov : null, errors: [...errors] })
  }
  for (const [n, p, a] of publicPages) await shoot(n, p, a)
  // login page + error states
  await shoot('admin-login', '/admin/login')
  await page.getByLabel('Email').fill('nobody@x.test')
  await page.getByLabel('Fjalëkalimi').fill('wrong-password-123')
  await page.getByRole('button', { name: 'Hyr' }).click()
  await page.waitForTimeout(800)
  await page.screenshot({ path: `${OUT}/${vname}-admin-login-error.png`, fullPage: true })
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await page.getByRole('heading', { name: 'Paneli' }).waitFor()
  for (const [n, p, a] of adminPages) await shoot(n, p, a)
  await ctx.close()
}
await browser.close()
fs.writeFileSync(`${OUT}/tour-report.json`, JSON.stringify(report, null, 1))
for (const r of report) if (r.overflow || r.errors.length) console.log(JSON.stringify(r))
console.log('done', report.length)
