import { motion } from 'motion/react'
import {
  ArrowRight,
  ArrowUpRight,
  CalendarDays,
  Clock3,
  Flame,
  MapPin,
  Radar,
  RefreshCw,
  Search,
  ShieldCheck,
  Sparkles,
  Tag,
  X,
} from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { flat, useCities, useDeals, useEvents, type EventSummaryDto } from './api'
import { addDays, formatEventStart, pristinaToday, safeHref } from './format'
import { Reveal } from './Intro'
import { DealCard, EventCard } from './ItemCards'
import ListShell, { cardGrid } from './ListShell'
import { spring } from './motion'
import { container } from './PublicLayout'
import RadarSignal from './RadarSignal'
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

/** The cities shown as chips in the hero, in this order, if the API knows them. */
const chipCities = ['prishtine', 'prizren', 'peje', 'gjilan', 'ferizaj', 'mitrovice', 'gjakove']

const features = [Tag, CalendarDays, ShieldCheck, RefreshCw]

export default function HomePage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = isTab(params.get('tab')) ? (params.get('tab') as TabId) : 'today'
  const search = params.get('search') ?? ''
  const city = params.get('city') ?? ''
  const day = tab === 'events' ? (params.get('day') ?? '') : ''
  const today = pristinaToday()
  const cities = useCities()
  const cityName = cities.data?.find((c) => c.slug === city)?.name
  const place = cityName ?? p.kosovo

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

  const words = t.pages.home.split(' ')
  const last = words.pop() ?? ''
  return (
    <>
      <Hero city={city} cityNames={cities.data} onCity={(v) => update('city', v)} today={today}>
        {/* The h1 keeps the wording of the requirement; only the last word is highlighted. */}
        {words.join(' ')} <span className="text-gradient">{last}?</span>
      </Hero>

      <section aria-label={p.feedTitle} className="border-y border-line bg-soft">
        <div className={`${container} grid gap-x-8 gap-y-5 py-6 sm:grid-cols-2 lg:grid-cols-4`}>
          {p.features.map((f, i) => {
            const Icon = features[i] ?? Tag
            return (
              <motion.div
                key={f.title}
                initial={{ opacity: 0, y: 14 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.45, ease: [0.16, 1, 0.3, 1], delay: i * 0.07 }}
                className="flex items-center gap-3"
              >
                <span className="grid size-11 shrink-0 place-items-center rounded-full bg-surface text-accent shadow-card">
                  <Icon aria-hidden="true" className="size-5" />
                </span>
                <span className="min-w-0">
                  <span className="block text-sm font-extrabold">{f.title}</span>
                  <span className="block text-xs text-muted">{f.text}</span>
                </span>
              </motion.div>
            )
          })}
        </div>
      </section>

      <section id="feed" className={`${container} scroll-mt-24 pt-10 sm:pt-14`}>
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="eyebrow text-accent">
              <Radar aria-hidden="true" className="size-4" /> {p.feedTitle}
            </p>
            <p className="mt-1.5 text-sm text-muted">{p.feedLead}</p>
          </div>
          <div
            role="tablist"
            aria-label={p.tabs.label}
            className="flex gap-1 rounded-full border border-line bg-soft p-1.5"
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
                  className={`relative isolate inline-flex min-h-11 items-center gap-2 rounded-full px-4 text-sm font-extrabold transition-colors duration-200 sm:px-5 ${
                    active ? 'text-accent-ink' : 'text-muted hover:text-ink'
                  }`}
                >
                  {active && (
                    <motion.span
                      layoutId="home-tab"
                      aria-hidden="true"
                      className="absolute inset-0 -z-10 rounded-full bg-gradient-to-br from-accent to-[#0d9488] shadow-glow"
                      transition={spring}
                    />
                  )}
                  {x.label}
                </button>
              )
            })}
          </div>
        </div>

        <div className="mt-5 flex flex-wrap items-center gap-2">
          {tab === 'events' &&
            dayFilters.map((d) => {
              const active = day === d.id
              return (
                <button
                  key={d.id}
                  type="button"
                  aria-pressed={active}
                  onClick={() => update('day', d.id)}
                  className={`inline-flex min-h-10 items-center gap-1.5 rounded-full border px-4 text-xs font-bold transition-colors ${
                    active
                      ? 'border-accent bg-accent-soft text-accent'
                      : 'border-line text-muted hover:border-accent/40 hover:text-ink'
                  }`}
                >
                  {d.label}
                </button>
              )
            })}
          {search && (
            <button
              type="button"
              onClick={() => update('search', '')}
              className="inline-flex min-h-10 items-center gap-2 rounded-full border border-accent bg-accent-soft px-4 text-xs font-bold text-accent"
            >
              <Search aria-hidden="true" className="size-3.5" />
              {search}
              <X aria-hidden="true" className="size-3.5" />
            </button>
          )}
        </div>

        <div role="tabpanel" id="feed-panel" aria-labelledby={`tab-${tab}`} className="mt-6">
          <Feed tab={tab} search={search} city={city} day={day} today={today} place={place} />
        </div>
      </section>

      <Banner />
    </>
  )
}

