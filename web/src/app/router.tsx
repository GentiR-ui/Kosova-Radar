import { lazy, Suspense, type ReactNode } from 'react'
import { createBrowserRouter } from 'react-router'
import { t } from '../lib/strings'
import PublicLayout, { PageTitle } from '../features/public/PublicLayout'

// The admin area is a separate bundle: public visitors never download it.
const LoginPage = lazy(() => import('../features/auth/LoginPage'))
const AdminLayout = lazy(() => import('../features/admin/AdminLayout'))
const DashboardPage = lazy(() => import('../features/admin/DashboardPage'))
const RequireAdmin = lazy(() =>
  import('../features/admin/AdminLayout').then((m) => ({ default: m.RequireAdmin })),
)

const loading = (
  <p role="status" className="p-6 text-muted">
    {t.loading}
  </p>
)
const lazyPage = (node: ReactNode) => <Suspense fallback={loading}>{node}</Suspense>
const adminOnly = (node: ReactNode) => lazyPage(<RequireAdmin>{node}</RequireAdmin>)

export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { path: '/', element: <PageTitle title={t.pages.home} /> },
      { path: '/events', element: <PageTitle title={t.nav.events} /> },
      { path: '/deals', element: <PageTitle title={t.nav.deals} /> },
      { path: '/search', element: <PageTitle title={t.pages.search} /> },
      { path: '*', element: <PageTitle title={t.notFound} /> },
    ],
  },
  { path: '/admin/login', element: lazyPage(<LoginPage />) },
  {
    path: '/admin',
    element: lazyPage(<AdminLayout />),
    children: [
      { index: true, element: lazyPage(<DashboardPage />) },
      { path: 'review', element: <PageTitle title={t.admin.nav.review} /> },
      { path: 'published', element: <PageTitle title={t.admin.nav.published} /> },
      { path: 'sources', element: adminOnly(<PageTitle title={t.admin.nav.sources} />) },
      { path: 'users', element: adminOnly(<PageTitle title={t.admin.nav.users} />) },
    ],
  },
])
