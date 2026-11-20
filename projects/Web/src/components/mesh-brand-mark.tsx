import './ui/theme.css'

export function MeshBrandMark({ size = 24, className }: { size?: number; className?: string }) {
  return (
    <svg className={className ? `ks-mesh-mark ${className}` : 'ks-mesh-mark'} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <g opacity="0.65">
        <path d="M4 12 L12 4 L20 12" />
        <path d="M4 12 L12 20 L20 12" />
        <path d="M4 12 H20" />
      </g>
      <g fill="var(--ks-mark-fill)">
        <circle cx="4" cy="12" r="1.7" />
        <circle cx="12" cy="4" r="1.7" />
        <circle cx="20" cy="12" r="1.7" />
        <circle cx="12" cy="20" r="1.7" />
      </g>
    </svg>
  )
}
