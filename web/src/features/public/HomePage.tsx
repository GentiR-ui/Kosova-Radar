import { motion } from 'motion/react'
import { ArrowRight, ChevronRight, Sparkles, Tag } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { flat, useCategories, useDeals, useEvents, type DealSummaryDto } from './api'
import { BrandMark } from './Brand'
import { categoryStyle } from './categories'
import { addDays, pristinaToday, safeHref } from './format'
import { DealCard, DiscountBadge, EventCard, Media } from './ItemCards'
import ListShell from './ListShell'
import { spring } from './motion'
import { p } from './strings'

const tabs = [
  { id: 'today', label: p.tabs.today },
  { id: 'events', label: p.tabs.events },
  { id: 'deals', label: p.tabs.deals },
] as const
type TabId = (typeof tabs)[number]['id']
const isTab = (v: string | null): v is TabId => tabs.some((x) => x.id === v)

/** Day filter inside the "Evente" tab. */
const dayFilters = [
  { id: '', label: p.days.all },
  { id: '1', label: p.days.tomorrow },
  { id: '2', label: p.days.dayAfter },
] as const

const ease = [0.16, 1, 0.3, 1] as const

export default function HomePage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = isTab(params.get('tab')) ? (params.get('tab') as TabId) : 'today'
  const search = params.get('search') ?? ''
  const city = params.get('city') ?? ''
  const day = tab === 'events' ? (params.get('day') ?? '') : ''
  const today = pristinaToday()

  // Today's events feed the hero (picture + LIVE badge) and, in the "Sot" tab, the list below.
  const todays = useEvents({
    search: search || undefined,
    city: city || undefined,
    from: today,
    to: today,
  })
  const deals = useDeals({ search: search || undefined, city: city || undefined })

  const update = (key: string, value: string) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value && !(key === 'tab' && value === 'today')) next.set(key, value)
        else next.delete(key)
        if (key === 'tab') next.delete('day')
        return next
      },
      { replace: true },
    )

  const goToList = (id: TabId) => {
    update('tab', id)
    document.getElementById('feed')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  const todayItems = flat(todays)
  const hero = todayItems.find((e) => e.imageUrl && safeHref(e.imageUrl))
  const topDeals = [...flat(deals)]
    .sort((a, b) => (b.discountPercent ?? 0) - (a.discountPercent ?? 0))
    .slice(0, 3)

  return (
    <div className="page-width py-6 sm:py-8">
      <div className="grid gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(320px,1fr)]">
        <Hero
          image={hero?.imageUrl ?? null}
          live={todayItems.length > 0}
          onEvents={() => goToList('today')}
          onDeals={() => goToList('deals')}
        />
        <DealsPanel deals={topDeals} loading={deals.isPending} />
      </div>

      <CategoryRow />

      <div
        id="feed"
        className="mt-10 grid scroll-mt-24 gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(0,5fr)]"
      >
        <section aria-labelledby="feed-heading" className="min-w-0">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <h2 id="feed-heading" className="sr-only">
              {t.pages.home}
            </h2>
            <div
              role="tablist"
              aria-label={p.tabs.label}
              className="tabs min-w-0 flex-1 overflow-x-auto"
            >
              {tabs.map((x) => {
                const active = tab === x.id
                return (
                  <button
                    key={x.id}
                    type="button"
                    role="tab"
                    id={`tab-${x.id}`}
                    aria-selected={active}
                    aria-controls="feed-panel"
                    tabIndex={active ? 0 : -1}
                    onClick={() => update('tab', x.id)}
                    onKeyDown={(e) => {
                      const step = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0
                      if (!step) return
                      const i = tabs.findIndex((y) => y.id === tab)
                      const nextTab = tabs[(i + step + tabs.length) % tabs.length] ?? tabs[0]
                      update('tab', nextTab.id)
                      document.getElementById(`tab-${nextTab.id}`)?.focus()
                    }}
                    className="tab"
                  >
                    {x.label}
                    {active && (
                      <motion.span
                        layoutId="feed-tab"
                        aria-hidden="true"
                        className="tab-indicator"
                        transition={spring}
                      />
                    )}
                  </button>
                )
              })}
            </div>
            <Link
              to={tab === 'deals' ? '/deals' : '/events'}
              className="group inline-flex min-h-11 items-center gap-1.5 text-sm font-bold text-brand-text"
            >
              {tab === 'deals' ? p.seeAllDeals : p.seeAllEvents}
              <ArrowRight
                aria-hidden="true"
                className="size-4 transition-transform duration-200 group-hover:translate-x-1"
              />
            </Link>
          </div>

          {tab === 'events' && (
            <div className="mt-4 flex flex-wrap gap-2">
              {dayFilters.map((d) => (
                <button
                  key={d.id}
                  type="button"
                  aria-pressed={day === d.id}
                  onClick={() => update('day', d.id)}
                  className={`chip ${day === d.id ? 'chip-active' : ''}`}
                >
                  {d.label}
                </button>
              ))}
            </div>
          )}

          <div role="tabpanel" id="feed-panel" aria-labelledby={`tab-${tab}`} className="mt-5">
            {/* Keyed fade: the old panel is never kept mounted, so only one copy of an item exists. */}
            <motion.div
              key={`${tab}-${day}`}
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.2, ease }}
            >
              <Feed
                tab={tab}
                day={day}
                today={today}
                search={search}
                city={city}
                todays={todays}
                deals={deals}
              />
            </motion.div>
          </div>
        </section>

        <Promo />
      </div>
    </div>
  )
}

