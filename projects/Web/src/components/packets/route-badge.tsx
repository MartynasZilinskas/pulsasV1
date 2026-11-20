import { ExplainedBadge } from '../ui/explained-badge'
import { lucideGlyphs } from '../ui/lucide-glyph'
import './route-badge.css'

const routeGlyphs = {
  direct: lucideGlyphs.route,
  flood: <>
    <rect x="16" y="16" width="6" height="6" rx="1" />
    <rect x="2" y="16" width="6" height="6" rx="1" />
    <rect x="9" y="2" width="6" height="6" rx="1" />
    <path d="M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3" />
    <path d="M12 12V8" />
  </>,
  unavailable: lucideGlyphs['circle-question-mark'],
} as const

const routes = {
  direct: { label: 'Direct', icon: 'direct', explanation: 'Follows a supplied route through the mesh.' },
  flood: { label: 'Flood', icon: 'flood', explanation: 'Spreads through the mesh, recording its route.' },
  transportDirect: { label: 'T·Direct', icon: 'direct', explanation: 'Transport direct · Follows a supplied path.' },
  transportFlood: { label: 'T·Flood', icon: 'flood', explanation: 'Transport flood · Spreads through the mesh.' },
  unavailable: { label: '???', icon: 'unavailable', explanation: 'Transport unavailable' },
} as const

export type RouteKind = keyof typeof routes

export function RouteBadge({ route, scope }: { route: RouteKind; scope?: string }) {
  const { label, icon, explanation } = routes[route]
  return (
    <ExplainedBadge
      className="ks-route-badge"
      aria-label={route === 'unavailable' ? '??? (unavailable)' : undefined}
      explanation={scope ? `${explanation} Scope: ${scope}` : explanation}
    >
      <span className="ks-route-label">
        <svg className="ks-glyph" aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          {routeGlyphs[icon]}
        </svg>{label}
      </span>
      {scope && <> <code data-tone="green">{scope}</code></>}
    </ExplainedBadge>
  )
}
