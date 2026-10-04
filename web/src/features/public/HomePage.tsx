import { useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { flat, useDeals, useEvents } from './api'
import { pristinaToday } from './format'
import { DealCard, EventCard } from './ItemCards'
import ListShell from './ListShell'
import SearchInput from './SearchInput'
import { p } from './strings'

const tabs = [
  { id: 'today', label: p.tabs.today },
  { id: 'events', label: p.tabs.events },
  { id: 'deals', label: p.tabs.deals },
] as const
type TabId = (typeof tabs)[number]['id']

const isTab = (v: string | null): v is TabId => tabs.some((x) => x.id === v)

export default function HomePage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = isTab(params.get('tab')) ? (params.get('tab') as TabId) : 'today'
  const search = params.get('search') ?? ''

  const update = (key: string, value: string) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value && !(key === 'tab' && value === 'today')) next.set(key, value)
        else next.delete(key)
        return next
      },
      { replace: true },
    )

  return (
    <>
      <h1 className="text-4xl sm:text-5xl">{t.pages.home}</h1>
      <div className="mt-8 max-w-md">
        <label htmlFor="home-search" className="mb-1 block text-sm text-muted">
          {p.search}
        </label>
        <SearchInput id="home-search" value={search} onCommit={(v) => update('search', v)} />
      </div>
      <div
        role="tablist"
        aria-label={p.tabs.label}
        className="mt-8 flex gap-6 border-b border-line"
      >
        {tabs.map((x) => (
          <button
            key={x.id}
            type="button"
            role="tab"
            id={`tab-${x.id}`}
            aria-selected={tab === x.id}
            aria-controls="feed-panel"
            tabIndex={tab === x.id ? 0 : -1}
            onClick={() => update('tab', x.id)}
            onKeyDown={(e) => {
              const step = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0
              if (!step) return
              const i = tabs.findIndex((y) => y.id === tab)
              const nextTab = tabs[(i + step + tabs.length) % tabs.length] ?? tabs[0]
              update('tab', nextTab.id)
              document.getElementById(`tab-${nextTab.id}`)?.focus()
            }}
            className={`-mb-px border-b-2 py-3 text-sm ${
              tab === x.id ? 'border-ink text-ink' : 'border-transparent text-muted hover:text-ink'
            }`}
          >
            {x.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id="feed-panel" aria-labelledby={`tab-${tab}`} className="mt-4">
        <Feed tab={tab} search={search} />
      </div>
    </>
  )
}

function Feed({ tab, search }: { tab: TabId; search: string }) {
  const today = pristinaToday()
  const dayFilter = tab === 'today' ? { from: today, to: today } : {}
  const events = useEvents({ search, ...dayFilter }, tab !== 'deals')
  const deals = useDeals({ search }, tab !== 'events')
  const active = tab === 'today' ? [events, deals] : tab === 'events' ? [events] : [deals]

  return (
    <ListShell queries={active}>
      {tab !== 'deals' && flat(events).map((e) => <EventCard key={e.id} event={e} />)}
      {tab !== 'events' && flat(deals).map((d) => <DealCard key={d.id} deal={d} />)}
    </ListShell>
  )
}
