import { motion } from 'motion/react'
import {
  ArrowRight,
  Bookmark,
  CalendarDays,
  Clock3,
  Flame,
  MapPin,
  Search,
  Zap,
} from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { flat, useCities, useDeals, useEvents } from './api'
import { addDays, dayAndMonth, pristinaToday, safeHref } from './format'
import { DealCard, EventCard } from './ItemCards'
import ListShell from './ListShell'
import { spring } from './motion'
import RadarSignal from './RadarSignal'
import { savedKey, useSaved } from './saved'
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

/** The cities offered as chips in the hero, in this order, if the API knows them. */
const chipCities = ['prishtine', 'prizren', 'peje', 'gjilan', 'ferizaj']

/** "15 tetor" for a yyyy-MM-dd date. */
function dayLabel(ymd: string) {
  const { day, month } = dayAndMonth(`${ymd}T12:00:00Z`)
  return `${Number(day)} ${p.months[month] ?? ''}`
}

export default function HomePage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = isTab(params.get('tab')) ? (params.get('tab') as TabId) : 'today'
  const search = params.get('search') ?? ''
  const city = params.get('city') ?? ''
  const day = tab === 'events' ? (params.get('day') ?? '') : ''
  const today = pristinaToday()
  const cities = useCities()
  const place = cities.data?.find((c) => c.slug === city)?.name ?? p.kosovo
  const saved = useSaved()
  const [savedOnly, setSavedOnly] = useState(false)

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
  const hint = { today: dayLabel(today), events: p.tabHint.events, deals: p.tabHint.deals }

  return (
    <>
      <Hero city={city} cities={cities.data} onCity={(v) => update('city', v)} today={today}>
        {/* The h1 keeps the wording of the requirement; only the last word moves to its own line. */}
        {words.join(' ')} <span className="block">{last}?</span>
      </Hero>

      <div className="benefit-band">
        <div className="page-width benefits">
          {p.benefits.map((b, i) => {
            const Icon = [Zap, CalendarDays, MapPin][i] ?? Zap
            return (
              <motion.div
                key={b.title}
                initial={{ opacity: 0, y: 10 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.4, ease: [0.16, 1, 0.3, 1], delay: i * 0.08 }}
              >
                <Icon aria-hidden="true" />
                <span>
                  <strong>{b.title}</strong>
                  <small>{b.text}</small>
                </span>
              </motion.div>
            )
          })}
        </div>
      </div>

      <section id="feed" className="page-width feed-section">
        <div className="feed-top">
          <div>
            <p className="eyebrow">
              <Flame aria-hidden="true" size={15} /> {p.feedEyebrow}
            </p>
            <h2>
              {p.feedTitlePre} <span>{place}?</span>
            </h2>
          </div>
          <button
            type="button"
            aria-pressed={savedOnly}
            onClick={() => setSavedOnly((v) => !v)}
            className={`saved-toggle ${savedOnly ? 'active' : ''}`}
          >
            <Bookmark aria-hidden="true" size={18} />
            <span>{p.savedItems}</span>
            {saved.count > 0 && <small>{saved.count}</small>}
          </button>
        </div>

        <div className="feed-controls">
          <div role="tablist" aria-label={p.tabs.label} className="day-switcher">
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
                  className={`day-tab ${active ? 'active' : ''}`}
                >
                  {active && (
                    <motion.span
                      layoutId="feed-tab"
                      aria-hidden="true"
                      className="day-indicator"
                      transition={spring}
                    />
                  )}
                  <span>{x.label}</span>
                  <small aria-hidden="true">{hint[x.id]}</small>
                </button>
              )
            })}
          </div>
          {tab === 'events' && (
            <div className="filter-row">
              {dayFilters.map((d) => {
                const active = day === d.id
                return (
                  <button
                    key={d.id}
                    type="button"
                    aria-pressed={active}
                    onClick={() => update('day', d.id)}
                    className={`filter-pill ${active ? 'selected' : ''}`}
                  >
                    {active && (
                      <motion.span
                        layoutId="feed-day"
                        aria-hidden="true"
                        className="filter-indicator"
                        transition={spring}
                      />
                    )}
                    <CalendarDays aria-hidden="true" size={15} />
                    {d.label}
                  </button>
                )
              })}
            </div>
          )}
        </div>

        <div role="tabpanel" id="feed-panel" aria-labelledby={`tab-${tab}`}>
          <Feed
            tab={tab}
            search={search}
            city={city}
            day={day}
            today={today}
            savedOnly={savedOnly}
          />
        </div>
      </section>
    </>
  )
}