function Hero({
  image,
  live,
  onEvents,
  onDeals,
}: {
  image: string | null
  live: boolean
  onEvents: () => void
  onDeals: () => void
}) {
  const href = image ? safeHref(image) : undefined
  // The headline words stagger in; everything else follows within 700 ms.
  const words = t.pages.home.split(' ')
  return (
    <section
      className={`on-charcoal relative isolate flex min-h-[380px] flex-col justify-center overflow-hidden rounded-card p-6 sm:p-10 lg:min-h-[420px] ${
        href ? '' : 'brand-pattern'
      }`}
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
        <span aria-hidden="true" className="brand-glow absolute inset-0 -z-20" />
      )}
      <span
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[linear-gradient(100deg,rgb(18_18_20/0.96),rgb(18_18_20/0.86)_45%,rgb(18_18_20/0.55))]"
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
        <h1 className="mt-4 text-[34px] font-extrabold leading-[1.08] sm:text-[44px] lg:text-[52px]">
          {words.map((w, i) => (
            <motion.span
              key={`${w}-${i}`}
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.45, ease, delay: 0.05 * i }}
              className="inline-block"
            >
              {w}
              {i < words.length - 1 ? ' ' : ''}
            </motion.span>
          ))}
        </h1>
        <motion.p
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.45, ease, delay: 0.3 }}
          className="mt-4 max-w-lg text-[15px] leading-relaxed text-on-dark-muted"
        >
          {p.heroLead}
        </motion.p>
        <motion.div
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.45, ease, delay: 0.4 }}
          className="mt-7 flex flex-wrap gap-3"
        >
          <button type="button" onClick={onEvents} className="btn btn-primary">
            {p.ctaEvents}
            <ArrowRight aria-hidden="true" className="size-4" />
          </button>
          <button type="button" onClick={onDeals} className="btn btn-on-dark">
            {p.ctaDeals}
          </button>
        </motion.div>
      </div>
    </section>
  )
}

function DealsPanel({ deals, loading }: { deals: DealSummaryDto[]; loading: boolean }) {
  return (
    <section
      aria-labelledby="deals-panel"
      className="card flex flex-col p-5 lg:max-h-[420px] lg:overflow-y-auto"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 id="deals-panel" className="flex items-center gap-2 text-lg">
            <Sparkles aria-hidden="true" className="size-5 text-brand-text" />
            {p.dealsPanelTitle}
          </h2>
          <p className="mt-1 text-xs text-muted">{p.dealsPanelLead}</p>
        </div>
        <Link
          to="/deals"
          className="group inline-flex min-h-11 shrink-0 items-center gap-1 text-sm font-bold text-brand-text"
        >
          {p.seeAll}
          <ArrowRight
            aria-hidden="true"
            className="size-4 transition-transform duration-200 group-hover:translate-x-1"
          />
        </Link>
      </div>

      <div className="mt-4 flex flex-col gap-2">
        {loading && [0, 1, 2].map((i) => <div key={i} className="skeleton h-[76px] rounded-xl" />)}
        {!loading && deals.length === 0 && (
          <p className="flex flex-1 flex-col items-center justify-center gap-2 py-8 text-center text-sm text-muted">
            {p.noDeals}{' '}
            <Link to="/deals" className="font-bold text-brand-text underline underline-offset-4">
              {p.seeAllDeals}
            </Link>
          </p>
        )}
        {deals.map((d, i) => (
          <motion.article
            key={d.id}
            initial={{ opacity: 0, x: 12 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.4, ease, delay: 0.08 * i }}
            className="card-hover relative flex items-center gap-3 rounded-xl border border-line p-2"
          >
            <div className="size-16 shrink-0 overflow-hidden rounded-lg">
              <Media
                url={d.imageUrl}
                category={d.category}
                kind="deal"
                className="!aspect-square"
              />
            </div>
            <div className="min-w-0 flex-1">
              <h3 className="line-clamp-2 text-sm font-bold leading-snug">
                <Link to={`/deals/${d.id}`} className="stretched rounded">
                  {d.title}
                </Link>
              </h3>
              {d.merchantName && (
                <p className="mt-0.5 truncate text-xs text-muted">{d.merchantName}</p>
              )}
              {d.discountPercent !== null && (
                <span className="mt-1.5 inline-block">
                  <DiscountBadge percent={d.discountPercent} />
                </span>
              )}
            </div>
            <ChevronRight aria-hidden="true" className="size-5 shrink-0 text-muted" />
          </motion.article>
        ))}
      </div>
    </section>
  )
}

