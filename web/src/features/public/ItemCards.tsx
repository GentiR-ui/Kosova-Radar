import { motion } from 'motion/react'
import { ArrowUpRight, CalendarDays, Clock3, MapPin, Store, Tag, Ticket } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { DealSummaryDto, EventSummaryDto } from './api'
import Countdown from './Countdown'
import { endOfPristinaDay, formatDate, formatEventStart, formatMoney, safeHref } from './format'
import { spring } from './motion'
import RadarSignal from './RadarSignal'
import { p } from './strings'

type Tone = 'event' | 'deal'
const toneText = { event: 'text-event', deal: 'text-accent' } as const

export function SyntheticBadge() {
  return (
    <span className="inline-flex w-fit rounded-sm border border-warn px-2 py-0.5 text-xs font-semibold text-warn">
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
  return (
    <span className="inline-flex items-center gap-1.5">
      <span
        aria-hidden="true"
        className={`size-1.5 rounded-full ${freshness === 'fresh' ? 'bg-accent' : 'bg-warn'}`}
      />
      {freshness === 'fresh' ? p.fresh : p.stale(formatDate(lastVerifiedAt))}
    </span>
  )
}

export function SourceLink({ name, url, tone }: { name: string; url: string; tone?: Tone }) {
  const href = safeHref(url)
  const label = p.source(name)
  if (!href) return <span>{label}</span>
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className={`relative z-10 inline-flex w-fit items-center gap-1 rounded-sm font-semibold underline-offset-4 hover:underline ${
        tone ? toneText[tone] : 'text-ink'
      }`}
    >
      {label}
      <ArrowUpRight aria-hidden="true" className="size-3.5 shrink-0" />
    </a>
  )
}

/** Tilted lime discount label; the only place the percentage is shown on a card or a detail page. */
export function DiscountBadge({
  percent,
  className = '',
}: {
  percent: number
  className?: string
}) {
  return (
    <span
      className={`inline-block -rotate-[4deg] rounded-[3px] bg-accent px-2.5 py-1 font-extrabold leading-none text-accent-ink shadow-card ${className}`}
    >{`-${percent}%`}</span>
  )
}

/** Picture of the item, or a radar on graphite when the source has none, so every card has the same shape. */
export function Media({
  url,
  eager = false,
  className = '',
  children,
}: {
  url: string | null
  /** The main picture of a detail page: load it first. */
  eager?: boolean
  className?: string
  children?: ReactNode
}) {
  const href = url ? safeHref(url) : undefined
  return (
    <div className={`relative overflow-hidden bg-raised ${className}`}>
      {href ? (
        <img
          src={href}
          alt=""
          width={1024}
          height={768}
          loading={eager ? 'eager' : 'lazy'}
          fetchPriority={eager ? 'high' : 'auto'}
          decoding="async"
          referrerPolicy="no-referrer"
          className="size-full object-cover transition-transform duration-500 group-hover:scale-[1.04]"
        />
      ) : (
        <div className="grid size-full place-items-center bg-[radial-gradient(circle_at_50%_45%,oklch(0.27_0.025_150),oklch(0.18_0.006_170)_70%)]">
          <RadarSignal className="radar-large w-28" />
        </div>
      )}
      {children}
    </div>
  )
}

function Detail({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <span className="flex items-start gap-1.5 leading-relaxed">
      <span aria-hidden="true" className="mt-0.5 shrink-0">
        {icon}
      </span>
      <span className="min-w-0">{children}</span>
    </span>
  )
}

function Card({
  tone,
  title,
  to,
  index,
  synthetic,
  photo,
  countdown,
  category,
  details,
  footer,
}: {
  tone: Tone
  title: string
  to: string
  index: number
  synthetic: boolean
  photo: ReactNode
  countdown: ReactNode
  category: ReactNode
  details: ReactNode
  footer: ReactNode
}) {
  return (
    <motion.article
      initial={{ opacity: 0, y: 35, scale: 0.97 }}
      whileInView={{ opacity: 1, y: 0, scale: 1 }}
      viewport={{ once: true, margin: '0px 0px -40px 0px' }}
      whileHover={{ y: -7 }}
      transition={{ ...spring, delay: (index % 4) * 0.055 }}
      className={`group relative flex h-full flex-col overflow-hidden rounded-md border bg-surface transition-[border-color,box-shadow] duration-200 hover:shadow-card focus-within:shadow-card ${
        tone === 'event'
          ? 'border-event/30 hover:border-event focus-within:border-event'
          : 'border-accent/30 hover:border-accent focus-within:border-accent'
      }`}
    >
      <div className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2.5 px-4 py-3 text-[10px] font-semibold uppercase tracking-wide text-muted lg:px-3">
        <span aria-hidden="true" className={`font-mono text-xs ${toneText[tone]}`}>
          {String(index + 1).padStart(2, '0')}
        </span>
        <span>{tone === 'event' ? p.cardEvent : p.cardDeal}</span>
        <ArrowUpRight
          aria-hidden="true"
          className={`size-3.5 transition-transform duration-200 group-hover:-translate-y-0.5 group-hover:translate-x-0.5 ${toneText[tone]}`}
        />
      </div>
      {photo}
      <div className="flex min-w-0 flex-1 flex-col p-4 lg:p-3.5">
        {countdown}
        <div className="mt-3.5 flex items-center justify-between gap-2 text-[11px] text-muted">
          {category}
        </div>
        <h2 className="mt-1.5 text-lg font-extrabold leading-snug lg:text-[15px]">
          <Link
            to={to}
            className="rounded-sm after:absolute after:inset-0 after:content-[''] hover:underline hover:underline-offset-4"
          >
            {title}
          </Link>
        </h2>
        {synthetic && (
          <div className="mt-2">
            <SyntheticBadge />
          </div>
        )}
        <div
          className={`mb-3.5 mt-3.5 flex flex-col gap-1.5 text-xs text-muted [&_svg]:size-3.5 ${
            tone === 'event' ? '[&_svg]:text-event' : '[&_svg]:text-accent'
          }`}
        >
          {details}
        </div>
        <div className="mt-auto flex flex-col gap-1.5 border-t border-line pt-3 text-[11px] text-muted">
          {footer}
        </div>
      </div>
    </motion.article>
  )
}

