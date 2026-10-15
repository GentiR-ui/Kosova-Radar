import { useState, type FormEvent } from 'react'
import { motion } from 'motion/react'
import { ChevronRight, Search } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { flat, useSearch } from './api'
import { formatDate } from './format'
import ListShell from './ListShell'
import Intro, { Panel } from './Intro'
import { spring } from './motion'
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
      <Intro title={p.searchHeading} lead={p.searchLead}>
        <Panel>
          <form onSubmit={submit} role="search" className="flex flex-col gap-3 sm:flex-row">
            <label htmlFor="f-q" className="sr-only">
              {p.search}
            </label>
            <div className="relative flex-1">
              <Search
                aria-hidden="true"
                className="pointer-events-none absolute left-4 top-1/2 size-4 -translate-y-1/2 text-muted"
              />
              <input
                id="f-q"
                type="search"
                value={text}
                placeholder={p.searchPlaceholder}
                onChange={(e) => setText(e.target.value)}
                className="field pl-11"
              />
            </div>
            <button type="submit" className="btn btn-primary shrink-0 px-7">
              {p.search}
            </button>
          </form>
        </Panel>
      </Intro>
      <div className="page-width pt-6">
        {q.length < 2 ? (
          <p className="text-muted">{p.searchHint}</p>
        ) : (
          <ListShell queries={[results]}>
            <ul className="grid gap-3">
              {flat(results).map((r, i) => (
                <motion.li
                  key={`${r.kind}-${r.id}`}
                  initial={{ opacity: 0, y: 12 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ ...spring, delay: Math.min(i, 8) * 0.04 }}
                  className="card card-hover relative flex items-center gap-4 p-4 sm:p-5"
                >
                  <div className="min-w-0 flex-1">
                    <span className={`badge ${r.kind === 'event' ? 'cat-teater' : 'badge-soft'}`}>
                      {r.kind === 'event' ? p.kindEvent : p.kindDeal}
                    </span>
                    <p className="mt-2 text-base font-bold leading-snug sm:text-lg">
                      <Link
                        to={`/${r.kind === 'event' ? 'events' : 'deals'}/${r.id}`}
                        className="stretched rounded transition-colors hover:text-brand-text"
                      >
                        {r.title}
                      </Link>
                    </p>
                    <p className="mt-1 text-sm text-muted">
                      {[r.date && formatDate(r.date), r.cityName, r.sourceName]
                        .filter(Boolean)
                        .join(' · ')}
                    </p>
                  </div>
                  <ChevronRight aria-hidden="true" className="size-5 shrink-0 text-muted" />
                </motion.li>
              ))}
            </ul>
          </ListShell>
        )}
      </div>
    </>
  )
}
