import { useState, type FormEvent } from 'react'
import { motion } from 'motion/react'
import { ArrowUpRight, Search } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { flat, useSearch } from './api'
import { formatDate } from './format'
import ListShell from './ListShell'
import Intro, { Panel } from './Intro'
import { spring } from './motion'
import { container } from './PublicLayout'
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
                className="h-12 w-full rounded-sm border border-line bg-paper pl-10 pr-4 text-ink transition-colors placeholder:text-muted hover:border-muted focus:border-accent focus:outline-none"
              />
            </div>
            <button type="submit" className="btn btn-primary h-12 px-7">
              {p.search} <ArrowUpRight aria-hidden="true" className="size-4" />
            </button>
          </form>
        </Panel>
      </Intro>
      <div className={`${container} pt-4`}>
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
                    className={`group relative grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-4 rounded-md border bg-surface p-4 transition-colors sm:p-5 ${
                      r.kind === 'event'
                        ? 'border-event/30 hover:border-event'
                        : 'border-accent/30 hover:border-accent'
                    }`}
                  >
                    <span aria-hidden="true" className={`font-mono text-xs ${tone}`}>
                      {String(i + 1).padStart(2, '0')}
                    </span>
                    <div className="min-w-0">
                      <span
                        className={`inline-flex rounded-[3px] px-2 py-0.5 text-[11px] font-bold text-accent-ink ${
                          r.kind === 'event' ? 'bg-event' : 'bg-accent'
                        }`}
                      >
                        {r.kind === 'event' ? p.kindEvent : p.kindDeal}
                      </span>
                      <p className="mt-2 font-extrabold leading-snug sm:text-lg">
                        <Link
                          to={`/${r.kind === 'event' ? 'events' : 'deals'}/${r.id}`}
                          className="rounded-sm after:absolute after:inset-0 after:content-[''] hover:underline hover:underline-offset-4"
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
                    <ArrowUpRight
                      aria-hidden="true"
                      className={`size-5 transition-transform duration-200 group-hover:-translate-y-0.5 group-hover:translate-x-0.5 ${tone}`}
                    />
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
