import { motion } from 'motion/react'
import { ArrowUpRight, CalendarDays, Clock3, MapPin, Store } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { CategoryDto, DealSummaryDto, EventSummaryDto } from './api'
import { categoryStyle } from './categories'
import Countdown from './Countdown'
import { endOfPristinaDay, formatDate, formatEventStart, formatMoney, safeHref } from './format'
import { spring } from './motion'
import { p } from './strings'

export function CategoryBadge({ category }: { category: CategoryDto }) {
  const { tint, Icon } = categoryStyle(category.slug, category.kind)
  return (
    <span className={`badge ${tint}`}>
      <Icon aria-hidden="true" className="size-3.5" />
      {category.name}
    </span>
  )
}

export function SyntheticBadge() {
  return <span className="badge badge-outline">{p.synthetic}</span>
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
      className="above-stretched inline-flex w-fit items-center gap-1 font-bold text-brand-text underline-offset-4 hover:underline"
    >
      {label}
      <ArrowUpRight aria-hidden="true" className="size-3.5 shrink-0" />
    </a>
  )
}

/** Discount pill. The minus is a plain "-" so the published percentage stays searchable. */
export function DiscountBadge({ percent }: { percent: number }) {
  return <span className="badge badge-discount text-sm">{`-${percent}%`}</span>
}

/** Picture of the item, or a tile in the category colour with its icon. */
export function Media({
  url,
  category,
  kind = 'event',
  eager = false,
  className = '',
}: {
  url: string | null
  category?: CategoryDto | null
  kind?: 'event' | 'deal'
  /** The main picture of a detail page: load it first. */
  eager?: boolean
  className?: string
}) {
  const href = url ? safeHref(url) : undefined
  const { tint, Icon } = categoryStyle(category?.slug, kind)
  return (
    <div className={`media-frame ${className}`}>
      {href ? (
        <img
          src={href}
          alt=""
          width={1024}
          height={640}
          loading={eager ? 'eager' : 'lazy'}
          fetchPriority={eager ? 'high' : 'auto'}
          decoding="async"
          referrerPolicy="no-referrer"
        />
      ) : (
        <div className={`grid size-full place-items-center ${tint}`}>
          <Icon aria-hidden="true" className="size-10 opacity-80" />
        </div>
      )}
    </div>
  )
}

function Meta({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <span className="flex items-center gap-1.5">
      <span aria-hidden="true" className="shrink-0 [&_svg]:size-3.5">
        {icon}
      </span>
      {children}
    </span>
  )
}

function Card({
  title,
  to,
  index,
  media,
  children,
  footer,
}: {
  title: string
  to: string
  index: number
  media: ReactNode
  children: ReactNode
  footer: ReactNode
}) {
  return (
    <motion.article
      initial={{ opacity: 0, y: 20 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '0px 0px -40px 0px' }}
      transition={{ ...spring, delay: (index % 4) * 0.04 }}
      className="card card-hover relative flex h-full flex-col overflow-hidden"
    >
      {media}
      <div className="flex min-w-0 flex-1 flex-col gap-2 p-4">
        <h2 className="text-[17px] font-bold leading-snug">
          <Link to={to} className="stretched rounded transition-colors hover:text-brand-text">
            {title}
          </Link>
        </h2>
        {children}
        <div className="mt-auto flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-t border-line pt-3 text-xs text-muted">
          {footer}
        </div>
      </div>
    </motion.article>
  )
}

export function EventCard({ event, index = 0 }: { event: EventSummaryDto; index?: number }) {
  return (
    <Card
      title={event.title}
      to={`/events/${event.id}`}
      index={index}
      media={
        <div className="relative">
          <Media url={event.imageUrl} category={event.category} />
          {event.isSynthetic && (
            <span className="absolute right-3 top-3">
              <SyntheticBadge />
            </span>
          )}
        </div>
      }
      footer={
        <>
          <Freshness lastVerifiedAt={event.lastVerifiedAt} freshness={event.freshness} />
          <SourceLink name={event.sourceName} url={event.sourceUrl} />
        </>
      }
    >
      <p className="order-first text-[13px] font-bold text-brand-text">
        <Meta icon={<CalendarDays />}>
          <span className="tnum">{formatEventStart(event.startsAt, event.startTimeKnown)}</span>
        </Meta>
      </p>
      <p className="text-sm text-muted">
        <Meta icon={<MapPin />}>
          {[event.venueName, event.city.name].filter(Boolean).join(', ')}
        </Meta>
      </p>
      <div className="flex flex-wrap items-center gap-2">
        {event.category && <CategoryBadge category={event.category} />}
        <Countdown
          start={event.startsAt}
          end={event.endsAt ?? endOfPristinaDay(event.startsAt)}
          allDay={!event.startTimeKnown}
        />
      </div>
    </Card>
  )
}

/** Old (struck through) and new price; `withDiscount` adds the percentage label. */
export function DealPrice({
  deal,
  withDiscount = false,
}: {
  deal: DealSummaryDto
  withDiscount?: boolean
}) {
  if (deal.newPrice === null && deal.oldPrice === null) {
    return <span>{deal.priceText ?? p.priceUnknown}</span>
  }
  return (
    <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
      {deal.newPrice !== null && (
        <strong className="tnum text-xl font-extrabold">
          {formatMoney(deal.newPrice, deal.currency)}
        </strong>
      )}
      {deal.oldPrice !== null && (
        <s className="tnum text-sm text-muted">{formatMoney(deal.oldPrice, deal.currency)}</s>
      )}
      {withDiscount && deal.discountPercent !== null && (
        <DiscountBadge percent={deal.discountPercent} />
      )}
    </span>
  )
}

export function DealCard({ deal, index = 0 }: { deal: DealSummaryDto; index?: number }) {
  return (
    <Card
      title={deal.title}
      to={`/deals/${deal.id}`}
      index={index}
      media={
        <div className="relative">
          <Media url={deal.imageUrl} category={deal.category} kind="deal" />
          {deal.discountPercent !== null && (
            <span className="absolute left-3 top-3">
              <DiscountBadge percent={deal.discountPercent} />
            </span>
          )}
          {deal.isSynthetic && (
            <span className="absolute right-3 top-3">
              <SyntheticBadge />
            </span>
          )}
        </div>
      }
      footer={
        <>
          <Freshness lastVerifiedAt={deal.lastVerifiedAt} freshness={deal.freshness} />
          <SourceLink name={deal.sourceName} url={deal.sourceUrl} />
        </>
      }
    >
      <p className="order-first">
        <DealPrice deal={deal} />
      </p>
      {(deal.merchantName || deal.city) && (
        <p className="text-sm text-muted">
          <Meta icon={<Store />}>
            {[deal.merchantName, deal.city?.name].filter(Boolean).join(', ')}
          </Meta>
        </p>
      )}
      <p className="text-[13px] font-bold text-brand-text">
        <Meta icon={<Clock3 />}>
          {deal.validUntil
            ? p.validUntil(formatDate(deal.validUntil))
            : deal.isTodaysDeal
              ? p.todaysDeal
              : p.validityUnknown}
        </Meta>
      </p>
      <div className="flex flex-wrap items-center gap-2">
        {deal.category && <CategoryBadge category={deal.category} />}
        <Countdown end={deal.validUntil} />
      </div>
    </Card>
  )
}
