"use client"

import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { Button } from '@ui-sdk/react/button'
import { Menu, MenuContent, MenuItem, MenuItemIcon, MenuTrigger } from '@ui-sdk/react/menu'
import { MeshBrandMark } from '../mesh-brand-mark'
import { NavigationIcon, type NavigationIconName } from './navigation-icon'
import './app-shell.css'

const sections = [
  { to: '/packets', label: 'Packets', icon: 'packets' },
  { to: '/channels', label: 'Channels', icon: 'channels' },
  { to: '/map', label: 'Map', icon: 'map' },
  { to: '/nodes', label: 'Nodes', icon: 'nodes' },
  { to: '/observers', label: 'Observers', icon: 'observers' },
  { to: '/analytics', label: 'Analytics', icon: 'analytics' },
] as const satisfies ReadonlyArray<{ to: string; label: string; icon: NavigationIconName }>

export function AppShell() {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const overflowSelected = sections.slice(4).some(({ to }) => pathname === to || pathname.startsWith(`${to}/`))

  return (
    <div className="app-shell">
      <Button className="app-skip-link" render={<a href="#app-content" />}>Skip to content</Button>
      <header className="app-header">
        <Button className="app-brand" aria-label="Pulsas home" render={<Link to="/packets" />}>
          <MeshBrandMark size={24} />
          <span>PULSAS</span>
        </Button>
        <nav className="app-navigation" aria-label="Main navigation">
          {sections.map(({ to, label, icon }, index) => (
            <Button
              key={to}
              aria-label={label}
              className={`app-nav-link${index > 3 ? ' app-desktop-destination' : ''}`}
              render={<Link to={to} activeOptions={{ exact: false }} activeProps={{ 'aria-current': 'page' }} />}
            >
              <NavigationIcon name={icon} />
              <span>{label}</span>
            </Button>
          ))}
          <Menu>
            <MenuTrigger className="app-nav-link app-more-trigger" aria-label="More" data-overflow-selected={overflowSelected ? '' : undefined}>
              <NavigationIcon name="more" />
              <span>More</span>
            </MenuTrigger>
            <MenuContent className="app-more-menu" align="end" sideOffset={8} collisionPadding={8}>
              {sections.slice(4).map(({ to, label, icon }) => (
                <MenuItem
                  key={to}
                  value={to}
                  className="app-more-item app-mobile-destination"
                  render={<Link to={to} activeProps={{ 'aria-current': 'page' }} />}
                >
                  <MenuItemIcon><NavigationIcon name={icon} /></MenuItemIcon>
                  {label}
                </MenuItem>
              ))}
              <MenuItem value="/kitchen-sink" className="app-more-item" render={<Link to="/kitchen-sink" />}>
                <MenuItemIcon><NavigationIcon name="kitchen-sink" /></MenuItemIcon>
                Kitchen Sink
              </MenuItem>
            </MenuContent>
          </Menu>
        </nav>
      </header>
      <main className="app-content" id="app-content" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="app-footer">Pulsas <span aria-hidden="true">·</span> Atviras Tinklas</footer>
    </div>
  )
}
