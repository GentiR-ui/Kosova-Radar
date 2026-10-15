import { ArrowLeft, ArrowUpRight, CalendarDays, Clock3, MapPin, Store, Tag } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../lib/api'
import { t } from '../../lib/strings'
import { BrandMark } from './Brand'
import { PageTitle } from './PublicLayout'
import { useDeal, useEvent, type DealDetailDto, type EventDetailDto } from './api'
import { CategoryBadge, DealPrice, Freshness, Media, SourceLink, SyntheticBadge } from './ItemCards'
import { formatDate, formatEventStart, safeHref } from './format'
import { Reveal } from './Intro'
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
      <div role="status" className="page-width py-12">
        <p className="sr-only">{t.loading}</p>
        <div aria-hidden="true" className="space-y-4">
          <div className="skeleton h-5 w-40 rounded-full" />
          <div className="skeleton h-12 w-3/4 rounded-xl" />
          <div className="skeleton aspect-[16/10] w-full rounded-card" />
        </div>
      </div>
    )
  if (query.isError) {
    if (query.error instanceof ApiError && query.error.status === 404)
      return <PageTitle title={t.notFound} />
    return (
      <div role="alert" className="page-width space-y-4 py-12">
        <p className="font-bold text-danger">{t.error}</p>
        <button type="button" onClick={() => void query.refetch()} className="btn">
          {t.retry}
        </button>
      </div>
    )
  }
  return data ? <article className="page-width py-8 sm:py-10">{children(data)}</article> : null
}

/** Shown instead of a picture: the brand plate, not an oversized category tile. */
function BrandTile() {
  return (
    <div className="on-charcoal brand-pattern relative grid aspect-[16/10] max-h-[420px] place-items-center">
      <span aria-hidden="true" className="brand-glow absolute inset-0" />
      <BrandMark className="relative h-20" />
    </div>
  )
}

function BackLink({ to, label }: { to: string; label: string }) {
  return (
    <Link
      to={to}
      className="group inline-flex min-h-11 items-center gap-2 text-sm font-bold text-muted transition-colors hover:text-ink"
    >
      <ArrowLeft
        aria-hidden="true"
        className="size-4 transition-transform duration-200 group-hover:-translate-x-0.5"
      />
      {label}
    </Link>
  )
}

/** One line of the facts block. */
function Fact({ icon, label, children }: { icon: ReactNode; label: string; children: ReactNode }) {
  return (
    <div className="flex gap-3 border-b border-line py-3.5 last:border-0">
      <span
        aria-hidden="true"
        className="grid size-9 shrink-0 place-items-center rounded-xl tile tile-violet [&_svg]:size-4"
      >
        {icon}
      </span>
      <div className="min-w-0">
        <dt className="text-xs font-bold uppercase tracking-wide text-muted">{label}</dt>
        <dd className="mt-0.5 font-bold">{children}</dd>
      </div>
    </div>
  )
}

/** Facts, the source button and the other sources, beside (desktop) or above (mobile) the text. */
function Facts({ item, children }: { item: EventDetailDto | DealDetailDto; children: ReactNode }) {
  // The primary source is shown first; list only the others below.
  const others = item.sources.filter(
    (s) => !(s.name === item.sourceName && s.url === item.sourceUrl),
  )
  const href = safeHref(item.sourceUrl)
  return (
    <aside className="card p-5 sm:p-6 lg:sticky lg:top-24">
      <h2 className="text-lg">{p.details}</h2>
      <dl className="mt-3">{children}</dl>
      {href && (
        <a
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          className="btn btn-primary mt-5 w-full"
        >
          {p.openSource}
          <ArrowUpRight aria-hidden="true" className="size-4" />
        </a>
      )}
      <div className="mt-4 flex flex-col gap-1.5 rounded-xl bg-soft p-4 text-sm text-muted">
        <SourceLink name={item.sourceName} url={item.sourceUrl} />
        <Freshness lastVerifiedAt={item.lastVerifiedAt} freshness={item.freshness} />
      </div>
      {others.length > 0 && (
        <section aria-labelledby="other-sources" className="mt-5">
          <h2 id="other-sources" className="text-base">
            {p.otherSources}
          </h2>
          <ul className="mt-2 space-y-1.5 text-sm text-muted">
            {others.map((s) => (
              <li key={`${s.name}|${s.url}`}>
                <SourceLink name={s.name} url={s.url} />
              </li>
            ))}
          </ul>
        </section>
      )}
    </aside>
  )
}

