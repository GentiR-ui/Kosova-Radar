import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { flat, useSearch } from './api'
import { Field, selectClass } from './Field'
import { formatDate } from './format'
import ListShell from './ListShell'
import { p } from './strings'

export default function SearchPage() {
  const [params, setParams] = useSearchParams()
  const q = (params.get('q') ?? '').trim()
  const [text, setText] = useState(q)
  const results = useSearch(q)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const value = text.trim()
    setParams(value ? { q: value } : {})
  }

  return (
    <>
      <h1 className="text-4xl sm:text-5xl">{p.searchHeading}</h1>
      <form onSubmit={submit} role="search" className="mt-8 flex items-end gap-3">
        <div className="flex-1">
          <Field label={p.search} htmlFor="f-q">
            <input
              id="f-q"
              type="search"
              value={text}
              onChange={(e) => setText(e.target.value)}
              className={selectClass}
            />
          </Field>
        </div>
        <button
          type="submit"
          className="rounded-sm border border-ink px-5 py-2.5 text-sm hover:bg-ink hover:text-paper"
        >
          {p.search}
        </button>
      </form>
      <div className="mt-6">
        {q.length < 2 ? (
          <p className="text-muted">{p.searchHint}</p>
        ) : (
          <ListShell queries={[results]}>
            <ul>
              {flat(results).map((r) => (
                <li key={`${r.kind}-${r.id}`} className="border-t border-line py-4 first:border-t-0">
                  <span className="mr-3 text-xs uppercase tracking-wide text-muted">
                    {r.kind === 'event' ? p.kindEvent : p.kindDeal}
                  </span>
                  <Link
                    to={`/${r.kind === 'event' ? 'events' : 'deals'}/${r.id}`}
                    className="underline-offset-4 hover:underline"
                  >
                    {r.title}
                  </Link>
                  <p className="mt-1 text-sm text-muted">
                    {[r.date && formatDate(r.date), r.cityName, r.sourceName]
                      .filter(Boolean)
                      .join(' · ')}
                  </p>
                </li>
              ))}
            </ul>
          </ListShell>
        )}
      </div>
    </>
  )
}
