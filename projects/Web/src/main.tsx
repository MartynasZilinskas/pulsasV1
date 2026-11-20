import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRootRoute, createRoute, createRouter, redirect, RouterProvider } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { KitchenSinkPage } from './routes/kitchen-sink/page'

const rootRoute = createRootRoute()
const index = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/kitchen-sink', replace: true })
  },
})
const kitchenSink = createRoute({
  getParentRoute: () => rootRoute,
  path: '/kitchen-sink',
  component: KitchenSinkPage,
})
const router = createRouter({ routeTree: rootRoute.addChildren([index, kitchenSink]) })
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
