import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { renderRoutes, stubFetch } from '../../../test/render'
import PublishedEditPage from './PublishedEditPage'
import PublishedPage from './PublishedPage'

const row = (over: object) => ({
  kind: 'event',
  id: 'e1',
  title: 'Koncert',
  adminStatus: 'published',
  cityName: 'Prishtinë',
  date: '2026-07-01T18:00:00+00:00',
  sourceName: 'Burimi A',
  lastVerifiedAt: '2026-01-01T00:00:00Z',
  ...over,
})
const page = (items: object[]) => ({ items, page: 1, pageSize: 25, totalCount: items.length })
const listUrl = 'GET /api/admin/items?page=1&pageSize=25'

const detail = (over: object = {}) => ({
  id: 'e1',
  title: 'Koncert',
  description: null,
  category: null,
  imageUrl: null,
  priceText: null,
  currency: null,
  sourceName: 'Burimi A',
  sourceUrl: 'https://example.com/e',
  lastVerifiedAt: '2026-01-01T00:00:00Z',
  freshness: 'fresh',
  status: 'active',
  isSynthetic: false,
  startsAt: '2026-07-01T18:00:00+00:00',
  endsAt: null,
  city: { slug: 'prishtine', name: 'Prishtinë' },
  venueName: null,
  priceAmount: null,
  sources: [],
  adminStatus: 'published',
  ...over,
})
const reference = {
  'GET /api/cities': [200, [{ slug: 'prishtine', name: 'Prishtinë' }]] as [number, unknown],
  'GET /api/categories?kind=event': [200, []] as [number, unknown],
}
const routes = [
  { path: '/admin/published', element: <PublishedPage /> },
  { path: '/admin/published/:kind/:id', element: <PublishedEditPage /> },
]

