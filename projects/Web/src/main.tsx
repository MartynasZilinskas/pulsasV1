import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRootRoute, createRoute, createRouter, redirect, RouterProvider } from '@tanstack/react-router'
import { lazy, StrictMode, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import { AppShell } from './components/layout/app-shell'
import { KitchenSinkPage } from './routes/kitchen-sink/page'
import { SectionPage } from './routes/section-page'

const MapPage = lazy(() => import('./routes/map/page'))

const rootRoute = createRootRoute()
const appRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'app',
  component: AppShell,
})
const index = createRoute({
  getParentRoute: () => appRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/packets', replace: true })
  },
})
const packets = createRoute({
  getParentRoute: () => appRoute,
  path: '/packets',
  component: () => <SectionPage section="Packets" />,
})
const channels = createRoute({
  getParentRoute: () => appRoute,
  path: '/channels',
  component: () => <SectionPage section="Channels" />,
})
const map = createRoute({
  getParentRoute: () => appRoute,
  path: '/map',
  component: () => <Suspense fallback={<p role="status">Loading map workspace…</p>}><MapPage /></Suspense>,
})
const nodes = createRoute({
  getParentRoute: () => appRoute,
  path: '/nodes',
  component: () => <SectionPage section="Nodes" />,
})
const observers = createRoute({
  getParentRoute: () => appRoute,
  path: '/observers',
  component: () => <SectionPage section="Observers" />,
})
const analytics = createRoute({
  getParentRoute: () => appRoute,
  path: '/analytics',
  component: () => <SectionPage section="Analytics" />,
})
const kitchenSink = createRoute({
  getParentRoute: () => appRoute,
  path: '/kitchen-sink',
  component: KitchenSinkPage,
})
const router = createRouter({
  routeTree: rootRoute.addChildren([
    appRoute.addChildren([index, packets, channels, map, nodes, observers, analytics, kitchenSink]),
  ]),
  scrollRestoration: true,
})
const queryClient = new QueryClient()

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

const root = document.getElementById('root')
if (!root) throw new Error('Pulsas app root was not found')

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
