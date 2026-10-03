import { QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router'
import { vi } from 'vitest'
import { createQueryClient } from '../app/queryClient'

/** Renders routes at `path` with the app's query client (global 401 handling included). */
export function renderRoutes(routes: RouteObject[], path: string) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false }, mutations: { retry: false } })
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

export const page = (text: string): ReactElement => <h1>{text}</h1>

/** fetch stub: maps "METHOD path" to [status, body]; unknown routes answer 404. Undone after each test (setup.ts). */
export function stubFetch(routes: Record<string, [number, unknown?]>) {
  const calls: string[] = []
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${String(input)}`
    calls.push(key)
    const [status, body] = routes[key] ?? [404, { status: 404 }]
    return new Response(body === undefined ? null : JSON.stringify(body), { status }) // 204 must have no body
  })
  return calls
}
