import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { renderRoutes, stubFetch } from '../../../test/render'
import CandidatePage from './CandidatePage'
import ReviewQueuePage from './ReviewQueuePage'

const summary = (over: object) => ({
  id: 'c1',
  kind: 'event',
  title: 'Koncert',
  status: 'pending',
  validation: 'needsReview',
  issues: [],
  sourceId: 's1',
  sourceName: 'Burimi A',
  fetchedAt: '2026-01-01T00:00:00Z',
  isSynthetic: false,
  ...over,
})
const page = (items: object[]) => ({ items, page: 1, pageSize: 25, totalCount: items.length })
const sources = [{ id: 's1', name: 'Burimi A' }]

const fields = {
  title: 'Koncert',
  description: 'Përshkrim',
  url: 'https://example.com/e',
  imageUrl: null,
  cityText: null,
  citySlug: 'prishtine',
  venueName: null,
  categoryText: null,
  categorySlug: null,
  startsAt: '2026-07-01T18:00:00+00:00',
  endsAt: null,
  priceText: null,
  priceAmount: null,
  currency: null,
  merchantName: null,
  oldPrice: null,
  newPrice: null,
  statedDiscountPercent: null,
  discountPercent: null,
  validFrom: null,
  validUntil: null,
}
const candidate = {
  ...summary({ issues: [{ code: 'MissingPrice', severity: 'review', field: 'priceText', message: 'Çmimi mungon' }] }),
  externalId: 'ext-1',
  sourceUrl: 'javascript:alert(1)',
  fields,
  evidence: '<b>raw</b>',
  suggestedCategory: null,
  duplicateOf: null,
  matchedItemId: null,
  publishedItemId: null,
  rejectionReason: null,
}
const reference = {
  'GET /api/cities': [200, [{ slug: 'prishtine', name: 'Prishtinë' }]] as [number, unknown],
  'GET /api/categories?kind=event': [200, []] as [number, unknown],
}
const routes = [
  { path: '/admin/review', element: <ReviewQueuePage /> },
  { path: '/admin/items/:id', element: <CandidatePage /> },
]

describe('ReviewQueuePage', () => {
  it('defaults to the pending queue and lists rows with labels and untitled fallback', async () => {
    const calls = stubFetch({
      'GET /api/admin/candidates/sources': [200, sources],
      'GET /api/admin/candidates?status=pending&page=1&pageSize=25': [
        200,
        page([summary({}), summary({ id: 'c2', title: null, validation: 'rejected' })]),
      ],
    })
    renderRoutes(routes, '/admin/review')
    const row = (await screen.findByRole('link', { name: 'Koncert' })).closest('tr')!
    expect(within(row).getByText('Kërkon shqyrtim')).toBeInTheDocument()
    expect(within(row).getByText('Në pritje')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '(pa titull)' })).toHaveAttribute('href', '/admin/items/c2')
    expect(screen.getByRole('columnheader', { name: 'Titulli' })).toBeInTheDocument()
    expect(calls).toContain('GET /api/admin/candidates?status=pending&page=1&pageSize=25')
  })

  it('"Të gjitha" drops the status filter', async () => {
    const calls = stubFetch({
      'GET /api/admin/candidates/sources': [200, sources],
      'GET /api/admin/candidates?status=pending&page=1&pageSize=25': [200, page([summary({})])],
      'GET /api/admin/candidates?page=1&pageSize=25': [200, page([summary({ status: 'merged' })])],
    })
    renderRoutes(routes, '/admin/review')
    await screen.findByRole('link', { name: 'Koncert' })
    await userEvent.selectOptions(screen.getByLabelText('Statusi'), 'Të gjitha')
    expect(await within(await screen.findByRole('table')).findByText('Bashkuar')).toBeInTheDocument()
    expect(calls).toContain('GET /api/admin/candidates?page=1&pageSize=25')
  })
})

describe('CandidatePage', () => {
  it('shows evidence as text, no link for a non-http source url, fields and issues', async () => {
    stubFetch({ ...reference, 'GET /api/admin/candidates/c1': [200, candidate] })
    renderRoutes(routes, '/admin/items/c1')
    expect(await screen.findByRole('heading', { level: 1, name: 'Koncert' })).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Evidenca nga burimi' })).getByText('<b>raw</b>')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Hap burimin' })).toBeNull()
    expect(screen.getByLabelText('Fillon më')).toHaveValue('2026-07-01T20:00')
    expect(screen.getByLabelText('Titulli')).toHaveValue('Koncert')
    expect(within(screen.getByRole('list', { name: 'Problemet e validimit' })).getByText(/Çmimi mungon/)).toBeInTheDocument()
  })

  it('approves and shows the public link', async () => {
    stubFetch({
      ...reference,
      'GET /api/admin/candidates/c1': [200, candidate],
      'POST /api/admin/candidates/c1/approve': [200, { kind: 'event', itemId: 'e1' }],
    })
    renderRoutes(routes, '/admin/items/c1')
    await userEvent.click(await screen.findByRole('button', { name: 'Aprovo' }))
    expect(await screen.findByText(/U publikua/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Shiko faqen publike' })).toHaveAttribute('href', '/events/e1')
  })

  it('names the missing fields when approval is refused', async () => {
    stubFetch({
      ...reference,
      'GET /api/admin/candidates/c1': [200, candidate],
      'POST /api/admin/candidates/c1/approve': [400, { status: 400, errors: { citySlug: ['x'] } }],
    })
    renderRoutes(routes, '/admin/items/c1')
    await userEvent.click(await screen.findByRole('button', { name: 'Aprovo' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Qyteti')
  })

  it('rejects with a reason and the dialog closes', async () => {
    const calls = stubFetch({
      ...reference,
      'GET /api/admin/candidates/c1': [200, candidate],
      'POST /api/admin/candidates/c1/reject': [200, { ...candidate, status: 'rejected' }],
    })
    renderRoutes(routes, '/admin/items/c1')
    await userEvent.click(await screen.findByRole('button', { name: 'Refuzo' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Konfirmo refuzimin' })).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText('Arsyeja'), 'Jo publike')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Konfirmo refuzimin' }))
    await screen.findByRole('heading', { level: 1, name: 'Koncert' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(calls).toContain('POST /api/admin/candidates/c1/reject')
  })

  it('shows not-found for an unknown candidate', async () => {
    stubFetch({})
    renderRoutes(routes, '/admin/items/zzz')
    expect(await screen.findByText('Kandidati nuk u gjet')).toBeInTheDocument()
  })
})
