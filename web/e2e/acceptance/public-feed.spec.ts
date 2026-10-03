import { expect, test, type Page } from '@playwright/test'
import { adminApi, card, dealItem, eventItem, expireItem, iso, pristinaAt, publish, token } from './helpers'

test('REQ-100 home shows the tabs "Sot", "Evente", "Oferta" and "Sot" lists today\'s events and active deals', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const todayTitle = `Sot ${g} ${token()}`
  const tomorrowTitle = `Nesër ${g} ${token()}`
  const dealTitle = `Zbritje ${g} ${token()}`
  await publish(api, [
    eventItem(todayTitle, pristinaAt(0, 0, 1), { endsAt: iso(pristinaAt(1, 2, 0)) }),
    eventItem(tomorrowTitle, pristinaAt(1, 20)),
    dealItem(dealTitle),
  ])

  await page.goto(`/?search=${g}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Çfarë ka sot në Kosovë' })).toBeVisible()
  const tablist = page.getByRole('tablist')
  for (const name of ['Sot', 'Evente', 'Oferta']) await expect(tablist.getByRole('tab', { name, exact: true })).toBeVisible()

  await tablist.getByRole('tab', { name: 'Sot', exact: true }).click()
  await expect(tablist.getByRole('tab', { name: 'Sot', exact: true })).toHaveAttribute('aria-selected', 'true')
  const panel = page.getByRole('tabpanel')
  await expect(panel.getByRole('heading', { name: todayTitle })).toBeVisible()
  await expect(panel.getByRole('heading', { name: dealTitle })).toBeVisible()
  await expect(panel.getByRole('heading', { name: tomorrowTitle })).toHaveCount(0)

  await tablist.getByRole('tab', { name: 'Evente', exact: true }).click()
  await expect(panel.getByRole('heading', { name: tomorrowTitle })).toBeVisible()
  await expect(panel.getByRole('heading', { name: todayTitle })).toBeVisible()
  await expect(panel.getByRole('heading', { name: dealTitle })).toHaveCount(0)

  await tablist.getByRole('tab', { name: 'Oferta', exact: true }).click()
  await expect(panel.getByRole('heading', { name: dealTitle })).toBeVisible()
  await expect(panel.getByRole('heading', { name: todayTitle })).toHaveCount(0)
  await api.dispose()
})

test('REQ-102 selecting a city in "Qyteti" shows only events in that city', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const prizrenTitle = `Koncert ${g} ${token()}`
  const prishtinaTitle = `Koncert ${g} ${token()}`
  await publish(api, [
    eventItem(prizrenTitle, pristinaAt(6, 20), { city: 'Prizren' }),
    eventItem(prishtinaTitle, pristinaAt(6, 21), { city: 'Prishtinë' }),
  ])

  await page.goto(`/events?search=${g}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Evente' })).toBeVisible()
  await expect(card(page, prizrenTitle)).toBeVisible()
  await expect(card(page, prishtinaTitle)).toBeVisible()

  await page.getByLabel('Qyteti').selectOption({ label: 'Prizren' })

  await expect(card(page, prizrenTitle)).toBeVisible()
  await expect(card(page, prishtinaTitle)).toHaveCount(0)
  await expect(page).toHaveURL(/city=prizren/)
  await api.dispose()
})

test('REQ-103 detail of an expired event or deal shows the ended banner', async ({ page }) => {
  const api = await adminApi()
  const eventTitle = `Koncert ${token()}`
  const dealTitle = `Zbritje ${token()}`
  const { ids } = await publish(api, [eventItem(eventTitle, pristinaAt(4, 20)), dealItem(dealTitle)])
  await expireItem(api, 'event', ids.get(eventTitle)!)
  await expireItem(api, 'deal', ids.get(dealTitle)!)

  await page.goto(`/events/${ids.get(eventTitle)}`)
  await expect(page.getByRole('heading', { level: 1, name: eventTitle })).toBeVisible()
  await expect(page.getByText('Ky event ka përfunduar')).toBeVisible()

  await page.goto(`/deals/${ids.get(dealTitle)}`)
  await expect(page.getByRole('heading', { level: 1, name: dealTitle })).toBeVisible()
  await expect(page.getByText('Kjo ofertë ka skaduar')).toBeVisible()
  await api.dispose()
})

test('REQ-103 detail of an active event does not show the ended banner', async ({ page }) => {
  const api = await adminApi()
  const title = `Koncert ${token()}`
  const { ids } = await publish(api, [eventItem(title, pristinaAt(4, 20))])

  await page.goto(`/events/${ids.get(title)}`)
  await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
  await expect(page.getByText('Ky event ka përfunduar')).toHaveCount(0)
  await api.dispose()
})

