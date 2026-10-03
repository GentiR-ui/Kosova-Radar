import { Navigate, NavLink, Outlet, useNavigate } from 'react-router'
import { t } from '../../lib/strings'
import { useLogout, useMe, type Role } from '../auth/api'

const s = t.admin

const links: { to: string; label: string; roles: Role[] }[] = [
  { to: '/admin', label: s.nav.dashboard, roles: ['admin', 'reviewer'] },
  { to: '/admin/review', label: s.nav.review, roles: ['admin', 'reviewer'] },
  { to: '/admin/published', label: s.nav.published, roles: ['admin', 'reviewer'] },
  { to: '/admin/sources', label: s.nav.sources, roles: ['admin'] },
  { to: '/admin/users', label: s.nav.users, roles: ['admin'] },
]

/** Signed-in area. Signed-out visitors (and sessions that end with a 401 anywhere) go to the login page. */
export default function AdminLayout() {
  const me = useMe()
  const logout = useLogout()
  const navigate = useNavigate()

  if (me.isPending)
    return (
      <p role="status" className="p-6 text-muted">
        {t.loading}
      </p>
    )
  if (me.isError)
    return (
      <div role="alert" className="space-y-3 p-6">
        <p>{t.error}</p>
        <button
          type="button"
          onClick={() => void me.refetch()}
          className="rounded-md border border-line px-3 py-1.5"
        >
          {t.retry}
        </button>
      </div>
    )
  if (!me.data) return <Navigate to="/admin/login" replace />
  const role = me.data.role

  const onLogout = () =>
    logout.mutate(undefined, { onSuccess: () => navigate('/admin/login', { replace: true }) })

  return (
    <div className="min-h-dvh">
      <header className="border-b border-line bg-surface">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-3 px-4 py-4 sm:px-6">
          <span className="font-display text-lg">{t.brand}</span>
          <nav aria-label={s.nav.label} className="order-3 w-full sm:order-none sm:w-auto">
            <ul className="flex flex-wrap gap-x-4 gap-y-2 text-sm">
              {links
                .filter((l) => l.roles.includes(role))
                .map((l) => (
                  <li key={l.to}>
                    <NavLink
                      to={l.to}
                      end={l.to === '/admin'}
                      className={({ isActive }) =>
                        isActive ? 'font-semibold text-accent' : 'text-muted hover:text-ink'
                      }
                    >
                      {l.label}
                    </NavLink>
                  </li>
                ))}
            </ul>
          </nav>
          <button
            type="button"
            onClick={onLogout}
            disabled={logout.isPending}
            className="ml-auto rounded-md border border-line px-3 py-1.5 text-sm hover:border-ink"
          >
            {s.logout}
          </button>
        </div>
        {logout.isError && (
          <p role="alert" className="mx-auto max-w-6xl px-4 pb-3 text-sm text-danger sm:px-6">
            {s.logoutFailed}
          </p>
        )}
      </header>
      <main className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <Outlet />
      </main>
    </div>
  )
}

/** Wraps Admin-only pages: Reviewers see a clear message instead of the page. */
export function RequireAdmin({ children }: { children: React.ReactNode }) {
  const me = useMe()
  if (me.data?.role !== 'admin')
    return (
      <p role="alert" className="text-lg">
        {t.forbidden}
      </p>
    )
  return children
}
