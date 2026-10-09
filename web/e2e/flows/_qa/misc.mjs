// Scratch: misc flows — logout+back, reviewer role, form validation, refresh mid-edit, slow network, error states.
import { chromium, request } from '@playwright/test'
import { randomUUID } from 'node:crypto'
const BASE = 'http://localhost:5173'
const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }
const OUT = 'test-results/qa'
const browser = await chromium.launch()
const newPage = async (vp = { width: 1280, height: 800 }) => {
  const page = await (await browser.newContext({ viewport: vp, baseURL: BASE })).newPage()
  page.errs = []
  page.on('console', (m) => m.type() === 'error' && !/status of (401|404|400|409|500|503)/.test(m.text()) && page.errs.push(m.text()))
  page.on('pageerror', (e) => page.errs.push('pageerror ' + e.message))
  return page
}
const login = async (page, u = ADMIN) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(u.email)
  await page.getByLabel('Fjalëkalimi').fill(u.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await page.getByRole('heading', { name: 'Paneli' }).waitFor()
}
const say = (...a) => console.log(...a)

// 1. logout then Back
{
  const page = await newPage()
  await login(page)
  await page.getByRole('link', { name: 'Shqyrtimi' }).click()
  await page.getByRole('heading', { name: 'Radha e shqyrtimit' }).waitFor()
  await page.getByRole('button', { name: 'Dil' }).click()
  await page.getByRole('heading', { name: 'Hyrje për administratorë' }).waitFor()
  await page.goBack(); await page.waitForTimeout(1500)
  say('1 logout+back ->', page.url(), '| h1:', await page.locator('h1').first().textContent().catch(() => null))
  await page.screenshot({ path: `${OUT}/desktop-logout-back.png` })
  // signed-in user opening /admin/login
  await login(page); await page.goto('/admin/login'); await page.waitForTimeout(800)
  say('1b signed-in /admin/login ->', page.url())
  say('   errs', page.errs)
}

// 2. reviewer role
const api = await request.newContext({ baseURL: BASE })
await api.post('/api/auth/login', { data: ADMIN })
const reviewer = { email: `qa-rev-${randomUUID().slice(0, 6)}@test.local`, password: 'Strong-pass-2026!' }
await api.post('/api/admin/users', { data: { ...reviewer, role: 'reviewer' } })
{
  const page = await newPage({ width: 360, height: 780 })
  await login(page, reviewer)
  say('2 reviewer nav:', (await page.getByRole('navigation').innerText()).replace(/\s+/g, ' '))
  await page.goto('/admin/sources'); await page.waitForTimeout(800)
  say('  /admin/sources ->', (await page.getByRole('main').innerText()).slice(0, 80))
  await page.screenshot({ path: `${OUT}/w360-reviewer-sources-forbidden.png` })
  await page.goto('/admin/users'); await page.waitForTimeout(800)
  say('  /admin/users ->', (await page.getByRole('main').innerText()).slice(0, 80))
  say('   errs', page.errs)
}

// 3. users form validation
{
  const page = await newPage()
  await login(page)
  await page.goto('/admin/users')
  await page.getByLabel('Email').fill('bad-email')
  await page.getByLabel('Fjalëkalimi').fill('short')
  await page.getByRole('button', { name: 'Krijo përdoruesin' }).click(); await page.waitForTimeout(800)
  say('3 users invalid ->', await page.getByRole('alert').allTextContents(), '| focused:', await page.evaluate(() => document.activeElement?.id))
  await page.screenshot({ path: `${OUT}/desktop-users-invalid.png` })
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill('Strong-pass-2026!')
  await page.getByRole('button', { name: 'Krijo përdoruesin' }).click(); await page.waitForTimeout(800)
  say('  duplicate ->', await page.getByRole('alert').allTextContents())
  const own = page.getByRole('row').filter({ hasText: ADMIN.email })
  say('  own row buttons:', await own.getByRole('button').count())
  // deactivate the reviewer via the dialog
  const row = page.getByRole('row').filter({ hasText: reviewer.email })
  await row.getByRole('button', { name: /Çaktivizo/ }).click()
  await page.getByRole('button', { name: 'Po, çaktivizo' }).click(); await page.waitForTimeout(800)
  say('  after deactivate row:', (await row.innerText()).replace(/\s+/g, ' '), '| status:', await page.getByRole('status').allTextContents())
  say('   errs', page.errs)
}

// 4. source form: html adapter visibility + server validation
{
  const page = await newPage()
  await login(page)
  await page.goto('/admin/sources')
  await page.getByRole('button', { name: 'Shto burim' }).click()
  say('4 adapter visible (json):', await page.getByLabel('Adapteri').isVisible().catch(() => false))
  await page.getByLabel('Lloji', { exact: true }).selectOption('html')
  say('  adapter visible (html):', await page.getByLabel('Adapteri').isVisible().catch(() => false), 'fixture json visible:', await page.getByLabel('Të dhënat testuese (JSON)').isVisible().catch(() => false))
  await page.getByLabel('Emri').fill(`QA html ${randomUUID().slice(0, 4)}`)
  await page.getByLabel('URL').fill('https://example.com/x')
  await page.getByLabel('Intervali (minuta)').fill('5')
  await page.getByRole('button', { name: 'Ruaj burimin' }).click(); await page.waitForTimeout(1000)
  say('  interval 5 ->', await page.getByRole('alert').allTextContents(), 'invalid fields:', await page.locator('[aria-invalid="true"]').evaluateAll((e) => e.map((x) => x.id)))
  await page.screenshot({ path: `${OUT}/desktop-source-invalid-interval.png`, fullPage: false })
  // fixture type: payload field
  await page.getByLabel('Lloji', { exact: true }).selectOption('fixture')
  say('  fixture: json visible:', await page.getByLabel('Të dhënat testuese (JSON)').isVisible().catch(() => false), 'url visible:', await page.getByLabel('URL').isVisible().catch(() => false))
  await page.getByRole('button', { name: 'Anulo' }).click()
  say('   errs', page.errs)
}

