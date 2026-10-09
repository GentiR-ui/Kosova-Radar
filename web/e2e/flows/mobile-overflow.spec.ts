// T091 QA regression: no page scrolls sideways at 360 px (design.md §7 / non-functional "usable at 360 px").
// Wide admin tables may scroll inside their own container, but the document itself must not get wider than the screen.
import { randomUUID } from 'node:crypto'
import { expect, request, test, type Page } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

test.use({ viewport: { width: 360, height: 780 } })

let eventId = ''
let candidateId = ''

test.beforeAll(async () => {
  // One published event with a long title and a long unbroken word, plus one pending candidate.
  const tag = randomUUID().slice(0, 8)
  const startsAt = new Date(Date.now() + 9 * 86_400_000)
    .toISOString()
    .replace(/\.\d{3}Z$/, '+00:00')
  const api = await request.newContext({ baseURL: 'http://localhost:5173' })
  expect((await api.post('/api/auth/login', { data: ADMIN })).status()).toBe(200)
  const items = [
    `Overflow ${tag} Festivali Ndërkombëtar i Filmit Dokumentar dhe të Shkurtër në Prizren me shfaqje çdo natë`,
    `Overflow${tag}-${'x'.repeat(90)}`,
  ].map((title) => ({
    externalId: `ov-${randomUUID()}`,
    kind: 'event',
    title,
    url: `https://example.com/events/${randomUUID()}`,
    city: 'Prizren',
    venue: 'Kalaja e Prizrenit, skena kryesore pranë lumit Lumbardhi',
    startsAt,
  }))
  const created = await api.post('/api/admin/sources', {
    data: {
      name: `Overflow ${tag}`,
      type: 'fixture',
      baseUrl: null,
      fixturePayload: JSON.stringify({ items }),
      defaultKind: 'event',
      pollIntervalMinutes: 1440,
      isActive: false,
      isUnreliable: false,
    },
  })
  const { id: sourceId } = (await created.json()) as { id: string }
  expect((await api.post(`/api/admin/sources/${sourceId}/fetch`)).status()).toBe(200)
  const list = (await (
    await api.get(`/api/admin/candidates?sourceId=${sourceId}&pageSize=10`)
  ).json()) as {
    items: { id: string; title: string }[]
  }
  const [first, second] = list.items
  const approved = await api.post(`/api/admin/candidates/${first.id}/approve`)
  eventId = ((await approved.json()) as { itemId: string }).itemId
  candidateId = second.id
  await api.dispose()
})

async function expectNoSidewaysScroll(page: Page, path: string) {
  await page.goto(path)
  await page.waitForLoadState('networkidle')
  await expect(page.locator('h1').first()).toBeVisible()
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }))
  expect(scrollWidth, `${path} document width`).toBeLessThanOrEqual(clientWidth)
}

const publicPaths = ['/', '/events', '/deals', '/search?q=Overflow', () => `/events/${eventId}`]
for (const p of publicPaths) {
  test(`public ${typeof p === 'string' ? p : '/events/:id'} fits 360 px`, async ({ page }) => {
    await expectNoSidewaysScroll(page, typeof p === 'string' ? p : p())
  })
}

const adminPaths = [
  '/admin',
  '/admin/review',
  '/admin/published',
  () => `/admin/items/${candidateId}`,
]
for (const p of adminPaths) {
  test(`admin ${typeof p === 'string' ? p : '/admin/items/:id'} fits 360 px`, async ({ page }) => {
    await page.request.post('/api/auth/login', { data: ADMIN })
    await expectNoSidewaysScroll(page, typeof p === 'string' ? p : p())
  })
}

// Known failure (T091 QA finding): the sr-only "Veprimet"/"Veprime" header text is position:absolute without a
// positioned ancestor, so it escapes the table's overflow-x-auto wrapper and widens the document (569 / 475 px).
// Enable these once the wrappers get `relative`.
for (const p of ['/admin/sources', '/admin/users']) {
  test.fixme(`admin ${p} fits 360 px`, async ({ page }) => {
    await page.request.post('/api/auth/login', { data: ADMIN })
    await expectNoSidewaysScroll(page, p)
  })
}
