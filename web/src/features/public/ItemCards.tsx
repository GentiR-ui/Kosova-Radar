import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { DealSummaryDto, EventSummaryDto } from './api'
import { formatDate, formatEventStart, formatMoney, safeHref } from './format'
import { p } from './strings'

export function SyntheticBadge() {
  return (
    <span className="inline-block rounded-full border border-warn px-2 py-0.5 text-xs text-warn">
      {p.synthetic}
    </span>
  )
}

export function Freshness({
  lastVerifiedAt,
  freshness,
}: {
  lastVerifiedAt: string
  freshness: 'fresh' | 'stale'
}) {
  return <span>{freshness === 'fresh' ? p.fresh : p.stale(formatDate(lastVerifiedAt))}</span>
}

export function SourceLink({ name, url }: { name: string; url: string }) {
  const href = safeHref(url)
  const label = p.source(name)
  if (!href) return <span>{label}</span>
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className="underline underline-offset-4 hover:text-ink"
    >
      {label}
    </a>
  )
}

const imageBox = 'h-24 w-28 shrink-0 rounded-sm bg-line sm:h-28 sm:w-40'

function Thumb({ url }: { url: string | null }) {
  const href = url ? safeHref(url) : undefined
  return href ? (
    <img
      src={href}
      alt=""
      width={160}
      height={112}
      loading="lazy"
      referrerPolicy="no-referrer"
      className={`${imageBox} object-cover`}
    />
  ) : (
    <div aria-hidden="true" className={imageBox} />
  )
}

function Card({
  title,
  to,
  imageUrl,
  synthetic,
  children,
  footer,
}: {
  title: string
  to: string
  imageUrl: string | null
  synthetic: boolean
  children: ReactNode
  footer: ReactNode
}) {
  return (
    <article className="flex gap-4 border-t border-line py-5 first:border-t-0">
      <Thumb url={imageUrl} />
      <div className="min-w-0 flex-1 space-y-1.5">
        <h2 className="text-xl leading-snug">
          <Link to={to} className="underline-offset-4 hover:underline">
            {title}
          </Link>
        </h2>
        {synthetic && <SyntheticBadge />}
        <div className="space-y-0.5 text-sm text-muted">{children}</div>
        <p className="flex flex-wrap gap-x-3 text-xs text-muted">{footer}</p>
      </div>
    </article>
  )
}

export function EventCard({ event }: { event: EventSummaryDto }) {
  return (
    <Card
      title={event.title}
      to={`/events/${event.id}`}
      imageUrl={event.imageUrl}
      synthetic={event.isSynthetic}
      footer={
        <>
          <SourceLink name={event.sourceName} url={event.sourceUrl} />
          <Freshness lastVerifiedAt={event.lastVerifiedAt} freshness={event.freshness} />
        </>
      }
    >
      <p className="text-ink">{formatEventStart(event.startsAt, event.startTimeKnown)}</p>
      <p>{[event.city.name, event.venueName].filter(Boolean).join(' · ')}</p>
    </Card>
  )
}

export function DealPrice({ deal }: { deal: DealSummaryDto }) {
  if (deal.newPrice === null && deal.oldPrice === null) {
    return <span>{deal.priceText ?? p.priceUnknown}</span>
  }
  return (
    <span className="flex flex-wrap items-baseline gap-x-2">
      {deal.oldPrice !== null && <s>{formatMoney(deal.oldPrice, deal.currency)}</s>}
      {deal.newPrice !== null && (
        <strong className="text-ink">{formatMoney(deal.newPrice, deal.currency)}</strong>
      )}
      {deal.discountPercent !== null && <span>{`-${deal.discountPercent}%`}</span>}
    </span>
  )
}

export function DealCard({ deal }: { deal: DealSummaryDto }) {
  return (
    <Card
      title={deal.title}
      to={`/deals/${deal.id}`}
      imageUrl={deal.imageUrl}
      synthetic={deal.isSynthetic}
      footer={
        <>
          <SourceLink name={deal.sourceName} url={deal.sourceUrl} />
          <Freshness lastVerifiedAt={deal.lastVerifiedAt} freshness={deal.freshness} />
        </>
      }
    >
      <DealPrice deal={deal} />
      <p>{deal.validUntil ? p.validUntil(formatDate(deal.validUntil)) : p.validityUnknown}</p>
      <p>{[deal.merchantName, deal.city?.name].filter(Boolean).join(' · ')}</p>
    </Card>
  )
}
