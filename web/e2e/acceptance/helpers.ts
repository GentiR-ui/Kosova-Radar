// Shared helpers for the acceptance suite (black box: HTTP API + accessible names from design.md §7).
// Test data is created through the API with unique tokens; the database is shared and persistent.
import { randomUUID } from 'node:crypto'
import { expect, request, type APIRequestContext, type Locator, type Page } from '@playwright/test'

export const BASE_URL = 'http://localhost:5173'
export const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }
export const USER_PASSWORD = 'Strong-pass-2026!'

/** Unique token: "t" + 8 hex chars. */
export const token = (): string => 't' + randomUUID().replace(/-/g, '').slice(0, 8)

// ---------- time (Europe/Pristina; the IANA zone for Kosovo is Europe/Belgrade) ----------

const ZONE = 'Europe/Belgrade'

function parts(d: Date): Record<string, number> {
  const f = new Intl.DateTimeFormat('en-GB', {
    timeZone: ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  })
  const out: Record<string, number> = {}
  for (const p of f.formatToParts(d)) if (p.type !== 'literal') out[p.type] = Number(p.value)
  return out
}

/** A Pristina wall-clock time `days` from today's Pristina date. */
export function pristinaAt(days: number, hour: number, minute = 0): Date {
  const today = parts(new Date())
  const wall = Date.UTC(today.year, today.month - 1, today.day + days, hour, minute)
  let guess = wall
  for (let i = 0; i < 2; i++) {
    const p = parts(new Date(guess))
    const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second)
    guess = wall - (asUtc - guess)
  }
  return new Date(guess)
}

const pad = (n: number): string => String(n).padStart(2, '0')

/** "dd.MM.yyyy, HH:mm" in Pristina time. */
export function formatPristina(d: Date): string {
  const p = parts(d)
  return `${pad(p.day)}.${pad(p.month)}.${p.year}, ${pad(p.hour)}:${pad(p.minute)}`
}

/** "dd.MM.yyyy" in Pristina time. */
export function formatPristinaDate(d: Date): string {
  const p = parts(d)
  return `${pad(p.day)}.${pad(p.month)}.${p.year}`
}

/** ISO-8601 with an explicit offset, whole seconds. */
export const iso = (d: Date): string => d.toISOString().replace(/\.\d{3}Z$/, '+00:00')

/** ISO-8601 UTC for the X-Test-Now header. */
export const utc = (d: Date): string => d.toISOString().replace(/\.\d{3}Z$/, 'Z')

export const hoursFromNow = (h: number): Date => new Date(Math.floor(Date.now() / 1000) * 1000 + h * 3_600_000)

// ---------- feed items (Radar JSON feed, design.md §6) ----------

export type FeedItem = Record<string, string | number>

export function eventItem(title: string, startsAt: Date, extra: FeedItem = {}): FeedItem {
  return {
    externalId: 'ev-' + randomUUID(),
    kind: 'event',
    title,
    url: `https://example.com/events/${randomUUID()}`,
    city: 'Prishtinë',
    startsAt: iso(startsAt),
    ...extra,
  }
}

export function dealItem(title: string, extra: FeedItem = {}): FeedItem {
  return {
    externalId: 'dl-' + randomUUID(),
    kind: 'deal',
    title,
    url: `https://example.com/deals/${randomUUID()}`,
    city: 'Prishtinë',
    merchant: 'Dyqani Test',
    oldPrice: 25,
    newPrice: 20,
    currency: 'EUR',
    validUntil: iso(pristinaAt(10, 20)),
    ...extra,
  }
}

export const payload = (items: FeedItem[]): string => JSON.stringify({ items })

// ---------- API ----------

async function ok<T>(res: { status(): number; text(): Promise<string> }, expected = 200): Promise<T> {
  const body = await res.text()
  expect(res.status(), body).toBe(expected)
  return (body ? JSON.parse(body) : undefined) as T
}

