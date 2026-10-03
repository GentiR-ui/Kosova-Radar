import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'
import type { Role } from '../../auth/api'

export interface UserDto {
  id: string
  email: string
  role: Role
  isActive: boolean
  createdAt: string
}

export interface CreateUserBody {
  email: string
  password: string
  role: Role
}

const usersKey = ['admin', 'users'] as const

export function useUsers() {
  return useQuery({ queryKey: usersKey, queryFn: () => api<UserDto[]>('/api/admin/users') })
}

export function useCreateUser() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateUserBody) =>
      api<UserDto>('/api/admin/users', { method: 'POST', body }),
    onSuccess: () => client.invalidateQueries({ queryKey: usersKey }),
  })
}

export function useDeactivateUser() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/admin/users/${id}/deactivate`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: usersKey }),
  })
}
