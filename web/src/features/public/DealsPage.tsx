import { useSearchParams } from 'react-router'
import { flat, useCategories, useCities, useDeals } from './api'
import { Field, selectClass } from './Field'
import { DealCard } from './ItemCards'
import ListShell from './ListShell'
import Intro, { Panel } from './Intro'
import SearchInput from './SearchInput'
import { p } from './strings'

export default function DealsPage() {
  const [params, setParams] = useSearchParams()
  const filters = {
    city: params.get('city') ?? '',
    category: params.get('category') ?? '',
    search: params.get('search') ?? '',
  }
  const deals = useDeals(filters)
  const cities = useCities()
  const categories = useCategories('deal')

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
      <Intro eyebrow={p.eyebrow} title={p.dealsHeading} lead={p.dealsLead}>
        <Panel>
          <h2 className="sr-only">{p.filters}</h2>
          <div className="grid gap-4 sm:grid-cols-3">
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
            <Field label={p.searchDeals} htmlFor="f-search">
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
        <ListShell queries={[deals]} grid>
          {flat(deals).map((d, i) => (
            <DealCard key={d.id} deal={d} index={i} />
          ))}
        </ListShell>
      </div>
    </>
  )
}
