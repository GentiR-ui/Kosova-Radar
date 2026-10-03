/** RFC 9457 problem details as returned by the API. */
export interface ProblemDetails {
  type?: string
  title?: string
  status: number
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly problem: ProblemDetails

  constructor(problem: ProblemDetails) {
    super(problem.title ?? `HTTP ${problem.status}`)
    this.name = 'ApiError'
    this.problem = problem
  }

  get status(): number {
    return this.problem.status
  }
}

/** The single fetch wrapper: same-origin cookies, JSON in and out, typed problem details on failure. */
export async function api<T>(
  path: string,
  init: { method?: string; body?: unknown } = {},
): Promise<T> {
  const res = await fetch(path, {
    method: init.method ?? 'GET',
    credentials: 'same-origin',
    headers:
      init.body === undefined
        ? { Accept: 'application/json' }
        : { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  })
  const text = await res.text()
  let data: unknown
  try {
    data = text ? JSON.parse(text) : undefined
  } catch {
    data = undefined // not JSON, e.g. a proxy's HTML error page
  }
  if (!res.ok) {
    const problem = (data && typeof data === 'object' ? data : {}) as Partial<ProblemDetails>
    throw new ApiError({ ...problem, status: res.status })
  }
  return data as T
}
