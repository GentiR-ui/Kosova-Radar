import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../lib/api'
import { t } from '../../lib/strings'
import { PageTitle } from './PublicLayout'
import { useDeal, useEvent, type DealDetailDto, type EventDetailDto } from './api'
import { formatDate, formatEventStart, safeHref } from './format'
import { DealPrice, Freshness, SourceLink, SyntheticBadge } from './ItemCards'
import { p } from './strings'

interface QueryState {
  isPending: boolean
  isError: boolean
  error: Error | null
  refetch: () => unknown
}

/** Loading, 404 and error states shared by both detail pages. */
function DetailShell<T>({
  query,
  data,
  children,
}: {
  query: QueryState
  data: T | undefined
  children: (item: T) => ReactNode
}) {
  if (query.isPending)
    return (
      <p role="status" className="py-6 text-muted">
        {t.loading}
      </p>
    )
  if (query.isError) {
    if (query.error instanceof ApiError && query.error.status === 404)
      return <PageTitle title={t.notFound} />
    return (
      <div role="alert" className="space-y-3 py-6">
        <p>{t.error}</p>
        <button type="button" onClick={() => void query.refetch()} className="btn">
          {t.retry}
        </button>
      </div>
    )
  }
  return data ? <article className="max-w-3xl">{children(data)}</article> : null
}

function Header({
  title,
  synthetic,
  ended,
}: {
  title: string
  synthetic: boolean
  ended: string | null
}) {
  return (
    <>
      <h1 className="text-4xl leading-tight sm:text-5xl">{title}</h1>
      <div className="mt-4 flex flex-wrap items-center gap-3">
        {synthetic && <SyntheticBadge />}
        {ended && (
          <p role="status" className="rounded-sm border border-line px-3 py-1 text-sm text-muted">
            {ended}
          </p>
        )}
      </div>
    </>
  )
}

function Footer({ item, back }: { item: EventDetailDto | DealDetailDto; back: ReactNode }) {
  // The primary source is shown first; list only the others below.
  const others = item.sources.filter(
    (s) => !(s.name === item.sourceName && s.url === item.sourceUrl),
  )
  return (
    <>
      <p className="mt-8 flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted">
        <SourceLink name={item.sourceName} url={item.sourceUrl} />
        <Freshness lastVerifiedAt={item.lastVerifiedAt} freshness={item.freshness} />
      </p>
      {others.length > 0 && (
        <section aria-labelledby="other-sources" className="mt-6">
          <h2 id="other-sources" className="text-lg">
            {p.otherSources}
          </h2>
          <ul className="mt-2 space-y-1 text-sm text-muted">
            {others.map((s) => (
              <li key={`${s.name}|${s.url}`}>
                <SourceLink name={s.name} url={s.url} />
              </li>
            ))}
          </ul>
        </section>
      )}
      <p className="mt-10 text-sm">{back}</p>
    </>
  )
}

function Hero({ url }: { url: string | null }) {
  const href = url ? safeHref(url) : undefined
  return href ? (
    <img
      src={href}
      alt=""
      referrerPolicy="no-referrer"
      className="mt-6 max-h-96 w-full rounded-sm object-cover"
    />
  ) : null
}

const description = (d: string | null) =>
  d ? <p className="mt-6 whitespace-pre-line leading-relaxed">{d}</p> : null

const backLink = (to: string, label: string) => (
  <Link to={to} className="underline underline-offset-4">
    {label}
  </Link>
)

export function EventDetailPage() {
  const { id } = useParams()
  const query = useEvent(id)
  return (
    <DetailShell query={query} data={query.data}>
      {(e) => (
        <>
          <Header
            title={e.title}
            synthetic={e.isSynthetic}
            ended={e.status === 'expired' ? p.eventEnded : null}
          />
          <p className="mt-6 text-lg">{formatEventStart(e.startsAt, e.startTimeKnown)}</p>
          <p className="text-muted">{[e.city.name, e.venueName].filter(Boolean).join(' · ')}</p>
          {e.priceText && <p className="mt-1 text-muted">{e.priceText}</p>}
          <Hero url={e.imageUrl} />
          {description(e.description)}
          <Footer item={e} back={backLink('/events', p.backToEvents)} />
        </>
      )}
    </DetailShell>
  )
}

export function DealDetailPage() {
  const { id } = useParams()
  const query = useDeal(id)
  return (
    <DetailShell query={query} data={query.data}>
      {(d) => (
        <>
          <Header
            title={d.title}
            synthetic={d.isSynthetic}
            ended={d.status === 'expired' ? p.dealExpired : null}
          />
          <p className="mt-6 text-lg">
            <DealPrice deal={d} />
          </p>
          <p className="text-muted">
            {d.validUntil ? p.validUntil(formatDate(d.validUntil)) : p.validityUnknown}
          </p>
          <p className="text-muted">{[d.merchantName, d.city?.name].filter(Boolean).join(' · ')}</p>
          <Hero url={d.imageUrl} />
          {description(d.description)}
          <Footer item={d} back={backLink('/deals', p.backToDeals)} />
        </>
      )}
    </DetailShell>
  )
}
