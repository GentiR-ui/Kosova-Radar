import { motion } from 'motion/react'
import { ArrowUpRight, CalendarDays, ChevronRight, Clock3, MapPin } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { cn } from '../../lib/cn'
import type { CategoryDto, DealSummaryDto, EventSummaryDto } from './api'
import { categoryStyle } from './categories'
import Countdown from './Countdown'
import {
  dayAndMonth,
  endOfPristinaDay,
  formatDate,
  formatEventStart,
  formatMoney,
  formatTime,
  safeHref,
} from './format'
import { p } from './strings'

const ease = [0.16, 1, 0.3, 1] as const

export function CategoryBadge({
  category,
  className,
}: {
  category: CategoryDto
  className?: string
}) {
  const { tile, Icon } = categoryStyle(category.slug, category.kind)
  return (
    <span className={cn('badge', tile, className)}>
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
      className="above-stretched inline-flex w-fit items-center gap-1 font-bold text-violet underline-offset-4 hover:underline"
    >
      {label}
      <ArrowUpRight aria-hidden="true" className="size-3.5 shrink-0" />
    </a>
  )
}

/** Discount pill. The minus is a plain "-" so the published percentage stays searchable. */
export function DiscountBadge({ percent, className }: { percent: number; className?: string }) {
  return <span className={cn('badge badge-brand', className)}>{`-${percent}%`}</span>
}

/** Picture of the item, or a tile in the category colour with its icon. */
export function Media({
  url,
  category,
  kind = 'event',
  eager = false,
  className,
  contain = false,
  children,
}: {
  url: string | null
  category?: CategoryDto | null
  kind?: 'event' | 'deal'
  /** The main picture of a detail page: load it first. */
  eager?: boolean
  className?: string
  /** Product shots look better whole on a soft tile than cropped. */
  contain?: boolean
  children?: ReactNode
}) {
  const href = url ? safeHref(url) : undefined
  const { tile, Icon } = categoryStyle(category?.slug, kind)
  return (
    <div className={cn('media-frame', className)}>
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
          className={contain ? 'object-contain p-3' : undefined}
        />
      ) : (
        <div className={cn('grid size-full place-items-center', tile)}>
          <Icon aria-hidden="true" className="size-10 opacity-85" />
        </div>
      )}
      {children}
    </div>
  )
}

function Meta({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <span className="flex items-center gap-1.5">
      <span aria-hidden="true" className="shrink-0 [&_svg]:size-4">
        {icon}
      </span>
      {children}
    </span>
  )
}

/** Shared shell: entrance reveal, hover lift, stretched title link (proportions from the shop pages). */
function Card({
  index,
  media,
  children,
  footer,
}: {
  index: number
  media: ReactNode
  children: ReactNode
  footer: ReactNode
}) {
  return (
    <motion.article
      initial={{ opacity: 0, y: 16 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '0px 0px -40px 0px' }}
      transition={{ duration: 0.45, ease, delay: (index % 4) * 0.05 }}
      whileTap={{ scale: 0.98 }}
      className="card card-hover group relative flex h-full flex-col overflow-hidden p-3"
    >
      {media}
      <div className="flex min-w-0 flex-1 flex-col gap-2 p-3 pt-4">
        {children}
        <div className="mt-auto flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-t border-line pt-3 text-xs text-muted">
          {footer}
        </div>
      </div>
    </motion.article>
  )
}

function Title({ title, to }: { title: string; to: string }) {
  return (
    <h3 className="line-clamp-2 text-[17px] font-extrabold leading-snug">
      <Link to={to} className="stretched rounded transition-colors group-hover:text-violet">
        {title}
      </Link>
    </h3>
  )
}

