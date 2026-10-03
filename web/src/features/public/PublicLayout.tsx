import { Link, NavLink, Outlet } from 'react-router'
import { t } from '../../lib/strings'

const nav = [
  { to: '/events', label: t.nav.events },
  { to: '/deals', label: t.nav.deals },
  { to: '/search', label: t.nav.search },
]

export default function PublicLayout() {
  return (
    <div className="min-h-dvh">
      <header className="border-b border-line">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-5 sm:px-6">
          <Link to="/" className="font-display text-xl tracking-tight">
            {t.brand}
          </Link>
          <nav aria-label={t.nav.label}>
            <ul className="flex gap-4 text-sm sm:gap-6">
              {nav.map((n) => (
                <li key={n.to}>
                  <NavLink
                    to={n.to}
                    className={({ isActive }) =>
                      isActive
                        ? 'text-ink underline underline-offset-8'
                        : 'text-muted hover:text-ink'
                    }
                  >
                    {n.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-10 sm:px-6 sm:py-16">
        <Outlet />
      </main>
    </div>
  )
}

/** Placeholder for public pages built in later tasks (T033–T035). */
export function PageTitle({ title }: { title: string }) {
  return <h1 className="text-4xl sm:text-5xl">{title}</h1>
}
