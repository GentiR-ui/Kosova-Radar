import { motion } from 'motion/react'
import { ArrowUpRight, CalendarDays, Clock3, MapPin, Store } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { CategoryDto, DealSummaryDto, EventSummaryDto } from './api'
import Countdown, { DAY_MS } from './Countdown'
import {
  dayAndMonth,
  endOfPristinaDay,
  formatDate,
  formatEventStart,
  formatMoney,
  safeHref,
} from './format'
import { LogoMark } from './Logo'
import { spring } from './motion'
import { p } from './strings'

type Tone = 'event' | 'deal'

export function SyntheticBadge() {
  return <span className="chip border border-warn text-warn">{p.synthetic}</span>
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
        className={`size-1.5 shrink-0 rounded-full ${freshness === 'fresh' ? 'bg-brand' : 'bg-warn'}`}
      />
      {freshness === 'fresh' ? p.fresh : p.stale(formatDate(lastVerifiedAt))}
    </span>
  )
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
      className="relative z-10 inline-flex w-fit items-center gap-1 rounded font-semibold text-ink underline-offset-4 transition-colors hover:text-accent hover:underline"
    >
      {label}
      <ArrowUpRight aria-hidden="true" className="size-3 shrink-0" />
    </a>
  )
}

/** Discount label; the colour rises with the discount, from violet to rose. */
export function DiscountBadge({
  percent,
  className = '',
}: {
  percent: number
  className?: string
}) {
  const tone =
    percent >= 50
      ? 'bg-hot'
      : percent >= 40
        ? 'bg-accent'
        : percent >= 30
          ? 'bg-[#0e7490]'
          : percent >= 20
            ? 'bg-[#c2410c]'
            : 'bg-event'
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-extrabold leading-none text-white shadow-sm ${tone} ${className}`}
    >{`-${percent}%`}</span>
  )
}

/** Category chips get a stable colour from their slug, so the same category always looks the same. */
const categoryTones = [
  'bg-event-soft text-event',
  'bg-accent-soft text-accent',
  'bg-hot-soft text-hot',
  'bg-[#e7f2fb] text-[#0e5f8a]',
  'bg-[#fdf0e3] text-[#9a4a06]',
]

export function CategoryChip({
  category,
  className = '',
}: {
  category: CategoryDto
  className?: string
}) {
  const tone =
    categoryTones[
      [...category.slug].reduce((n, c) => n + c.charCodeAt(0), 0) % categoryTones.length
    ]
  return <span className={`chip ${tone} ${className}`}>{category.name}</span>
}

/** Picture of the item, or a soft brand panel when the source has none, so every card keeps its shape. */
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
    <div className={`relative overflow-hidden bg-soft ${className}`}>
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
          className="size-full object-cover transition-transform duration-700 ease-out-expo group-hover:scale-[1.06]"
        />
      ) : (
        <div className="grid size-full place-items-center bg-[radial-gradient(circle_at_50%_40%,#e6f7ef,#f3f8f6_70%)]">
          <LogoMark className="w-16 text-ink/70 transition-transform duration-700 ease-out-expo group-hover:scale-110" />
        </div>
      )}
      {children}
    </div>
  )
}

/** Calendar-style date badge on an event picture. */
function DateBadge({ iso }: { iso: string }) {
  const { day, month } = dayAndMonth(iso)
  const name = p.months[month] ?? ''
  return (
    <span
      aria-hidden="true"
      className="absolute left-3 top-3 flex w-12 flex-col items-center rounded-xl bg-surface/95 py-1.5 shadow-card backdrop-blur-sm"
    >
      <span className="text-lg font-extrabold leading-none text-event">{Number(day)}</span>
      <span className="mt-0.5 text-[10px] font-bold uppercase leading-none text-muted">
        {name.slice(0, 3)}
      </span>
    </span>
  )
}

function Meta({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <span className="flex items-start gap-1.5 leading-snug">
      <span aria-hidden="true" className="mt-px shrink-0 [&_svg]:size-3.5">
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
  children,
  footer,
}: {
  tone: Tone
  title: string
  to: string
  index: number
  synthetic: boolean
  photo: ReactNode
  children: ReactNode
  footer: ReactNode
}) {
  return (
    <motion.article
      initial={{ opacity: 0, y: 26 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '0px 0px -40px 0px' }}
      transition={{ ...spring, delay: (index % 4) * 0.06 }}
      className="card group relative flex h-full flex-col overflow-hidden transition-[box-shadow,translate,border-color] duration-300 ease-out-expo hover:-translate-y-1.5 hover:shadow-lift focus-within:-translate-y-1.5 focus-within:shadow-lift"
    >
      {photo}
      <div className="flex min-w-0 flex-1 flex-col p-4">
        <h2 className="line-clamp-2 text-[15px] font-extrabold leading-snug sm:text-base">
          <Link
            to={to}
            className="rounded after:absolute after:inset-0 after:content-[''] group-hover:text-accent"
          >
            {title}
          </Link>
        </h2>
        <div className="mt-2 flex flex-col gap-1.5 text-xs text-muted">{children}</div>
        {synthetic && (
          <div className="mt-2.5">
            <SyntheticBadge />
          </div>
        )}
        <div className="mt-4 flex items-end justify-between gap-3 border-t border-line pt-3">
          <p className="flex min-w-0 flex-col gap-1 text-[11px] text-muted">{footer}</p>
          <span
            aria-hidden="true"
            className={`grid size-8 shrink-0 place-items-center rounded-full transition-[background-color,color,transform] duration-300 ease-out-expo group-hover:translate-x-0.5 group-hover:text-white ${
              tone === 'event'
                ? 'bg-event-soft text-event group-hover:bg-event'
                : 'bg-accent-soft text-accent group-hover:bg-accent'
            }`}
          >
            <ArrowUpRight className="size-4" />
          </span>
        </div>
      </div>
    </motion.article>
  )
}

const photoSize = 'h-[190px] sm:h-[200px] lg:h-[170px]'

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
          <DateBadge iso={event.startsAt} />
          {event.category && (
            <CategoryChip
              category={event.category}
              className="absolute bottom-3 right-3 bg-surface/95 shadow-card backdrop-blur-sm"
            />
          )}
          <span className="absolute bottom-3 left-3">
            <Countdown
              tone="event"
              start={event.startsAt}
              end={event.endsAt ?? endOfPristinaDay(event.startsAt)}
              allDay={!event.startTimeKnown}
              within={DAY_MS}
            />
          </span>
        </Media>
      }
      footer={
        <>
          <SourceLink name={event.sourceName} url={event.sourceUrl} />
          <Freshness lastVerifiedAt={event.lastVerifiedAt} freshness={event.freshness} />
        </>
      }
    >
      <Meta icon={<Clock3 />}>
        <span className="font-bold text-ink">
          {formatEventStart(event.startsAt, event.startTimeKnown)}
        </span>
      </Meta>
      <Meta icon={<MapPin />}>{[event.venueName, event.city.name].filter(Boolean).join(', ')}</Meta>
      {event.priceText && <Meta icon={<Store />}>{event.priceText}</Meta>}
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
        <strong className="text-lg font-extrabold text-accent">
          {formatMoney(deal.newPrice, deal.currency)}
        </strong>
      )}
      {deal.oldPrice !== null && (
        <s className="text-sm font-medium text-muted">
          {formatMoney(deal.oldPrice, deal.currency)}
        </s>
      )}
      {withDiscount && deal.discountPercent !== null && (
        <DiscountBadge percent={deal.discountPercent} className="text-sm" />
      )}
    </span>
  )
}

export function DealCard({ deal, index = 0 }: { deal: DealSummaryDto; index?: number }) {
  const place = [deal.merchantName, deal.city?.name].filter(Boolean).join(' · ')
  return (
    <Card
      tone="deal"
      title={deal.title}
      to={`/deals/${deal.id}`}
      index={index}
      synthetic={deal.isSynthetic}
      photo={
        <Media url={deal.imageUrl} className={photoSize}>
          {deal.discountPercent !== null && (
            <span className="absolute left-3 top-3">
              <DiscountBadge percent={deal.discountPercent} className="px-3 py-1.5 text-sm" />
            </span>
          )}
          {deal.category && (
            <CategoryChip
              category={deal.category}
              className="absolute bottom-3 right-3 bg-surface/95 shadow-card backdrop-blur-sm"
            />
          )}
          <span className="absolute bottom-3 left-3">
            <Countdown tone="deal" end={deal.validUntil} within={DAY_MS} />
          </span>
        </Media>
      }
      footer={
        <>
          <SourceLink name={deal.sourceName} url={deal.sourceUrl} />
          <Freshness lastVerifiedAt={deal.lastVerifiedAt} freshness={deal.freshness} />
        </>
      }
    >
      {place && <span className="truncate">{place}</span>}
      <span className="my-0.5">
        <DealPrice deal={deal} />
      </span>
      <Meta icon={<CalendarDays />}>
        {deal.validUntil
          ? p.validUntil(formatDate(deal.validUntil))
          : deal.isTodaysDeal
            ? p.todaysDeal
            : p.validityUnknown}
      </Meta>
    </Card>
  )
}
