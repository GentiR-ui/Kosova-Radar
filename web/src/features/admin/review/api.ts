import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'
import type { Page } from '../../public/api'

export type CandidateStatus = 'pending' | 'rejected' | 'published' | 'merged'
export type Validation = 'valid' | 'needsReview' | 'rejected'
export type Kind = 'event' | 'deal'

export interface IssueDto {
  code: string
  severity: 'rejected' | 'review' | 'info'
  field: string | null
  message: string
}

export interface CandidateSummaryDto {
  id: string
  kind: Kind
  title: string | null
  status: CandidateStatus
  validation: Validation
  issues: IssueDto[]
  sourceId: string
  sourceName: string
  fetchedAt: string
  isSynthetic: boolean
}

export interface CandidateFields {
  title: string | null
  description: string | null
  url: string | null
  imageUrl: string | null
  cityText: string | null
  citySlug: string | null
  venueName: string | null
  categoryText: string | null
  categorySlug: string | null
  startsAt: string | null
  endsAt: string | null
  priceText: string | null
  priceAmount: number | null
  currency: string | null
  merchantName: string | null
  oldPrice: number | null
  newPrice: number | null
  statedDiscountPercent: number | null
  discountPercent: number | null
  validFrom: string | null
  validUntil: string | null
}

export interface CandidateDto extends CandidateSummaryDto {
  externalId: string | null
  sourceUrl: string
  fields: CandidateFields
  evidence: string
  suggestedCategory: { slug: string; name: string; kind: Kind } | null
  duplicateOf: { kind: Kind; id: string; title: string | null; isCandidate: boolean } | null
  matchedItemId: string | null
  publishedItemId: string | null
  rejectionReason: string | null
}

export interface QueueFilters {
  status?: string
  validation?: string
  kind?: string
  sourceId?: string
}

const PAGE_SIZE = 25

export function useCandidates(filters: QueueFilters) {
  return useInfiniteQuery({
    queryKey: ['admin', 'candidates', filters],
    placeholderData: keepPreviousData,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => {
      const q = new URLSearchParams()
      for (const [k, v] of Object.entries(filters)) if (v) q.set(k, v)
      q.set('page', String(pageParam))
      q.set('pageSize', String(PAGE_SIZE))
      return api<Page<CandidateSummaryDto>>(`/api/admin/candidates?${q}`)
    },
    getNextPageParam: (last) =>
      last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
  })
}

export function useSourceOptions() {
  return useQuery({
    queryKey: ['admin', 'candidate-sources'],
    queryFn: () => api<{ id: string; name: string }[]>('/api/admin/candidates/sources'),
  })
}

export function useCandidate(id: string | undefined) {
  return useQuery({
    queryKey: ['admin', 'candidate', id],
    enabled: !!id,
    retry: false,
    queryFn: () => api<CandidateDto>(`/api/admin/candidates/${encodeURIComponent(id!)}`),
  })
}

function useDecision<TBody, TResult>(id: string, action: 'approve' | 'reject' | 'merge') {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: TBody) =>
      api<TResult>(`/api/admin/candidates/${encodeURIComponent(id)}/${action}`, {
        method: 'POST',
        body,
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'candidate', id] })
      void client.invalidateQueries({ queryKey: ['admin', 'candidates'] })
      void client.invalidateQueries({ queryKey: ['admin', 'dashboard'] })
    },
  })
}

export const useApprove = (id: string) =>
  useDecision<undefined, { kind: Kind; itemId: string }>(id, 'approve')
export const useReject = (id: string) => useDecision<{ reason: string }, CandidateDto>(id, 'reject')

export type FieldsRequest = Omit<CandidateFields, 'cityText' | 'categoryText' | 'discountPercent'>

export function useSaveFields(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: FieldsRequest) =>
      api<CandidateDto>(`/api/admin/candidates/${encodeURIComponent(id)}`, { method: 'PUT', body }),
    onSuccess: (saved) => {
      client.setQueryData(['admin', 'candidate', id], saved)
      void client.invalidateQueries({ queryKey: ['admin', 'candidates'] })
    },
  })
}

export const useMerge = (id: string) => useDecision<{ targetId: string }, CandidateDto>(id, 'merge')

export interface AdminItemDto {
  kind: Kind
  id: string
  title: string
  cityName: string | null
  date: string | null
  sourceName: string
}

export function useMergeTargets(kind: Kind, search: string) {
  return useQuery({
    queryKey: ['admin', 'items', 'merge', kind, search],
    queryFn: () => {
      const q = new URLSearchParams({ kind, status: 'published', pageSize: '10' })
      if (search) q.set('search', search)
      return api<Page<AdminItemDto>>(`/api/admin/items?${q}`)
    },
  })
}
