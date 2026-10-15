import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import { Radar } from 'lucide-react'
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
  isEmpty = false,
  emptyHint,
  children,
}: {
  queries: Q[]
  /** Lay children out as the card grid of the design; otherwise they render as given. */
  grid?: boolean
  /** Force the empty state even when the lists returned items (a client-side filter removed them). */
  isEmpty?: boolean
  emptyHint?: string
  children: ReactNode
}) {
  const failed = queries.some((q) => q.isError)
  const loading = queries.some((q) => q.isPending)
  const empty = isEmpty || queries.every((q) => q.data?.pages.every((pg) => pg.items.length === 0))
  const more = queries.filter((q) => q.hasNextPage)
  const fetching = queries.some((q) => q.isFetchingNextPage)

  if (failed)
    return (
      <div role="alert" className="card flex flex-col items-start gap-4 p-6">
        <p className="font-bold text-danger">{t.error}</p>
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
      <div role="status">
        <p className={grid ? 'sr-only' : 'py-6 text-muted'}>{t.loading}</p>
        {grid && (
          <div aria-hidden="true" className="feed-grid">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="feed-card">
                <div className="skeleton h-[157px]" />
                <div className="card-body gap-3">
                  <div className="skeleton h-4 w-4/5 rounded-full" />
                  <div className="skeleton h-3 w-3/5 rounded-full" />
                  <div className="skeleton h-8 rounded" />
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    )
  if (empty)
    return (
      <div className="empty-state">
        <Radar aria-hidden="true" size={34} />
        <h3>{p.empty}</h3>
        <p>{emptyHint ?? p.emptyHint}</p>
      </div>
    )
  return (
    <>
      {grid ? <div className="feed-grid">{children}</div> : children}
      {more.length > 0 && (
        <div className="mt-8 flex justify-center">
          <button
            type="button"
            disabled={fetching}
            onClick={() => more.forEach((q) => void q.fetchNextPage())}
            className="btn btn-primary"
          >
            {p.more}
          </button>
        </div>
      )}
    </>
  )
}
