import { lazy, Suspense, type ReactNode } from 'react'
import { createBrowserRouter } from 'react-router'
import { t } from '../lib/strings'
import PublicLayout, { PageTitle } from '../features/public/PublicLayout'
import HomePage from '../features/public/HomePage'
import EventsPage from '../features/public/EventsPage'
import DealsPage from '../features/public/DealsPage'
import SearchPage from '../features/public/SearchPage'
import { DealDetailPage, EventDetailPage } from '../features/public/ItemDetail'

// The admin area is a separate bundle: public visitors never download it.
const LoginPage = lazy(() => import('../features/auth/LoginPage'))
const AdminLayout = lazy(() => import('../features/admin/AdminLayout'))
const DashboardPage = lazy(() => import('../features/admin/DashboardPage'))
const ReviewQueuePage = lazy(() => import('../features/admin/review/ReviewQueuePage'))
const CandidatePage = lazy(() => import('../features/admin/review/CandidatePage'))
const UsersPage = lazy(() => import('../features/admin/users/UsersPage'))
const PublishedPage = lazy(() => import('../features/admin/published/PublishedPage'))
const PublishedEditPage = lazy(() => import('../features/admin/published/PublishedEditPage'))
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
      { path: '/', element: <HomePage /> },
      { path: '/events', element: <EventsPage /> },
      { path: '/events/:id', element: <EventDetailPage /> },
      { path: '/deals', element: <DealsPage /> },
      { path: '/deals/:id', element: <DealDetailPage /> },
      { path: '/search', element: <SearchPage /> },
      { path: '*', element: <PageTitle title={t.notFound} /> },
    ],
  },
  { path: '/admin/login', element: lazyPage(<LoginPage />) },
  {
    path: '/admin',
    element: lazyPage(<AdminLayout />),
    children: [
      { index: true, element: lazyPage(<DashboardPage />) },
      { path: 'review', element: lazyPage(<ReviewQueuePage />) },
      { path: 'items/:id', element: lazyPage(<CandidatePage />) },
      { path: 'published', element: lazyPage(<PublishedPage />) },
      { path: 'published/:kind/:id', element: lazyPage(<PublishedEditPage />) },
      { path: 'sources', element: adminOnly(<PageTitle title={t.admin.nav.sources} />) },
      { path: 'users', element: adminOnly(<UsersPage />) },
    ],
  },
])
