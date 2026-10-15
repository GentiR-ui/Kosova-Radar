import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import { CalendarSearch } from 'lucide-react'
import type { ReactNode } from 'react'
import { t } from '../../lib/strings'
import type { Page } from './api'
import { p } from './strings'

export const cardGrid = 'grid gap-6 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4'

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
  skeleton = grid,
  emptyHint,
  children,
}: {
  queries: Q[]
  /** Lay children out as the responsive card grid; off when the children bring their own layout. */
  grid?: boolean
  /** Show card skeletons while loading (on by default with `grid`). */
  skeleton?: boolean
  emptyHint?: string
  children: ReactNode
}) {
  const failed = queries.some((q) => q.isError)
  const loading = queries.some((q) => q.isPending)
  const empty = queries.every((q) => q.data?.pages.every((pg) => pg.items.length === 0))
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
        <p className={skeleton ? 'sr-only' : 'py-6 text-muted'}>{t.loading}</p>
        {skeleton && (
          <div aria-hidden="true" className={cardGrid}>
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="card overflow-hidden p-3">
                <div className="skeleton aspect-[16/10] rounded-[20px]" />
                <div className="space-y-3 p-3 pt-4">
                  <div className="skeleton h-4 w-2/5 rounded-full" />
                  <div className="skeleton h-5 w-4/5 rounded-full" />
                  <div className="skeleton h-4 w-3/5 rounded-full" />
                  <div className="skeleton h-6 w-1/3 rounded-full" />
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
        <span className="tile tile-violet size-14">
          <CalendarSearch aria-hidden="true" className="size-7" />
        </span>
        <p className="mt-4 text-lg font-extrabold">{p.empty}</p>
        <p className="mt-1.5 max-w-sm text-sm text-muted">{emptyHint ?? p.emptyHint}</p>
      </div>
    )
  return (
    <>
      {grid ? <div className={cardGrid}>{children}</div> : children}
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