/** A request context signed in as the given user (separate from any browser page). */
export async function apiAs(email: string, password: string): Promise<APIRequestContext> {
  const ctx = await request.newContext({ baseURL: BASE_URL })
  await ok(await ctx.post('/api/auth/login', { data: { email, password } }))
  return ctx
}

export const adminApi = (): Promise<APIRequestContext> => apiAs(ADMIN.email, ADMIN.password)

export async function createUser(api: APIRequestContext, role: 'admin' | 'reviewer'): Promise<{ email: string; password: string }> {
  const email = `u${randomUUID().replace(/-/g, '')}@test.local`
  await ok(await api.post('/api/admin/users', { data: { email, password: USER_PASSWORD, role } }), 201)
  return { email, password: USER_PASSWORD }
}

/** Signs the browser page in (cookies are shared between page.request and the page). */
export async function signIn(page: Page, email: string, password: string): Promise<void> {
  await ok(await page.request.post('/api/auth/login', { data: { email, password } }))
}

export interface Source {
  id: string
  name: string
}

export async function createFixtureSource(api: APIRequestContext, items: FeedItem[], name = `Burim ${token()}`): Promise<Source> {
  const dto = await ok<{ id: string; name: string }>(
    await api.post('/api/admin/sources', {
      data: {
        name,
        type: 'fixture',
        baseUrl: null,
        fixturePayload: payload(items),
        defaultKind: 'event',
        pollIntervalMinutes: 1440,
        isActive: false,
        isUnreliable: false,
      },
    }),
    201,
  )
  return { id: dto.id, name: dto.name }
}

export async function fetchSource(api: APIRequestContext, sourceId: string, now?: Date): Promise<Record<string, unknown>> {
  const headers: Record<string, string> = now ? { 'X-Test-Now': utc(now) } : {}
  const run = await ok<Record<string, unknown>>(await api.post(`/api/admin/sources/${sourceId}/fetch`, { headers }))
  expect(run.status).toBe('succeeded')
  return run
}

export interface CandidateSummary {
  id: string
  title: string | null
  kind: 'event' | 'deal'
  status: string
  validation: string
}

/** Creates an inactive fixture source with the items, fetches it and returns the candidates by title. */
export async function ingest(
  api: APIRequestContext,
  items: FeedItem[],
  opts: { now?: Date; sourceName?: string } = {},
): Promise<{ source: Source; candidates: Map<string, CandidateSummary> }> {
  const source = await createFixtureSource(api, items, opts.sourceName)
  await fetchSource(api, source.id, opts.now)
  const page = await ok<{ items: CandidateSummary[] }>(await api.get(`/api/admin/candidates?sourceId=${source.id}&pageSize=100`))
  const candidates = new Map<string, CandidateSummary>()
  for (const c of page.items) candidates.set(c.title ?? '', c)
  return { source, candidates }
}

export async function approve(api: APIRequestContext, candidateId: string): Promise<string> {
  const res = await ok<{ itemId: string }>(await api.post(`/api/admin/candidates/${candidateId}/approve`))
  return res.itemId
}

/** Ingests and approves every item; returns the source and item ids by title. */
export async function publish(
  api: APIRequestContext,
  items: FeedItem[],
  opts: { now?: Date; sourceName?: string } = {},
): Promise<{ source: Source; ids: Map<string, string> }> {
  const { source, candidates } = await ingest(api, items, opts)
  const ids = new Map<string, string>()
  for (const item of items) {
    const c = candidates.get(String(item.title))
    expect(c, `candidate for ${item.title}`).toBeTruthy()
    ids.set(String(item.title), await approve(api, c!.id))
  }
  return { source, ids }
}

export async function expireItem(api: APIRequestContext, kind: 'event' | 'deal', id: string): Promise<void> {
  await ok(await api.post(`/api/admin/${kind}s/${id}/expire`), 204)
}

// ---------- UI ----------

/** The card (`<article>`) whose heading is the item title. */
export const card = (page: Page, title: string): Locator =>
  page.getByRole('article').filter({ has: page.getByRole('heading', { name: title }) })
