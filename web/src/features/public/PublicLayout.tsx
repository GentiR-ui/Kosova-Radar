import { motion, useReducedMotion, useScroll, useSpring } from 'motion/react'
import { Link, NavLink, Outlet } from 'react-router'
import { t } from '../../lib/strings'

const nav = [
  { to: '/events', label: t.nav.events },
  { to: '/deals', label: t.nav.deals },
  { to: '/search', label: t.nav.search },
]

function RadarMark() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" className="size-7 text-accent">
      <circle cx="12" cy="12" r="10" fill="none" stroke="currentColor" strokeWidth="1.5" />
      <circle cx="12" cy="12" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
      <path d="M12 12 19 6" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      <circle cx="12" cy="12" r="1.7" fill="currentColor" />
    </svg>
  )
}

function ScrollProgress() {
  const { scrollYProgress } = useScroll()
  const scaleX = useSpring(scrollYProgress, { stiffness: 220, damping: 32, mass: 0.4 })
  return (
    <motion.div
      aria-hidden="true"
      style={{ scaleX }}
      className="absolute inset-x-0 bottom-0 h-0.5 origin-left bg-accent"
    />
  )
}

export default function PublicLayout() {
  const reduce = useReducedMotion()
  return (
    <div className="min-h-dvh">
      <header className="sticky top-0 z-40 border-b border-line bg-surface/85 backdrop-blur-md">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
          <Link
            to="/"
            className="flex shrink-0 items-center gap-2 whitespace-nowrap text-lg font-semibold tracking-tight"
          >
            <RadarMark />
            {t.brand}
          </Link>
          <nav aria-label={t.nav.label}>
            <ul className="flex gap-0.5 text-sm font-medium sm:gap-1">
              {nav.map((n) => (
                <li key={n.to}>
                  <NavLink
                    to={n.to}
                    className={({ isActive }) =>
                      `inline-flex min-h-11 items-center rounded-lg px-2 transition-colors sm:px-3 ${
                        isActive
                          ? 'bg-accent-soft text-accent'
                          : 'text-muted hover:bg-paper hover:text-ink'
                      }`
                    }
                  >
                    {n.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>
        {!reduce && <ScrollProgress />}
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8 sm:px-6 sm:py-12">
        <Outlet />
      </main>
    </div>
  )
}

/** Placeholder for public pages built in later tasks (T033–T035). */
export function PageTitle({ title }: { title: string }) {
  return <h1 className="text-3xl sm:text-4xl">{title}</h1>
}
