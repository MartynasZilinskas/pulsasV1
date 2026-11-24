"use client"

import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { Button } from '@ui-sdk/react/button'
import { Menu, MenuContent, MenuItem, MenuItemIcon, MenuTrigger } from '@ui-sdk/react/menu'
import { Select, SelectContent, SelectItem, SelectItemIndicator, SelectLabel, SelectTrigger, SelectValue } from '@ui-sdk/react/select'
import { MeshBrandMark } from '../mesh-brand-mark'
import { useAppearance } from '../ui/appearance'
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

const appearances = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
] satisfies { value: 'system' | 'light' | 'dark'; label: string }[]

export function AppShell() {
  const { appearance, setAppearance } = useAppearance()
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const kitchenSinkSelected = pathname === '/kitchen-sink' || pathname.startsWith('/kitchen-sink/')
  const mapSelected = pathname === '/map' || pathname.startsWith('/map/')
  const workspaceSelected = mapSelected || ['/packets', '/channels'].some(to => pathname === to || pathname.startsWith(`${to}/`))
  const overflowSelected = kitchenSinkSelected || sections.slice(4).some(({ to }) => pathname === to || pathname.startsWith(`${to}/`))

  return (
    <div className={`app-shell${workspaceSelected ? ' app-shell-workspace' : ''}`}>
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
            <MenuTrigger className="app-nav-link app-more-trigger" aria-label="More" data-overflow-selected={kitchenSinkSelected ? 'kitchen-sink' : overflowSelected ? '' : undefined}>
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
        <div className="app-appearance">
          <Select<'system' | 'light' | 'dark'> items={appearances} value={appearance} onValueChange={(value) => {
            if (value !== null) setAppearance(value)
          }}>
            <SelectLabel className="ks-visually-hidden">Appearance</SelectLabel>
            <SelectTrigger className="app-appearance-trigger">
              <svg aria-hidden="true" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.4">
                <circle cx="10" cy="10" r="6.5" />
                <path d="M10 3.5a6.5 6.5 0 0 1 0 13Z" fill="currentColor" stroke="none" />
              </svg>
              <SelectValue />
              <svg className="app-appearance-chevron" aria-hidden="true" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.4"><path d="m4 6 4 4 4-4" /></svg>
            </SelectTrigger>
            <SelectContent className="app-appearance-content" align="end" sideOffset={6} collisionPadding={8}>
              {appearances.map(({ value, label }) => (
                <SelectItem className="app-appearance-item" key={value} value={value} label={label}>
                  <span>{label}</span>
                  <SelectItemIndicator aria-hidden="true">✓</SelectItemIndicator>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </header>
      <main className={`app-content${kitchenSinkSelected ? ' app-content-showcase' : ''}${workspaceSelected ? ' app-content-workspace' : ''}`} id="app-content" tabIndex={-1}>
        <Outlet />
      </main>
      {!mapSelected && <footer className="app-footer">Pulsas <span aria-hidden="true">·</span> Atviras Tinklas</footer>}
    </div>
  )
}
