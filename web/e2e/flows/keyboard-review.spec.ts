// REQ-114 regression (T091 QA): the whole review loop with the keyboard only, with a visible focus indicator on
// every stop: login -> review queue -> filter -> approve -> reject (dialog) -> logout.
import { randomUUID } from 'node:crypto'
import { expect, request, test, type Page } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }
const startsAt = new Date(Date.now() + 6 * 86_400_000).toISOString().replace(/\.\d{3}Z$/, '+00:00')

/** The focused element must show a focus indicator (outline from :focus-visible, or a box-shadow ring). */
async function expectVisibleFocus(page: Page) {
  const f = await page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null
    if (!el || el === document.body) return { ok: true, what: 'body' } // end of the tab cycle
    const s = getComputedStyle(el)
    const ring =
      (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || s.boxShadow !== 'none'
    // Native date inputs show focus on the active segment instead of the outline.
    const ok = ring || (el instanceof HTMLInputElement && el.type.startsWith('date'))
    return {
      ok,
      what: `${el.tagName} ${el.getAttribute('aria-label') ?? el.textContent?.slice(0, 30)}`,
    }
  })
  expect(f.ok, `visible focus on ${f.what}`).toBe(true)
}

/** Presses Tab (checking every stop for visible focus) until the focused element has the accessible name. */
async function tabTo(page: Page, name: string | RegExp, max = 40) {
  for (let i = 0; i < max; i++) {
    await page.keyboard.press('Tab')
    await expectVisibleFocus(page)
    const label = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null
      if (!el) return ''
      const byLabel = (el as HTMLInputElement).labels?.[0]?.textContent
      return (el.getAttribute('aria-label') ?? byLabel ?? el.textContent ?? '').trim()
    })
    if (typeof name === 'string' ? label === name : name.test(label)) return
  }
  throw new Error(`no focus stop named ${String(name)} within ${max} tabs`)
}

test('REQ-114: login, review, approve, reject and logout with the keyboard only', async ({
  page,
}) => {
  // Seed two valid pending candidates from a fresh fixture source (outside the browser session).
  const tag = randomUUID().slice(0, 8)
  const titles = [`Tastiera A ${tag}`, `Tastiera B ${tag}`]
  const api = await request.newContext({ baseURL: 'http://localhost:5173' })
  expect((await api.post('/api/auth/login', { data: ADMIN })).status()).toBe(200)
  const sourceName = `Tastiera ${tag}`
  const created = await api.post('/api/admin/sources', {
    data: {
      name: sourceName,
      type: 'fixture',
      baseUrl: null,
      fixturePayload: JSON.stringify({
        items: titles.map((title) => ({
          externalId: `kb-${randomUUID()}`,
          kind: 'event',
          title,
          url: `https://example.com/events/${randomUUID()}`,
          city: 'Prishtinë',
          startsAt,
        })),
      }),
      defaultKind: 'event',
      pollIntervalMinutes: 1440,
      isActive: false,
      isUnreliable: false,
    },
  })
  const { id: sourceId } = (await created.json()) as { id: string }
  expect((await api.post(`/api/admin/sources/${sourceId}/fetch`)).status()).toBe(200)
  await api.dispose()

  // Login.
  await page.goto('/admin/login')
  await expect(page.getByRole('heading', { level: 1, name: 'Hyrje për administratorë' })).toBeVisible()
  await tabTo(page, 'Email')
  await page.keyboard.type(ADMIN.email)
  await tabTo(page, 'Fjalëkalimi')
  await page.keyboard.type(ADMIN.password)
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  // Review queue, filtered to the seeded source by typing into the focused "Burimi" select.
  await tabTo(page, 'Shqyrtimi')
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { level: 1, name: 'Radha e shqyrtimit' })).toBeVisible()
  await page.getByLabel('Burimi').focus()
  await page.getByLabel('Burimi').selectOption({ label: sourceName }) // equals arrow/type-ahead on the focused select
  await expect(page).toHaveURL(new RegExp(`sourceId=${sourceId}`))

  // Approve A.
  await expect(page.getByRole('link', { name: titles[0] })).toBeVisible()
  await tabTo(page, titles[0])
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { level: 1, name: titles[0] })).toBeVisible()
  await tabTo(page, 'Aprovo')
  await page.keyboard.press('Enter')
  await expect(page.getByRole('status').filter({ hasText: 'U publikua' })).toBeVisible()

  // Back to the queue (browser back), then reject B through the dialog.
  await page.goBack()
  await expect(page.getByRole('link', { name: titles[1] })).toBeVisible()
  await tabTo(page, titles[1])
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { level: 1, name: titles[1] })).toBeVisible()
  await tabTo(page, 'Refuzo')
  await page.keyboard.press('Enter')
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByLabel('Arsyeja')).toBeFocused()
  await page.keyboard.type('Nuk është event publik (test me tastierë).')
  await tabTo(page, 'Anulo', 3)
  await tabTo(page, 'Konfirmo refuzimin', 3)
  await page.keyboard.press('Enter')
  await expect(dialog).toHaveCount(0)
  await expect(page.getByText('Statusi: Refuzuar')).toBeVisible()

  // REQ-112: the queue shows it as "Refuzuar".
  await page.goto(`/admin/review?sourceId=${sourceId}&status=all`)
  const row = page.getByRole('row').filter({ has: page.getByRole('link', { name: titles[1] }) })
  await expect(row.getByRole('cell').last()).toHaveText('Refuzuar')

  // Logout.
  await tabTo(page, 'Dil')
  await page.keyboard.press('Enter')
  await expect(
    page.getByRole('heading', { level: 1, name: 'Hyrje për administratorë' }),
  ).toBeVisible()
})
