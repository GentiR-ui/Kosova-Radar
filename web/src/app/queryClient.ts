import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { ApiError } from '../lib/api'
import { meKey } from '../features/auth/api'

/**
 * One place for "the session has ended" (restart, redeploy, deactivation): any 401 marks the user as signed out,
 * so the admin guard redirects to the login page. Mutations that expect a 401 (login) opt out via meta.
 */
export function createQueryClient(): QueryClient {
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: (error) => signedOutOn401(error) }),
    mutationCache: new MutationCache({
      onError: (error, _vars, _ctx, mutation) => {
        if (!mutation.meta?.expects401) signedOutOn401(error)
      },
    }),
    defaultOptions: {
      queries: {
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 1,
        refetchOnWindowFocus: false,
      },
    },
  })

  function signedOutOn401(error: unknown) {
    if (!(error instanceof ApiError && error.status === 401)) return
    client.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' }) // never show one user's data to the next
    client.setQueryData(meKey, null)
  }

  return client
}
