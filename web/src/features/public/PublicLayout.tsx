import { MotionConfig, motion, useMotionValueEvent, useScroll, useSpring } from 'motion/react'
import { Radar, Search } from 'lucide-react'
import { useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import { t } from '../../lib/strings'
import RadarSignal from './RadarSignal'
import { p } from './strings'

const nav = [
  { to: '/events', label: t.nav.events },
  { to: '/deals', label: t.nav.deals },
  { to: '/search', label: t.nav.search, icon: true },
]

const year = new Date().getFullYear()

/** Page width and side gutters shared by every public section. */
export const container = 'mx-auto w-full max-w-[1120px] px-[18px] sm:px-8'

/** "kosovaradar." word mark. */
function Brand({ className = '' }: { className?: string }) {
  return (
    <span className={`whitespace-nowrap font-extrabold tracking-tight ${className}`}>
      kosova<span className="text-accent">radar.</span>
    </span>
  )
}

function ScrollProgress() {
  const { scrollYProgress } = useScroll()
  const scaleX = useSpring(scrollYProgress, { stiffness: 220, damping: 32, mass: 0.4 })
  return (
    <motion.div
      aria-hidden="true"
      style={{ scaleX }}
      className="absolute inset-x-0 -bottom-px h-0.5 origin-left bg-accent"
    />
  )
}

function Header() {
  const { scrollY } = useScroll()
  const [scrolled, setScrolled] = useState(false)
  useMotionValueEvent(scrollY, 'change', (y) => setScrolled(y > 8))
  return (
    // Solid background: the menu stays readable over any content while scrolling.
    <header
      className={`sticky top-0 z-40 border-b border-line bg-paper transition-shadow duration-300 ${
        scrolled ? 'shadow-[0_10px_30px_rgb(0_0_0/0.45)]' : ''
      }`}
    >
      <div className={`${container} flex h-[68px] items-center justify-between gap-3 sm:h-20`}>
        <Link to="/" aria-label={t.brand} className="flex min-w-0 items-center gap-2.5 rounded-sm">
          <RadarSignal className="w-9 sm:w-10" />
          <Brand className="text-lg max-[379px]:hidden sm:text-[23px]" />
        </Link>
        <div className="flex items-center gap-4">
          <span className="hidden items-center gap-2 text-sm text-muted lg:flex">
            <span className="live-dot" /> {p.liveTag}
          </span>
          <nav aria-label={t.nav.label}>
            <ul className="flex items-center gap-0.5 text-sm font-semibold sm:gap-1">
              {nav.map((n) => (
                <li key={n.to}>
                  <NavLink
                    to={n.to}
                    className={({ isActive }) =>
                      `relative isolate inline-flex min-h-11 min-w-11 items-center justify-center gap-1.5 whitespace-nowrap rounded-sm px-2 transition-colors duration-200 sm:px-3.5 ${
                        isActive ? 'text-accent-ink' : 'text-muted hover:text-ink'
                      }`
                    }
                  >
                    {({ isActive }) => (
                      <>
                        {isActive && (
                          <motion.span
                            layoutId="nav-indicator"
                            aria-hidden="true"
                            className="absolute inset-1 -z-10 rounded-sm bg-accent"
                            transition={{ type: 'spring', stiffness: 340, damping: 30 }}
                          />
                        )}
                        {n.icon && <Search aria-hidden="true" className="size-4" />}
                        <span className={n.icon ? 'max-sm:sr-only' : ''}>{n.label}</span>
                      </>
                    )}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </div>
      <ScrollProgress />
    </header>
  )
}

function Footer() {
  return (
    <footer className="mt-16 border-t border-line">
      <div
        className={`${container} flex flex-wrap items-center justify-between gap-4 py-6 text-xs text-muted`}
      >
        <Link to="/" className="flex items-center gap-2 rounded-sm text-base text-ink">
          <Radar aria-hidden="true" className="size-5 text-accent" />
          <Brand />
        </Link>
        <p className="max-sm:hidden">{p.footer}</p>
        <span>
          © {year} {t.brand}
        </span>
      </div>
    </footer>
  )
}

export default function PublicLayout() {
  return (
    // reducedMotion="user": transforms are skipped for visitors who prefer reduced motion.
    <MotionConfig reducedMotion="user">
      <div className="theme-radar flex min-h-dvh flex-col overflow-x-clip">
        <Header />
        <main className="flex-1">
          <Outlet />
        </main>
        <Footer />
      </div>
    </MotionConfig>
  )
}

/** Title-only page (not found). */
export function PageTitle({ title }: { title: string }) {
  return (
    <div className={`${container} flex flex-col items-center py-24 text-center`}>
      <RadarSignal className="w-24" />
      <h1 className="mt-8 text-3xl sm:text-4xl">{title}</h1>
      <Link to="/" className="btn btn-primary mt-8">
        {t.brand}
      </Link>
    </div>
  )
}
