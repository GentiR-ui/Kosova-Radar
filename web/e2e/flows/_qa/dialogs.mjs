// Scratch: focus behaviour of every dialog (return on close, trap, after action).
import { chromium, request } from '@playwright/test'
import { randomUUID } from 'node:crypto'
const BASE = 'http://localhost:5173'
const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }
const api = await request.newContext({ baseURL: BASE })
await api.post('/api/auth/login', { data: ADMIN })
const tag = randomUUID().slice(0, 6)
const startsAt = new Date(Date.now() + 7 * 86400000).toISOString().replace(/\.\d{3}Z$/, '+00:00')
const items = [1, 2, 3].map((n) => ({ externalId: `dlg-${randomUUID()}`, kind: 'event', title: `Dialog ${n} ${tag}`, url: `https://example.com/e/${randomUUID()}`, city: 'Prishtinë', startsAt }))
const src = await (await api.post('/api/admin/sources', { data: { name: `Dialog ${tag}`, type: 'fixture', baseUrl: null, fixturePayload: JSON.stringify({ items }), defaultKind: 'event', pollIntervalMinutes: 1440, isActive: false, isUnreliable: false } })).json()
await api.post(`/api/admin/sources/${src.id}/fetch`)
const cands = (await (await api.get(`/api/admin/candidates?sourceId=${src.id}&pageSize=10`)).json()).items
const c = Object.fromEntries(cands.map((x) => [x.title, x.id]))
const item3 = (await (await api.post(`/api/admin/candidates/${c[`Dialog 3 ${tag}`]}/approve`)).json()).itemId

const browser = await chromium.launch()
const page = await (await browser.newContext({ viewport: { width: 1280, height: 800 }, baseURL: BASE })).newPage()
const errs = []
page.on('console', (m) => m.type() === 'error' && errs.push(m.text()))
await page.request.post('/api/auth/login', { data: ADMIN })
const focused = () => page.evaluate(() => { const e = document.activeElement; return e === document.body ? 'BODY' : `${e.tagName}:${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30)}` })
const log = async (what) => console.log(what.padEnd(55), '->', await focused())
const tabs = async (n) => { const out = []; for (let i = 0; i < n; i++) { await page.keyboard.press('Tab'); out.push(await focused()) } return out.join(' | ') }

// Reject dialog
await page.goto(`/admin/items/${c[`Dialog 1 ${tag}`]}`)
await page.getByRole('button', { name: 'Refuzo' }).focus(); await page.keyboard.press('Enter')
await log('reject dialog open')
console.log('  tab x6 inside reject dialog:', await tabs(6))
await page.keyboard.press('Escape'); await log('reject dialog Escape')
await page.getByRole('button', { name: 'Refuzo' }).focus(); await page.keyboard.press('Enter')
await page.getByRole('button', { name: 'Anulo' }).focus(); await page.keyboard.press('Enter'); await log('reject dialog Anulo')
// empty reason
await page.getByRole('button', { name: 'Refuzo' }).focus(); await page.keyboard.press('Enter')
console.log('  confirm disabled with empty reason:', await page.getByRole('button', { name: 'Konfirmo refuzimin' }).isDisabled())
await page.keyboard.type('   '); console.log('  confirm disabled with spaces:', await page.getByRole('button', { name: 'Konfirmo refuzimin' }).isDisabled())
await page.keyboard.press('Escape')
// merge dialog
await page.getByRole('button', { name: 'Bashko' }).focus(); await page.keyboard.press('Enter')
await log('merge dialog open')
console.log('  tab x4 inside merge dialog:', await tabs(4))
await page.keyboard.press('Escape'); await log('merge dialog Escape')
await page.getByRole('button', { name: 'Bashko' }).focus(); await page.keyboard.press('Enter')
await page.getByLabel('Kërko artikullin').fill(`Dialog 3 ${tag}`)
await page.getByRole('button', { name: 'Bashko me këtë' }).first().waitFor()
await page.screenshot({ path: 'test-results/qa/desktop-merge-dialog.png' })
await page.getByRole('button', { name: 'Bashko me këtë' }).first().focus(); await page.keyboard.press('Enter')
await page.getByText('Kandidati u bashkua.').waitFor(); await log('after merge')
// approve
await page.goto(`/admin/items/${c[`Dialog 2 ${tag}`]}`)
await page.getByRole('button', { name: 'Aprovo' }).focus(); await page.keyboard.press('Enter')
await page.getByText('U publikua').waitFor(); await page.waitForTimeout(500); await log('after approve')
// confirm dialog on published
await page.goto('/admin/published')
const row = page.getByRole('row').filter({ hasText: `Dialog 3 ${tag}` })
await row.getByRole('button', { name: 'Skado' }).focus(); await page.keyboard.press('Enter')
await log('Skado confirm open')
await page.screenshot({ path: 'test-results/qa/desktop-confirm-dialog.png' })
console.log('  tab x4 inside confirm:', await tabs(4))
await page.keyboard.press('Escape'); await log('Skado Escape')
await row.getByRole('button', { name: 'Tërhiq' }).focus(); await page.keyboard.press('Enter')
await page.getByRole('button', { name: 'Anulo' }).focus(); await page.keyboard.press('Enter'); await log('Tërhiq Anulo')
await row.getByRole('button', { name: 'Tërhiq' }).focus(); await page.keyboard.press('Enter')
await page.getByRole('button', { name: 'Po, tërhiq' }).focus(); await page.keyboard.press('Enter')
await page.waitForTimeout(1000); await log('after Po, tërhiq')
console.log('  row count after withdraw', await row.count(), ' status texts:', await page.getByRole('status').allTextContents())
// users
await page.goto('/admin/users')
await page.getByRole('button', { name: /^Çaktivizo/ }).first().focus(); await page.keyboard.press('Enter')
await log('Çaktivizo open'); await page.keyboard.press('Escape'); await log('Çaktivizo Escape')
// sources form
await page.goto('/admin/sources')
await page.getByRole('button', { name: 'Shto burim' }).focus(); await page.keyboard.press('Enter'); await log('Shto burim')
await page.getByRole('button', { name: 'Ruaj burimin' }).click(); await page.waitForTimeout(500); await log('Ruaj burimin empty')
await page.screenshot({ path: 'test-results/qa/desktop-source-form-errors.png' })
console.log('  alerts:', await page.getByRole('alert').allTextContents())
console.log('console errors:', errs)
await browser.close()
