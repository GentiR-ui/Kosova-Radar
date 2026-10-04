import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'

export type SourceType = 'fixture' | 'jsonFeed' | 'rss'
export type SourceHealth = 'never' | 'healthy' | 'degraded' | 'failing' | 'disabled'

export interface SourceDto {
  id: string
  name: string
  type: SourceType
  baseUrl: string | null
  defaultKind: 'event' | 'deal'
  isActive: boolean
  isUnreliable: boolean
  pollIntervalMinutes: number
  health: SourceHealth
  lastFetchedAt: string | null
  lastSuccessAt: string | null
  lastErrorAt: string | null
  lastError: string | null
  consecutiveFailures: number
}

export interface SourceDetailDto extends SourceDto {
  fixturePayload: string | null
}

export interface SourceRequest {
  name: string
  type: SourceType
  baseUrl: string | null
  fixturePayload: string | null
  defaultKind: 'event' | 'deal'
  pollIntervalMinutes: number
  isActive: boolean
  isUnreliable: boolean
}

export interface RunDto {
  status: 'running' | 'succeeded' | 'failed'
  created: number
  updated: number
  unchanged: number
  failed: number
  errorCode: string | null
}

const listKey = ['admin', 'sources'] as const

export function useSources() {
  return useQuery({ queryKey: listKey, queryFn: () => api<SourceDto[]>('/api/admin/sources') })
}

export function useSourceDetail(id: string | null) {
  return useQuery({
    queryKey: [...listKey, id],
    queryFn: () => api<SourceDetailDto>(`/api/admin/sources/${id}`),
    enabled: id !== null,
    gcTime: 0,
  })
}

/** Create (no id) or update (id) a source. */
export function useSaveSource() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string | null; body: SourceRequest }) =>
      id === null
        ? api<SourceDto>('/api/admin/sources', { method: 'POST', body })
        : api<SourceDto>(`/api/admin/sources/${id}`, { method: 'PUT', body }),
    onSuccess: () => client.invalidateQueries({ queryKey: listKey }),
  })
}

export function useFetchSource() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api<RunDto>(`/api/admin/sources/${id}/fetch`, { method: 'POST' }),
    onSettled: () => client.invalidateQueries({ queryKey: listKey }),
  })
}
