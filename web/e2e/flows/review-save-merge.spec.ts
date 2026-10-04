import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext } from '@playwright/test'

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

async function ingest(api: APIRequestContext, titles: string[]) {
  const source = await api.post('/api/admin/sources', {
    data: {
      name: `Flow ${randomUUID().slice(0, 8)}`,
      type: 'fixture',
      baseUrl: null,
      fixturePayload: JSON.stringify({ items: titles.map(eventItem) }),
      defaultKind: 'event',
      pollIntervalMinutes: 1440,
      isActive: false,
      isUnreliable: false,
    },
  })
  const { id } = (await source.json()) as { id: string }
  expect((await api.post(`/api/admin/sources/${id}/fetch`)).status()).toBe(200)
  const list = (await (await api.get(`/api/admin/candidates?sourceId=${id}&pageSize=50`)).json()) as {
    items: { id: string; title: string }[]
  }
  return new Map(list.items.map((c) => [c.title, c.id]))
}

test('a reviewer edits a candidate with the keyboard, sees a field error, then merges it', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  const tag = randomUUID().slice(0, 8)
  const target = `Koncert Flow ${tag}`
  const dup = `Koncert Flow dublikat ${tag}`
  const ids = await ingest(page.request, [target, dup])
  const publish = await page.request.post(`/api/admin/candidates/${ids.get(target)}/approve`)
  expect(publish.status()).toBe(200)

  await page.goto(`/admin/items/${ids.get(dup)}`)
  const title = page.getByLabel('Titulli')
  await expect(title).toHaveValue(dup)
  await expect(page.getByRole('button', { name: 'Ruaj ndryshimet' })).toBeDisabled()

  // Keyboard-only: focus the field, edit, tab to the save button.
  await title.focus()
  await page.keyboard.press('End')
  await page.keyboard.type(' (ndryshuar)')
  await expect(page.getByRole('button', { name: 'Aprovo' })).toBeDisabled()
  await page.getByRole('button', { name: 'Ruaj ndryshimet' }).focus()
  await page.keyboard.press('Enter')
  await expect(page.getByText('Ndryshimet u ruajtën.')).toBeVisible()
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`${dup} (ndryshuar)`)

  // A server-side field error shows next to the field.
  await title.fill('')
  await page.getByRole('button', { name: 'Ruaj ndryshimet' }).click()
  await expect(page.locator('#fld-title-error')).toBeVisible()
  await expect(title).toHaveAttribute('aria-invalid', 'true')
  await title.fill(dup)

  // Merge with the keyboard; Escape closes the dialog, reopening and choosing merges.
  await page.getByRole('button', { name: 'Bashko', exact: true }).focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await page.getByRole('button', { name: 'Bashko', exact: true }).click()
  await page.getByLabel('Kërko artikullin').fill(target)
  const choose = page.getByRole('dialog').getByRole('button', { name: /Bashko me këtë/ })
  await expect(choose).toHaveCount(1)
  await choose.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByText('Kandidati u bashkua.')).toBeVisible()
  await expect(page.getByText('Statusi: Bashkuar')).toBeVisible()
})
