import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { t } from '../../lib/strings'
import type { Page } from './api'
import { p } from './strings'

type Q = Pick<
  UseInfiniteQueryResult<{ pages: Page<unknown>[] }, Error>,
  | 'isError'
  | 'isPending'
  | 'data'
  | 'hasNextPage'
  | 'isFetchingNextPage'
  | 'fetchNextPage'
  | 'refetch'
>

/** Loading, error (with retry), empty and "show more" states shared by every public list. */
export default function ListShell({
  queries,
  grid = false,
  children,
}: {
  queries: Q[]
  /** Lay children out as a responsive card grid (public cards); otherwise they render as given. */
  grid?: boolean
  children: ReactNode
}) {
  const failed = queries.some((q) => q.isError)
  const loading = queries.some((q) => q.isPending)
  const empty = queries.every((q) => q.data?.pages.every((pg) => pg.items.length === 0))
  const more = queries.filter((q) => q.hasNextPage)
  const fetching = queries.some((q) => q.isFetchingNextPage)

  if (failed)
    return (
      <div role="alert" className="space-y-3 py-6">
        <p>{t.error}</p>
        <button
          type="button"
          onClick={() => queries.filter((q) => q.isError).forEach((q) => void q.refetch())}
          className="btn"
        >
          {t.retry}
        </button>
      </div>
    )
  if (loading)
    return (
      <div role="status" className="py-6">
        <p className="text-muted">{t.loading}</p>
        {grid && (
          <div aria-hidden="true" className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <div
                key={i}
                className="h-64 animate-pulse rounded-card bg-line/60 motion-reduce:animate-none"
              />
            ))}
          </div>
        )}
      </div>
    )
  if (empty) return <p className="py-6 text-muted">{p.empty}</p>
  return (
    <>
      {grid ? <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">{children}</div> : children}
      {more.length > 0 && (
        <button
          type="button"
          disabled={fetching}
          onClick={() => more.forEach((q) => void q.fetchNextPage())}
          className="btn mt-8"
        >
          {p.more}
        </button>
      )}
    </>
  )
}