export function EventCard({
  event,
  index = 0,
  today = false,
}: {
  event: EventSummaryDto
  index?: number
  /** On the home page every card is today's, so the picture shows the time instead of the date. */
  today?: boolean
}) {
  const { day, month } = dayAndMonth(event.startsAt)
  return (
    <Card
      index={index}
      media={
        <Media url={event.imageUrl} category={event.category}>
          {event.category ? (
            <CategoryBadge
              category={event.category}
              className="absolute left-3 top-3 shadow-card"
            />
          ) : (
            <span className="badge badge-glass absolute left-3 top-3">{p.typeEvent}</span>
          )}
          {today ? (
            <span className="badge badge-glass tnum absolute right-3 top-3">
              {event.startTimeKnown ? formatTime(event.startsAt) : p.allDay}
            </span>
          ) : (
            <span className="absolute right-3 top-3 flex w-12 flex-col items-center rounded-xl bg-surface/95 py-1.5 shadow-card backdrop-blur-sm">
              <b className="text-lg font-extrabold leading-none text-violet">{Number(day)}</b>
              <small className="mt-0.5 text-[10px] font-bold uppercase leading-none text-muted">
                {(p.months[month] ?? '').slice(0, 3)}
              </small>
            </span>
          )}
          {event.isSynthetic && (
            <span className="absolute bottom-3 left-3">
              <SyntheticBadge />
            </span>
          )}
        </Media>
      }
      footer={
        <>
          <Freshness lastVerifiedAt={event.lastVerifiedAt} freshness={event.freshness} />
          <SourceLink name={event.sourceName} url={event.sourceUrl} />
        </>
      }
    >
      <p className="text-[13px] font-bold text-violet">
        <Meta icon={<CalendarDays />}>
          <span className="tnum">{formatEventStart(event.startsAt, event.startTimeKnown)}</span>
        </Meta>
      </p>
      <Title title={event.title} to={`/events/${event.id}`} />
      <p className="text-sm text-muted">
        <Meta icon={<MapPin />}>
          {[event.venueName, event.city.name].filter(Boolean).join(', ')}
        </Meta>
      </p>
      <Countdown
        start={event.startsAt}
        end={event.endsAt ?? endOfPristinaDay(event.startsAt)}
        allDay={!event.startTimeKnown}
      />
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
    <span className="flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
      {deal.newPrice !== null && (
        <strong className="tnum text-2xl font-extrabold">
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
      index={index}
      media={
        <Media url={deal.imageUrl} category={deal.category} kind="deal" contain>
          {deal.discountPercent !== null && (
            <span className="absolute left-3 top-3">
              <DiscountBadge percent={deal.discountPercent} className="text-sm shadow-card" />
            </span>
          )}
          {deal.isTodaysDeal && (
            <span className="badge badge-soft absolute right-3 top-3">{p.todaysDeal}</span>
          )}
          {deal.isSynthetic && (
            <span className="absolute bottom-3 left-3">
              <SyntheticBadge />
            </span>
          )}
        </Media>
      }
      footer={
        <>
          <Freshness lastVerifiedAt={deal.lastVerifiedAt} freshness={deal.freshness} />
          <SourceLink name={deal.sourceName} url={deal.sourceUrl} />
        </>
      }
    >
      {deal.merchantName && (
        <p className="text-[11px] font-bold uppercase tracking-wide text-muted">
          {deal.merchantName}
        </p>
      )}
      <Title title={deal.title} to={`/deals/${deal.id}`} />
      <p className="mt-1">
        <DealPrice deal={deal} />
      </p>
      {/* Listing deals carry "Oferta e sotme" as the badge on the picture, so no validity line here. */}
      {(deal.validUntil || !deal.isTodaysDeal) && (
        <p className="text-[13px] text-muted">
          <Meta icon={<Clock3 />}>
            {deal.validUntil ? p.validUntil(formatDate(deal.validUntil)) : p.validityUnknown}
          </Meta>
        </p>
      )}
      {deal.category && <CategoryBadge category={deal.category} />}
    </Card>
  )
}

/** Compact row of the home "Ofertat më të mira" panel. */
export function DealRow({ deal, index = 0 }: { deal: DealSummaryDto; index?: number }) {
  return (
    <motion.article
      initial={{ opacity: 0, x: 14 }}
      whileInView={{ opacity: 1, x: 0 }}
      viewport={{ once: true }}
      transition={{ duration: 0.4, ease, delay: index * 0.07 }}
      className="card card-hover group relative flex items-center gap-3 p-2.5"
    >
      <Media
        url={deal.imageUrl}
        category={deal.category}
        kind="deal"
        contain
        className="size-[72px] shrink-0 !aspect-square rounded-xl"
      />
      <div className="min-w-0 flex-1">
        <h3 className="line-clamp-2 text-sm font-bold leading-snug">
          <Link to={`/deals/${deal.id}`} className="stretched rounded group-hover:text-violet">
            {deal.title}
          </Link>
        </h3>
        {deal.merchantName && (
          <p className="mt-0.5 truncate text-xs text-muted">{deal.merchantName}</p>
        )}
        {deal.discountPercent !== null && (
          <span className="mt-1.5 inline-block">
            <DiscountBadge percent={deal.discountPercent} />
          </span>
        )}
      </div>
      <ChevronRight
        aria-hidden="true"
        className="size-5 shrink-0 text-muted transition-transform duration-200 group-hover:translate-x-1 group-hover:text-violet"
      />
    </motion.article>
  )
}
