import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { renderRoutes, stubFetch } from '../../test/render'
import DealsPage from './DealsPage'
import EventsPage from './EventsPage'
import { pristinaToday } from './format'
import HomePage from './HomePage'
import SearchPage from './SearchPage'
import { DealDetailPage, EventDetailPage } from './ItemDetail'
import { DealCard, EventCard } from './ItemCards'
import type { DealSummaryDto, EventSummaryDto } from './api'

const event: EventSummaryDto = {
  id: 'e1',
  title: 'Koncert xyz',
  startsAt: '2026-07-01T18:00:00Z',
  startTimeKnown: true,
  endsAt: null,
  city: { slug: 'prizren', name: 'Prizren' },
  venueName: 'Sheshi',
  category: null,
  priceText: null,
  priceAmount: null,
  currency: null,
  imageUrl: null,
  sourceName: 'Burimi A',
  sourceUrl: 'https://example.com/e',
  lastVerifiedAt: '2026-07-01T10:00:00Z',
  freshness: 'fresh',
  status: 'active',
  isSynthetic: true,
}
const deal: DealSummaryDto = {
  id: 'd1',
  title: 'Zbritje xyz',
  merchantName: 'Dyqani',
  city: null,
  category: null,
  oldPrice: 97,
  newPrice: 63.05,
  priceText: null,
  currency: 'EUR',
  discountPercent: 35,
  validFrom: null,
  validUntil: null,
  imageUrl: null,
  sourceName: 'Burimi B',
  sourceUrl: 'javascript:alert(1)',
  lastVerifiedAt: '2026-06-01T10:00:00Z',
  freshness: 'stale',
  status: 'active',
  isSynthetic: false,
}
const page = <T,>(items: T[], totalCount = items.length) => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount,
})

