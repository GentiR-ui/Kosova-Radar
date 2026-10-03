import { expect, test, type Locator } from '@playwright/test'
import { ADMIN, adminApi, card, createUser, eventItem, ingest, payload, pristinaAt, signIn, token } from './helpers'

async function box(locator: Locator): Promise<{ x: number; y: number; width: number; height: number }> {
  const b = await locator.boundingBox()
  expect(b, 'element must be rendered').not.toBeNull()
  return b!
}

test('REQ-110 candidate page shows evidence beside the editable fields and the validation issues', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Koncert ${g} ${token()}`
  const item = eventItem(title, pristinaAt(7, 20), { description: `Evidencë ${g}` })
  delete item.startsAt // gives the candidate a validation issue (MissingStartsAt)
  const { candidates } = await ingest(api, [item])
  const candidate = candidates.get(title)!
  await signIn(page, ADMIN.email, ADMIN.password)

  await page.goto(`/admin/items/${candidate.id}`)

  await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
  const evidence = page.getByRole('region', { name: 'Evidenca nga burimi' })
  const fields = page.getByRole('region', { name: 'Fushat e normalizuara' })
  await expect(evidence).toBeVisible()
  await expect(evidence).toContainText(`Evidencë ${g}`)
  await expect(evidence).toContainText(String(item.externalId))
  await expect(evidence.getByRole('link', { name: 'Hap burimin' })).toHaveAttribute('href', String(item.url))
  await expect(fields.getByLabel('Titulli')).toHaveValue(title)
  await expect(fields.getByLabel('Përshkrimi')).toHaveValue(`Evidencë ${g}`)
  await expect(fields.getByLabel('Lidhja e burimit')).toHaveValue(String(item.url))
  for (const label of ['Qyteti', 'Vendi', 'Kategoria', 'Fillon më', 'Përfundon më', 'Çmimi (tekst)', 'Çmimi', 'Monedha']) {
    await expect(fields.getByLabel(label, { exact: true })).toBeVisible()
  }
  const issues = page.getByRole('list', { name: 'Problemet e validimit' })
  await expect(issues).toBeVisible()
  await expect(issues.getByRole('listitem').first()).toBeVisible()
  for (const name of ['Ruaj ndryshimet', 'Aprovo', 'Refuzo', 'Bashko']) {
    await expect(page.getByRole('button', { name, exact: true })).toBeVisible()
  }

  const e = await box(evidence)
  const f = await box(fields)
  const width = page.viewportSize()!.width
  if (width >= 1024) {
    // Side by side: no horizontal overlap, vertical overlap.
    expect(e.x + e.width <= f.x + 1 || f.x + f.width <= e.x + 1).toBe(true)
    expect(e.y < f.y + f.height && f.y < e.y + e.height).toBe(true)
  } else {
    // Stacked: one below the other.
    expect(e.y + e.height <= f.y + 1 || f.y + f.height <= e.y + 1).toBe(true)
  }
  await api.dispose()
})

test('REQ-111 reviewer approves a valid event and it appears on the public events page', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Koncert ${g} ${token()}`
  const sourceName = `Burim ${g}`
  const { candidates } = await ingest(api, [eventItem(title, pristinaAt(8, 20))], { sourceName })
  const candidate = candidates.get(title)!
  expect(candidate.validation).toBe('valid')
  const reviewer = await createUser(api, 'reviewer')
  await signIn(page, reviewer.email, reviewer.password)

  await page.goto(`/admin/items/${candidate.id}`)
  await page.getByRole('button', { name: 'Aprovo', exact: true }).click()

  await expect(page.getByText('U publikua')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Shiko faqen publike' })).toBeVisible()

  await page.goto(`/events?search=${g}`)
  const c = card(page, title)
  await expect(c).toBeVisible()
  await expect(c.getByRole('link', { name: `Burimi: ${sourceName}` })).toBeVisible()
  await expect(c).toContainText('Verifikuar së fundmi')
  await api.dispose()
})

