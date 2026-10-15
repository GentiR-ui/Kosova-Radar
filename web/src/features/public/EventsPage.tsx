import { useSearchParams } from 'react-router'
import { flat, useCategories, useCities, useEvents } from './api'
import { Field, selectClass } from './Field'
import { EventCard } from './ItemCards'
import ListShell from './ListShell'
import Intro, { Panel } from './Intro'
import SearchInput from './SearchInput'
import { p } from './strings'

export default function EventsPage() {
  const [params, setParams] = useSearchParams()
  const filters = {
    city: params.get('city') ?? '',
    category: params.get('category') ?? '',
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    search: params.get('search') ?? '',
  }
  const events = useEvents(filters)
  const cities = useCities()
  const categories = useCategories('event')

  const set = (key: keyof typeof filters, value: string) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set(key, value)
        else next.delete(key)
        return next
      },
      { replace: true },
    )

  return (
    <>
      <Intro title={p.eventsHeading} lead={p.eventsLead}>
        <Panel>
          <h2 className="sr-only">{p.filters}</h2>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <Field label={p.city} htmlFor="f-city">
              <select
                id="f-city"
                className={selectClass}
                value={filters.city}
                onChange={(e) => set('city', e.target.value)}
              >
                <option value="">{p.allCities}</option>
                {cities.data?.map((c) => (
                  <option key={c.slug} value={c.slug}>
                    {c.name}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={p.category} htmlFor="f-category">
              <select
                id="f-category"
                className={selectClass}
                value={filters.category}
                onChange={(e) => set('category', e.target.value)}
              >
                <option value="">{p.allCategories}</option>
                {categories.data?.map((c) => (
                  <option key={c.slug} value={c.slug}>
                    {c.name}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={p.from} htmlFor="f-from">
              <input
                id="f-from"
                type="date"
                className={selectClass}
                value={filters.from}
                onChange={(e) => set('from', e.target.value)}
              />
            </Field>
            <Field label={p.to} htmlFor="f-to">
              <input
                id="f-to"
                type="date"
                className={selectClass}
                value={filters.to}
                onChange={(e) => set('to', e.target.value)}
              />
            </Field>
            <Field label={p.searchEvents} htmlFor="f-search">
              <SearchInput
                id="f-search"
                value={filters.search}
                onCommit={(v) => set('search', v)}
              />
            </Field>
          </div>
        </Panel>
      </Intro>
      <div className="page-width pt-4">
        <ListShell queries={[events]} grid>
          {flat(events).map((e, i) => (
            <EventCard key={e.id} event={e} index={i} />
          ))}
        </ListShell>
      </div>
    </>
  )
}