// 5. candidate edit, then refresh mid-edit, then save validation
{
  const page = await newPage()
  await login(page)
  const items = [{ externalId: `m-${randomUUID()}`, kind: 'event', title: `Misc ${randomUUID().slice(0, 6)}`, url: `https://example.com/${randomUUID()}`, city: 'Prishtinë', startsAt: new Date(Date.now() + 8 * 864e5).toISOString() }]
  const src = await (await api.post('/api/admin/sources', { data: { name: `Misc ${randomUUID().slice(0, 6)}`, type: 'fixture', baseUrl: null, fixturePayload: JSON.stringify({ items }), defaultKind: 'event', pollIntervalMinutes: 1440, isActive: false, isUnreliable: false } })).json()
  await api.post(`/api/admin/sources/${src.id}/fetch`)
  const cand = (await (await api.get(`/api/admin/candidates?sourceId=${src.id}`)).json()).items[0]
  await page.goto(`/admin/items/${cand.id}`)
  await page.getByLabel('Vendi').fill('Teatri Kombëtar')
  let dialogSeen = false
  page.on('dialog', async (d) => { dialogSeen = true; await d.dismiss() })
  await page.reload(); await page.waitForTimeout(800)
  say('5 refresh with unsaved edit: beforeunload prompt?', dialogSeen, '| venue after reload:', await page.getByLabel('Vendi').inputValue())
  await page.getByLabel('Lidhja e burimit').fill('not a url')
  await page.getByRole('button', { name: 'Ruaj ndryshimet' }).click(); await page.waitForTimeout(800)
  say('  invalid url save ->', await page.getByRole('alert').allTextContents(), 'invalid:', await page.locator('[aria-invalid="true"]').evaluateAll((e) => e.map((x) => x.id)), 'desc:', await page.getByLabel('Lidhja e burimit').getAttribute('aria-describedby'))
  await page.screenshot({ path: `${OUT}/desktop-candidate-invalid-url.png` })
  await page.getByLabel('Lidhja e burimit').fill(items[0].url)
  await page.getByLabel('Qyteti').selectOption({ index: 0 })
  await page.getByRole('button', { name: 'Ruaj ndryshimet' }).click(); await page.waitForTimeout(800)
  say('  cleared city save ->', await page.getByRole('status').allTextContents(), 'issues:', await page.getByRole('list', { name: 'Problemet e validimit' }).innerText().catch(() => '(none)'))
  await page.getByRole('button', { name: 'Aprovo', exact: true }).click(); await page.waitForTimeout(800)
  say('  approve w/o city ->', await page.getByRole('alert').allTextContents())
  say('   errs', page.errs)
}

// 6. slow network + error state + retry on public lists and admin queue
{
  const page = await newPage({ width: 360, height: 780 })
  let fail = true
  await page.route('**/api/events?**', async (route) => {
    await new Promise((r) => setTimeout(r, 1500))
    if (fail) return route.fulfill({ status: 503, body: '' })
    return route.continue()
  })
  await page.goto('/events')
  await page.waitForTimeout(400)
  say('6 loading state:', await page.getByRole('status').allTextContents())
  await page.screenshot({ path: `${OUT}/w360-events-loading.png` })
  await page.getByRole('button', { name: 'Provo sërish' }).waitFor({ timeout: 15000 })
  say('  error state:', (await page.getByRole('alert').allTextContents()).join('|'))
  await page.screenshot({ path: `${OUT}/w360-events-error.png` })
  fail = false
  await page.getByRole('button', { name: 'Provo sërish' }).click()
  await page.getByRole('article').first().waitFor({ timeout: 15000 })
  say('  after retry articles:', await page.getByRole('article').count())
  await page.goto('/events?search=zzzqqq'); await page.waitForTimeout(2500)
  say('  empty:', await page.getByText('Nuk u gjet asnjë rezultat').isVisible())
  await page.screenshot({ path: `${OUT}/w360-events-empty.png` })
  // admin queue error state
  await page.request.post('/api/auth/login', { data: ADMIN })
  await page.route('**/api/admin/candidates?**', (route) => route.fulfill({ status: 500, body: '' }))
  await page.goto('/admin/review'); await page.waitForTimeout(4000)
  say('  admin queue 500 ->', await page.getByRole('alert').allTextContents())
  await page.screenshot({ path: `${OUT}/w360-review-error.png` })
  say('   errs', page.errs)
}
await browser.close()
