import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { renderRoutes, stubFetch } from '../../../test/render'
import UsersPage from './UsersPage'

const me = { userId: 'u1', email: 'admin@test.local', role: 'admin' }
const users = [
  {
    id: 'u1',
    email: 'admin@test.local',
    role: 'admin',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'u2',
    email: 'rev@test.local',
    role: 'reviewer',
    isActive: true,
    createdAt: '2026-01-02T00:00:00Z',
  },
  {
    id: 'u3',
    email: 'old@test.local',
    role: 'reviewer',
    isActive: false,
    createdAt: '2026-01-03T00:00:00Z',
  },
]
const routes = [{ path: '/admin/users', element: <UsersPage /> }]

async function fillForm() {
  await userEvent.type(screen.getByLabelText('Email'), 'new@test.local')
  await userEvent.type(screen.getByLabelText('Fjalëkalimi'), 'Long-enough-pass-1')
  await userEvent.selectOptions(screen.getByLabelText('Roli'), 'Administrator')
  await userEvent.click(screen.getByRole('button', { name: 'Krijo përdoruesin' }))
}

describe('UsersPage', () => {
  it('lists users and offers deactivation only for other active users', async () => {
    stubFetch({ 'GET /api/me': [200, me], 'GET /api/admin/users': [200, users] })
    renderRoutes(routes, '/admin/users')
    const table = await screen.findByRole('table')
    expect(within(table).getByText('rev@test.local')).toBeInTheDocument()
    expect(within(table).getAllByRole('button', { name: /Çaktivizo/ })).toHaveLength(1)
    expect(
      within(table).getByRole('button', { name: 'Çaktivizo rev@test.local' }),
    ).toBeInTheDocument()
    expect(within(table).getByText('Jo aktiv')).toBeInTheDocument()
  })

  it('creates a user and reloads the list', async () => {
    const calls = stubFetch({
      'GET /api/me': [200, me],
      'GET /api/admin/users': [200, users],
      'POST /api/admin/users': [201, { ...users[1], id: 'u4' }],
    })
    renderRoutes(routes, '/admin/users')
    await screen.findByRole('table')
    await fillForm()
    expect(await screen.findByText('Përdoruesi u krijua.')).toBeInTheDocument()
    expect(calls.filter((c) => c === 'GET /api/admin/users')).toHaveLength(2)
  })

  it('says so when the email is taken', async () => {
    stubFetch({
      'GET /api/me': [200, me],
      'GET /api/admin/users': [200, users],
      'POST /api/admin/users': [409, { status: 409 }],
    })
    renderRoutes(routes, '/admin/users')
    await screen.findByRole('table')
    await fillForm()
    expect(await screen.findByRole('alert')).toHaveTextContent('Ky email përdoret tashmë.')
  })

  it('deactivates a user', async () => {
    const calls = stubFetch({
      'GET /api/me': [200, me],
      'GET /api/admin/users': [200, users],
      'POST /api/admin/users/u2/deactivate': [204],
    })
    renderRoutes(routes, '/admin/users')
    await userEvent.click(await screen.findByRole('button', { name: 'Çaktivizo rev@test.local' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Po, çaktivizo' }))
    expect(await screen.findByText('Përdoruesi u çaktivizua.')).toBeInTheDocument()
    expect(calls).toContain('POST /api/admin/users/u2/deactivate')
  })

  it('shows an error state with retry when the list fails', async () => {
    stubFetch({ 'GET /api/me': [200, me], 'GET /api/admin/users': [500, { status: 500 }] })
    renderRoutes(routes, '/admin/users')
    expect(await screen.findByRole('button', { name: 'Provo sërish' })).toBeInTheDocument()
  })
})