describe('cards', () => {
  it('shows an event with Pristina time, source link, freshness and the synthetic badge', () => {
    renderRoutes([{ path: '/', element: <EventCard event={event} /> }], '/')
    expect(screen.getByRole('heading', { name: 'Koncert xyz' })).toBeInTheDocument()
    expect(screen.getByText('01.07.2026, 20:00')).toBeInTheDocument()
    expect(screen.getByText('Të dhëna testuese')).toBeInTheDocument()
    expect(screen.getByText('Verifikuar së fundmi')).toBeInTheDocument()
    const link = screen.getByRole('link', { name: 'Burimi: Burimi A' })
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('shows only the date and "gjatë ditës" for an event whose time is unknown', () => {
    // Local midnight of 2 Jul in Pristina (CEST) = 1 Jul 22:00 UTC.
    const dateOnly = { ...event, startsAt: '2026-07-01T22:00:00Z', startTimeKnown: false }
    renderRoutes([{ path: '/', element: <EventCard event={dateOnly} /> }], '/')
    expect(screen.getByText('02.07.2026 · gjatë ditës')).toBeInTheDocument()
    expect(screen.queryByText(/00:00/)).not.toBeInTheDocument()
  })

  it('shows both deal prices, the discount, stale text, and never links a non-http source', () => {
    renderRoutes([{ path: '/', element: <DealCard deal={deal} /> }], '/')
    expect(screen.getByText('-35%')).toBeInTheDocument()
    expect(screen.getByText(/97/).tagName).toBe('S')
    expect(screen.getByText(/63[.,]05/)).toBeInTheDocument()
    expect(screen.getByText('Afati nuk dihet')).toBeInTheDocument()
    expect(screen.getByText('Kontrolluar së fundi më 01.06.2026')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Burimi: Burimi B' })).not.toBeInTheDocument()
    expect(screen.getByText('Burimi: Burimi B')).toBeInTheDocument()
  })
})

describe('list states', () => {
  const routes = [{ path: '/events', element: <EventsPage /> }]

  it('shows the empty state', async () => {
    stubFetch({
      'GET /api/cities': [200, []],
      'GET /api/categories?kind=event': [200, []],
      'GET /api/events?page=1&pageSize=20': [200, page([])],
    })
    renderRoutes(routes, '/events')
    expect(await screen.findByText('Nuk u gjet asnjë rezultat')).toBeInTheDocument()
  })

  it('shows the error state and retries', async () => {
    let fail = true
    stubFetch({ 'GET /api/cities': [200, []], 'GET /api/categories?kind=event': [200, []] })
    const base = globalThis.fetch
    globalThis.fetch = async (input, init) =>
      String(input).startsWith('/api/events')
        ? fail
          ? new Response('{}', { status: 500 })
          : new Response(JSON.stringify(page([event])), { status: 200 })
        : base(input, init)
    renderRoutes(routes, '/events')
    expect(await screen.findByText('Diçka shkoi keq.')).toBeInTheDocument()
    fail = false
    await userEvent.click(screen.getByRole('button', { name: 'Provo sërish' }))
    expect(await screen.findByRole('heading', { name: 'Koncert xyz' })).toBeInTheDocument()
    expect(screen.queryByText('Diçka shkoi keq.')).not.toBeInTheDocument()
  })

  it('loads the next page with "Shfaq më shumë"', async () => {
    const second = { ...event, id: 'e2', title: 'Tjetri' }
    stubFetch({
      'GET /api/cities': [200, []],
      'GET /api/categories?kind=event': [200, []],
      'GET /api/events?page=1&pageSize=20': [200, page([event], 25)],
      'GET /api/events?page=2&pageSize=20': [200, { ...page([second], 25), page: 2 }],
    })
    renderRoutes(routes, '/events')
    await userEvent.click(await screen.findByRole('button', { name: 'Shfaq më shumë' }))
    expect(await screen.findByRole('heading', { name: 'Tjetri' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Shfaq më shumë' })).not.toBeInTheDocument()
  })
})

describe('home tabs', () => {
  it('"Sot" asks for today only; the deals tab lists deals only', async () => {
    const today = pristinaToday()
    const calls = stubFetch({
      [`GET /api/events?from=${today}&to=${today}&page=1&pageSize=20`]: [200, page([event])],
      'GET /api/deals?page=1&pageSize=20': [200, page([deal])],
    })
    renderRoutes([{ path: '/', element: <HomePage /> }], '/')
    expect(await screen.findByRole('heading', { name: 'Koncert xyz' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Zbritje xyz' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'Oferta' }))
    expect(screen.getByRole('tab', { name: 'Oferta' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('heading', { name: 'Koncert xyz' })).not.toBeInTheDocument()
    expect(calls.filter((c) => c.includes('/api/deals'))).toHaveLength(1)
  })
})

describe('filters', () => {
  const lookups = {
    'GET /api/cities': [200, [{ slug: 'prizren', name: 'Prizren' }]] as [number, unknown],
    'GET /api/categories?kind=event': [200, []] as [number, unknown],
    'GET /api/categories?kind=deal': [200, []] as [number, unknown],
  }

  it('events: city, dates and search go to the API and the query string', async () => {
    const calls = stubFetch({
      ...lookups,
      'GET /api/events?page=1&pageSize=20': [200, page([event])],
      'GET /api/events?city=prizren&page=1&pageSize=20': [200, page([event])],
      'GET /api/events?city=prizren&from=2026-08-01&page=1&pageSize=20': [200, page([event])],
      'GET /api/events?city=prizren&from=2026-08-01&search=abc&page=1&pageSize=20': [
        200,
        page([event]),
      ],
    })
    renderRoutes([{ path: '/events', element: <EventsPage /> }], '/events')
    await screen.findByRole('heading', { name: 'Koncert xyz' })
    await userEvent.selectOptions(screen.getByLabelText('Qyteti'), 'Prizren')
    await userEvent.type(screen.getByLabelText('Nga data'), '2026-08-01')
    await userEvent.type(screen.getByLabelText('Kërko në evente'), 'abc')
    await vi.waitFor(() =>
      expect(calls).toContain(
        'GET /api/events?city=prizren&from=2026-08-01&search=abc&page=1&pageSize=20',
      ),
    )
  })

  it('deals: city filter and an error with retry', async () => {
    let fail = false
    const calls = stubFetch({
      ...lookups,
      'GET /api/deals?page=1&pageSize=20': [200, page([deal])],
      'GET /api/deals?city=prizren&page=1&pageSize=20': [200, page([deal])],
    })
    const base = globalThis.fetch
    globalThis.fetch = async (input, init) =>
      fail && String(input).startsWith('/api/deals')
        ? new Response('{}', { status: 500 })
        : base(input, init)
    renderRoutes([{ path: '/deals', element: <DealsPage /> }], '/deals')
    await screen.findByRole('heading', { name: 'Zbritje xyz' })
    fail = true
    await userEvent.selectOptions(screen.getByLabelText('Qyteti'), 'Prizren')
    expect(await screen.findByText('Diçka shkoi keq.')).toBeInTheDocument()
    fail = false
    await userEvent.click(screen.getByRole('button', { name: 'Provo sërish' }))
    expect(await screen.findByRole('heading', { name: 'Zbritje xyz' })).toBeInTheDocument()
    expect(calls).toContain('GET /api/deals?city=prizren&page=1&pageSize=20')
  })
})

describe('detail pages', () => {
  const evRoutes = [{ path: '/events/:id', element: <EventDetailPage /> }]
  const dlRoutes = [{ path: '/deals/:id', element: <DealDetailPage /> }]
  const evDetail = {
    ...event,
    status: 'expired',
    description: 'Përshkrim',
    sources: [
      { name: 'Burimi A', url: 'https://example.com/e' },
      { name: 'Burimi C', url: 'https://example.com/c' },
    ],
  }

  it('shows an expired event with the ended banner, badge, time and other sources', async () => {
    stubFetch({ 'GET /api/events/e1': [200, evDetail] })
    renderRoutes(evRoutes, '/events/e1')
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Koncert xyz' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Ky event ka përfunduar')).toBeInTheDocument()
    expect(screen.getByText('Të dhëna testuese')).toBeInTheDocument()
    expect(screen.getByText('01.07.2026, 20:00')).toBeInTheDocument()
    expect(screen.getByText('Përshkrim')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Burimi: Burimi C' })).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Burimi: Burimi A' })).toHaveLength(1)
  })

  it('shows an active event without the banner or badge', async () => {
    stubFetch({
      'GET /api/events/e1': [200, { ...evDetail, status: 'active', isSynthetic: false }],
    })
    renderRoutes(evRoutes, '/events/e1')
    await screen.findByRole('heading', { level: 1 })
    expect(screen.queryByText('Ky event ka përfunduar')).not.toBeInTheDocument()
    expect(screen.queryByText('Të dhëna testuese')).not.toBeInTheDocument()
  })

  it('shows a deal with prices, discount, stale text and the expired banner', async () => {
    stubFetch({
      'GET /api/deals/d1': [200, { ...deal, status: 'expired', description: null, sources: [] }],
    })
    renderRoutes(dlRoutes, '/deals/d1')
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Zbritje xyz' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Kjo ofertë ka skaduar')).toBeInTheDocument()
    expect(screen.getByText('-35%')).toBeInTheDocument()
    expect(screen.getByText(/63[.,]05/)).toBeInTheDocument()
    expect(screen.getByText('Kontrolluar së fundi më 01.06.2026')).toBeInTheDocument()
  })

  it('shows "not found" on 404', async () => {
    stubFetch({ 'GET /api/events/e1': [404, { status: 404 }] })
    renderRoutes(evRoutes, '/events/e1')
    expect(await screen.findByRole('heading', { name: 'Faqja nuk u gjet' })).toBeInTheDocument()
  })

  it('shows the error state with retry on a server error', async () => {
    stubFetch({ 'GET /api/deals/d1': [500, { status: 500 }] })
    renderRoutes(dlRoutes, '/deals/d1')
    expect(await screen.findByText('Diçka shkoi keq.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Provo sërish' })).toBeInTheDocument()
  })
})

describe('search page', () => {
  const routes = [{ path: '/search', element: <SearchPage /> }]

  it('labels events and deals and links to their pages', async () => {
    stubFetch({
      'GET /api/search?q=abc&page=1&pageSize=20': [
        200,
        page([
          { kind: 'event', id: 'e1', title: 'Koncert abc', date: null, cityName: 'Prishtinë', sourceName: 'S' },
          { kind: 'deal', id: 'd1', title: 'Zbritje abc', date: null, cityName: null, sourceName: 'S' },
        ]),
      ],
    })
    renderRoutes(routes, '/search')
    await userEvent.type(screen.getByRole('searchbox', { name: 'Kërko' }), 'abc')
    await userEvent.click(screen.getByRole('button', { name: 'Kërko' }))
    const rows = await screen.findAllByRole('listitem')
    expect(rows[0]).toHaveTextContent('Event')
    expect(rows[1]).toHaveTextContent('Ofertë')
    expect(screen.getByRole('link', { name: 'Koncert abc' })).toHaveAttribute('href', '/events/e1')
    expect(screen.getByRole('link', { name: 'Zbritje abc' })).toHaveAttribute('href', '/deals/d1')
  })

  it('asks for at least 2 characters without calling the API', () => {
    stubFetch({})
    renderRoutes(routes, '/search?q=a')
    expect(screen.getByText('Shkruani të paktën 2 shkronja.')).toBeInTheDocument()
  })
})
