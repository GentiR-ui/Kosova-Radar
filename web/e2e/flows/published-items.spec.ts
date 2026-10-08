import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

const startsAt = new Date(Date.now() + 5 * 86_400_000).toISOString().replace(/\.\d{3}Z$/, '+00:00')

async function publish(api: APIRequestContext, titles: string[]) {
  const source = await api.post('/api/admin/sources', {
    data: {
      name: `Flow ${randomUUID().slice(0, 8)}`,
      type: 'fixture',
      baseUrl: null,
      fixturePayload: JSON.stringify({
        items: titles.map((title) => ({
          externalId: `ev-${randomUUID()}`,
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
  const { id } = (await source.json()) as { id: string }
  expect((await api.post(`/api/admin/sources/${id}/fetch`)).status()).toBe(200)
  const list = (await (await api.get(`/api/admin/candidates?sourceId=${id}&pageSize=50`)).json()) as {
    items: { id: string; title: string }[]
  }
  const ids = new Map<string, string>()
  for (const c of list.items) {
    const res = await api.post(`/api/admin/candidates/${c.id}/approve`)
    expect(res.status()).toBe(200)
    ids.set(c.title, ((await res.json()) as { itemId: string }).itemId)
  }
  return ids
}

test('a reviewer edits, expires and withdraws published items', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  const tag = randomUUID().slice(0, 8)
  const edit = `Publikuar Ndrysho ${tag}`
  const expire = `Publikuar Skado ${tag}`
  const withdraw = `Publikuar Terhiq ${tag}`
  const ids = await publish(page.request, [edit, expire, withdraw])

  await page.getByRole('link', { name: 'Të publikuarat' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Të publikuarat' })).toBeVisible()
  const rowOf = (title: string) => page.getByRole('row').filter({ hasText: title })

  // Edit: the public API returns the new title.
  await rowOf(edit).getByRole('link', { name: 'Ndrysho' }).click()
  const title = page.getByLabel('Titulli')
  await expect(title).toHaveValue(edit)
  await title.focus()
  await page.keyboard.press('End')
  await page.keyboard.type(' (ndryshuar)')
  await page.getByRole('button', { name: 'Ruaj ndryshimet' }).focus()
  await page.keyboard.press('Enter')
  await expect(page.getByText('Ndryshimet u ruajtën.')).toBeVisible()
  const publicEdited = await page.request.get(`/api/events/${ids.get(edit)}`)
  expect(((await publicEdited.json()) as { title: string }).title).toBe(`${edit} (ndryshuar)`)

  // Expire: still readable publicly as expired.
  await page.getByRole('link', { name: 'Kthehu te të publikuarat' }).click()
  await rowOf(expire).getByRole('button', { name: 'Skado' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Po, skado' }).click()
  await expect(rowOf(expire).getByText('Skaduar')).toBeVisible()
  const publicExpired = await page.request.get(`/api/events/${ids.get(expire)}`)
  expect(((await publicExpired.json()) as { status: string }).status).toBe('expired')

  // Withdraw: gone publicly.
  await rowOf(withdraw).getByRole('button', { name: 'Tërhiq' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Po, tërhiq' }).click()
  await expect(rowOf(withdraw).getByText('Tërhequr')).toBeVisible()
  expect((await page.request.get(`/api/events/${ids.get(withdraw)}`)).status()).toBe(404)
})