function Header({
  kind,
  item,
  ended,
}: {
  kind: 'event' | 'deal'
  item: EventDetailDto | DealDetailDto
  ended: string | null
}) {
  return (
    <Reveal delay={0.04}>
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <span className="badge badge-soft">{kind === 'event' ? p.typeEvent : p.typeDeal}</span>
        {item.category && <CategoryBadge category={item.category} />}
        {item.isSynthetic && <SyntheticBadge />}
        {ended && (
          <p role="status" className="badge bg-ink text-white">
            {ended}
          </p>
        )}
      </div>
      <h1 className="mt-4 max-w-4xl text-[34px] leading-[1.08] sm:text-5xl">{item.title}</h1>
    </Reveal>
  )
}

function Body({
  media,
  description,
  facts,
}: {
  media: ReactNode
  description: string | null
  facts: ReactNode
}) {
  return (
    <div className="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_22rem] lg:gap-10">
      <div className="min-w-0 space-y-6">
        <Reveal delay={0.1}>
          <div className="overflow-hidden rounded-card border border-line">{media}</div>
        </Reveal>
        {description && (
          <Reveal delay={0.14}>
            <p className="max-w-[68ch] whitespace-pre-line text-[15px] leading-[1.8] text-muted sm:text-base">
              {description}
            </p>
          </Reveal>
        )}
      </div>
      <Reveal delay={0.12} className="order-first lg:order-none">
        {facts}
      </Reveal>
    </div>
  )
}

export function EventDetailPage() {
  const { id } = useParams()
  const query = useEvent(id)
  return (
    <DetailShell query={query} data={query.data}>
      {(e) => (
        <>
          <BackLink to="/events" label={p.backToEvents} />
          <Header kind="event" item={e} ended={e.status === 'expired' ? p.eventEnded : null} />
          <Body
            media={
              safeHref(e.imageUrl ?? '') ? (
                <Media url={e.imageUrl} category={e.category} eager className="max-h-[420px]" />
              ) : (
                <BrandTile />
              )
            }
            description={e.description}
            facts={
              <Facts item={e}>
                <Fact icon={<CalendarDays />} label={p.when}>
                  <span className="tnum">{formatEventStart(e.startsAt, e.startTimeKnown)}</span>
                </Fact>
                <Fact icon={<MapPin />} label={p.where}>
                  {[e.city.name, e.venueName].filter(Boolean).join(' · ')}
                </Fact>
                <Fact icon={<Tag />} label={p.price}>
                  {e.priceText ?? p.priceUnknown}
                </Fact>
              </Facts>
            }
          />
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
          <BackLink to="/deals" label={p.backToDeals} />
          <Header kind="deal" item={d} ended={d.status === 'expired' ? p.dealExpired : null} />
          <Body
            media={
              safeHref(d.imageUrl ?? '') ? (
                <Media
                  url={d.imageUrl}
                  category={d.category}
                  kind="deal"
                  eager
                  contain
                  className="max-h-[420px]"
                />
              ) : (
                <BrandTile />
              )
            }
            description={d.description}
            facts={
              <Facts item={d}>
                <Fact icon={<Tag />} label={p.price}>
                  <DealPrice deal={d} withDiscount />
                </Fact>
                <Fact icon={<Clock3 />} label={p.validity}>
                  {d.validUntil
                    ? p.validUntil(formatDate(d.validUntil))
                    : d.isTodaysDeal
                      ? p.todaysDeal
                      : p.validityUnknown}
                </Fact>
                {(d.merchantName || d.city) && (
                  <Fact icon={<Store />} label={p.merchant}>
                    {[d.merchantName, d.city?.name].filter(Boolean).join(' · ')}
                  </Fact>
                )}
              </Facts>
            }
          />
        </>
      )}
    </DetailShell>
  )
}