function CategoryRow() {
  const categories = useCategories('event')
  const items = categories.data ?? []
  return (
    <nav aria-label={p.categoriesLabel} className="-mx-5 mt-8 sm:mx-0">
      <ul className="flex snap-x snap-mandatory gap-3 overflow-x-auto px-5 pb-2 sm:flex-wrap sm:px-0">
        <CategoryChip
          to="/events"
          tint="chip-active"
          label={p.allCategories2}
          Icon={Sparkles}
          first
        />
        {items.map((c) => {
          const { tile, Icon } = categoryStyle(c.slug)
          return (
            <CategoryChip
              key={c.slug}
              to={`/events?category=${c.slug}`}
              tint={tile}
              label={c.name}
              Icon={Icon}
            />
          )
        })}
        <CategoryChip to="/deals" tint="cat-ushqim" label={p.dealsChip} Icon={Tag} />
      </ul>
    </nav>
  )
}

function CategoryChip({
  to,
  tint,
  label,
  Icon,
  first = false,
}: {
  to: string
  tint: string
  label: string
  Icon: React.ComponentType<{ className?: string; 'aria-hidden'?: boolean }>
  first?: boolean
}) {
  return (
    <li className="shrink-0 snap-start">
      <Link
        to={to}
        className={`chip shrink-0 whitespace-nowrap ${first ? 'chip-active' : ''} transition-transform hover:scale-[1.04]`}
      >
        <span
          className={`grid size-7 place-items-center rounded-full ${first ? 'bg-white/30' : tint}`}
        >
          <Icon aria-hidden className="size-4" />
        </span>
        {label}
      </Link>
    </li>
  )
}

function Promo() {
  return (
    <aside className="on-charcoal brand-pattern relative isolate flex min-w-0 flex-col justify-end overflow-hidden rounded-card p-6 sm:p-8 lg:sticky lg:top-24 lg:min-h-[380px] lg:self-start">
      <span aria-hidden="true" className="brand-glow absolute inset-0" />
      <BrandMark className="pointer-events-none absolute -right-6 -top-8 h-40 opacity-10" />
      <div className="relative">
        <p className="text-xs font-extrabold uppercase tracking-[0.18em] text-brand-on-dark">
          {t.brand}
        </p>
        <h2 className="mt-3 text-2xl leading-tight sm:text-3xl">{p.promoTitle}</h2>
        <p className="mt-3 max-w-sm text-sm leading-relaxed text-on-dark-muted">{p.promoLead}</p>
        <Link to="/events" className="btn btn-on-dark mt-6 w-fit">
          {p.promoCta}
          <ArrowRight aria-hidden="true" className="size-4" />
        </Link>
      </div>
    </aside>
  )
}

function Feed({
  tab,
  day,
  today,
  search,
  city,
  todays,
  deals,
}: {
  tab: TabId
  day: string
  today: string
  search: string
  city: string
  todays: ReturnType<typeof useEvents>
  deals: ReturnType<typeof useDeals>
}) {
  const base = { search: search || undefined, city: city || undefined }
  const date = day ? addDays(today, Number(day)) : ''
  // The "Evente" tab looks three days ahead (or at one chosen day); "Sot" reuses the hero's query.
  const events = useEvents(date ? { ...base, from: date, to: date } : base, tab === 'events')
  const list = tab === 'events' ? events : todays
  const active = tab === 'today' ? [todays, deals] : tab === 'events' ? [events] : [deals]
  const eventItems = tab !== 'deals' ? flat(list) : []
  const dealItems = tab !== 'events' ? flat(deals) : []

  return (
    <ListShell queries={active} skeleton={false}>
      <div className="card divide-y divide-line overflow-hidden">
        {dealItems.map((d, i) => (
          <DealCard key={d.id} deal={d} index={i} />
        ))}
        {eventItems.map((e, i) => (
          <EventCard key={e.id} event={e} index={dealItems.length + i} />
        ))}
      </div>
    </ListShell>
  )
}
