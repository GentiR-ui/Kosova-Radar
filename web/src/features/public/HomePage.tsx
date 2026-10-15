import useEmblaCarousel from 'embla-carousel-react'
import { motion } from 'motion/react'
import { ArrowRight, CalendarDays, Tag } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { cn } from '../../lib/cn'
import { t } from '../../lib/strings'
import { flat, useCategories, useDeals, useEvents, type DealSummaryDto } from './api'
import { allCategoriesStyle, categoryStyle, dealsStyle } from './categories'
import { pristinaToday, safeHref } from './format'
import { DealRow, EventCard } from './ItemCards'
import ListShell from './ListShell'
import { p } from './strings'

const ease = [0.16, 1, 0.3, 1] as const

export default function HomePage() {
  const [params] = useSearchParams()
  const search = params.get('search') ?? ''
  const city = params.get('city') ?? ''
  const today = pristinaToday()
  const filters = { search: search || undefined, city: city || undefined }

  // Today's events feed the hero picture and the "Sot në Kosovë" grid; deals feed the panel.
  const events = useEvents({ ...filters, from: today, to: today })
  const deals = useDeals(filters)

  const items = flat(events)
  const heroImage = items.find((e) => e.imageUrl && safeHref(e.imageUrl))?.imageUrl ?? null
  const topDeals = [...flat(deals)]
    .sort((a, b) => (b.discountPercent ?? 0) - (a.discountPercent ?? 0))
    .slice(0, 3)

  return (
    <div className="page-width py-6 sm:py-8">
      <div className="grid grid-cols-[minmax(0,1fr)] gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(330px,1fr)]">
        <Hero image={heroImage} live={items.length > 0} />
        <DealsPanel deals={topDeals} loading={deals.isPending} />
      </div>

      <CategoryChips />

      <section aria-labelledby="today-title" className="mt-10 scroll-mt-28" id="today">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="today-title" className="flex items-center gap-2.5 text-2xl sm:text-[28px]">
            <span className="tile tile-violet size-9">
              <CalendarDays aria-hidden="true" className="size-5" />
            </span>
            {p.todayTitle}
          </h2>
          <SeeAll to="/events" label={p.seeAll} />
        </div>

        <div className="mt-6">
          <ListShell
            queries={[events]}
            grid
            emptyHint={p.todayEmptyHint}
            emptyAction={<TomorrowLink />}
          >
            {items.map((e, i) => (
              <EventCard key={e.id} event={e} index={i} today />
            ))}
          </ListShell>
        </div>
      </section>
    </div>
  )
}

function SeeAll({ to, label }: { to: string; label: string }) {
  return (
    <Link
      to={to}
      className="group inline-flex min-h-11 items-center gap-1.5 text-sm font-bold text-violet"
    >
      {label}
      <ArrowRight
        aria-hidden="true"
        className="size-4 transition-transform duration-200 group-hover:translate-x-1"
      />
    </Link>
  )
}

function TomorrowLink() {
  return (
    <Link to="/events" className="btn btn-primary mt-5">
      {p.tomorrowLink}
      <ArrowRight aria-hidden="true" className="size-4" />
    </Link>
  )
}

function Hero({ image, live }: { image: string | null; live: boolean }) {
  const href = image ? safeHref(image) : undefined
  const words = t.pages.home.split(' ')
  const last = words.pop() ?? ''
  const scrollToToday = () =>
    document.getElementById('today')?.scrollIntoView({ behavior: 'smooth', block: 'start' })

  return (
    <section
      className={cn(
        'on-hero relative isolate flex min-h-[400px] flex-col justify-center overflow-hidden rounded-[28px] p-7 sm:p-10 lg:min-h-[440px]',
        href ? '' : 'brand-pattern',
      )}
    >
      {href ? (
        <img
          src={href}
          alt=""
          width={1600}
          height={900}
          fetchPriority="high"
          referrerPolicy="no-referrer"
          className="absolute inset-0 -z-20 size-full object-cover"
        />
      ) : (
        <span
          aria-hidden="true"
          className="absolute inset-0 -z-20 bg-[radial-gradient(70%_80%_at_80%_10%,#5B3FE8,transparent_70%)]"
        />
      )}
      <span
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[linear-gradient(100deg,rgb(3_20_46/0.95),rgb(3_20_46/0.82)_45%,rgb(3_20_46/0.45))]"
      />
      <div className="relative max-w-xl">
        {live && (
          <motion.span
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, ease }}
            className="badge badge-live"
          >
            <span className="live-dot" /> {p.live}
          </motion.span>
        )}
        <h1 className="mt-4 text-[36px] font-extrabold leading-[1.05] sm:text-[46px] lg:text-[56px]">
          {words.map((w, i) => (
            // A plain space between the spans keeps the headline breakable on narrow phones.
            <span key={`${w}-${i}`}>
              <motion.span
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.45, ease, delay: 0.05 * i }}
                className="inline-block"
              >
                {w}
              </motion.span>{' '}
            </span>
          ))}
          <motion.span
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.45, ease, delay: 0.05 * words.length }}
            className="headline-gradient inline-block"
          >
            {last}?
          </motion.span>
        </h1>
        <motion.p
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.45, ease, delay: 0.32 }}
          className="mt-4 max-w-lg text-[15px] leading-relaxed text-on-dark-muted"
        >
          {p.heroLead}
        </motion.p>
        <motion.div
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.45, ease, delay: 0.42 }}
          className="mt-7 flex flex-wrap gap-3"
        >
          <button type="button" onClick={scrollToToday} className="btn btn-primary group">
            <CalendarDays aria-hidden="true" className="size-4" />
            {p.ctaEvents}
            <ArrowRight
              aria-hidden="true"
              className="size-4 transition-transform duration-200 group-hover:translate-x-1"
            />
          </button>
          <Link to="/deals" className="btn btn-glass">
            <Tag aria-hidden="true" className="size-4" />
            {p.ctaDeals}
          </Link>
        </motion.div>
      </div>
    </section>
  )
}

