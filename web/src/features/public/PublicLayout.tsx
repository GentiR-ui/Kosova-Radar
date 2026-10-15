import { MotionConfig } from 'motion/react'
import { CalendarDays, Radar, Search, Sun, Tag } from 'lucide-react'
import { Link, NavLink, Outlet, useLocation, useNavigate, useSearchParams } from 'react-router'
import { t } from '../../lib/strings'
import { useCities } from './api'
import logo from '../../assets/kosova-radar-logo.jpg'
import { dayAndMonth, pristinaToday } from './format'
import LogoMark from './Logo'
import { p } from './strings'

const nav = [
  { to: '/', label: t.nav.home, Icon: Radar, end: true },
  { to: '/events', label: t.nav.events, Icon: CalendarDays, end: false },
  { to: '/deals', label: t.nav.deals, Icon: Tag, end: false },
]

const year = new Date().getFullYear()

/** Pages whose list reacts to the `city` query parameter. */
const filtered = ['/', '/events', '/deals']

/** The brand logo plate, exactly as in the logo file, with a radar ping over the mark. */
function BrandLogo() {
  return (
    <Link to="/" className="official-logo" aria-label={t.brand}>
      <img src={logo} alt={p.logoAlt} width={1097} height={290} />
      <span aria-hidden="true" className="logo-pulse" />
    </Link>
  )
}

/** City picker in the header: sets `city` on a list page, otherwise opens the home list for that city. */
function CityWidget() {
  const cities = useCities()
  const [params, setParams] = useSearchParams()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const city = params.get('city') ?? ''
  const name = cities.data?.find((c) => c.slug === city)?.name ?? p.allKosovo
  const today = pristinaToday()
  const { day, month } = dayAndMonth(`${today}T12:00:00Z`)

  const select = (slug: string) => {
    if (!filtered.includes(pathname)) {
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
    <label className="weather-widget">
      <Sun aria-hidden="true" className="weather-sun" />
      <span className="weather-copy">
        <span className="sr-only">{p.location}</span>
        <strong className="truncate">{name}</strong>
        <span>{`${Number(day)} ${p.months[month] ?? ''}`}</span>
      </span>
      <select
        value={city}
        aria-label={p.location}
        onChange={(e) => select(e.target.value)}
        className="absolute inset-0 size-full cursor-pointer opacity-0"
      >
        <option value="">{p.allKosovo}</option>
        {cities.data?.map((c) => (
          <option key={c.slug} value={c.slug}>
            {c.name}
          </option>
        ))}
      </select>
    </label>
  )
}

function Header() {
  return (
    <header className="site-header sticky top-0 z-40">
      <div className="page-width header-inner">
        <BrandLogo />
        <nav aria-label={t.nav.label} className="header-nav">
          {nav.map((n) => (
            <NavLink key={n.to} to={n.to} end={n.end}>
              <n.Icon aria-hidden="true" size={16} />
              {n.label}
            </NavLink>
          ))}
          <NavLink to="/search">
            <Search aria-hidden="true" size={16} />
            {t.nav.search}
          </NavLink>
        </nav>
        <div className="flex items-center gap-2">
          <Link
            to="/search"
            aria-label={t.nav.search}
            className="header-search size-11 place-items-center rounded-full text-muted transition-colors hover:text-ink"
          >
            <Search aria-hidden="true" size={18} />
          </Link>
          <CityWidget />
        </div>
      </div>
    </header>
  )
}

function Footer() {
  return (
    <footer className="site-footer">
      <div className="page-width footer-inner">
        <Link to="/">
          KOSOVA <strong>RADAR</strong>
        </Link>
        <p>{p.footer}</p>
        <small>
          © {year} {t.brand}
        </small>
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
    <div className="page-width flex flex-col items-center py-24 text-center">
      <LogoMark className="w-16" />
      <h1 className="mt-8 text-3xl sm:text-4xl">{title}</h1>
      <Link to="/" className="btn btn-primary mt-8">
        {p.backHome}
      </Link>
    </div>
  )
}