function Hero({
  children,
  city,
  cityNames,
  onCity,
  today,
}: {
  children: ReactNode
  city: string
  cityNames: { slug: string; name: string }[] | undefined
  onCity: (slug: string) => void
  today: string
}) {
  const navigate = useNavigate()
  const [text, setText] = useState('')
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = text.trim()
    if (q) navigate(`/search?q=${encodeURIComponent(q)}`)
  }
  const shown = chipCities
    .map((slug) => cityNames?.find((c) => c.slug === slug))
    .filter((c): c is { slug: string; name: string } => !!c)
  const selected = cityNames?.find((c) => c.slug === city)
  if (selected && !shown.some((c) => c.slug === selected.slug)) shown.push(selected)

  return (
    <section
      className={`${container} grid items-center gap-10 py-8 lg:grid-cols-[1.05fr_1fr] lg:py-14`}
    >
      <div className="min-w-0">
        <Reveal>
          <p className="eyebrow text-accent">
            <span className="live-dot" /> {p.eyebrow}
          </p>
        </Reveal>
        <Reveal delay={0.06}>
          <h1 className="mt-4 text-[38px] leading-[1.06] sm:text-[52px] lg:text-[58px]">
            {children}
          </h1>
        </Reveal>
        <Reveal delay={0.12}>
          <p className="mt-4 max-w-xl text-[15px] leading-relaxed text-muted">{p.heroLead}</p>
        </Reveal>
        <Reveal delay={0.18}>
          <form role="search" aria-label={p.search} onSubmit={submit} className="mt-7 max-w-xl">
            <div className="relative">
              <Search
                aria-hidden="true"
                className="pointer-events-none absolute left-5 top-1/2 size-5 -translate-y-1/2 text-muted"
              />
              <label htmlFor="home-search" className="sr-only">
                {p.search}
              </label>
              <input
                id="home-search"
                type="search"
                value={text}
                onChange={(e) => setText(e.target.value)}
                placeholder={p.searchPlaceholderHome}
                className="h-14 w-full rounded-full border border-line bg-surface pl-[52px] pr-16 text-[15px] shadow-card transition-[border-color,box-shadow] placeholder:text-muted hover:border-accent/40 focus:border-accent focus:shadow-lift focus:outline-none"
              />
              <button
                type="submit"
                aria-label={p.startSearch}
                className="absolute right-2 top-1/2 grid size-10 -translate-y-1/2 place-items-center rounded-full bg-gradient-to-br from-accent to-[#0d9488] text-white shadow-glow transition-transform duration-200 hover:scale-105 active:scale-95"
              >
                <ArrowRight aria-hidden="true" className="size-5" />
              </button>
            </div>
          </form>
        </Reveal>
        <Reveal delay={0.24}>
          <div className="-mx-5 mt-5 flex gap-2 overflow-x-auto px-5 pb-1 sm:mx-0 sm:flex-wrap sm:px-0">
            <CityChip active={!city} onClick={() => onCity('')}>
              {p.allCitiesShort}
            </CityChip>
            {shown.map((c) => (
              <CityChip key={c.slug} active={city === c.slug} onClick={() => onCity(c.slug)}>
                {c.name}
              </CityChip>
            ))}
          </div>
        </Reveal>
        <Reveal delay={0.3}>
          <a href="#feed" className="btn btn-outline mt-6">
            {p.heroCta} <ArrowRight aria-hidden="true" className="size-4" />
          </a>
        </Reveal>
      </div>
      <Reveal delay={0.2} className="min-w-0">
        <Featured today={today} />
      </Reveal>
    </section>
  )
}

