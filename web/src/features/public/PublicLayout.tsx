import * as Dialog from '@radix-ui/react-dialog'
import { AnimatePresence, MotionConfig, motion, useMotionValueEvent, useScroll } from 'motion/react'
import { MapPin, Menu, Search, X } from 'lucide-react'
import { useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate, useSearchParams } from 'react-router'
import { cn } from '../../lib/cn'
import { t } from '../../lib/strings'
import { useCities } from './api'
import { BrandLogo } from './Brand'
import { p } from './strings'

const nav = [
  { to: '/', label: t.nav.home, end: true },
  { to: '/events', label: t.nav.events, end: false },
  { to: '/deals', label: t.nav.deals, end: false },
  { to: '/search', label: t.nav.search, end: false },
]

/** Pages that carry their own "Qyteti" filter; a second city control would duplicate it. */
const ownCityFilter = ['/events', '/deals']
const year = new Date().getFullYear()

/** City picker in the header. Its label never contains "Qyteti", which belongs to the page filters. */
function CitySelect({ className }: { className?: string }) {
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
    <label className={cn('relative flex items-center', className)}>
      <span className="sr-only">{p.pickCity}</span>
      <MapPin
        aria-hidden="true"
        className="pointer-events-none absolute left-4 size-4 text-violet"
      />
      <select
        value={city}
        onChange={(e) => select(e.target.value)}
        className="field w-full pl-10 text-sm font-bold sm:w-[200px]"
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

/** Phone menu: a Radix dialog sheet (focus trap, Escape and focus return come from the primitive). */
function MenuSheet() {
  const [open, setOpen] = useState(false)
  const { pathname } = useLocation()
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger asChild>
        <button
          type="button"
          aria-label={p.openMenu}
          className="btn-icon bg-soft text-ink lg:hidden"
        >
          <Menu aria-hidden="true" className="size-5" />
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-hero/40 backdrop-blur-sm data-[state=open]:animate-in data-[state=open]:fade-in" />
        <Dialog.Content
          className="fixed inset-y-0 right-0 z-50 flex w-[min(88vw,22rem)] flex-col gap-6 bg-surface p-6 shadow-lift focus:outline-none"
          aria-describedby={undefined}
        >
          <div className="flex items-center justify-between">
            <Dialog.Title className="text-lg">{t.nav.label}</Dialog.Title>
            <Dialog.Close asChild>
              <button type="button" aria-label={p.closeMenu} className="btn-icon bg-soft text-ink">
                <X aria-hidden="true" className="size-5" />
              </button>
            </Dialog.Close>
          </div>
          <nav aria-label={t.nav.label}>
            <ul className="flex flex-col">
              {nav.map((n) => (
                <li key={n.to}>
                  <NavLink
                    to={n.to}
                    end={n.end}
                    onClick={() => setOpen(false)}
                    className={({ isActive }) =>
                      cn(
                        'flex min-h-14 items-center border-b border-line text-base font-bold',
                        isActive ? 'text-violet' : 'text-ink',
                      )
                    }
                  >
                    {n.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
          {!ownCityFilter.includes(pathname) && <CitySelect className="w-full" />}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function Header() {
  const { scrollY } = useScroll()
  const [scrolled, setScrolled] = useState(false)
  const { pathname } = useLocation()
  useMotionValueEvent(scrollY, 'change', (y) => setScrolled(y > 8))

  return (
    <header
      className={cn(
        'sticky top-0 z-40 transition-[background-color,box-shadow,backdrop-filter] duration-300',
        scrolled ? 'bg-surface/90 shadow-card backdrop-blur-md' : 'bg-surface',
      )}
    >
      <div className="page-width flex h-[72px] items-center justify-between gap-3 md:h-20">
        <Link to="/" className="flex shrink-0 items-center rounded-lg">
          <span className="flex flex-col">
            <BrandLogo className="h-8 md:h-9" />
            <span className="mt-1 hidden text-[11px] font-semibold text-muted sm:block">
              {p.tagline}
            </span>
          </span>
        </Link>

        <nav aria-label={t.nav.label} className="hidden lg:block">
          <ul className="flex items-center gap-1">
            {nav.map((n) => (
              <li key={n.to}>
                <NavLink
                  to={n.to}
                  end={n.end}
                  className={({ isActive }) =>
                    cn(
                      'relative inline-flex min-h-11 items-center rounded-lg px-4 text-[15px] font-semibold transition-colors',
                      isActive ? 'text-violet' : 'text-ink hover:text-violet',
                    )
                  }
                >
                  {({ isActive }) => (
                    <>
                      {n.label}
                      {isActive && (
                        <motion.span
                          layoutId="nav-underline"
                          aria-hidden="true"
                          className="absolute inset-x-4 bottom-1 h-0.5 rounded-full bg-violet"
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

        <div className="flex items-center gap-2">
          {!ownCityFilter.includes(pathname) && <CitySelect className="hidden md:flex" />}
          <Link
            to="/search"
            aria-label={t.nav.search}
            className="btn-icon bg-soft text-ink hover:bg-violet-soft hover:text-violet"
          >
            <Search aria-hidden="true" className="size-5" />
          </Link>
          <MenuSheet />
        </div>
      </div>
    </header>
  )
}

function Footer() {
  return (
    <footer className="on-hero relative mt-20 overflow-hidden">
      <div className="page-width relative grid gap-8 py-14 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-start">
        <div>
          <BrandLogo tone="dark" className="h-9" />
          <p className="mt-4 max-w-sm text-sm leading-relaxed text-on-dark-muted">{p.footerLead}</p>
        </div>
        <nav aria-label={p.footerNav}>
          <ul className="flex flex-wrap gap-2">
            {nav.map((n) => (
              <li key={n.to}>
                <Link
                  to={n.to}
                  className="inline-flex min-h-11 items-center rounded-full border border-white/15 px-5 text-sm font-bold text-on-dark-muted transition-colors hover:border-white/50 hover:text-on-dark"
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
      <span className="tile tile-violet size-16">
        <Search aria-hidden="true" className="size-7" />
      </span>
      <h1 className="mt-8 text-3xl sm:text-4xl">{title}</h1>
      <Link to="/" className="btn btn-primary mt-8">
        {p.backHome}
      </Link>
    </div>
  )
}
