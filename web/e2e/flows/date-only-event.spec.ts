// REQ-025: an event with a date but no time is valid, is published at local midnight and is shown with
// "gjatë ditës" instead of a time on the public card.
import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

const day = new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 10)
const [y, m, d] = day.split('-')

test('a date-only event is approved and shown with the date and "gjatë ditës"', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  const tag = randomUUID().slice(0, 8)
  const title = `Dite ${tag}`
  const api = page.request
  const source = await api.post('/api/admin/sources', {
    data: {
      name: `Dite ${tag}`,
      type: 'fixture',
      baseUrl: null,
      fixturePayload: JSON.stringify({
        items: [
          {
            externalId: `d-${randomUUID()}`,
            kind: 'event',
            title,
            url: `https://example.com/events/${randomUUID()}`,
            city: 'Prishtinë',
            startDate: day,
          },
        ],
      }),
      defaultKind: 'event',
      pollIntervalMinutes: 1440,
      isActive: false,
      isUnreliable: false,
    },
  })
  const { id: sourceId } = (await source.json()) as { id: string }
  expect((await api.post(`/api/admin/sources/${sourceId}/fetch`)).status()).toBe(200)
  const list = (await (await api.get(`/api/admin/candidates?sourceId=${sourceId}`)).json()) as {
    items: { id: string; validation: string }[]
  }
  expect(list.items[0].validation).toBe('valid')
  expect((await api.post(`/api/admin/candidates/${list.items[0].id}/approve`)).status()).toBe(200)

  await page.goto(`/events?search=${tag}`)
  const card = page.getByRole('article').filter({ hasText: title })
  await expect(card).toContainText(`${d}.${m}.${y} · gjatë ditës`)
  await expect(card).not.toContainText('00:00')
})
