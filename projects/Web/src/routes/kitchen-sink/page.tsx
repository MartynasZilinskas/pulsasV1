import { Checkbox, CheckboxIndicator } from '@ui-sdk/react/checkbox'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectItemIndicator,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@ui-sdk/react/select'
import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { MeshBrandMark } from '../../components/mesh-brand-mark'
import { NodeAvatar } from '../../components/nodes/node-avatar'
import { NodeRoleIcon, nodeRoles, type NodeIdentity, type NodeRole } from '../../components/nodes/node-role'
import { NodeRoleBadge } from '../../components/nodes/node-role-badge'
import { RouteBadge, type RouteKind } from '../../components/packets/route-badge'
import { PacketPathSpecimens } from './packet-path-specimens'
import { PacketTrafficSpecimens } from './packet-traffic-specimens'
import { PacketTypeSpecimens } from './packet-type-specimens'
import { SearchableMultiSelectSpecimen } from './searchable-multi-select-specimen'
import { ChannelKindSpecimens, MeshBrandSpecimens } from './static-specimens'
import './kitchen-sink.css'
import './map-specimens.css'

const MapSpecimens = lazy(() => import('./map-specimens'))

const roles = ['ROOM_SERVER', 'REPEATER', 'CLIENT', 'SENSOR', 'NONE'] satisfies NodeRole[]
const roleItems = roles.map((value) => ({ value, label: nodeRoles[value].label }))
const routes = ['direct', 'flood', 'transportDirect', 'transportFlood', 'unavailable'] satisfies RouteKind[]

const sizes = [
  { name: 'marker', label: 'Marker', pixels: 24 },
  { name: 'contact', label: 'Contact', pixels: 40 },
  { name: 'display', label: 'Display', pixels: 64 },
] as const

const glyphPaths = {
  chevron: 'm6 9 6 6 6-6',
  check: 'm5 12 5 5L19 7',
} as const

function Glyph({ name }: { name: keyof typeof glyphPaths }) {
  return (
    <svg className="ks-glyph" aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d={glyphPaths[name]} />
    </svg>
  )
}

