import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { page, renderRoutes, stubFetch } from '../../test/render'
import LoginPage from './LoginPage'

const routes = [
  { path: '/admin/login', element: <LoginPage /> },
  { path: '/admin', element: page('Paneli') },
]

async function submit() {
  await userEvent.type(screen.getByLabelText('Email'), 'a@test.local')
  await userEvent.type(screen.getByLabelText('Fjalëkalimi'), 'Some-password-1')
  await userEvent.click(screen.getByRole('button', { name: 'Hyr' }))
}

describe('LoginPage', () => {
  it('shows the generic error on 401', async () => {
    stubFetch({ 'GET /api/me': [401], 'POST /api/auth/login': [401, { status: 401 }] })
    renderRoutes(routes, '/admin/login')
    await submit()
    expect(await screen.findByRole('alert')).toHaveTextContent('Email ose fjalëkalim i pasaktë')
  })

  it('shows the throttling message on 429', async () => {
    stubFetch({ 'GET /api/me': [401], 'POST /api/auth/login': [429, { status: 429 }] })
    renderRoutes(routes, '/admin/login')
    await submit()
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Shumë përpjekje. Provo pas një minute.',
    )
  })

  it('goes to the dashboard after signing in', async () => {
    stubFetch({
      'GET /api/me': [401],
      'POST /api/auth/login': [200, { userId: '1', email: 'a@test.local', role: 'admin' }],
    })
    renderRoutes(routes, '/admin/login')
    await submit()
    expect(await screen.findByRole('heading', { name: 'Paneli' })).toBeInTheDocument()
  })
})
