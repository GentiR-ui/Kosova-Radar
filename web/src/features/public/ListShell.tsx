import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { t } from '../../lib/strings'
import type { Page } from './api'
import { LogoMark } from './Logo'
import { p } from './strings'

export const cardGrid = 'grid gap-4 sm:grid-cols-2 lg:grid-cols-4 lg:gap-5'

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
  children,
}: {
  queries: Q[]
  /** Lay children out as a responsive card grid; off when the children bring their own grids. */
  grid?: boolean
  /** Show card skeletons while loading (on by default with `grid`). */
  skeleton?: boolean
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
              <div key={i} className="card overflow-hidden">
                <div className="skeleton h-[170px]" />
                <div className="space-y-3 p-4">
                  <div className="skeleton h-5 rounded-full" />
                  <div className="skeleton h-4 w-4/5 rounded-full" />
                  <div className="skeleton h-4 w-3/5 rounded-full" />
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    )
  if (empty)
    return (
      <div className="flex flex-col items-center rounded-card border border-dashed border-line bg-soft px-5 py-14 text-center">
        <LogoMark className="w-12 text-ink/70" />
        <p className="mt-4 text-lg font-extrabold">{p.empty}</p>
        <p className="mx-auto mt-1.5 max-w-sm text-sm text-muted">{p.emptyHint}</p>
      </div>
    )
  return (
    <>
      {grid ? <div className={cardGrid}>{children}</div> : children}
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