test('REQ-112 reviewer rejects a candidate with a reason and the queue shows it as "Refuzuar"', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Koncert ${g} ${token()}`
  const sourceName = `Burim ${g}`
  const { candidates } = await ingest(api, [eventItem(title, pristinaAt(8, 20))], { sourceName })
  const candidate = candidates.get(title)!
  const reviewer = await createUser(api, 'reviewer')
  await signIn(page, reviewer.email, reviewer.password)

  await page.goto(`/admin/items/${candidate.id}`)
  await page.getByRole('button', { name: 'Refuzo', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Arsyeja').fill('Nuk është ngjarje publike')
  await dialog.getByRole('button', { name: 'Konfirmo refuzimin' }).click()
  await expect(dialog).toHaveCount(0)

  await page.goto('/admin/review')
  await expect(page.getByRole('heading', { level: 1, name: 'Radha e shqyrtimit' })).toBeVisible()
  await page.getByLabel('Burimi', { exact: true }).selectOption({ label: sourceName })
  await page.getByLabel('Statusi', { exact: true }).selectOption({ label: 'Refuzuar' })
  const row = page.getByRole('row').filter({ has: page.getByRole('link', { name: title }) })
  await expect(row).toBeVisible()
  await expect(row).toContainText('Refuzuar')
  await expect(row).not.toContainText('Në pritje')

  // The default queue ("Në pritje") no longer lists it.
  await page.getByLabel('Statusi', { exact: true }).selectOption({ label: 'Në pritje' })
  await expect(page.getByRole('row').filter({ has: page.getByRole('link', { name: title }) })).toHaveCount(0)
  await api.dispose()
})

async function addFixtureSource(page: import('@playwright/test').Page, name: string, json: string): Promise<void> {
  await page.getByRole('button', { name: 'Shto burim' }).click()
  await page.getByLabel('Emri', { exact: true }).fill(name)
  const type = page.getByLabel('Lloji', { exact: true })
  const labels = await type.getByRole('option').allTextContents()
  const fixtureLabel = labels.find((l) => /fixture|testues|sintetik/i.test(l))
  await type.selectOption(fixtureLabel ? { label: fixtureLabel } : 'fixture')
  await page.getByLabel('Të dhënat testuese (JSON)').fill(json)
  await expect(page.getByLabel('Intervali (minuta)')).toHaveValue('60')
  await expect(page.getByLabel('Aktiv', { exact: true })).not.toBeChecked()
  await page.getByRole('button', { name: 'Ruaj burimin' }).click()
}

test('REQ-113 admin adds a source, clicks "Merr tani" and sees the run result and health badge', async ({ page }) => {
  const g = token()
  const name = `Burim UI ${g}`
  const json = payload([eventItem(`Koncert ${g} ${token()}`, pristinaAt(9, 20)), eventItem(`Teatër ${g} ${token()}`, pristinaAt(9, 21))])
  await signIn(page, ADMIN.email, ADMIN.password)
  await page.goto('/admin/sources')
  await expect(page.getByRole('heading', { level: 1, name: 'Burimet' })).toBeVisible()

  await addFixtureSource(page, name, json)
  const row = page.getByRole('row').filter({ hasText: name })
  await expect(row).toBeVisible()
  await expect(row).toContainText('Joaktiv')

  await row.getByRole('button', { name: 'Merr tani' }).click()

  await expect(page.getByText('Marrja përfundoi: 2 të reja, 0 të përditësuara, 0 pa ndryshim, 0 të dështuara')).toBeVisible()
  await expect(row).toContainText('Joaktiv')
})

test('REQ-113 a failing "Merr tani" shows the error code and the degraded health badge', async ({ page }) => {
  const g = token()
  const name = `Burim UI ${g}`
  await signIn(page, ADMIN.email, ADMIN.password)
  await page.goto('/admin/sources')

  await addFixtureSource(page, name, 'not json')
  const row = page.getByRole('row').filter({ hasText: name })
  await expect(row).toContainText('Joaktiv')

  // Activate it through the edit form so the health badge reflects the run.
  await row.getByRole('button', { name: 'Ndrysho' }).click()
  await page.getByLabel('Aktiv', { exact: true }).check()
  await page.getByRole('button', { name: 'Ruaj burimin' }).click()
  await expect(row).not.toContainText('Joaktiv')

  await row.getByRole('button', { name: 'Merr tani' }).click()

  await expect(page.getByText('Marrja dështoi: MalformedPayload')).toBeVisible()
  await expect(row).toContainText('Me probleme')

  // Leave the shared development database tidy: deactivate the failing source again.
  await row.getByRole('button', { name: 'Ndrysho' }).click()
  await page.getByLabel('Aktiv', { exact: true }).uncheck()
  await page.getByRole('button', { name: 'Ruaj burimin' }).click()
  await expect(row).toContainText('Joaktiv')
})
