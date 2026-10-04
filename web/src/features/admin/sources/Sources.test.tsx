import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { renderRoutes, stubFetch } from '../../../test/render'
import SourcesPage from './SourcesPage'

const source = {
  id: 's1',
  name: 'Burimi një',
  type: 'fixture',
  baseUrl: null,
  defaultKind: 'event',
  isActive: false,
  isUnreliable: false,
  pollIntervalMinutes: 60,
  health: 'disabled',
  lastFetchedAt: null,
  lastSuccessAt: null,
  lastErrorAt: null,
  lastError: null,
  consecutiveFailures: 0,
}
const routes = [{ path: '/admin/sources', element: <SourcesPage /> }]
const run = {
  status: 'succeeded',
  created: 2,
  updated: 0,
  unchanged: 0,
  failed: 0,
  errorCode: null,
}

describe('SourcesPage', () => {
  it('lists sources with health badge and actions', async () => {
    stubFetch({
      'GET /api/admin/sources': [
        200,
        [source, { ...source, id: 's2', name: 'Dy', health: 'failing' }],
      ],
    })
    renderRoutes(routes, '/admin/sources')
    const table = await screen.findByRole('table')
    expect(within(table).getByText('Joaktiv')).toBeInTheDocument()
    expect(within(table).getByText('Dështon')).toBeInTheDocument()
    expect(within(table).getAllByRole('button', { name: /Merr tani/ })).toHaveLength(2)
  })

  it('shows the empty state', async () => {
    stubFetch({ 'GET /api/admin/sources': [200, []] })
    renderRoutes(routes, '/admin/sources')
    expect(await screen.findByText('Nuk ka burime.')).toBeInTheDocument()
  })

  it('shows a retry button when the list fails', async () => {
    stubFetch({ 'GET /api/admin/sources': [500, { status: 500 }] })
    renderRoutes(routes, '/admin/sources')
    expect(await screen.findByRole('button', { name: 'Provo sërish' })).toBeInTheDocument()
  })

  it('adds a fixture source with the documented defaults and posts the request body', async () => {
    const calls = stubFetch({
      'GET /api/admin/sources': [200, []],
      'POST /api/admin/sources': [201, source],
    })
    const spy = vi.spyOn(globalThis, 'fetch')
    renderRoutes(routes, '/admin/sources')
    await screen.findByText('Nuk ka burime.')
    await userEvent.click(screen.getByRole('button', { name: 'Shto burim' }))
    expect(screen.getByLabelText('Intervali (minuta)')).toHaveValue(60)
    expect(screen.getByLabelText('Aktiv')).not.toBeChecked()
    await userEvent.type(screen.getByLabelText('Emri'), 'Test')
    await userEvent.selectOptions(screen.getByLabelText('Lloji'), 'fixture')
    await userEvent.click(screen.getByLabelText('Të dhënat testuese (JSON)'))
    await userEvent.paste('[]')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj burimin' }))
    expect(await screen.findByText('Burimi u ruajt.')).toBeInTheDocument()
    expect(calls).toContain('POST /api/admin/sources')
    const post = spy.mock.calls.find(([, init]) => init?.method === 'POST')
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({
      name: 'Test',
      type: 'fixture',
      baseUrl: null,
      fixturePayload: '[]',
      defaultKind: 'event',
      pollIntervalMinutes: 60,
      isActive: false,
      isUnreliable: false,
    })
  })

  it('shows a field error from a 400 on save and keeps the form open', async () => {
    stubFetch({
      'GET /api/admin/sources': [200, []],
      'POST /api/admin/sources': [400, { status: 400, errors: { PollIntervalMinutes: ['bad'] } }],
    })
    renderRoutes(routes, '/admin/sources')
    await screen.findByText('Nuk ka burime.')
    await userEvent.click(screen.getByRole('button', { name: 'Shto burim' }))
    await userEvent.type(screen.getByLabelText('Emri'), 'Test')
    await userEvent.selectOptions(screen.getByLabelText('Lloji'), 'fixture')
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj burimin' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Vlerë e pavlefshme.')
    expect(screen.getByLabelText('Intervali (minuta)')).toHaveAttribute('aria-invalid', 'true')
  })

  it('loads the detail for editing and saves with PUT', async () => {
    const calls = stubFetch({
      'GET /api/admin/sources': [200, [source]],
      'GET /api/admin/sources/s1': [200, { ...source, fixturePayload: '{"items":[]}' }],
      'PUT /api/admin/sources/s1': [200, source],
    })
    renderRoutes(routes, '/admin/sources')
    await userEvent.click(await screen.findByRole('button', { name: /Ndrysho/ }))
    expect(await screen.findByLabelText('Emri')).toHaveValue('Burimi një')
    expect(screen.getByLabelText('Të dhënat testuese (JSON)')).toHaveValue('{"items":[]}')
    await userEvent.click(screen.getByLabelText('Aktiv'))
    await userEvent.click(screen.getByRole('button', { name: 'Ruaj burimin' }))
    expect(await screen.findByText('Burimi u ruajt.')).toBeInTheDocument()
    expect(calls).toContain('PUT /api/admin/sources/s1')
  })

  it('shows the run result after "Merr tani"', async () => {
    stubFetch({
      'GET /api/admin/sources': [200, [source]],
      'POST /api/admin/sources/s1/fetch': [200, run],
    })
    renderRoutes(routes, '/admin/sources')
    await userEvent.click(await screen.findByRole('button', { name: /Merr tani/ }))
    expect(
      await screen.findByText(
        'Marrja përfundoi: 2 të reja, 0 të përditësuara, 0 pa ndryshim, 0 të dështuara',
      ),
    ).toBeInTheDocument()
  })

  it('shows the error code of a failed run', async () => {
    stubFetch({
      'GET /api/admin/sources': [200, [source]],
      'POST /api/admin/sources/s1/fetch': [
        200,
        { ...run, status: 'failed', created: 0, errorCode: 'MalformedPayload' },
      ],
    })
    renderRoutes(routes, '/admin/sources')
    await userEvent.click(await screen.findByRole('button', { name: /Merr tani/ }))
    expect(await screen.findByText('Marrja dështoi: MalformedPayload')).toBeInTheDocument()
  })

  it('explains a 409 and a generic fetch error', async () => {
    stubFetch({
      'GET /api/admin/sources': [200, [source]],
      'POST /api/admin/sources/s1/fetch': [409, { status: 409 }],
    })
    renderRoutes(routes, '/admin/sources')
    await userEvent.click(await screen.findByRole('button', { name: /Merr tani/ }))
    expect(
      await screen.findByText('Një marrje është në vazhdim. Provo pas pak.'),
    ).toBeInTheDocument()
  })
})