function TypeBadge({ tone }: { tone: Tone }) {
  const Icon = tone === 'event' ? Ticket : Tag
  return (
    <span
      className={`absolute left-2.5 top-2.5 flex items-center gap-1 rounded-[3px] px-2 py-1 text-[11px] font-bold text-accent-ink ${
        tone === 'event' ? 'bg-event' : 'bg-accent'
      }`}
    >
      <Icon aria-hidden="true" className="size-3" />
      {tone === 'event' ? p.typeEvent : p.typeDeal}
    </span>
  )
}

const photoSize = 'h-[210px] sm:h-[220px] lg:h-[168px]'

export function EventCard({ event, index = 0 }: { event: EventSummaryDto; index?: number }) {
  return (
    <Card
      tone="event"
      title={event.title}
      to={`/events/${event.id}`}
      index={index}
      synthetic={event.isSynthetic}
      photo={
        <Media url={event.imageUrl} className={photoSize}>
          <TypeBadge tone="event" />
        </Media>
      }
      countdown={
        <Countdown
          tone="event"
          start={event.startsAt}
          end={event.endsAt ?? endOfPristinaDay(event.startsAt)}
          allDay={!event.startTimeKnown}
        />
      }
      category={
        <>
          <span>{event.category?.name}</span>
          {event.priceText && <span className="font-bold text-ink">{event.priceText}</span>}
        </>
      }
      details={
        <>
          <Detail icon={<Clock3 />}>
            <span className="font-semibold text-ink">
              {formatEventStart(event.startsAt, event.startTimeKnown)}
            </span>
          </Detail>
          <Detail icon={<MapPin />}>
            {[event.venueName, event.city.name].filter(Boolean).join(', ')}
          </Detail>
        </>
      }
      footer={
        <>
          <SourceLink name={event.sourceName} url={event.sourceUrl} tone="event" />
          <Freshness lastVerifiedAt={event.lastVerifiedAt} freshness={event.freshness} />
        </>
      }
    />
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
    <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1.5">
      {deal.oldPrice !== null && (
        <s className="font-normal text-muted">{formatMoney(deal.oldPrice, deal.currency)}</s>
      )}
      {deal.newPrice !== null && (
        <strong className="font-extrabold text-accent">
          {formatMoney(deal.newPrice, deal.currency)}
        </strong>
      )}
      {withDiscount && deal.discountPercent !== null && (
        <DiscountBadge percent={deal.discountPercent} className="text-base" />
      )}
    </span>
  )
}

export function DealCard({ deal, index = 0 }: { deal: DealSummaryDto; index?: number }) {
  return (
    <Card
      tone="deal"
      title={deal.title}
      to={`/deals/${deal.id}`}
      index={index}
      synthetic={deal.isSynthetic}
      photo={
        <Media url={deal.imageUrl} className={photoSize}>
          <TypeBadge tone="deal" />
          {deal.discountPercent !== null && (
            <span className="absolute bottom-3 right-3">
              <DiscountBadge
                percent={deal.discountPercent}
                className="text-[29px] max-sm:text-[34px]"
              />
            </span>
          )}
        </Media>
      }
      countdown={deal.validUntil ? <Countdown tone="deal" end={deal.validUntil} /> : null}
      category={<span>{deal.category?.name}</span>}
      details={
        <>
          <Detail icon={<CalendarDays />}>
            {deal.validUntil
              ? p.validUntil(formatDate(deal.validUntil))
              : deal.isTodaysDeal
                ? p.todaysDeal
                : p.validityUnknown}
          </Detail>
          {(deal.merchantName || deal.city) && (
            <Detail icon={<Store />}>
              {[deal.merchantName, deal.city?.name].filter(Boolean).join(', ')}
            </Detail>
          )}
          <span className="mt-1 text-base text-ink">
            <DealPrice deal={deal} />
          </span>
        </>
      }
      footer={
        <>
          <SourceLink name={deal.sourceName} url={deal.sourceUrl} tone="deal" />
          <Freshness lastVerifiedAt={deal.lastVerifiedAt} freshness={deal.freshness} />
        </>
      }
    />
  )
}
