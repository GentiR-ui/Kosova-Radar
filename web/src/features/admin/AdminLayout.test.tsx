import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { page, renderRoutes, stubFetch } from '../../test/render'
import AdminLayout, { RequireAdmin } from './AdminLayout'
import DashboardPage from './DashboardPage'

const routes = [
  { path: '/admin/login', element: page('Hyrje') },
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'users', element: <RequireAdmin>{page('Përdoruesit')}</RequireAdmin> },
    ],
  },
]

const admin = { userId: '1', email: 'a@test.local', role: 'admin' }
const reviewer = { userId: '2', email: 'r@test.local', role: 'reviewer' }

describe('AdminLayout', () => {
  it('sends signed-out visitors to the login page', async () => {
    stubFetch({ 'GET /api/me': [401] })
    renderRoutes(routes, '/admin')
    expect(await screen.findByRole('heading', { name: 'Hyrje' })).toBeInTheDocument()
  })

  it('sends the user to the login page when the session ends mid-use (any 401)', async () => {
    stubFetch({ 'GET /api/me': [200, admin], 'GET /api/admin/dashboard': [401] })
    renderRoutes(routes, '/admin')
    expect(await screen.findByRole('heading', { name: 'Hyrje' })).toBeInTheDocument()
  })

  it('shows all five links and the dashboard tiles to an admin', async () => {
    stubFetch({ 'GET /api/me': [200, admin], 'GET /api/admin/dashboard': [404] })
    renderRoutes(routes, '/admin')
    for (const name of ['Paneli', 'Shqyrtimi', 'Të publikuarat', 'Burimet', 'Përdoruesit'])
      expect(await screen.findByRole('link', { name })).toBeInTheDocument()
    for (const label of [
      'Në pritje për shqyrtim',
      'Kërkojnë vëmendje',
      'Evente aktive',
      'Oferta aktive',
      'Burime me probleme',
    ])
      expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('hides Admin-only links from reviewers and blocks the pages', async () => {
    stubFetch({ 'GET /api/me': [200, reviewer] })
    renderRoutes(routes, '/admin/users')
    expect(await screen.findByText('Nuk keni leje për këtë faqe')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Shqyrtimi' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Burimet' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Përdoruesit' })).not.toBeInTheDocument()
  })

  it('logs out and returns to the login page', async () => {
    const calls = stubFetch({ 'GET /api/me': [200, admin], 'POST /api/auth/logout': [204] })
    renderRoutes(routes, '/admin/users')
    await userEvent.click(await screen.findByRole('button', { name: 'Dil' }))
    expect(await screen.findByRole('heading', { name: 'Hyrje' })).toBeInTheDocument()
    expect(calls).toContain('POST /api/auth/logout')
  })

  it('stays signed in and says so when logout fails', async () => {
    stubFetch({ 'GET /api/me': [200, admin], 'POST /api/auth/logout': [500] })
    renderRoutes(routes, '/admin/users')
    await userEvent.click(await screen.findByRole('button', { name: 'Dil' }))
    expect(await screen.findByText('Dalja dështoi. Provo sërish.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Përdoruesit' })).toBeInTheDocument()
  })
})
