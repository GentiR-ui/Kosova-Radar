import { motion } from 'motion/react'
import {
  ArrowUpRight,
  CalendarDays,
  Clock3,
  MapPin,
  Radar,
  Search,
  Sparkles,
  Tag,
  Ticket,
} from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { flat, useCities, useDeals, useEvents, type EventSummaryDto } from './api'
import { addDays, dayAndMonth, formatEventStart, pristinaToday, safeHref } from './format'
import Intro, { Reveal } from './Intro'
import { spring } from './motion'
import { DealCard, EventCard } from './ItemCards'
import ListShell from './ListShell'
import { container } from './PublicLayout'
import RadarSignal from './RadarSignal'
import SearchInput from './SearchInput'
import { p } from './strings'

const tabs = [
  { id: 'today', label: p.tabs.today },
  { id: 'events', label: p.tabs.events },
  { id: 'deals', label: p.tabs.deals },
] as const
type TabId = (typeof tabs)[number]['id']
const isTab = (v: string | null): v is TabId => tabs.some((x) => x.id === v)

/** Day filter inside the "Evente" tab: all three days, tomorrow or the day after. */
const dayFilters = [
  { id: '', label: p.days.all },
  { id: '1', label: p.days.tomorrow },
  { id: '2', label: p.days.dayAfter },
] as const

/** "12 tet" or "12 tetor". */
function dayLabel(ymd: string, long = false) {
  const { day, month } = dayAndMonth(`${ymd}T12:00:00Z`)
  const name = p.months[month] ?? ''
  return `${Number(day)} ${long ? name : name.slice(0, 3)}`
}

