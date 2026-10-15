import { AnimatePresence, MotionConfig, motion, useMotionValueEvent, useScroll } from 'motion/react'
import { Menu, Search, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate, useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { useCities } from './api'
import { BrandLogo, BrandMark } from './Brand'
import { p } from './strings'

const nav = [
  { to: '/', label: t.nav.home, end: true },
  { to: '/events', label: t.nav.events, end: false },
  { to: '/deals', label: t.nav.deals, end: false },
  { to: '/search', label: t.nav.search, end: false },
]

/** Pages that carry their own "Qyteti" filter; the header one would duplicate it. */
const ownCityFilter = ['/events', '/deals']
const year = new Date().getFullYear()

/** City picker in the header: sets `city` on the pages that filter by it. */
function CitySelect() {
  const cities = useCities()
  const [params, setParams] = useSearchParams()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const city = params.get('city') ?? ''

  const select = (slug: string) => {
    if (pathname !== '/') {
      navigate(slug ? `/?city=${slug}` : '/')
      return
    }
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (slug) next.set('city', slug)
        else next.delete('city')
        return next
      },
      { replace: true },
    )
  }

  return (
    <label className="relative hidden items-center lg:flex">
      <span className="sr-only">{p.city}</span>
      <select
        value={city}
        onChange={(e) => select(e.target.value)}
        className="min-h-11 cursor-pointer rounded-xl border border-white/20 bg-white/5 px-3 text-sm font-bold text-on-dark transition-colors hover:border-white/50 focus:border-brand focus:outline-none"
      >
        <option value="">{p.allCities}</option>
        {cities.data?.map((c) => (
          <option key={c.slug} value={c.slug}>
            {c.name}
          </option>
        ))}
      </select>
    </label>
  )
}

/** Full-width menu sheet for phones: Escape closes it, Tab stays inside, focus goes back to the button. */
function MenuSheet({ onClose }: { onClose: () => void }) {
  const sheet = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const first = sheet.current?.querySelector<HTMLElement>('a, button')
    first?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose()
        return
      }
      if (e.key !== 'Tab' || !sheet.current) return
      const items = sheet.current.querySelectorAll<HTMLElement>('a, button')
      if (items.length === 0) return
      const start = items[0]!
      const end = items[items.length - 1]!
      if (e.shiftKey && document.activeElement === start) {
        e.preventDefault()
        end.focus()
      } else if (!e.shiftKey && document.activeElement === end) {
        e.preventDefault()
        start.focus()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <motion.div
      ref={sheet}
      role="dialog"
      aria-modal="true"
      aria-label={t.nav.label}
      initial={{ opacity: 0, y: -12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -12 }}
      transition={{ duration: 0.25, ease: [0.16, 1, 0.3, 1] }}
      className="on-charcoal absolute inset-x-0 top-full border-t border-white/10 shadow-lift lg:hidden"
    >
      <ul className="page-width flex flex-col py-2">
        {nav.map((n) => (
          <li key={n.to}>
            <NavLink
              to={n.to}
              end={n.end}
              onClick={onClose}
              className={({ isActive }) =>
                `flex min-h-14 items-center text-base font-bold ${
                  isActive ? 'text-brand-on-dark' : 'text-on-dark'
                }`
              }
            >
              {n.label}
            </NavLink>
          </li>
        ))}
        <li>
          <button
            type="button"
            onClick={onClose}
            className="flex min-h-14 items-center gap-2 text-base font-bold text-on-dark-muted"
          >
            <X aria-hidden="true" className="size-5" /> {p.closeMenu}
          </button>
        </li>
      </ul>
    </motion.div>
  )
}

