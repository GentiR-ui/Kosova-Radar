import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../../lib/api'

export type Role = 'admin' | 'reviewer'

export interface Me {
  userId: string
  email: string
  role: Role
}

export const meKey = ['me'] as const

/** The signed-in user, or null when signed out (401). */
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async (): Promise<Me | null> => {
      try {
        return await api<Me>('/api/me')
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: 60_000,
  })
}

export function useLogin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { email: string; password: string }) =>
      api<Me>('/api/auth/login', { method: 'POST', body }),
    meta: { expects401: true }, // 401 here means "invalid credentials", not "session ended"
    onSuccess: (me) => client.setQueryData(meKey, me),
  })
}

export function useLogout() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      try {
        await api<void>('/api/auth/logout', { method: 'POST' })
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 401)) throw e // already signed out counts as success
      }
    },
    // Only a confirmed logout clears local state: on failure the server session may still be valid.
    onSuccess: () => {
      client.setQueryData(meKey, null)
      client.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' })
    },
  })
}