export default function HomePage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = isTab(params.get('tab')) ? (params.get('tab') as TabId) : 'today'
  const search = params.get('search') ?? ''
  const city = params.get('city') ?? ''
  const day = tab === 'events' ? (params.get('day') ?? '') : ''
  const today = pristinaToday()
  const cities = useCities()

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

  const [first, ...rest] = t.pages.home.split(' sot ')
  return (
    <>
      <Intro
        eyebrow={p.eyebrow}
        title={
          // "Çfarë ka sot në Kosovë" with "sot" in lime.
          rest.length ? (
            <>
              {first} <span className="text-accent">sot</span> {rest.join(' sot ')}
            </>
          ) : (
            t.pages.home
          )
        }
        lead={
          <>
            <strong className="font-semibold text-ink max-sm:block">{p.homeLeadStrong}</strong>{' '}
            {p.homeLead}
          </>
        }
        aside={
          <p className="flex items-center gap-2 text-sm text-muted">
            <CalendarDays aria-hidden="true" className="size-4" />
            {`${Number(today.slice(8))} – ${dayLabel(addDays(today, 2), true)}, ${today.slice(0, 4)}`}
          </p>
        }
      />
      <div className={container}>
        <Focus today={today} />
        <Reveal delay={0.3}>
          <div className="mt-[17px] flex items-center justify-between gap-3 border-y border-line py-3 text-[10px] font-bold uppercase tracking-wide sm:mt-[22px] sm:py-[15px] sm:text-[11px]">
            <span className="flex items-center gap-2 whitespace-nowrap text-accent">
              <span className="live-dot" /> {p.ticker[0]}
            </span>
            <span className="hidden items-center gap-2 whitespace-nowrap text-event sm:flex">
              <Ticket aria-hidden="true" className="size-3.5" /> {p.ticker[1]}
            </span>
            <span className="flex items-center gap-2 whitespace-nowrap text-accent">
              <Tag aria-hidden="true" className="size-3.5" /> {p.ticker[2]}
            </span>
            <Link
              to="/events"
              className="hidden items-center gap-2 whitespace-nowrap rounded-sm hover:text-accent lg:flex"
            >
              {p.ticker[3]} <ArrowUpRight aria-hidden="true" className="size-3.5" />
            </Link>
          </div>
        </Reveal>

        <section id="feed" aria-labelledby="feed-title" className="pt-[26px] sm:pt-[31px]">
          <div className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-4">
            <div>
              <h2 id="feed-title" className="text-[21px] sm:text-[26px]">
                {p.feedTitle} <span className="text-accent">↗</span>
              </h2>
              <p className="mt-1 text-xs text-muted sm:text-sm">{p.feedLead}</p>
            </div>
            <div className="relative">
              <label htmlFor="home-city" className="sr-only">
                {p.city}
              </label>
              <MapPin
                aria-hidden="true"
                className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-accent"
              />
              <select
                id="home-city"
                value={city}
                onChange={(e) => update('city', e.target.value)}
                className="h-11 w-[140px] cursor-pointer rounded-sm border border-line bg-surface pl-9 pr-2 text-xs font-semibold text-ink transition-colors hover:border-accent sm:w-[175px] sm:text-sm"
              >
                <option value="">{p.allCities}</option>
                {cities.data?.map((c) => (
                  <option key={c.slug} value={c.slug}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div
            role="tablist"
            aria-label={p.tabs.label}
            className="mt-[22px] grid grid-cols-3 rounded-md border border-line bg-surface p-[5px] sm:mt-[23px]"
          >
            {tabs.map((x) => {
              const active = tab === x.id
              const hint = x.id === 'today' ? dayLabel(today) : p.tabHint[x.id]
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
                  className={`relative isolate flex h-16 flex-col items-center justify-center gap-1 rounded-sm px-1 transition-colors duration-200 sm:h-[61px] sm:flex-row sm:gap-2.5 ${
                    active ? 'text-accent-ink' : 'text-muted hover:text-ink'
                  }`}
                >
                  {active && (
                    <motion.span
                      layoutId="home-tab"
                      aria-hidden="true"
                      className="absolute inset-0 -z-10 rounded-sm bg-accent"
                      transition={spring}
                    />
                  )}
                  <span className="flex items-center gap-2 text-xs font-extrabold sm:text-sm">
                    {x.label}
                    {x.id === 'today' && (
                      <span
                        aria-hidden="true"
                        className={`size-[5px] rounded-full ${active ? 'bg-accent-ink' : 'bg-accent'}`}
                      />
                    )}
                  </span>
                  <span aria-hidden="true" className="text-[10px] opacity-80 sm:text-[11px]">
                    {hint}
                  </span>
                </button>
              )
            })}
          </div>

          <div className="mt-[18px] flex flex-wrap items-center justify-between gap-3 sm:mt-[22px]">
            <div className="flex min-w-0 flex-wrap items-center gap-1.5 sm:gap-2">
              {tab === 'events' &&
                dayFilters.map((d) => {
                  const active = day === d.id
                  return (
                    <button
                      key={d.id}
                      type="button"
                      aria-pressed={active}
                      onClick={() => update('day', d.id)}
                      className={`relative isolate inline-flex h-11 items-center gap-1.5 rounded-full border px-3.5 text-xs font-semibold transition-colors ${
                        active
                          ? 'border-transparent text-accent-ink'
                          : 'border-line text-muted hover:text-ink'
                      }`}
                    >
                      {active && (
                        <motion.span
                          layoutId="home-day"
                          aria-hidden="true"
                          className="absolute -inset-px -z-10 rounded-full bg-accent"
                          transition={spring}
                        />
                      )}
                      {d.label}
                      {d.id && (
                        <span aria-hidden="true" className="opacity-75">
                          {dayLabel(addDays(today, Number(d.id)))}
                        </span>
                      )}
                    </button>
                  )
                })}
            </div>
            <div className="relative w-full sm:w-64">
              <label htmlFor="home-search" className="sr-only">
                {p.search}
              </label>
              <Search
                aria-hidden="true"
                className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted"
              />
              <SearchInput
                id="home-search"
                value={search}
                placeholder={p.search}
                onCommit={(v) => update('search', v)}
                className="h-11 w-full rounded-full border border-line bg-transparent pl-10 pr-4 text-sm text-ink transition-colors placeholder:text-muted hover:border-muted focus:border-accent focus:outline-none"
              />
            </div>
          </div>

          <div role="tabpanel" id="feed-panel" aria-labelledby={`tab-${tab}`}>
            <Feed tab={tab} search={search} city={city} day={day} today={today} />
          </div>

          <div
            aria-hidden="true"
            className="mx-auto mb-2.5 mt-[34px] flex items-center justify-center gap-3.5 text-accent"
          >
            <span className="h-px w-9 bg-line" />
            <Radar className="size-[17px]" />
            <span className="h-px w-9 bg-line" />
          </div>
          <p className="text-center text-sm text-muted">{p.endOfFeed}</p>
        </section>
      </div>
    </>
  )
}

function Feed({
  tab,
  search,
  city,
  day,
  today,
}: {
  tab: TabId
  search: string
  city: string
  day: string
  today: string
}) {
  const date = tab === 'today' ? today : day ? addDays(today, Number(day)) : ''
  const base = { search: search || undefined, city: city || undefined }
  const events = useEvents(date ? { ...base, from: date, to: date } : base, tab !== 'deals')
  const deals = useDeals(base, tab !== 'events')
  const active = tab === 'today' ? [events, deals] : tab === 'events' ? [events] : [deals]
  const eventItems = tab !== 'deals' ? flat(events) : []
  const dealItems = tab !== 'events' ? flat(deals) : []
  const total = active.reduce((n, q) => n + (q.data?.pages[0]?.totalCount ?? 0), 0)
  const label = tabs.find((x) => x.id === tab)?.label
  const when = date
    ? dayLabel(date, true)
    : tab === 'events'
      ? `${Number(today.slice(8))} – ${dayLabel(addDays(today, 2), true)}`
      : dayLabel(today, true)

  return (
    <>
      <div className="mb-3.5 mt-6 flex items-center justify-between text-[13px] font-bold">
        <span>
          {label} <span className="font-normal text-muted">/ {when}</span>
        </span>
        <span className="text-xs font-normal text-muted">{p.count(total)}</span>
      </div>
      <ListShell queries={active} grid>
        {eventItems.map((e, i) => (
          <EventCard key={e.id} event={e} index={i} />
        ))}
        {dealItems.map((d, i) => (
          <DealCard key={d.id} deal={d} index={eventItems.length + i} />
        ))}
      </ListShell>
    </>
  )
}

/** Today's highlighted event: the first one of the day with a picture. */
function Focus({ today }: { today: string }) {
  const query = useEvents({ from: today, to: today })
  const items = flat(query)
  const item: EventSummaryDto | undefined =
    items.find((e) => e.imageUrl && safeHref(e.imageUrl)) ?? items[0]
  if (!item) return null
  const href = item.imageUrl ? safeHref(item.imageUrl) : undefined
  const tags = [item.city.name, item.category?.name].filter(Boolean).join(' · ')
  return (
    <Reveal delay={0.2}>
      <section
        aria-label={p.focus}
        className="relative h-[320px] overflow-hidden rounded-[4px] border border-line bg-raised text-white sm:h-[355px]"
      >
        {href && (
          <img
            src={href}
            alt=""
            width={1536}
            height={1024}
            fetchPriority="high"
            referrerPolicy="no-referrer"
            className="photo-drift size-full object-cover object-[42%_center] sm:object-[center_48%]"
          />
        )}
        <div
          aria-hidden="true"
          className="absolute inset-0 bg-[linear-gradient(90deg,oklch(0.12_0.012_170/96%),oklch(0.12_0.012_170/72%)_48%,oklch(0.12_0.012_170/30%))]"
        />
        <div className="absolute right-[15px] top-[15px] grid w-[72px] justify-items-center gap-3.5 text-accent opacity-70 sm:right-10 sm:top-10 sm:w-[235px] sm:opacity-100">
          <RadarSignal className="radar-large w-[72px] sm:w-[220px]" />
          <span aria-hidden="true" className="hidden font-mono text-[9px] opacity-80 sm:block">
            42.6629° N / 21.1655° E
          </span>
        </div>
        <div className="absolute inset-x-[22px] top-6 z-10 sm:left-[35px] sm:right-auto sm:top-[29px] sm:max-w-[60%]">
          <span className="inline-flex items-center gap-1.5 rounded-[4px] border border-[oklch(0.91_0.19_127/27%)] bg-[oklch(0.14_0.02_160)] px-2.5 py-1.5 text-[10px] font-extrabold uppercase text-accent">
            <Sparkles aria-hidden="true" className="size-3" /> {p.focus}
          </span>
          {tags && (
            <p className="mt-6 text-[10px] font-semibold uppercase tracking-wide opacity-75">
              {tags}
            </p>
          )}
          {/* Not a heading: the same title is the card heading in the list below. */}
          <p className="mt-2 line-clamp-2 text-[30px] font-extrabold leading-[1.13] sm:text-[44px]">
            {item.title}
          </p>
          <p className="mt-4 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] opacity-90 sm:text-xs">
            <Clock3 aria-hidden="true" className="size-[15px]" />
            {formatEventStart(item.startsAt, item.startTimeKnown)}
            {item.venueName && (
              <>
                <span aria-hidden="true">·</span>
                <MapPin aria-hidden="true" className="size-[15px]" />
                {item.venueName}
              </>
            )}
          </p>
          <Link
            to={`/events/${item.id}`}
            className="btn btn-primary mt-[22px] min-h-10 rounded-[4px] text-xs"
          >
            {p.discover} <ArrowUpRight aria-hidden="true" className="size-[17px]" />
          </Link>
        </div>
        <span
          aria-hidden="true"
          className="absolute bottom-[17px] right-[17px] flex items-center gap-2 text-[8px] font-bold uppercase text-accent sm:bottom-[25px] sm:right-[25px] sm:text-[9px]"
        >
          <span className="h-[3px] w-[15px] bg-accent sm:w-[23px]" /> {tags}
        </span>
      </section>
    </Reveal>
  )
}
