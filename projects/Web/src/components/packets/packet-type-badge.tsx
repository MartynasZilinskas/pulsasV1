import { ExplainedBadge } from '../ui/explained-badge'
import { LucideGlyph, type LucideGlyphName } from '../ui/lucide-glyph'
import './packet-type-badge.css'

type PacketType = {
  label: string
  glyph: LucideGlyphName
  tone?: 'neutral' | 'green' | 'amber' | 'blue' | 'violet'
  token: string
  name: string
  note?: string
}

const packetTypes = {
  '0x00': { label: 'Request', glyph: 'arrow-up-right', token: 'REQ', name: 'Request' },
  '0x01': { label: 'Response', glyph: 'arrow-down-left', token: 'RESPONSE', name: 'Response to REQ or ANON_REQ' },
  '0x02': { label: 'Text', tone: 'green', glyph: 'message-square', token: 'TXT_MSG', name: 'Text message', note: 'May also carry CLI commands or replies' },
  '0x03': { label: 'Ack', tone: 'amber', glyph: 'check', token: 'ACK', name: 'Acknowledgement', note: 'A packet acknowledgement, not a local delivery or read status' },
  '0x04': { label: 'Advert', tone: 'blue', glyph: 'radio', token: 'ADVERT', name: 'Node advertisement' },
  '0x05': { label: 'Text', tone: 'green', glyph: 'users', token: 'GRP_TXT', name: 'Group text message' },
  '0x06': { label: 'Data', glyph: 'users', token: 'GRP_DATA', name: 'Group datagram' },
  '0x07': { label: 'Anon req', glyph: 'arrow-up-right', token: 'ANON_REQ', name: 'Anonymous request' },
  '0x08': { label: 'Path', glyph: 'route', token: 'PATH', name: 'Returned path' },
  '0x09': { label: 'Trace', tone: 'violet', glyph: 'waypoints', token: 'TRACE', name: 'Trace collecting per-hop SNR' },
  '0x0A': { label: 'Multipart', glyph: 'layers', token: 'MULTIPART', name: 'Part of a packet sequence' },
  '0x0B': { label: 'Control', glyph: 'settings-2', token: 'CONTROL', name: 'Control/discovery data' },
  '0x0C': { label: 'Rsv 0xC', glyph: 'circle-dashed', token: 'Reserved 0x0C', name: 'Reserved payload type' },
  '0x0D': { label: 'Rsv 0xD', glyph: 'circle-dashed', token: 'Reserved 0x0D', name: 'Reserved payload type' },
  '0x0E': { label: 'Rsv 0xE', glyph: 'circle-dashed', token: 'Reserved 0x0E', name: 'Reserved payload type' },
  '0x0F': { label: 'Custom', glyph: 'braces', token: 'RAW_CUSTOM', name: 'Custom payload; no standard format' },
} satisfies Record<string, PacketType>

export type PacketTypeCode = keyof typeof packetTypes

export function PacketTypeBadge({ code, icons = true, passive = false }: { code?: PacketTypeCode; icons?: boolean; passive?: boolean }) {
  const packet: PacketType | undefined = code ? packetTypes[code] : undefined
  const glyph = icons && <LucideGlyph name={packet?.glyph ?? 'circle-question-mark'} />
  if (passive) return <span className="ks-badge ks-type-badge" data-packet-type={code ?? 'unavailable'} data-tone={packet?.tone ?? 'neutral'}>
    {glyph}
    <span>{code === '0x05' ? 'Group text' : packet?.label ?? 'Unavailable'}</span>
  </span>
  return (
    <ExplainedBadge
      className="ks-type-badge"
      data-packet-type={code ?? 'unavailable'}
      data-tone={packet?.tone ?? 'neutral'}
      aria-label={packet ? undefined : '??? (unavailable)'}
      explanation={packet ? `${packet.token} · ${packet.name} (${code})${packet.note ? `. ${packet.note}` : ''}` : 'Type unavailable'}
    >
      {glyph}
      <span>{packet?.label ?? '???'}</span>
    </ExplainedBadge>
  )
}
