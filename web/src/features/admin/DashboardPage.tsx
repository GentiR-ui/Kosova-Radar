import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/strings'

const s = t.admin.dashboard

/** GET /api/admin/dashboard (design.md §5). The endpoint arrives in T031; until then the tiles show an error state. */
interface Dashboard {
  pendingCandidates: number
  needsReviewCandidates: number
  activeEvents: number
  activeDeals: number
  expiredLast7Days: number
  failingSources: number
  degradedSources: number
  aiCallsToday: number
  aiErrorsToday: number
}

export default function DashboardPage() {
  const dashboard = useQuery({
    queryKey: ['admin', 'dashboard'],
    queryFn: () => api<Dashboard>('/api/admin/dashboard'),
  })
  const d = dashboard.data
  const stats: [string, number | undefined][] = [
    [s.pending, d?.pendingCandidates],
    [s.needsReview, d?.needsReviewCandidates],
    [s.activeEvents, d?.activeEvents],
    [s.activeDeals, d?.activeDeals],
    [s.sourcesWithProblems, d ? d.failingSources + d.degradedSources : undefined],
  ]

  return (
    <>
      <h1 className="mb-8 text-3xl">{s.title}</h1>
      {dashboard.isPending && (
        <p role="status" className="sr-only">
          {t.loading}
        </p>
      )}
      {dashboard.isError && (
        <div role="alert" className="mb-6 flex items-center gap-3 text-sm">
          <span>{t.error}</span>
          <button
            type="button"
            onClick={() => void dashboard.refetch()}
            className="rounded-md border border-line px-3 py-1"
          >
            {t.retry}
          </button>
        </div>
      )}
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line sm:grid-cols-3 lg:grid-cols-5">
        {stats.map(([label, value]) => (
          <div key={label} className="bg-surface p-5">
            <dt className="text-sm text-muted">{label}</dt>
            <dd className="mt-2 font-display text-3xl">{value ?? '—'}</dd>
          </div>
        ))}
      </dl>
    </>
  )
}
