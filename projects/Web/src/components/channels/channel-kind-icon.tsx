import { LucideGlyph } from '../ui/lucide-glyph'
import './channel-kind-icon.css'

const glyphs = { public: 'radio', hashtag: 'hash', 'shared-key': 'lock' } as const
export type ChannelKind = keyof typeof glyphs

/** Decorative; the surrounding channel label identifies the kind. */
export function ChannelKindIcon({ kind, size = 16 }: { kind: ChannelKind; size?: number }) {
  return <span className="ks-channel-icon"><LucideGlyph name={glyphs[kind]} size={size} /></span>
}
