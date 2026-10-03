import { expect, test } from '@playwright/test'
import {
  adminApi,
  card,
  dealItem,
  eventItem,
  formatPristina,
  formatPristinaDate,
  hoursFromNow,
  iso,
  pristinaAt,
  publish,
  token,
} from './helpers'

// The browser runs in another time zone: times must still be shown in Europe/Pristina.
test.use({ timezoneId: 'America/New_York' })

test('REQ-101 event card and detail show Pristina time, source link and "Verifikuar së fundmi"', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Koncert ${g} ${token()}`
  const startsAt = pristinaAt(5, 20, 15)
  const item = eventItem(title, startsAt)
  const sourceName = `Burim ${g}`
  const { ids } = await publish(api, [item], { sourceName })

  await page.goto(`/events?search=${g}`)
  const c = card(page, title)
  await expect(c).toContainText(formatPristina(startsAt))
  await expect(c.getByRole('link', { name: `Burimi: ${sourceName}` })).toHaveAttribute('href', String(item.url))
  await expect(c).toContainText('Verifikuar së fundmi')

  await page.goto(`/events/${ids.get(title)}`)
  await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
  await expect(page.getByText(formatPristina(startsAt)).first()).toBeVisible()
  const link = page.getByRole('link', { name: `Burimi: ${sourceName}` })
  await expect(link).toHaveAttribute('href', String(item.url))
  await expect(link).toHaveAttribute('target', '_blank')
  await expect(link).toHaveAttribute('rel', /noopener/)
  await expect(page.getByText('Verifikuar së fundmi')).toBeVisible()
  await api.dispose()
})

test('REQ-101 stale items show "Kontrolluar së fundi më {date}"', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Koncert ${g} ${token()}`
  const verifiedAt = hoursFromNow(-72)
  const sourceName = `Burim ${g}`
  const { ids } = await publish(api, [eventItem(title, pristinaAt(6, 19))], { now: verifiedAt, sourceName })

  await page.goto(`/events?search=${g}`)
  const c = card(page, title)
  await expect(c).toContainText(`Kontrolluar së fundi më ${formatPristinaDate(verifiedAt)}`)
  await expect(c).not.toContainText('Verifikuar së fundmi')
  await expect(c.getByRole('link', { name: `Burimi: ${sourceName}` })).toBeVisible()

  await page.goto(`/events/${ids.get(title)}`)
  await expect(page.getByText(`Kontrolluar së fundi më ${formatPristinaDate(verifiedAt)}`)).toBeVisible()
  await api.dispose()
})

test('REQ-101 deal card and detail show validity, source link and freshness', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Zbritje ${g} ${token()}`
  const validUntil = pristinaAt(9, 0, 30) // the previous day in UTC and in the browser's zone
  const item = dealItem(title, { validUntil: iso(validUntil) })
  const sourceName = `Burim ${g}`
  const { ids } = await publish(api, [item], { sourceName })

  await page.goto(`/deals?search=${g}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Oferta' })).toBeVisible()
  const c = card(page, title)
  await expect(c).toContainText(`Vlen deri më ${formatPristinaDate(validUntil)}`)
  await expect(c.getByRole('link', { name: `Burimi: ${sourceName}` })).toHaveAttribute('href', String(item.url))
  await expect(c).toContainText('Verifikuar së fundmi')

  await page.goto(`/deals/${ids.get(title)}`)
  await expect(page.getByText(`Vlen deri më ${formatPristinaDate(validUntil)}`).first()).toBeVisible()
  await expect(page.getByRole('link', { name: `Burimi: ${sourceName}` })).toHaveAttribute('href', String(item.url))
  await expect(page.getByText('Verifikuar së fundmi')).toBeVisible()
  await api.dispose()
})
