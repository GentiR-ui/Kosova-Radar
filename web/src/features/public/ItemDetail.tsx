import { ArrowLeft, CalendarDays, Clock3, MapPin, Store, Tag, Ticket } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../lib/api'
import { t } from '../../lib/strings'
import { container, PageTitle } from './PublicLayout'
import { useDeal, useEvent, type CategoryDto, type DealDetailDto, type EventDetailDto } from './api'
import { formatDate, formatEventStart } from './format'
import { DealPrice, Freshness, Media, SourceLink, SyntheticBadge } from './ItemCards'
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
      <div role="status" className={`${container} py-12`}>
        <p className="sr-only">{t.loading}</p>
        <div aria-hidden="true" className="space-y-4">
          <div className="skeleton h-5 w-40 rounded-sm" />
          <div className="skeleton h-12 w-3/4 rounded-sm" />
          <div className="skeleton h-[240px] w-full rounded-md sm:h-[380px]" />
        </div>
      </div>
    )
  if (query.isError) {
    if (query.error instanceof ApiError && query.error.status === 404)
      return <PageTitle title={t.notFound} />
    return (
      <div role="alert" className={`${container} space-y-4 py-12`}>
        <p className="font-semibold text-danger">{t.error}</p>
        <button type="button" onClick={() => void query.refetch()} className="btn">
          {t.retry}
        </button>
      </div>
    )
  }
  return data ? <article className={`${container} py-8 sm:py-12`}>{children(data)}</article> : null
}

type Tone = 'event' | 'deal'
const toneClasses = {
  event: { badge: 'bg-event', soft: 'bg-event-soft text-event', border: 'border-event/40' },
  deal: { badge: 'bg-accent', soft: 'bg-accent-soft text-accent', border: 'border-accent/40' },
} as const

function BackLink({ to, label }: { to: string; label: string }) {
  return (
    <Link
      to={to}
      className="group inline-flex min-h-11 items-center gap-2 rounded-sm border border-line px-4 text-sm font-semibold text-ink transition-colors hover:border-accent hover:text-accent"
    >
      <ArrowLeft
        aria-hidden="true"
        className="size-4 transition-transform duration-200 group-hover:-translate-x-0.5"
      />
      {label}
    </Link>
  )
}

function Header({
  tone,
  title,
  category,
  synthetic,
  ended,
}: {
  tone: Tone
  title: string
  category: CategoryDto | null
  synthetic: boolean
  ended: string | null
}) {
  const Icon = tone === 'event' ? Ticket : Tag
  return (
    <Reveal delay={0.04}>
      <div className="mt-8 flex flex-wrap items-center gap-2.5 text-xs">
        <span
          className={`flex items-center gap-1 rounded-[3px] px-2 py-1 font-bold text-accent-ink ${toneClasses[tone].badge}`}
        >
          <Icon aria-hidden="true" className="size-3" />
          {tone === 'event' ? p.typeEvent : p.typeDeal}
        </span>
        {category && <span className="font-semibold text-muted">{category.name}</span>}
        {synthetic && <SyntheticBadge />}
        {ended && (
          <p
            role="status"
            className="rounded-sm border border-line px-2 py-1 font-semibold text-muted"
          >
            {ended}
          </p>
        )}
      </div>
      <h1 className="mt-4 max-w-4xl text-[34px] leading-[1.12] sm:text-5xl">{title}</h1>
    </Reveal>
  )
}

function Hero({ url }: { url: string | null }) {
  return (
    <Reveal delay={0.1}>
      <Media
        url={url}
        eager
        className="h-[240px] rounded-md border border-line sm:h-[380px] lg:h-[440px]"
      />
    </Reveal>
  )
}

