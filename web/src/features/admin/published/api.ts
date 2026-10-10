import {
  keepPreviousData,
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { api } from '../../../lib/api'
import type { DealDetailDto, EventDetailDto, Page } from '../../public/api'
import { pristinaToday } from '../../public/format'
import type { CandidateFields, FieldsRequest, Kind } from '../review/api'

export type AdminStatus = 'published' | 'expired' | 'withdrawn'

export interface PublishedItemDto {
  kind: Kind
  id: string
  title: string
  adminStatus: AdminStatus
  cityName: string | null
  date: string | null
  sourceName: string
  lastVerifiedAt: string
}

export interface PublishedFilters {
  kind?: string
  status?: string
}

const PAGE_SIZE = 25

export function usePublishedItems(filters: PublishedFilters) {
  return useInfiniteQuery({
    queryKey: ['admin', 'items', filters],
    placeholderData: keepPreviousData,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => {
      const q = new URLSearchParams()
      for (const [k, v] of Object.entries(filters)) if (v) q.set(k, v)
      q.set('page', String(pageParam))
      q.set('pageSize', String(PAGE_SIZE))
      return api<Page<PublishedItemDto>>(`/api/admin/items?${q}`)
    },
    getNextPageParam: (last) =>
      last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
  })
}

const path = (kind: Kind, id: string) => `/api/admin/${kind}s/${encodeURIComponent(id)}`

export type PublishedDetail = (EventDetailDto | DealDetailDto) & { adminStatus: AdminStatus }

export function usePublishedItem(kind: Kind, id: string | undefined) {
  return useQuery({
    queryKey: ['admin', 'item', kind, id],
    enabled: !!id,
    retry: false,
    queryFn: () => api<PublishedDetail>(path(kind, id!)),
  })
}

export function useSaveItem(kind: Kind, id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: FieldsRequest) => api<PublishedDetail>(path(kind, id), { method: 'PUT', body }),
    onSuccess: (saved) => {
      client.setQueryData(['admin', 'item', kind, id], saved)
      void client.invalidateQueries({ queryKey: ['admin', 'items'] })
    },
  })
}

export function useItemAction(action: 'expire' | 'withdraw') {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (item: Pick<PublishedItemDto, 'kind' | 'id'>) =>
      api<void>(`${path(item.kind, item.id)}/${action}`, { method: 'POST' }),
    // A failed action usually means the item changed under us: reload the list to show its real status.
    onError: () => void client.invalidateQueries({ queryKey: ['admin', 'items'] }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'items'] })
      void client.invalidateQueries({ queryKey: ['admin', 'item'] })
      void client.invalidateQueries({ queryKey: ['admin', 'dashboard'] })
    },
  })
}

/** The editable fields of a published item, in the shape the shared fields form works with. */
export function fieldsOf(item: PublishedDetail): CandidateFields {
  const event = 'startsAt' in item ? item : null
  const deal = 'startsAt' in item ? null : item
  return {
    title: item.title,
    description: item.description,
    url: item.sourceUrl,
    imageUrl: item.imageUrl,
    cityText: null,
    citySlug: item.city?.slug ?? null,
    venueName: event?.venueName ?? null,
    categoryText: null,
    categorySlug: item.category?.slug ?? null,
    startsAt: event && event.startTimeKnown !== false ? event.startsAt : null,
    startDate: event && event.startTimeKnown === false ? pristinaToday(new Date(event.startsAt)) : null,
    endsAt: event?.endsAt ?? null,
    priceText: item.priceText,
    priceAmount: event?.priceAmount ?? null,
    currency: item.currency,
    merchantName: deal?.merchantName ?? null,
    oldPrice: deal?.oldPrice ?? null,
    newPrice: deal?.newPrice ?? null,
    statedDiscountPercent: null,
    discountPercent: null,
    validFrom: deal?.validFrom ?? null,
    validUntil: deal?.validUntil ?? null,
  }
}
