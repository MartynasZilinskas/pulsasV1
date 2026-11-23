import type { PacketTableRow } from '../../components/packets/packet-table'
import { trafficRows, type TrafficReception } from '../kitchen-sink/packet-traffic-fixtures'

export type FixturePathCandidate = {
  id: string
  publicKey: string
  name: string
  confidence: 'Strong' | 'Weak'
  selected: boolean
  reasons: readonly string[]
  supportCount: number
}

export type FixturePathHop = {
  id: string
  prefix: string
  outcome: 'Resolved' | 'Ambiguous' | 'Unresolved'
  candidates: readonly FixturePathCandidate[]
}

export type FixturePacketOverview = {
  reception: {
    row: PacketTableRow
    observer: string
    broker: string
    heardAt: string | null
    rssi: number | null
    snr: number | null
  }
  packet: null | {
    id: string
    payloadId: string
    code: string
    facts: readonly { label: string; value: string }[]
    path: {
      kind: 'flood' | 'direct' | 'trace' | 'unavailable'
      status: 'Partial' | 'Unresolved' | 'Empty' | 'Unavailable'
      prefixWidth: number | null
      assessedAt: string | null
      hops: readonly FixturePathHop[]
    }
  }
  otherHearings: readonly { row: PacketTableRow; observer: string; samePacket: boolean }[]
}

const receptionsById = new Map(trafficRows.map(row => [row.id, row]))

// Shared content is assigned explicitly for this synthetic example, not inferred from real traffic.
const payloadId = (row: PacketTableRow) => `fixture-payload-${row.id.split('-')[1]}-${row.packetType ?? 'unavailable'}`
const hearingsByPayload = new Map<string, TrafficReception[]>()
for (const row of trafficRows) {
  if (!row.packetType) continue
  const key = payloadId(row)
  const hearings = hearingsByPayload.get(key) ?? []
  hearings.push(row)
  hearingsByPayload.set(key, hearings)
}

function payloadFacts(row: PacketTableRow): readonly { label: string; value: string }[] {
  switch (row.packetType) {
    case '0x04': return [
      { label: 'Advert name', value: 'Hilltop repeater' },
      { label: 'Role', value: 'Repeater' },
      { label: 'Signature', value: 'Not checked' },
    ]
    case '0x02': return [
      { label: 'Source hash', value: '2c' },
      { label: 'Destination hash', value: 'b4' },
      { label: 'Message', value: 'Encrypted contents unavailable' },
    ]
    case '0x05': return [{ label: 'Channel hash', value: '7a' }, { label: 'Message', value: 'Message text not exposed' }]
    case '0x03': return [{ label: 'Checksum', value: 'd1a9c304' }]
    case '0x09': return [{ label: 'Trace tag', value: '78563412' }, { label: 'Flags', value: '0' }]
    default: return []
  }
}

function candidate(prefix: string, ordinal: number, selected: boolean): FixturePathCandidate {
  const publicKey = prefix + ordinal.toString(16).padStart(64 - prefix.length, '0')
  return {
    id: `fixture-candidate-${prefix}-${ordinal}`,
    publicKey,
    name: `Example relay ${prefix.toUpperCase()}-${ordinal}`,
    confidence: selected ? 'Strong' : 'Weak',
    selected,
    reasons: selected ? ['Prefix match', 'Neighbour support'] : ['Prefix match'],
    supportCount: selected ? 3 : 0,
  }
}

function packetPath(row: PacketTableRow, seed: number): NonNullable<FixturePacketOverview['packet']>['path'] {
  if (row.packetType === '0x09') return { kind: 'trace', status: 'Unavailable', prefixWidth: null, assessedAt: null, hops: [] }
  const kind = row.route === 'direct' || row.route === 'transportDirect' ? 'direct' : 'flood'
  if (seed % 11 === 0) return { kind, status: 'Unavailable', prefixWidth: 1, assessedAt: null, hops: [] }
  if (seed % 7 === 0) return { kind, status: 'Empty', prefixWidth: 1, assessedAt: '2026-11-20T14:00:00Z', hops: [] }
  const prefixes = [0x2c + seed % 8, 0xb4 + seed % 8, 0xe7 + seed % 8].map(value => value.toString(16))
  return {
    kind,
    status: 'Partial',
    prefixWidth: 1,
    assessedAt: '2026-11-20T14:00:00Z',
    hops: prefixes.map((prefix, index) => ({
      id: `${row.code}-${index}`,
      prefix,
      outcome: index === 0 ? 'Resolved' : index === 1 ? 'Ambiguous' : 'Unresolved',
      candidates: index === 0 ? [candidate(prefix, 1, true)]
        : index === 1 ? [candidate(prefix, 1, false), candidate(prefix, 2, false)] : [],
    })),
  }
}

export function getPacketOverview(row: PacketTableRow): FixturePacketOverview {
  const stored = receptionsById.get(row.id)
  const index = Number(row.id.split('-')[2]) || 0
  const seed = Number.parseInt(row.code, 16) || 0
  const decoded = row.packetType !== undefined && row.route !== 'unavailable'
  return {
    reception: {
      row,
      observer: stored?.observer ?? 'Observer unavailable',
      broker: 'Example broker',
      heardAt: index % 5 === 0 ? null : new Date(Date.parse(row.arrivedAt) - 240).toISOString(),
      rssi: index % 5 === 0 ? null : -92 - index % 18,
      snr: index % 5 === 0 ? null : 7.25 - index % 12,
    },
    packet: decoded ? {
      id: `fixture-packet-${row.code.toLowerCase()}`,
      payloadId: payloadId(row),
      code: row.code,
      facts: payloadFacts(row),
      path: packetPath(row, seed),
    } : null,
    otherHearings: decoded ? (hearingsByPayload.get(payloadId(row)) ?? [])
      .filter(other => other.id !== row.id)
      .map(other => ({ row: other, observer: other.observer, samePacket: other.code === row.code })) : [],
  }
}