function Hero({
  children,
  city,
  cities,
  onCity,
  today,
}: {
  children: ReactNode
  city: string
  cities: { slug: string; name: string }[] | undefined
  onCity: (slug: string) => void
  today: string
}) {
  const navigate = useNavigate()
  const [text, setText] = useState('')
  // Today's first picture carries the hero; the overlay fades it into the page as in the design.
  const featured = flat(useEvents({ from: today, to: today })).find(
    (e) => e.imageUrl && safeHref(e.imageUrl),
  )
  const photo = featured?.imageUrl ? safeHref(featured.imageUrl) : undefined
  const chips = chipCities
    .map((slug) => cities?.find((c) => c.slug === slug))
    .filter((c): c is { slug: string; name: string } => !!c)
  const selected = cities?.find((c) => c.slug === city)
  if (selected && !chips.some((c) => c.slug === selected.slug)) chips.push(selected)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = text.trim()
    if (q) navigate(`/search?q=${encodeURIComponent(q)}`)
  }

  return (
    <section className="discovery">
      {photo && (
        <img
          className="city-backdrop"
          src={photo}
          alt=""
          width={1536}
          height={1024}
          fetchPriority="high"
          referrerPolicy="no-referrer"
        />
      )}
      <div aria-hidden="true" className="city-fade" />
      <div className="page-width discovery-inner">
        <div className="discovery-copy">
          <p className="eyebrow">
            <span className="live-dot" /> {p.eyebrow}
          </p>
          <h1>{children}</h1>
          <p className="intro-copy">{p.heroLead}</p>
          <form role="search" className="search-bar" onSubmit={submit}>
            <Search aria-hidden="true" size={20} />
            <input
              type="search"
              value={text}
              aria-label={p.searchAria}
              placeholder={p.searchPlaceholderHome}
              onChange={(e) => setText(e.target.value)}
            />
            <button type="submit" aria-label={p.search}>
              <ArrowRight aria-hidden="true" size={20} />
            </button>
          </form>
          <div className="city-chips">
            <button
              type="button"
              aria-pressed={!city}
              onClick={() => onCity('')}
              className={`city-chip ${!city ? 'active' : ''}`}
            >
              <MapPin aria-hidden="true" size={13} />
              {p.allCitiesShort}
            </button>
            {chips.map((c) => (
              <button
                key={c.slug}
                type="button"
                aria-pressed={city === c.slug}
                onClick={() => onCity(c.slug)}
                className={`city-chip ${city === c.slug ? 'active' : ''}`}
              >
                <MapPin aria-hidden="true" size={13} />
                {c.name}
              </button>
            ))}
          </div>
        </div>
        <div className="hero-radar">
          <RadarSignal className="radar-large" />
          <span>{p.heroScript}</span>
        </div>
      </div>
    </section>
  )
}

function Feed({
  tab,
  search,
  city,
  day,
  today,
  savedOnly,
}: {
  tab: TabId
  search: string
  city: string
  day: string
  today: string
  savedOnly: boolean
}) {
  const saved = useSaved()
  const date = tab === 'today' ? today : day ? addDays(today, Number(day)) : ''
  const base = { search: search || undefined, city: city || undefined }
  const events = useEvents(date ? { ...base, from: date, to: date } : base, tab !== 'deals')
  const deals = useDeals(base, tab !== 'events')
  const active = tab === 'today' ? [events, deals] : tab === 'events' ? [events] : [deals]
  const eventItems = (tab !== 'deals' ? flat(events) : []).filter(
    (e) => !savedOnly || saved.has(savedKey('event', e.id)),
  )
  const dealItems = (tab !== 'events' ? flat(deals) : []).filter(
    (d) => !savedOnly || saved.has(savedKey('deal', d.id)),
  )
  const total = active.reduce((n, q) => n + (q.data?.pages[0]?.totalCount ?? 0), 0)
  const shown = eventItems.length + dealItems.length
  const when =
    tab === 'deals'
      ? dayLabel(today)
      : date
        ? dayLabel(date)
        : `${dayLabel(today)} – ${dayLabel(addDays(today, 2))}`

  return (
    <>
      <p className="feed-heading">
        <span>
          {savedOnly ? p.savedItems : tabs.find((x) => x.id === tab)?.label}{' '}
          <span>
            · {when}, {today.slice(0, 4)}
          </span>
        </span>
        <span>
          <Clock3 aria-hidden="true" size={13} /> {p.byTime}
          <b>{savedOnly ? shown : total}</b>
        </span>
      </p>
      <ListShell
        queries={active}
        grid
        isEmpty={savedOnly && shown === 0}
        emptyHint={savedOnly ? p.savedEmpty : undefined}
      >
        {dealItems.map((d, i) => (
          <DealCard key={d.id} deal={d} index={i} />
        ))}
        {eventItems.map((e, i) => (
          <EventCard key={e.id} event={e} index={dealItems.length + i} />
        ))}
      </ListShell>
    </>
  )
}
