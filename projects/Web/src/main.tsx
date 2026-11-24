import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRootRoute, createRoute, createRouter, redirect, RouterProvider, useNavigate, useSearch } from '@tanstack/react-router'
import { lazy, StrictMode, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import { AppShell } from './components/layout/app-shell'
import { LoadingState, PageFailure } from './components/ui/generic-states'
import { KitchenSinkPage } from './routes/kitchen-sink/page'
import { SectionPage } from './routes/section-page'

const MapPage = lazy(() => import('./routes/map/page'))
const PacketsPage = lazy(() => import('./routes/packets/page'))
const ChannelsPage = lazy(() => import('./routes/channels/page'))

function ChannelsRoute() {
  const { channel } = useSearch({ from: '/app/channels' })
  const navigate = useNavigate({ from: '/channels' })
  return <Suspense fallback={<LoadingState label="Loading channels" />}>
    <ChannelsPage channelId={channel ?? null} onSelectChannel={id => {
      void navigate({ search: { channel: id ?? undefined } })
    }} />
  </Suspense>
}

function RouteFailure() {
  return <PageFailure title="Unable to load this page" retryLabel="Reload page" onRetry={() => window.location.reload()} />
}

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
  component: () => <Suspense fallback={<LoadingState label="Loading packets" />}><PacketsPage /></Suspense>,
  errorComponent: RouteFailure,
})
const channels = createRoute({
  getParentRoute: () => appRoute,
  path: '/channels',
  validateSearch: (search: Record<string, unknown>): { channel?: string } => ({
    channel: typeof search.channel === 'string' ? search.channel : undefined,
  }),
  component: ChannelsRoute,
  errorComponent: RouteFailure,
})
const map = createRoute({
  getParentRoute: () => appRoute,
  path: '/map',
  component: () => <Suspense fallback={<LoadingState label="Loading map workspace" />}><MapPage /></Suspense>,
  errorComponent: RouteFailure,
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