describe('PublishedPage', () => {
  it('lists items; expire is only enabled for published, withdraw for non-withdrawn', async () => {
    stubFetch({
      [listUrl]: [
        200,
        page([
          row({}),
          row({ id: 'e2', title: 'Ofertë A', adminStatus: 'expired' }),
          row({ id: 'e3', title: 'Ofertë B', adminStatus: 'withdrawn' }),
        ]),
      ],
    })
    renderRoutes(routes, '/admin/published')
    const published = (await screen.findByText('Koncert')).closest('tr')!
    expect(within(published).getByRole('link', { name: 'Ndrysho' })).toHaveAttribute(
      'href',
      '/admin/published/event/e1',
    )
    expect(within(published).getByRole('button', { name: 'Skado' })).toBeEnabled()
    const expired = screen.getByText('Ofertë A').closest('tr')!
    expect(within(expired).getByRole('button', { name: 'Skado' })).toBeDisabled()
    expect(within(expired).getByRole('button', { name: 'Tërhiq' })).toBeEnabled()
    const withdrawn = screen.getByText('Ofertë B').closest('tr')!
    expect(within(withdrawn).getByRole('button', { name: 'Tërhiq' })).toBeDisabled()
  })

  it('expires and withdraws through the API and reloads the list', async () => {
    const calls = stubFetch({
      [listUrl]: [200, page([row({})])],
      'POST /api/admin/events/e1/expire': [204],
      'POST /api/admin/events/e1/withdraw': [204],
    })
    renderRoutes(routes, '/admin/published')
    await screen.findByText('Koncert')
    await userEvent.click(screen.getByRole('button', { name: 'Skado' }))
    expect(calls).not.toContain('POST /api/admin/events/e1/expire')
    await userEvent.click(await screen.findByRole('button', { name: 'Po, skado' }))
    await userEvent.click(screen.getByRole('button', { name: 'Tërhiq' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Po, tërhiq' }))
    expect(calls).toContain('POST /api/admin/events/e1/expire')
    expect(calls).toContain('POST /api/admin/events/e1/withdraw')
  })

  it('cancelling the confirmation sends nothing and returns focus to the trigger', async () => {
    const calls = stubFetch({ [listUrl]: [200, page([row({})])] })
    renderRoutes(routes, '/admin/published')
    await screen.findByText('Koncert')
    const trigger = screen.getByRole('button', { name: 'Tërhiq' })
    await userEvent.click(trigger)
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Anulo' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
    expect(calls.some((c) => c.startsWith('POST'))).toBe(false)
  })

  it('shows an error when the action fails and filters through the query string', async () => {
    const calls = stubFetch({
      [listUrl]: [200, page([row({})])],
      'GET /api/admin/items?status=expired&page=1&pageSize=25': [200, page([])],
      'POST /api/admin/events/e1/expire': [409],
    })
    renderRoutes(routes, '/admin/published')
    await screen.findByText('Koncert')
    await userEvent.click(screen.getByRole('button', { name: 'Skado' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Po, skado' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('u ndryshua nga dikush tjetër')
    await userEvent.selectOptions(screen.getByLabelText('Statusi'), 'Skaduar')
    await waitFor(() => expect(screen.queryByText('Koncert')).not.toBeInTheDocument())
    expect(calls).toContain('GET /api/admin/items?status=expired&page=1&pageSize=25')
  })
})

describe('PublishedEditPage', () => {
  const url = 'PUT /api/admin/events/e1'

  it('saves only the edited title and keeps the other values', async () => {
    const bodies: unknown[] = []
    const calls = stubFetch({
      ...reference,
      'GET /api/admin/events/e1': [200, detail()],
      'GET /api/admin/items?page=1&pageSize=25': [200, page([])],
      [url]: [200, detail({ title: 'Koncert 2' })],
    })
    const stub = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') bodies.push(JSON.parse(String(init.body)))
      return stub(input, init)
    }) as typeof fetch
    void calls
    renderRoutes(routes, '/admin/published/event/e1')
    const title = await screen.findByLabelText('Titulli')
    expect(title).toHaveValue('Koncert')
    expect(screen.getByRole('button', { name: 'Ruaj ndryshimet' })).toBeDisabled()
    await userEvent.type(title, ' 2')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj ndryshimet' }))
    expect(await screen.findByText('Ndryshimet u ruajtën.')).toBeInTheDocument()
    expect(bodies[0]).toMatchObject({
      title: 'Koncert 2',
      url: 'https://example.com/e',
      citySlug: 'prishtine',
      startsAt: '2026-07-01T18:00:00+00:00',
    })
  })

  it('shows a conflict message on 409', async () => {
    stubFetch({ ...reference, 'GET /api/admin/events/e1': [200, detail()], [url]: [409] })
    renderRoutes(routes, '/admin/published/event/e1')
    await userEvent.type(await screen.findByLabelText('Titulli'), 'x')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj ndryshimet' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('u ndryshua nga dikush tjetër')
  })

  it('shows a generic message for a 400 on a field that is not in the form', async () => {
    stubFetch({
      ...reference,
      'GET /api/admin/events/e1': [200, detail()],
      [url]: [400, { errors: { imageUrl: ['bad'] } }],
    })
    renderRoutes(routes, '/admin/published/event/e1')
    await userEvent.type(await screen.findByLabelText('Titulli'), 'x')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj ndryshimet' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ruajtja dështoi')
  })

  it('edits a deal with its own fields and sends them in the PUT body', async () => {
    const bodies: unknown[] = []
    stubFetch({
      'GET /api/cities': [200, [{ slug: 'prishtine', name: 'Prishtinë' }]],
      'GET /api/categories?kind=deal': [200, []],
      'GET /api/admin/deals/d1': [
        200,
        {
          ...detail({ id: 'd1', title: 'Ofertë', adminStatus: 'published' }),
          startsAt: undefined,
          endsAt: undefined,
          venueName: undefined,
          priceAmount: undefined,
          merchantName: 'Shitësi',
          oldPrice: 20,
          newPrice: 10,
          discountPercent: 50,
          validFrom: null,
          validUntil: '2026-08-01T10:00:00+00:00',
        },
      ],
      'PUT /api/admin/deals/d1': [200, {}],
    })
    const stub = globalThis.fetch
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') bodies.push(JSON.parse(String(init.body)))
      return stub(input, init)
    }) as typeof fetch
    renderRoutes(routes, '/admin/published/deal/d1')
    expect(await screen.findByLabelText('Tregtari')).toHaveValue('Shitësi')
    const price = screen.getByLabelText('Çmimi i ri')
    expect(price).toHaveValue(10)
    await userEvent.clear(price)
    await userEvent.type(price, '8')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj ndryshimet' }))
    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toMatchObject({
      merchantName: 'Shitësi',
      oldPrice: 20,
      newPrice: 8,
      validUntil: '2026-08-01T10:00:00+00:00',
    })
  })

  it('shows the field error from a 400', async () => {
    stubFetch({
      ...reference,
      'GET /api/admin/events/e1': [200, detail()],
      [url]: [400, { status: 400, errors: { title: ['Titulli është i detyrueshëm'] } }],
    })
    renderRoutes(routes, '/admin/published/event/e1')
    const title = await screen.findByLabelText('Titulli')
    await userEvent.type(title, 'x')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj ndryshimet' }))
    expect(await screen.findByText('Titulli është i detyrueshëm')).toBeInTheDocument()
    expect(title).toHaveAttribute('aria-invalid', 'true')
  })

  it('is read-only for a withdrawn item', async () => {
    stubFetch({
      ...reference,
      'GET /api/admin/events/e1': [200, detail({ adminStatus: 'withdrawn' })],
    })
    renderRoutes(routes, '/admin/published/event/e1')
    expect(await screen.findByLabelText('Titulli')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Ruaj ndryshimet' })).toBeDisabled()
  })

  it('shows not found on 404', async () => {
    stubFetch({ ...reference })
    renderRoutes(routes, '/admin/published/event/zzz')
    expect(await screen.findByRole('heading', { name: 'Artikulli nuk u gjet' })).toBeInTheDocument()
  })
})
