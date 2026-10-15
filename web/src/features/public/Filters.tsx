import { AnimatePresence, motion } from 'motion/react'
import { X } from 'lucide-react'
import type { ReactNode } from 'react'
import { p } from './strings'

/** Sticky filter bar under the header; the page scrolls beneath it. */
export function FilterBar({ children }: { children: ReactNode }) {
  return (
    <div className="sticky top-[72px] z-30 -mx-4 bg-paper/85 px-4 py-3 backdrop-blur-md md:top-20">
      <div className="card p-3 sm:p-4">
        <h2 className="sr-only">{p.filters}</h2>
        {children}
      </div>
    </div>
  )
}

export interface ActiveFilter {
  key: string
  label: string
  onClear: () => void
}

/** The filters currently in force, as removable chips. */
export function ActiveFilters({
  filters,
  onClearAll,
}: {
  filters: ActiveFilter[]
  onClearAll: () => void
}) {
  return (
    <div className="mt-4 flex min-h-[36px] flex-wrap items-center gap-2">
      <AnimatePresence initial={false}>
        {filters.map((f) => (
          <motion.span
            key={f.key}
            layout
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.9 }}
            transition={{ duration: 0.2, ease: [0.16, 1, 0.3, 1] }}
            className="chip-filter"
          >
            {f.label}
            <button
              type="button"
              onClick={f.onClear}
              aria-label={p.removeFilter(f.label)}
              className="grid size-6 place-items-center rounded-full transition-colors hover:bg-white/70"
            >
              <X aria-hidden="true" className="size-3.5" />
            </button>
          </motion.span>
        ))}
      </AnimatePresence>
      {filters.length > 1 && (
        <button
          type="button"
          onClick={onClearAll}
          className="text-xs font-bold text-muted underline underline-offset-4 hover:text-ink"
        >
          {p.clearFilters}
        </button>
      )}
    </div>
  )
}
