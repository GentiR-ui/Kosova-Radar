// Scratch: approve some live HTML candidates (single + bulk) through the UI and check REQ-022 on the result.
import { chromium } from '@playwright/test'
import fs from 'node:fs'
const SRC = { prishtina: '67eb92da-005a-43f7-b804-93ecc9b68fe7', kapodalim: 'effd1314-0964-4e00-83df-e7b365c0e2df' }
const browser = await chromium.launch()
const out = {}
for (const [vname, vp] of [['desktop', { width: 1280, height: 800 }], ['w360', { width: 360, height: 780 }]]) {
  const page = await (await browser.newContext({ viewport: vp, baseURL: 'http://localhost:5173' })).newPage()
  await page.request.post('/api/auth/login', { data: { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' } })
  const list = async (s) => (await (await page.request.get(`/api/admin/candidates?sourceId=${s}&pageSize=100&validation=valid`)).json()).items
  const p = (await list(SRC.prishtina)).filter((c) => c.status === 'pending')
  const k = (await list(SRC.kapodalim)).filter((c) => c.status === 'pending')
  // Single approve: one from each source, via the candidate page.
  for (const c of [p[0], k.find((x) => x.title.startsWith('DUA')) ?? k[0]]) {
    await page.goto(`/admin/items/${c.id}`)
    await page.getByRole('heading', { level: 1 }).waitFor()
    const open = page.getByRole('link', { name: 'Hap burimin' })
    const href = await open.getAttribute('href')
    const ev = await page.getByRole('region', { name: 'Evidenca nga burimi' }).locator('pre').textContent()
    await page.screenshot({ path: `test-results/qa/${vname}-live-candidate-${c.id.slice(0, 6)}.png`, fullPage: true })
    await page.getByRole('button', { name: 'Aprovo', exact: true }).click()
    await page.getByText('U publikua').waitFor()
    const pub = await page.getByRole('link', { name: 'Shiko faqen publike' }).getAttribute('href')
    out[c.title] = { mode: `single/${vname}`, href, evidence: ev, publicPage: pub }
    await page.goto(pub)
    await page.getByRole('heading', { level: 1 }).waitFor()
    await page.screenshot({ path: `test-results/qa/${vname}-live-public-${c.id.slice(0, 6)}.png`, fullPage: true })
    out[c.title].sourceLink = await page.getByRole('link', { name: /^Burimi:/ }).getAttribute('href')
    out[c.title].publicText = (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 300)
    if (vname === 'w360') break
  }
  if (vname === 'w360') break
  // Bulk approve: two rows per source from the queue.
  for (const s of Object.values(SRC)) {
    await page.goto(`/admin/review?sourceId=${s}&validation=valid`)
    const boxes = page.getByRole('checkbox', { name: /^Zgjidh / })
    await boxes.first().waitFor()
    const names = []
    for (let i = 0; i < 2; i++) { names.push(await boxes.nth(i).getAttribute('aria-label')); await boxes.nth(i).check() }
    await page.getByRole('button', { name: 'Aprovo të zgjedhurat' }).click()
    const st = page.getByRole('status').filter({ hasText: 'u aprovuan' })
    await st.waitFor()
    out[`bulk ${s.slice(0, 4)}`] = { names, result: await st.textContent() }
    await page.screenshot({ path: `test-results/qa/desktop-live-bulk-${s.slice(0, 4)}.png` })
  }
}
fs.writeFileSync('test-results/qa/live-approve.json', JSON.stringify(out, null, 1))
console.log(JSON.stringify(out, null, 1))
await browser.close()
