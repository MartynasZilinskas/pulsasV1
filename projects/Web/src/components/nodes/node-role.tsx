import './node-identity.css'

export const nodeRoles = {
  ROOM_SERVER: { label: 'Room server', tone: 'green', icon: 'server' },
  REPEATER: { label: 'Repeater', tone: 'violet', icon: 'antenna' },
  CLIENT: { label: 'Client', tone: 'blue', icon: 'client' },
  SENSOR: { label: 'Sensor', tone: 'amber', icon: 'sensor' },
  NONE: { label: 'Unknown', tone: 'neutral', icon: 'unknown' },
} as const

export type NodeRole = keyof typeof nodeRoles
export type NodeIdentity = { role: NodeRole; observer?: boolean; warning?: boolean }

const paths = {
  server: ['M4 5h16v6H4zM4 13h16v6H4zM7 8h.01M7 16h.01'],
  antenna: ['M5 12a7 7 0 0 1 14 0M8 12a4 4 0 0 1 8 0M12 11v1m-4 8 4-8 4 8m-6-3h4'],
  // Tabler Icons (MIT), device-mobile-message. See THIRD_PARTY_NOTICES.md.
  client: [
    'M11 3h10v8h-3l-4 2v-2h-3l0 -8',
    'M15 16v4a1 1 0 0 1 -1 1h-8a1 1 0 0 1 -1 -1v-14a1 1 0 0 1 1 -1h2',
    'M10 18v.01',
  ],
  sensor: ['M10 14V6a2 2 0 0 1 4 0v8a4 4 0 1 1-4 0Zm2 1v2m4-9h2m-2 3h2'],
  unknown: ['M9 9a3 3 0 1 1 4.8 2.4C12.7 12.2 12 12.7 12 14m0 4h.01'],
  observer: ['M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6Zm12.5 0a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0Z'],
  warning: ['M12 4 3 20h18L12 4Zm0 6v4m0 3h.01'],
} as const

function NodeGlyph({ name }: { name: keyof typeof paths }) {
  return (
    <svg className="ks-glyph" aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={name === 'client' ? 2 : 1.75} strokeLinecap="round" strokeLinejoin="round">
      {paths[name].map((path) => <path key={path} d={path} />)}
    </svg>
  )
}

export function NodeRoleIcon({ role }: { role: NodeRole }) {
  return <NodeGlyph name={nodeRoles[role].icon} />
}

export function NodeModifierIcon({ modifier }: { modifier: 'observer' | 'warning' }) {
  return <NodeGlyph name={modifier} />
}
