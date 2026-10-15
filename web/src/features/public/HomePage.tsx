import { motion } from 'motion/react'
import { useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { flat, useDeals, useEvents } from './api'
import { addDays, pristinaToday } from './format'
import { DealCard, EventCard } from './ItemCards'
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

export default function HomePage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = isTab(params.get('tab')) ? (params.get('tab') as TabId) : 'today'
  const search = params.get('search') ?? ''
  const city = params.get('city') ?? ''
  const day = tab === 'events' ? (params.get('day') ?? '') : ''
  const today = pristinaToday()

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

  return (
    <div className="page-width py-10">
      <h1 className="text-[40px] leading-[1.05] sm:text-5xl">{t.pages.home}</h1>
      <p className="mt-4 max-w-xl text-base text-muted">{p.heroLead}</p>

      <div role="tablist" aria-label={p.tabs.label} className="tabs mt-8">
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

      {tab === 'events' && (
        <div className="mt-5 flex flex-wrap gap-2">
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

      <div role="tabpanel" id="feed-panel" aria-labelledby={`tab-${tab}`} className="mt-6">
        <Feed tab={tab} search={search} city={city} day={day} today={today} />
      </div>
    </div>
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

  return (
    <ListShell queries={active} grid>
      {dealItems.map((d, i) => (
        <DealCard key={d.id} deal={d} index={i} />
      ))}
      {eventItems.map((e, i) => (
        <EventCard key={e.id} event={e} index={dealItems.length + i} />
      ))}
    </ListShell>
  )
}
