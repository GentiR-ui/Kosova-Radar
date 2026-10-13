import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import { Radar } from 'lucide-react'
import type { ReactNode } from 'react'
import { t } from '../../lib/strings'
import type { Page } from './api'
import { p } from './strings'

const gridClass = 'grid gap-[23px] sm:grid-cols-2 sm:gap-5 lg:grid-cols-4 lg:gap-[18px]'

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
      <div
        role="alert"
        className="flex flex-col items-start gap-4 rounded-md border border-danger/40 bg-surface p-6"
      >
        <p className="font-semibold text-danger">{t.error}</p>
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
          <div aria-hidden="true" className={gridClass}>
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="overflow-hidden rounded-md border border-line bg-surface">
                <div className="skeleton h-[168px]" />
                <div className="space-y-3 p-4">
                  <div className="skeleton h-8 rounded-sm" />
                  <div className="skeleton h-4 w-4/5 rounded-sm" />
                  <div className="skeleton h-3 w-3/5 rounded-sm" />
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    )
  if (empty)
    return (
      <div className="flex flex-col items-center border-y border-dashed border-line px-5 py-14 text-center">
        <Radar aria-hidden="true" className="size-9 text-accent" />
        <p className="mt-4 font-bold">{p.empty}</p>
        <p className="mx-auto mt-2 max-w-sm text-sm text-muted">{p.emptyHint}</p>
      </div>
    )
  return (
    <>
      {grid ? <div className={gridClass}>{children}</div> : children}
      {more.length > 0 && (
        <div className="mt-10 flex justify-center">
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