test('REQ-104 lists without results show "Nuk u gjet asnjë rezultat"', async ({ page }) => {
  const nothing = token()
  for (const path of [`/events?search=${nothing}`, `/deals?search=${nothing}`, `/?search=${nothing}`]) {
    await page.goto(path)
    await expect(page.getByText('Nuk u gjet asnjë rezultat').first()).toBeVisible()
    await expect(page.getByRole('article')).toHaveCount(0)
  }
})

test('REQ-105 search on /search shows events and deals labelled "Event" and "Ofertë"', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const eventTitle = `Koncert ${g} ${token()}`
  const dealTitle = `Zbritje ${g} ${token()}`
  await publish(api, [eventItem(eventTitle, pristinaAt(5, 20)), dealItem(dealTitle)])

  await page.goto('/search')
  await expect(page.getByRole('heading', { level: 1, name: 'Kërkim' })).toBeVisible()
  await page.getByRole('searchbox', { name: 'Kërko' }).fill(g)
  await page.getByRole('button', { name: 'Kërko', exact: true }).click()

  const eventRow = page.getByRole('listitem').filter({ has: page.getByRole('link', { name: eventTitle }) })
  const dealRow = page.getByRole('listitem').filter({ has: page.getByRole('link', { name: dealTitle }) })
  await expect(eventRow).toBeVisible()
  await expect(eventRow).toContainText('Event')
  await expect(eventRow).not.toContainText('Ofertë')
  await expect(dealRow).toBeVisible()
  await expect(dealRow).toContainText('Ofertë')
  await expect(page).toHaveURL(new RegExp(`q=${g}`))

  await eventRow.getByRole('link', { name: eventTitle }).click()
  await expect(page.getByRole('heading', { level: 1, name: eventTitle })).toBeVisible()
  await api.dispose()
})

test('REQ-106 a deal with both prices shows old price, new price and "-{n}%"', async ({ page }) => {
  const api = await adminApi()
  const title = `Zbritje ${token()}`
  const { ids } = await publish(api, [dealItem(title, { oldPrice: 97, newPrice: 63.05 })])

  await page.goto(`/deals?search=${title.split(' ')[1]}`)
  const c = card(page, title)
  await expect(c).toContainText('-35%')
  await expect(c).toContainText(/97/)
  await expect(c).toContainText(/63[.,]05/)

  await page.goto(`/deals/${ids.get(title)}`)
  await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
  await expect(page.getByText('-35%')).toBeVisible()
  await expect(page.getByText(/\b97\b/).first()).toBeVisible()
  await expect(page.getByText(/63[.,]05/).first()).toBeVisible()
  await api.dispose()
})

test('REQ-107 synthetic items show the badge "Të dhëna testuese" on card and detail', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const eventTitle = `Koncert ${g} ${token()}`
  const dealTitle = `Zbritje ${g} ${token()}`
  const { ids } = await publish(api, [eventItem(eventTitle, pristinaAt(5, 20)), dealItem(dealTitle)])

  await page.goto(`/events?search=${g}`)
  await expect(card(page, eventTitle).getByText('Të dhëna testuese')).toBeVisible()
  await page.goto(`/deals?search=${g}`)
  await expect(card(page, dealTitle).getByText('Të dhëna testuese')).toBeVisible()
  await page.goto(`/events/${ids.get(eventTitle)}`)
  await expect(page.getByText('Të dhëna testuese')).toBeVisible()
  await page.goto(`/deals/${ids.get(dealTitle)}`)
  await expect(page.getByText('Të dhëna testuese')).toBeVisible()
  await api.dispose()
})

async function failListThenRetry(page: Page, apiPath: string, pagePath: string, title: string): Promise<void> {
  let fail = true
  await page.route(
    (url) => url.pathname === apiPath,
    (route) =>
      fail
        ? route.fulfill({ status: 500, contentType: 'application/problem+json', body: '{"title":"Error","status":500}' })
        : route.continue(),
  )
  await page.goto(pagePath)
  await expect(page.getByText('Diçka shkoi keq.')).toBeVisible({ timeout: 30_000 })
  await expect(card(page, title)).toHaveCount(0)

  fail = false
  await page.getByRole('button', { name: 'Provo sërish' }).click()

  await expect(card(page, title)).toBeVisible()
  await expect(page.getByText('Diçka shkoi keq.')).toHaveCount(0)
}

test('REQ-108 a failed events list shows "Diçka shkoi keq." and "Provo sërish" retries', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Koncert ${g} ${token()}`
  await publish(api, [eventItem(title, pristinaAt(5, 20))])
  await failListThenRetry(page, '/api/events', `/events?search=${g}`, title)
  await api.dispose()
})

test('REQ-108 a failed deals list shows "Diçka shkoi keq." and "Provo sërish" retries', async ({ page }) => {
  const api = await adminApi()
  const g = token()
  const title = `Zbritje ${g} ${token()}`
  await publish(api, [dealItem(title)])
  await failListThenRetry(page, '/api/deals', `/deals?search=${g}`, title)
  await api.dispose()
})
