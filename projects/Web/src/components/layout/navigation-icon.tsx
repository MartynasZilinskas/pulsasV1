import { LucideGlyph } from '../ui/lucide-glyph'

export type NavigationIconName = 'packets' | 'channels' | 'map' | 'nodes' | 'observers' | 'analytics' | 'more' | 'kitchen-sink'

export function NavigationIcon({ name }: { name: NavigationIconName }) {
  if (name === 'channels') return <LucideGlyph name="radio" size={18} />
  if (name === 'kitchen-sink') return <LucideGlyph name="braces" size={18} />

  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      {name === 'packets' && <><rect x="3" y="2" width="8" height="8" rx="2" /><rect x="13" y="14" width="8" height="8" rx="2" /><path d="M7 10v5a3 3 0 0 0 3 3h3" /></>}
      {name === 'map' && <><path d="m9 3-6 3v15l6-3 6 3 6-3V3l-6 3-6-3Z" /><path d="M9 3v15M15 6v15" /></>}
      {name === 'nodes' && <><rect x="9" y="2" width="6" height="6" rx="1" /><rect x="2" y="16" width="6" height="6" rx="1" /><rect x="16" y="16" width="6" height="6" rx="1" /><path d="M12 8v4M5 16v-4h14v4" /></>}
      {name === 'observers' && <><path d="M8.5 8.5a5 5 0 0 0 0 7M15.5 8.5a5 5 0 0 1 0 7M5 5a10 10 0 0 0 0 14M19 5a10 10 0 0 1 0 14M12 14v8" /><circle cx="12" cy="11" r="2" /></>}
      {name === 'analytics' && <><path d="M4 3v17h17M9 15v-4M14 15V6M19 15V9" /></>}
      {name === 'more' && <><circle cx="5" cy="12" r="1" /><circle cx="12" cy="12" r="1" /><circle cx="19" cy="12" r="1" /></>}
    </svg>
  )
}