function NodeIdentityPreview({ identity, onRole, onObserver, onWarning }: {
  identity: Required<NodeIdentity>
  onRole: (role: NodeRole) => void
  onObserver: (observer: boolean) => void
  onWarning: (warning: boolean) => void
}) {
  const { role, observer, warning } = identity
  return (
    <section aria-labelledby="ks-identity-heading" className="ks-section">
      <div className="ks-section-heading">
        <h2 id="ks-identity-heading">Node identity</h2>
        <p>Compare the same avatar and role badge on light and dark surfaces.</p>
      </div>
      <div className="ks-workspace">
        <div className="ks-controls">
          <Select<NodeRole> items={roleItems} value={role} onValueChange={(value) => {
            if (value !== null) onRole(value)
          }}>
            <SelectLabel className="ks-field-label">Advertised role</SelectLabel>
            <SelectTrigger className="ks-select-trigger">
              <span className="ks-role-glyph" data-tone={nodeRoles[role].tone}><NodeRoleIcon role={role} /></span>
              <SelectValue />
              <Glyph name="chevron" />
            </SelectTrigger>
            <SelectContent className="ks-select-content" align="start" sideOffset={5}>
              {roleItems.map(({ value, label }) => (
                <SelectItem className="ks-select-item" key={value} value={value} label={label}>
                  <span className="ks-role-glyph" data-tone={nodeRoles[value].tone}><NodeRoleIcon role={value} /></span>
                  <span>{label}</span>
                  <SelectItemIndicator className="ks-select-indicator" aria-hidden="true"><Glyph name="check" /></SelectItemIndicator>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <fieldset className="ks-modifiers">
            <legend className="ks-field-label">Modifiers</legend>
            <label className="ks-checkbox-row">
              <Checkbox className="ks-checkbox" checked={observer} onCheckedChange={(checked) => onObserver(checked === true)}>
                <CheckboxIndicator aria-hidden="true"><Glyph name="check" /></CheckboxIndicator>
              </Checkbox>
              Observer
            </label>
            <label className="ks-checkbox-row">
              <Checkbox className="ks-checkbox" checked={warning} onCheckedChange={(checked) => onWarning(checked === true)}>
                <CheckboxIndicator aria-hidden="true"><Glyph name="check" /></CheckboxIndicator>
              </Checkbox>
              Warning
            </label>
          </fieldset>
        </div>
        <div className="ks-preview">
          <h3 className="ks-visually-hidden">Identity preview</h3>
          <div className="ks-surfaces">
            {['Light', 'Dark'].map((theme) => (
              <figure className="ks-surface" data-theme={theme.toLowerCase()} key={theme}>
                <figcaption>{theme} surface</figcaption>
                <div className="ks-surface-specimen" aria-hidden="true">
                  <NodeAvatar {...identity} size="display" />
                  <NodeRoleBadge {...identity} />
                </div>
              </figure>
            ))}
          </div>
          <p className="ks-preview-status" role="status" aria-label="Node identity preview" aria-atomic="true">
            <span>Preview:</span> {nodeRoles[role].label}{observer && ' · Observer'}{warning && ' · Warning: example node needs attention'}
          </p>
        </div>
      </div>
      <div className="ks-size-strip">
        <h3>Avatar sizes</h3>
        <ul>
          {sizes.map(({ name, label, pixels }) => (
            <li key={name}>
              <span className="ks-size-sample"><NodeAvatar {...identity} size={name} /></span>
              <span>{label} <code>{pixels}px</code></span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}

function NodeIdentityContext({ identity }: { identity: NodeIdentity }) {
  const { role, observer, warning } = identity
  const label = `${nodeRoles[role].label}${observer ? ' · Observer' : ''}${warning ? ' · Warning' : ''}`

  return (
    <section aria-labelledby="ks-context-heading" className="ks-section">
      <div className="ks-section-heading">
        <h2 id="ks-context-heading">Node identity in context</h2>
        <p>The same role and modifiers appear in a contact row and as static markers.</p>
      </div>
      <div className="ks-context">
        <figure className="ks-context-figure">
          <figcaption>Contact row</figcaption>
          <div className="ks-contact-row">
            <NodeAvatar {...identity} />
            <div>
              <p>Example node</p>
              <p className="ks-contact-identity">{label}</p>
            </div>
          </div>
        </figure>
        <figure className="ks-context-figure">
          <figcaption>Static markers <span className="ks-visually-hidden">· {label}</span></figcaption>
          <div className="ks-surfaces">
            {['Light', 'Dark'].map((theme) => (
              <div className="ks-surface ks-marker-surface" data-theme={theme.toLowerCase()} key={theme}>
                <NodeAvatar {...identity} size="marker" />
                <span>{theme} surface</span>
              </div>
            ))}
          </div>
        </figure>
      </div>
    </section>
  )
}

function RoleSpecimens() {
  return (
    <section aria-labelledby="ks-roles-heading" className="ks-section">
      <div className="ks-section-heading">
        <h2 id="ks-roles-heading">Role specimens</h2>
        <p>Advertised roles without modifiers, including the unknown fallback.</p>
      </div>
      <ul className="ks-role-gallery">
        {roles.map((role) => (
          <li key={role}>
            <NodeAvatar role={role} />
            <NodeRoleBadge role={role} />
          </li>
        ))}
      </ul>
    </section>
  )
}

function PacketSpecimens() {
  return (
    <section aria-labelledby="ks-packets-heading" className="ks-section">
      <div className="ks-section-heading">
        <h2 id="ks-packets-heading">Packet badges</h2>
        <p>Payload type and how a packet was routed are separate labels.</p>
      </div>
      <div className="ks-packet-gallery" data-agent-hidden>
        <PacketTypeSpecimens />
        <section aria-labelledby="ks-route-heading">
          <h3 id="ks-route-heading">Route</h3>
          <ul className="ks-badge-list">
            {routes.map((route) => (
              <li key={route}><RouteBadge route={route} /></li>
            ))}
          </ul>
          <h3>Route with a transport scope</h3>
          <ul className="ks-badge-list">
            <li><RouteBadge route="transportDirect" scope="#vno" /></li>
            <li><RouteBadge route="transportFlood" scope="#baltics" /></li>
          </ul>
        </section>
      </div>
    </section>
  )
}

function MapExamplesSection() {
  const section = useRef<HTMLElement>(null)
  const [requested, setRequested] = useState(false)

  useEffect(() => {
    if (!section.current || requested) return
    const observer = new IntersectionObserver(([entry]) => {
      if (!entry.isIntersecting) return
      observer.disconnect()
      setRequested(true)
    }, { rootMargin: '500px 0px' })
    observer.observe(section.current)
    return () => { observer.disconnect() }
  }, [requested])

  return (
    <section ref={section} className="ks-section ks-map-section" aria-labelledby="ks-map-heading">
      <header className="ks-section-heading ks-map-section-heading">
        <div>
          <h2 id="ks-map-heading">Map examples</h2>
          <p>Inspect node markers using a Beacon directory snapshot near Kaunas, then explore generated paths and packet flow. The examples don’t show live reception.</p>
        </div>
        <span className="ks-fixture-label">Beacon snapshot</span>
      </header>
      {requested && <Suspense fallback={<p className="ks-map-pending" role="status">Loading map examples…</p>}><MapSpecimens /></Suspense>}
    </section>
  )
}

export function KitchenSinkPage() {
  const [role, setRole] = useState<NodeRole>('ROOM_SERVER')
  const [observer, setObserver] = useState(true)
  const [warning, setWarning] = useState(false)
  const identity = { role, observer, warning }

  return (
    <main className="ks-page">
      <div className="ks-container">
        <header className="ks-header">
          <div className="ks-masthead">
            <span className="ks-brand">
              <span className="ks-brand-icon">
                <MeshBrandMark className="ks-glyph" size={20} />
              </span>
              Pulsas
            </span>
            <span className="ks-fixture-label">Fixture data</span>
          </div>
          <div className="ks-intro">
            <p className="ks-kicker">Component workbench</p>
            <h1>Kitchen Sink</h1>
            <p>Explore the icons, node identities, and packet details Pulsas uses to describe a mesh network. These examples don’t show live network activity.</p>
          </div>
        </header>
        <ChannelKindSpecimens />
        <NodeIdentityPreview identity={identity} onRole={setRole} onObserver={setObserver} onWarning={setWarning} />
        <NodeIdentityContext identity={identity} />
        <RoleSpecimens />
        <PacketSpecimens />
        <SearchableMultiSelectSpecimen />
        <PacketTrafficSpecimens />
        <PacketPathSpecimens />
        <MapExamplesSection />
        <MeshBrandSpecimens />
      </div>
    </main>
  )
}
