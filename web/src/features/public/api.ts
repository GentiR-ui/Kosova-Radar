import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'

export interface CityDto {
  slug: string
  name: string
}
export interface CategoryDto extends CityDto {
  kind: 'event' | 'deal'
}
interface ItemBase {
  id: string
  title: string
  category: CategoryDto | null
  imageUrl: string | null
  priceText: string | null
  currency: string | null
  sourceName: string
  sourceUrl: string
  lastVerifiedAt: string
  freshness: 'fresh' | 'stale'
  status: 'active' | 'expired'
  isSynthetic: boolean
}
export interface EventSummaryDto extends ItemBase {
  startsAt: string
  endsAt: string | null
  city: CityDto
  venueName: string | null
  priceAmount: number | null
}
export interface DealSummaryDto extends ItemBase {
  merchantName: string | null
  city: CityDto | null
  oldPrice: number | null
  newPrice: number | null
  discountPercent: number | null
  validFrom: string | null
  validUntil: string | null
}
export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

const PAGE_SIZE = 20

export type Filters = Record<string, string | undefined>

function url(path: string, filters: Filters, page: number): string {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(filters)) if (v) q.set(k, v)
  q.set('page', String(page))
  q.set('pageSize', String(PAGE_SIZE))
  return `${path}?${q}`
}

function useList<T>(path: string, filters: Filters, enabled: boolean) {
  return useInfiniteQuery({
    queryKey: ['public', path, filters],
    enabled,
    placeholderData: keepPreviousData,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => api<Page<T>>(url(path, filters, pageParam)),
    getNextPageParam: (last) =>
      last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
  })
}

export const useEvents = (filters: Filters, enabled = true) =>
  useList<EventSummaryDto>('/api/events', filters, enabled)
export const useDeals = (filters: Filters, enabled = true) =>
  useList<DealSummaryDto>('/api/deals', filters, enabled)

export function useCities() {
  return useQuery({
    queryKey: ['public', 'cities'],
    queryFn: () => api<CityDto[]>('/api/cities'),
    staleTime: 3_600_000,
  })
}

export function useCategories(kind: 'event' | 'deal') {
  return useQuery({
    queryKey: ['public', 'categories', kind],
    queryFn: () => api<CategoryDto[]>(`/api/categories?kind=${kind}`),
    staleTime: 3_600_000,
  })
}

export const flat = <T>(q: { data?: { pages: Page<T>[] } }): T[] =>
  q.data?.pages.flatMap((pg) => pg.items) ?? []
