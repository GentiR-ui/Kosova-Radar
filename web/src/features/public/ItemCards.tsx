import { motion } from 'motion/react'
import { ArrowUpRight, Bookmark, Check, Clock3, MapPin, Tag, Ticket } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { DealSummaryDto, EventSummaryDto } from './api'
import Countdown from './Countdown'
import {
  dayAndMonth,
  endOfPristinaDay,
  formatDate,
  formatEventStart,
  formatMoney,
  safeHref,
} from './format'
import LogoMark from './Logo'
import { spring } from './motion'
import { savedKey, useSaved } from './saved'
import { p } from './strings'

type Kind = 'event' | 'deal'

export function SyntheticBadge() {
  return <span className="chip mt-2.5 border border-warn text-warn">{p.synthetic}</span>
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
    <a href={href} target="_blank" rel="noopener noreferrer" className="source-link">
      {label}
      <ArrowUpRight aria-hidden="true" size={12} />
    </a>
  )
}

/** Rose discount pill. The minus is a plain "-" so the published percentage stays searchable. */
export function DiscountBadge({
  percent,
  className = '',
}: {
  percent: number
  className?: string
}) {
  return <span className={`discount-badge ${className}`}>{`-${percent}%`}</span>
}

/** Picture of the item, or the brand mark on a soft panel when the source has none. */
export function Media({
  url,
  eager = false,
  alt = '',
  children,
}: {
  url: string | null
  /** The main picture of a detail page: load it first. */
  eager?: boolean
  alt?: string
  children?: ReactNode
}) {
  const href = url ? safeHref(url) : undefined
  return (
    <>
      {href ? (
        <img
          src={href}
          alt={alt}
          width={1024}
          height={768}
          loading={eager ? 'eager' : 'lazy'}
          fetchPriority={eager ? 'high' : 'auto'}
          decoding="async"
          referrerPolicy="no-referrer"
        />
      ) : (
        <div className="photo-empty">
          <LogoMark className="w-12 opacity-70" />
        </div>
      )}
      {children}
    </>
  )
}

function SaveButton({ kind, id, title }: { kind: Kind; id: string; title: string }) {
  const saved = useSaved()
  const key = savedKey(kind, id)
  const on = saved.has(key)
  return (
    <button
      type="button"
      aria-pressed={on}
      aria-label={on ? p.unsave(title) : p.save(title)}
      onClick={() => saved.toggle(key)}
      className={`bookmark-button ${on ? 'is-saved' : ''}`}
    >
      {on ? <Check aria-hidden="true" size={15} /> : <Bookmark aria-hidden="true" size={15} />}
    </button>
  )
}

function CategoryTag({ kind, name }: { kind: Kind; name: string }) {
  const Icon = kind === 'deal' ? Tag : Ticket
  return (
    <span className="photo-category">
      <Icon aria-hidden="true" size={12} />
      {name}
    </span>
  )
}

/** Calendar-style date badge on an event picture. */
function DateBadge({ iso }: { iso: string }) {
  const { day, month } = dayAndMonth(iso)
  return (
    <span aria-hidden="true" className="event-date">
      <b>{Number(day)}</b>
      <small>{(p.months[month] ?? '').slice(0, 3)}</small>
    </span>
  )
}

function Card({
  kind,
  id,
  title,
  to,
  index,
  photo,
  children,
}: {
  kind: Kind
  id: string
  title: string
  to: string
  index: number
  photo: ReactNode
  children: ReactNode
}) {
  return (
    <motion.article
      initial={{ opacity: 0, y: 18 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '0px 0px -40px 0px' }}
      whileHover={{ y: -5 }}
      transition={{ ...spring, delay: (index % 4) * 0.04 }}
      className={`feed-card ${kind}-card`}
    >
      <div className="card-photo">
        {photo}
        <SaveButton kind={kind} id={id} title={title} />
      </div>
      <div className="card-body">
        <h2>
          <Link to={to} className="card-title after:absolute after:inset-0 after:content-['']">
            {title}
            <ArrowUpRight aria-hidden="true" size={17} />
          </Link>
        </h2>
        {children}
      </div>
    </motion.article>
  )
}

export function EventCard({ event, index = 0 }: { event: EventSummaryDto; index?: number }) {
  return (
    <Card
      kind="event"
      id={event.id}
      title={event.title}
      to={`/events/${event.id}`}
      index={index}
      photo={
        <Media url={event.imageUrl}>
          <DateBadge iso={event.startsAt} />
          {event.category && <CategoryTag kind="event" name={event.category.name} />}
        </Media>
      }
    >
      <p className="venue-line">{event.venueName}</p>
      {event.priceText && (
        <p className="price-row">
          <strong>{event.priceText}</strong>
        </p>
      )}
      <p className="card-details">
        <span>
          <Clock3 aria-hidden="true" size={13} />
          {formatEventStart(event.startsAt, event.startTimeKnown)}
        </span>
        <span>
          <MapPin aria-hidden="true" size={13} />
          {event.city.name}
        </span>
      </p>
      <Countdown
        tone="event"
        start={event.startsAt}
        end={event.endsAt ?? endOfPristinaDay(event.startsAt)}
        allDay={!event.startTimeKnown}
      />
      {event.isSynthetic && <SyntheticBadge />}
      <p className="card-bottom">
        <Freshness lastVerifiedAt={event.lastVerifiedAt} freshness={event.freshness} />
        <SourceLink name={event.sourceName} url={event.sourceUrl} />
      </p>
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
    <>
      {deal.newPrice !== null && <strong>{formatMoney(deal.newPrice, deal.currency)}</strong>}
      {deal.oldPrice !== null && <s>{formatMoney(deal.oldPrice, deal.currency)}</s>}
      {withDiscount && deal.discountPercent !== null && (
        <DiscountBadge percent={deal.discountPercent} className="static" />
      )}
    </>
  )
}

export function DealCard({ deal, index = 0 }: { deal: DealSummaryDto; index?: number }) {
  return (
    <Card
      kind="deal"
      id={deal.id}
      title={deal.title}
      to={`/deals/${deal.id}`}
      index={index}
      photo={
        <Media url={deal.imageUrl}>
          {deal.discountPercent !== null && <DiscountBadge percent={deal.discountPercent} />}
          {deal.category && <CategoryTag kind="deal" name={deal.category.name} />}
        </Media>
      }
    >
      <p className="venue-line">{deal.merchantName}</p>
      <p className="price-row">
        <DealPrice deal={deal} />
        {deal.isTodaysDeal && <small>{p.todayOnly}</small>}
      </p>
      <p className="card-details">
        <span>
          <Clock3 aria-hidden="true" size={13} />
          {deal.validUntil
            ? p.validUntil(formatDate(deal.validUntil))
            : deal.isTodaysDeal
              ? p.todaysDeal
              : p.validityUnknown}
        </span>
        {deal.city && (
          <span>
            <MapPin aria-hidden="true" size={13} />
            {deal.city.name}
          </span>
        )}
      </p>
      <Countdown tone="deal" end={deal.validUntil} />
      {deal.isSynthetic && <SyntheticBadge />}
      <p className="card-bottom">
        <Freshness lastVerifiedAt={deal.lastVerifiedAt} freshness={deal.freshness} />
        <SourceLink name={deal.sourceName} url={deal.sourceUrl} />
      </p>
    </Card>
  )
}
