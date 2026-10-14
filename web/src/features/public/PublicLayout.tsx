import { MotionConfig, motion, useMotionValueEvent, useScroll, useSpring } from 'motion/react'
import { CalendarDays, Home, Search, Tag } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { t } from '../../lib/strings'
import Logo, { LogoMark } from './Logo'
import { spring } from './motion'
import { p } from './strings'

const nav = [
  { to: '/', label: t.nav.home, icon: Home, end: true },
  { to: '/events', label: t.nav.events, icon: CalendarDays, end: false },
  { to: '/deals', label: t.nav.deals, icon: Tag, end: false },
]

const year = new Date().getFullYear()

/** Page width and side gutters shared by every public section. */
export const container = 'mx-auto w-full max-w-[1200px] px-5 sm:px-8'

function ScrollProgress() {
  const { scrollYProgress } = useScroll()
  const scaleX = useSpring(scrollYProgress, { stiffness: 220, damping: 32, mass: 0.4 })
  return (
    <motion.div
      aria-hidden="true"
      style={{ scaleX }}
      className="absolute inset-x-0 -bottom-px h-0.5 origin-left bg-gradient-to-r from-brand-2 via-brand to-brand-3"
    />
  )
}

/** Header search: goes to /search. Its label never contains "Kërko", so the search page keeps one searchbox. */
function QuickSearch() {
  const navigate = useNavigate()
  const [text, setText] = useState('')
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = text.trim()
    if (q) navigate(`/search?q=${encodeURIComponent(q)}`)
  }
  return (
    <form role="search" aria-label={p.quickSearch} onSubmit={submit} className="hidden lg:block">
      <div className="relative">
        <Search
          aria-hidden="true"
          className="pointer-events-none absolute left-4 top-1/2 size-4 -translate-y-1/2 text-muted"
        />
        <input
          type="search"
          value={text}
          aria-label={p.quickSearch}
          placeholder={p.quickSearchPlaceholder}
          onChange={(e) => setText(e.target.value)}
          className="h-11 w-56 rounded-full border border-line bg-soft pl-10 pr-4 text-sm transition-[width,border-color,background-color] duration-300 placeholder:text-muted hover:border-accent/40 focus:w-64 focus:border-accent focus:bg-surface focus:outline-none"
        />
      </div>
    </form>
  )
}

function Header() {
  const { scrollY } = useScroll()
  const [scrolled, setScrolled] = useState(false)
  useMotionValueEvent(scrollY, 'change', (y) => setScrolled(y > 8))
  return (
    // Solid background: the menu stays readable over any content while scrolling.
    <header
      className={`sticky top-0 z-40 border-b bg-paper/95 backdrop-blur-md transition-[box-shadow,border-color] duration-300 ${
        scrolled ? 'border-line shadow-[0_8px_30px_rgb(11_26_22/0.07)]' : 'border-transparent'
      }`}
    >
      <div className={`${container} flex h-[68px] items-center gap-2 sm:h-20 sm:gap-4`}>
        <Link to="/" aria-label={t.brand} className="rounded-xl">
          <Logo markClass="w-8 sm:w-11" compact />
        </Link>
        <nav aria-label={t.nav.label} className="ml-auto">
          <ul className="flex items-center gap-0.5 text-sm font-bold sm:gap-1">
            {nav.map((n) => (
              // The logo already links home, so "Ballina" makes room for the icons on phones.
              <li key={n.to} className={n.end ? 'max-sm:hidden' : ''}>
                <NavLink
                  to={n.to}
                  end={n.end}
                  className={({ isActive }) =>
                    `relative isolate inline-flex min-h-11 min-w-11 items-center justify-center gap-2 whitespace-nowrap rounded-full px-2.5 transition-colors duration-200 sm:px-4 ${
                      isActive ? 'text-accent' : 'text-muted hover:text-ink'
                    }`
                  }
                >
                  {({ isActive }) => (
                    <>
                      {isActive && (
                        <motion.span
                          layoutId="nav-indicator"
                          aria-hidden="true"
                          className="absolute inset-0 -z-10 rounded-full bg-accent-soft"
                          transition={spring}
                        />
                      )}
                      <n.icon aria-hidden="true" className="size-[18px]" />
                      <span className="max-sm:sr-only">{n.label}</span>
                    </>
                  )}
                </NavLink>
              </li>
            ))}
            <li className="lg:hidden">
              <Link
                to="/search"
                aria-label={t.nav.search}
                className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-full text-muted transition-colors hover:text-ink"
              >
                <Search aria-hidden="true" className="size-[18px]" />
              </Link>
            </li>
          </ul>
        </nav>
        <QuickSearch />
      </div>
      <ScrollProgress />
    </header>
  )
}

function Footer() {
  return (
    <footer className="relative mt-20 overflow-hidden bg-night text-white">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -right-24 -top-24 size-[28rem] rounded-full bg-[radial-gradient(circle,rgb(16_185_129/0.22),transparent_65%)]"
      />
      <div className={`${container} relative py-14`}>
        <div className="flex flex-col items-start justify-between gap-10 sm:flex-row sm:items-center">
          <div>
            {/* The brand mark gets its own moment here: it floats while the radar waves pulse. */}
            <motion.div
              animate={{ y: [0, -6, 0] }}
              transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
              className="[&_.logo-tagline]:text-white/55"
            >
              <Logo markClass="w-14 sm:w-16" className="text-white" />
            </motion.div>
            <p className="mt-5 max-w-xs text-sm leading-relaxed text-white/60">{p.footerLead}</p>
          </div>
          <nav aria-label={p.footerNav}>
            <ul className="flex flex-wrap gap-2 text-sm font-bold">
              {[...nav, { to: '/search', label: t.nav.search }].map((n) => (
                <li key={n.to}>
                  <Link
                    to={n.to}
                    className="inline-flex min-h-11 items-center rounded-full border border-white/15 px-5 text-white/75 transition-colors hover:border-brand/60 hover:text-white"
                  >
                    {n.label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </div>
      <div className="relative border-t border-white/10">
        <p className={`${container} py-5 text-xs text-white/50`}>
          © {year} {t.brand} · {p.footer}
        </p>
      </div>
    </footer>
  )
}

export default function PublicLayout() {
  return (
    // reducedMotion="user": transforms are skipped for visitors who prefer reduced motion.
    <MotionConfig reducedMotion="user">
      <div className="flex min-h-dvh flex-col overflow-x-clip bg-paper">
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
      <LogoMark className="w-20" />
      <h1 className="mt-8 text-3xl sm:text-4xl">{title}</h1>
      <Link to="/" className="btn btn-primary mt-8">
        {p.backHome}
      </Link>
    </div>
  )
}