function Fact({
  tone,
  icon,
  label,
  children,
}: {
  tone: Tone
  icon: ReactNode
  label: string
  children: ReactNode
}) {
  return (
    <div className="flex gap-3.5 py-4 first:pt-0 last:pb-0">
      <span
        aria-hidden="true"
        className={`grid size-10 shrink-0 place-items-center rounded-sm [&_svg]:size-[18px] ${toneClasses[tone].soft}`}
      >
        {icon}
      </span>
      <div className="min-w-0">
        <dt className="text-[11px] font-bold uppercase tracking-wide text-muted">{label}</dt>
        <dd className="mt-0.5 font-semibold text-ink">{children}</dd>
      </div>
    </div>
  )
}

/** Key facts, the source and the other sources, beside (desktop) or above (mobile) the text. */
function InfoCard({
  tone,
  item,
  children,
}: {
  tone: Tone
  item: EventDetailDto | DealDetailDto
  children: ReactNode
}) {
  // The primary source is shown first; list only the others below.
  const others = item.sources.filter(
    (s) => !(s.name === item.sourceName && s.url === item.sourceUrl),
  )
  return (
    <aside
      className={`rounded-md border bg-surface p-5 sm:p-6 lg:sticky lg:top-28 ${toneClasses[tone].border}`}
    >
      <h2 className="text-lg">{p.details}</h2>
      <dl className="mt-5 divide-y divide-line">{children}</dl>
      <div className="mt-6 flex flex-col gap-1.5 rounded-sm bg-paper p-4 text-sm text-muted">
        <SourceLink name={item.sourceName} url={item.sourceUrl} tone={tone} />
        <Freshness lastVerifiedAt={item.lastVerifiedAt} freshness={item.freshness} />
      </div>
      {others.length > 0 && (
        <section aria-labelledby="other-sources" className="mt-6">
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

function Body({
  hero,
  description,
  info,
}: {
  hero: ReactNode
  description: string | null
  info: ReactNode
}) {
  return (
    <div className="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_22rem] lg:gap-10">
      <div className="min-w-0 space-y-8">
        {hero}
        {description && (
          <Reveal delay={0.14}>
            <p className="max-w-[68ch] whitespace-pre-line leading-[1.8] text-muted sm:text-lg">
              {description}
            </p>
          </Reveal>
        )}
      </div>
      <Reveal delay={0.12} className="order-first lg:order-none">
        {info}
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
          <Header
            tone="event"
            title={e.title}
            category={e.category}
            synthetic={e.isSynthetic}
            ended={e.status === 'expired' ? p.eventEnded : null}
          />
          <Body
            hero={<Hero url={e.imageUrl} />}
            description={e.description}
            info={
              <InfoCard tone="event" item={e}>
                <Fact tone="event" icon={<CalendarDays />} label={p.when}>
                  {formatEventStart(e.startsAt, e.startTimeKnown)}
                </Fact>
                <Fact tone="event" icon={<MapPin />} label={p.where}>
                  {[e.city.name, e.venueName].filter(Boolean).join(' · ')}
                </Fact>
                {e.priceText && (
                  <Fact tone="event" icon={<Tag />} label={p.price}>
                    {e.priceText}
                  </Fact>
                )}
              </InfoCard>
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
          <Header
            tone="deal"
            title={d.title}
            category={d.category}
            synthetic={d.isSynthetic}
            ended={d.status === 'expired' ? p.dealExpired : null}
          />
          <Body
            hero={<Hero url={d.imageUrl} />}
            description={d.description}
            info={
              <InfoCard tone="deal" item={d}>
                <Fact tone="deal" icon={<Tag />} label={p.price}>
                  <DealPrice deal={d} withDiscount />
                </Fact>
                <Fact tone="deal" icon={<Clock3 />} label={p.validity}>
                  {d.validUntil
                    ? p.validUntil(formatDate(d.validUntil))
                    : d.isTodaysDeal
                      ? p.todaysDeal
                      : p.validityUnknown}
                </Fact>
                {(d.merchantName || d.city) && (
                  <Fact tone="deal" icon={<Store />} label={p.merchant}>
                    {[d.merchantName, d.city?.name].filter(Boolean).join(' · ')}
                  </Fact>
                )}
              </InfoCard>
            }
          />
        </>
      )}
    </DetailShell>
  )
}