function CityChip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`inline-flex min-h-10 shrink-0 items-center gap-1.5 rounded-full border px-4 text-sm font-bold transition-colors duration-200 ${
        active
          ? 'border-accent bg-accent-soft text-accent'
          : 'border-line bg-surface text-muted hover:border-accent/40 hover:text-ink'
      }`}
    >
      <MapPin aria-hidden="true" className="size-3.5" />
      {children}
    </button>
  )
}

/** Today's highlighted event: the first one of the day, picture first. */
function Featured({ today }: { today: string }) {
  const query = useEvents({ from: today, to: today })
  const items = flat(query)
  const item: EventSummaryDto | undefined =
    items.find((e) => e.imageUrl && safeHref(e.imageUrl)) ?? items[0]
  const href = item?.imageUrl ? safeHref(item.imageUrl) : undefined
  return (
    <div className="relative aspect-[4/3] overflow-hidden rounded-card bg-night shadow-lift sm:aspect-[16/10] lg:aspect-[5/4]">
      {href ? (
        <img
          src={href}
          alt=""
          width={1536}
          height={1024}
          fetchPriority="high"
          referrerPolicy="no-referrer"
          className="photo-drift size-full object-cover"
        />
      ) : (
        <div
          aria-hidden="true"
          className="size-full bg-[radial-gradient(circle_at_70%_30%,#0f766e,#07211b_70%)]"
        />
      )}
      <div
        aria-hidden="true"
        className="absolute inset-0 bg-[linear-gradient(to_top,rgb(7_33_27/0.92),rgb(7_33_27/0.45)_45%,rgb(7_33_27/0.15))]"
      />
      <div className="absolute right-4 top-4 flex flex-col items-end gap-2">
        <RadarSignal className="radar-large w-24 sm:w-32" />
        <p className="script hidden max-w-[9rem] text-right text-xl leading-tight text-brand-3 sm:block sm:text-2xl">
          {p.heroScript}
        </p>
      </div>
      <div className="absolute inset-x-5 bottom-5 text-white">
        <span className="chip bg-white/15 text-white backdrop-blur-sm">
          <Sparkles aria-hidden="true" className="size-3.5" /> {p.focus}
        </span>
        {item ? (
          <>
            {/* Not a heading: the same title is the card heading in the list below. */}
            <p className="mt-3 line-clamp-2 text-xl font-extrabold leading-tight sm:text-2xl">
              {item.title}
            </p>
            <p className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-white/85 sm:text-sm">
              <span className="inline-flex items-center gap-1.5">
                <Clock3 aria-hidden="true" className="size-4" />
                {formatEventStart(item.startsAt, item.startTimeKnown)}
              </span>
              {item.venueName && (
                <span className="inline-flex items-center gap-1.5">
                  <MapPin aria-hidden="true" className="size-4" />
                  {item.venueName}
                </span>
              )}
            </p>
            <Link to={`/events/${item.id}`} className="btn btn-primary mt-4 min-h-10 text-sm">
              {p.discover} <ArrowUpRight aria-hidden="true" className="size-4" />
            </Link>
          </>
        ) : (
          <>
            <p className="mt-3 text-xl font-extrabold leading-tight sm:text-2xl">{p.feedLead}</p>
            <Link to="/events" className="btn btn-primary mt-4 min-h-10 text-sm">
              {p.seeAllEvents} <ArrowUpRight aria-hidden="true" className="size-4" />
            </Link>
          </>
        )}
      </div>
    </div>
  )
}

