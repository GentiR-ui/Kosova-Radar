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

const button =
  'rounded-sm border border-ink px-5 py-2.5 text-sm hover:bg-ink hover:text-paper disabled:opacity-50'

/** Loading, error (with retry), empty and "show more" states shared by every public list. */
export default function ListShell({ queries, children }: { queries: Q[]; children: ReactNode }) {
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
          className={button}
        >
          {t.retry}
        </button>
      </div>
    )
  if (loading)
    return (
      <p role="status" className="py-6 text-muted">
        {t.loading}
      </p>
    )
  if (empty) return <p className="py-6 text-muted">{p.empty}</p>
  return (
    <>
      {children}
      {more.length > 0 && (
        <button
          type="button"
          disabled={fetching}
          onClick={() => more.forEach((q) => void q.fetchNextPage())}
          className={`mt-4 ${button}`}
        >
          {p.more}
        </button>
      )}
    </>
  )
}
