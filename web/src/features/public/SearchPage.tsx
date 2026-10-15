import { useState, type FormEvent } from 'react'
import { motion } from 'motion/react'
import { ArrowUpRight, Search } from 'lucide-react'
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
      <Intro eyebrow={p.eyebrow} title={p.searchHeading} lead={p.searchLead}>
        <Panel>
          <form onSubmit={submit} role="search" className="flex flex-col gap-3 sm:flex-row">
            <label htmlFor="f-q" className="sr-only">
              {p.search}
            </label>
            <div className="relative flex-1">
              <Search
                aria-hidden="true"
                className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted"
              />
              <input
                id="f-q"
                type="search"
                value={text}
                placeholder={p.searchPlaceholder}
                onChange={(e) => setText(e.target.value)}
                className="h-12 w-full rounded-full border border-line bg-soft pl-10 pr-4 text-ink transition-[border-color,background-color] placeholder:text-muted hover:border-accent/40 focus:border-accent focus:bg-surface focus:outline-none"
              />
            </div>
            <button type="submit" className="btn btn-primary h-12 shrink-0 px-7">
              {p.search} <ArrowUpRight aria-hidden="true" className="size-4" />
            </button>
          </form>
        </Panel>
      </Intro>
      <div className="page-width pt-4">
        {q.length < 2 ? (
          <p className="text-muted">{p.searchHint}</p>
        ) : (
          <ListShell queries={[results]}>
            <ul className="grid gap-3">
              {flat(results).map((r, i) => {
                const tone = r.kind === 'event' ? 'text-event' : 'text-accent'
                return (
                  <motion.li
                    key={`${r.kind}-${r.id}`}
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ ...spring, delay: Math.min(i, 8) * 0.045 }}
                    className={`card group relative grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-4 p-4 transition-[box-shadow,translate,border-color] duration-300 hover:-translate-y-0.5 hover:shadow-lift sm:p-5 ${
                      r.kind === 'event' ? 'hover:border-event/50' : 'hover:border-accent/50'
                    }`}
                  >
                    <span aria-hidden="true" className={`font-mono text-xs ${tone}`}>
                      {String(i + 1).padStart(2, '0')}
                    </span>
                    <div className="min-w-0">
                      <span
                        className={`chip text-[11px] ${
                          r.kind === 'event'
                            ? 'bg-event-soft text-event'
                            : 'bg-accent-soft text-accent'
                        }`}
                      >
                        {r.kind === 'event' ? p.kindEvent : p.kindDeal}
                      </span>
                      <p className="mt-2 font-extrabold leading-snug sm:text-lg">
                        <Link
                          to={`/${r.kind === 'event' ? 'events' : 'deals'}/${r.id}`}
                          className="rounded after:absolute after:inset-0 after:content-[''] group-hover:text-accent"
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
                    <span
                      aria-hidden="true"
                      className={`grid size-9 shrink-0 place-items-center rounded-full transition-[background-color,color,transform] duration-300 group-hover:translate-x-0.5 group-hover:text-white ${
                        r.kind === 'event'
                          ? 'bg-event-soft text-event group-hover:bg-event'
                          : 'bg-accent-soft text-accent group-hover:bg-accent'
                      }`}
                    >
                      <ArrowUpRight className="size-4" />
                    </span>
                  </motion.li>
                )
              })}
            </ul>
          </ListShell>
        )}
      </div>
    </>
  )
}