function Group({
  eyebrow,
  icon,
  title,
  link,
  linkLabel,
  children,
}: {
  eyebrow: string
  icon: ReactNode
  title: ReactNode
  link: string
  linkLabel: string
  children: ReactNode
}) {
  return (
    <div className="mt-12 first:mt-0">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <p className="eyebrow text-hot">
            {icon} {eyebrow}
          </p>
          <h2 className="mt-1.5 text-xl sm:text-2xl">{title}</h2>
        </div>
        <Link
          to={link}
          className="group inline-flex min-h-11 items-center gap-1.5 text-sm font-bold text-accent"
        >
          {linkLabel}
          <ArrowRight
            aria-hidden="true"
            className="size-4 transition-transform duration-200 group-hover:translate-x-1"
          />
        </Link>
      </div>
      <div className={`mt-5 ${cardGrid}`}>{children}</div>
    </div>
  )
}

function Feed({
  tab,
  search,
  city,
  day,
  today,
  place,
}: {
  tab: TabId
  search: string
  city: string
  day: string
  today: string
  place: string
}) {
  const date = tab === 'today' ? today : day ? addDays(today, Number(day)) : ''
  const base = { search: search || undefined, city: city || undefined }
  const events = useEvents(date ? { ...base, from: date, to: date } : base, tab !== 'deals')
  const deals = useDeals(base, tab !== 'events')
  const active = tab === 'today' ? [events, deals] : tab === 'events' ? [events] : [deals]
  const eventItems = tab !== 'deals' ? flat(events) : []
  const dealItems = tab !== 'events' ? flat(deals) : []

  return (
    <ListShell queries={active} skeleton>
      {dealItems.length > 0 && (
        <Group
          eyebrow={p.dealsEyebrow}
          icon={<Flame aria-hidden="true" className="size-4" />}
          title={
            <>
              {p.dealsTitlePre} <span className="text-accent">{place}</span>
            </>
          }
          link="/deals"
          linkLabel={p.seeAllDeals}
        >
          {dealItems.map((d, i) => (
            <DealCard key={d.id} deal={d} index={i} />
          ))}
        </Group>
      )}
      {eventItems.length > 0 && (
        <Group
          eyebrow={p.eventsEyebrow}
          icon={<CalendarDays aria-hidden="true" className="size-4" />}
          title={
            <>
              {p.eventsTitlePre} <span className="text-accent">{place}</span>?
            </>
          }
          link="/events"
          linkLabel={p.seeAllEvents}
        >
          {eventItems.map((e, i) => (
            <EventCard key={e.id} event={e} index={i} />
          ))}
        </Group>
      )}
    </ListShell>
  )
}

function Banner() {
  return (
    <section className={`${container} mt-16`}>
      <div className="relative overflow-hidden rounded-card bg-gradient-to-br from-accent to-[#0d9488] px-6 py-10 text-white sm:px-10 sm:py-12">
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -right-16 -top-20 size-80 rounded-full bg-[radial-gradient(circle,rgb(255_255_255/0.22),transparent_65%)]"
        />
        <RadarSignal className="radar-large absolute -bottom-12 right-6 hidden w-56 opacity-40 sm:block" />
        <div className="relative max-w-xl">
          <p className="eyebrow text-white/75">
            <MapPin aria-hidden="true" className="size-4" /> {p.bannerEyebrow}
          </p>
          <h2 className="mt-3 text-2xl leading-tight sm:text-3xl">{p.bannerTitle}</h2>
          <Link
            to="/events"
            className="btn mt-7 border-transparent bg-white text-accent hover:text-accent-strong"
          >
            {p.bannerButton} <ArrowRight aria-hidden="true" className="size-4" />
          </Link>
        </div>
      </div>
    </section>
  )
}