function Header() {
  const { scrollY } = useScroll()
  const [scrolled, setScrolled] = useState(false)
  const { pathname } = useLocation()
  // The sheet remembers the route it was opened on, so navigating closes it without an effect.
  const [openedAt, setOpenedAt] = useState<string | null>(null)
  const open = openedAt === pathname
  const menuButton = useRef<HTMLButtonElement>(null)
  useMotionValueEvent(scrollY, 'change', (y) => setScrolled(y > 8))

  const close = () => {
    setOpenedAt(null)
    menuButton.current?.focus()
  }

  return (
    // Solid charcoal at every scroll position: the menu never disappears over the content below.
    <header
      className={`on-charcoal sticky top-0 z-50 transition-shadow duration-300 ${
        scrolled ? 'shadow-[0_10px_30px_rgb(0_0_0/0.35)]' : ''
      }`}
    >
      <div
        className={`page-width flex items-center justify-between gap-3 transition-[height] duration-300 ${
          scrolled ? 'h-16' : 'h-[72px] md:h-20'
        }`}
      >
        <Link to="/" className="flex shrink-0 items-center rounded">
          <BrandLogo className="hidden h-7 min-[380px]:block md:h-8" />
          <BrandMark className="h-8 min-[380px]:hidden" decorative={false} />
        </Link>

        <nav aria-label={t.nav.label} className="hidden lg:block">
          <ul className="flex items-center gap-1">
            {nav.map((n) => (
              <li key={n.to}>
                <NavLink
                  to={n.to}
                  end={n.end}
                  className={({ isActive }) =>
                    `relative inline-flex min-h-11 items-center rounded-lg px-3 text-sm font-bold transition-colors ${
                      isActive ? 'text-on-dark' : 'text-on-dark-muted hover:text-on-dark'
                    }`
                  }
                >
                  {({ isActive }) => (
                    <>
                      {n.label}
                      {isActive && (
                        <motion.span
                          layoutId="nav-underline"
                          aria-hidden="true"
                          className="absolute inset-x-3 -bottom-0.5 h-0.5 rounded-full bg-brand"
                          transition={{ type: 'spring', stiffness: 380, damping: 32 }}
                        />
                      )}
                    </>
                  )}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>

        <div className="flex items-center gap-1 sm:gap-2">
          {!ownCityFilter.includes(pathname) && <CitySelect />}
          <Link
            to="/search"
            aria-label={t.nav.search}
            className="btn-icon text-on-dark-muted hover:bg-white/10 hover:text-on-dark"
          >
            <Search aria-hidden="true" className="size-5" />
          </Link>
          <button
            ref={menuButton}
            type="button"
            aria-expanded={open}
            aria-label={open ? p.closeMenu : p.openMenu}
            onClick={() => (open ? close() : setOpenedAt(pathname))}
            className="btn-icon text-on-dark hover:bg-white/10 lg:hidden"
          >
            {open ? (
              <X aria-hidden="true" className="size-6" />
            ) : (
              <Menu aria-hidden="true" className="size-6" />
            )}
          </button>
        </div>
      </div>
      <AnimatePresence>{open && <MenuSheet onClose={close} />}</AnimatePresence>
    </header>
  )
}

function Footer() {
  return (
    <footer className="on-charcoal relative mt-20 overflow-hidden">
      <div className="page-width relative grid gap-8 py-12 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-start">
        <div>
          <BrandLogo className="h-8" />
          <p className="mt-4 max-w-sm text-sm leading-relaxed text-on-dark-muted">{p.footerLead}</p>
        </div>
        <nav aria-label={p.footerNav}>
          <ul className="flex flex-wrap gap-2">
            {nav.map((n) => (
              <li key={n.to}>
                <Link
                  to={n.to}
                  className="inline-flex min-h-11 items-center rounded-xl border border-white/15 px-4 text-sm font-bold text-on-dark-muted transition-colors hover:border-brand hover:text-on-dark"
                >
                  {n.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </div>
      <div className="border-t border-white/10">
        <p className="page-width py-5 text-xs text-on-dark-muted">
          © {year} {t.brand} · {p.footer}
        </p>
      </div>
    </footer>
  )
}

export default function PublicLayout() {
  const { pathname } = useLocation()
  return (
    // reducedMotion="user": transforms are skipped for visitors who prefer reduced motion.
    <MotionConfig reducedMotion="user">
      <div className="theme-radar flex min-h-dvh flex-col overflow-x-clip">
        <Header />
        <main className="flex-1">
          <AnimatePresence mode="wait" initial={false}>
            <motion.div
              key={pathname}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.15 }}
            >
              <Outlet />
            </motion.div>
          </AnimatePresence>
        </main>
        <Footer />
      </div>
    </MotionConfig>
  )
}

/** Title-only page (not found). */
export function PageTitle({ title }: { title: string }) {
  return (
    <div className="page-width flex flex-col items-center py-24 text-center">
      <BrandMark className="h-16" />
      <h1 className="mt-8 text-3xl sm:text-4xl">{title}</h1>
      <Link to="/" className="btn btn-primary mt-8">
        {p.backHome}
      </Link>
    </div>
  )
}