function DealsPanel({ deals, loading }: { deals: DealSummaryDto[]; loading: boolean }) {
  // Below lg the three rows become a swipeable rail; above it they stack in the panel.
  const [emblaRef] = useEmblaCarousel({
    align: 'start',
    containScroll: 'trimSnaps',
    breakpoints: { '(min-width: 1024px)': { active: false } },
  })
  const { Icon } = dealsStyle
  return (
    <section aria-labelledby="deals-title" className="card flex flex-col p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id="deals-title" className="flex items-center gap-2.5 text-lg">
            <span className="tile tile-orange size-9">
              <Icon aria-hidden="true" className="size-5" />
            </span>
            {p.dealsPanelTitle}
          </h2>
          <p className="mt-1.5 text-xs text-muted">{p.dealsPanelLead}</p>
        </div>
        <SeeAll to="/deals" label={p.seeAll} />
      </div>

      <div className="mt-4">
        {loading && (
          <div className="flex flex-col gap-3">
            {[0, 1, 2].map((i) => (
              <div key={i} className="skeleton h-[92px] rounded-2xl" />
            ))}
          </div>
        )}
        {!loading && deals.length === 0 && (
          <p className="flex flex-col items-center gap-3 py-8 text-center text-sm text-muted">
            {p.noDeals}
            <Link to="/deals" className="btn">
              {p.seeAllDeals}
            </Link>
          </p>
        )}
        {deals.length > 0 && (
          <div ref={emblaRef} className="min-w-0 overflow-hidden lg:overflow-visible">
            <div className="flex gap-3 lg:flex-col">
              {deals.map((d, i) => (
                <div key={d.id} className="w-[86%] shrink-0 sm:w-[46%] lg:w-auto">
                  <DealRow deal={d} index={i} />
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </section>
  )
}

function CategoryChips() {
  const categories = useCategories('event')
  const [emblaRef] = useEmblaCarousel({
    align: 'start',
    containScroll: 'trimSnaps',
    breakpoints: { '(min-width: 768px)': { active: false } },
  })
  return (
    <nav aria-label={p.categoriesLabel} className="mt-8">
      <div ref={emblaRef} className="min-w-0 overflow-hidden md:overflow-visible">
        <ul className="flex gap-3 md:flex-wrap">
          <Chip to="/events" label={p.allCategories2} style={allCategoriesStyle} active />
          {(categories.data ?? []).map((c) => (
            <Chip
              key={c.slug}
              to={`/events?category=${c.slug}`}
              label={c.name}
              style={categoryStyle(c.slug)}
            />
          ))}
          <Chip to="/deals" label={p.dealsChip} style={dealsStyle} />
        </ul>
      </div>
    </nav>
  )
}

function Chip({
  to,
  label,
  style,
  active = false,
}: {
  to: string
  label: string
  style: {
    tile: string
    Icon: React.ComponentType<{ className?: string; 'aria-hidden'?: boolean }>
  }
  active?: boolean
}) {
  const { tile, Icon } = style
  return (
    <li className="shrink-0">
      <Link to={to} className={cn('chip whitespace-nowrap', active && 'chip-active')}>
        <span className={cn('tile size-9', active ? 'bg-white/25 text-white' : tile)}>
          <Icon aria-hidden className="size-4" />
        </span>
        {label}
      </Link>
    </li>
  )
}
