import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

const startsAt = new Date(Date.now() + 5 * 86_400_000).toISOString().replace(/\.\d{3}Z$/, '+00:00')
const eventItem = (title: string) => ({
  externalId: `ev-${randomUUID()}`,
  kind: 'event',
  title,
  url: `https://example.com/events/${randomUUID()}`,
  city: 'Prishtinë',
  startsAt,
})

test('a reviewer approves selected valid rows with the keyboard; a stale row is reported as failed', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  const tag = randomUUID().slice(0, 8)
  const titles = [`Bulk A ${tag}`, `Bulk B ${tag}`, `Bulk C ${tag}`]
  const api = page.request
  const source = await api.post('/api/admin/sources', {
    data: {
      name: `Bulk ${tag}`,
      type: 'fixture',
      baseUrl: null,
      fixturePayload: JSON.stringify({ items: titles.map(eventItem) }),
      defaultKind: 'event',
      pollIntervalMinutes: 1440,
      isActive: false,
      isUnreliable: false,
    },
  })
  const { id: sourceId } = (await source.json()) as { id: string }
  expect((await api.post(`/api/admin/sources/${sourceId}/fetch`)).status()).toBe(200)
  const list = (await (await api.get(`/api/admin/candidates?sourceId=${sourceId}&pageSize=50`)).json()) as {
    items: { id: string; title: string }[]
  }
  const ids = new Map(list.items.map((c) => [c.title, c.id]))

  await page.goto(`/admin/review?sourceId=${sourceId}&validation=valid`)
  for (const t of titles) await expect(page.getByRole('checkbox', { name: `Zgjidh ${t}` })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Aprovo të zgjedhurat' })).toBeDisabled()

  // Someone else approves C after the list loaded: the bulk call on it must come back 409.
  expect((await api.post(`/api/admin/candidates/${ids.get(titles[2])}/approve`)).status()).toBe(200)

  for (const t of titles) {
    const box = page.getByRole('checkbox', { name: `Zgjidh ${t}` })
    await box.focus()
    await page.keyboard.press('Space')
    await expect(box).toBeChecked()
  }
  const approve = page.getByRole('button', { name: 'Aprovo të zgjedhurat' })
  await approve.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('status')).toContainText('2 u aprovuan, 1 dështuan')
  await expect(page.getByRole('checkbox', { name: `Zgjidh ${titles[0]}` })).toHaveCount(0)
})
