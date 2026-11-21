import type { PacketTableRow, PacketTableStatusRow } from '../../components/packets/packet-table'
import type { PacketTypeCode } from '../../components/packets/packet-type-badge'
import type { RouteKind } from '../../components/packets/route-badge'
import type { HistogramBucket, HistogramRange, HistogramSeries } from '../../components/ui/time-histogram'

const packetExamples = [
  { key: '0x04', label: 'Advert', color: '#2563eb', summary: 'Hilltop repeater · node announcement' },
  { key: '0x02', label: 'Text', color: '#059669', summary: 'Direct message · encrypted contents unavailable' },
  { key: '0x05', label: 'Group text', color: '#7c3aed', summary: '#baltics · group message' },
  { key: '0x03', label: 'Ack', color: '#b45309', summary: 'Acknowledgement for a received packet' },
  { key: '0x09', label: 'Trace', color: '#be185d', summary: 'Path trace · signal samples reported' },
  { key: 'unavailable', label: 'Unavailable', color: '#64748b', summary: 'Payload type could not be decoded' },
] as const satisfies readonly { key: PacketTypeCode | 'unavailable'; label: string; color: string; summary: string }[]

const routes: readonly RouteKind[] = ['direct', 'flood', 'transportDirect', 'transportFlood']
const regions = ['VNO', 'KUN', 'KLP'] as const
const observersByRegion = {
  VNO: ['Vilnius 1', 'Vilnius 2'],
  KUN: ['Kaunas 1', 'Kaunas 2'],
  KLP: ['Klaipėda 1', 'Klaipėda 2'],
} as const
const packetMix = [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 4, 5] as const
const interval = 15 * 60 * 1000
const maxBuckets = 96
const bucketSizes = [1_000, 5_000, 15_000, 30_000, 60_000, 300_000, interval]
const start = Date.parse('2026-11-19T14:00:00Z')
const activityPattern = [8, 16, 10, 0, 9, 21, 17, 12, 22, 29, 18, 32, 38, 24, 12, 0, 14, 18, 28, 38, 46, 27, 19, 12, 15, 31, 37, 23, 17, 10, 19, 12]
const activity = [7, 3, 7].flatMap(scale => activityPattern.map(count => count * scale))

export const trafficReferenceTime = new Date(start + activity.length * interval).toISOString()
export const trafficSeries: readonly HistogramSeries[] = packetExamples.map(({ key, label, color }) => ({ key, label, color }))

export type TrafficReception = PacketTableRow & { observer: string }

export const trafficRows: readonly TrafficReception[] = activity.flatMap((count, bucketIndex) => {
  const bucketStart = start + bucketIndex * interval
  return Array.from({ length: count }, (_, index): TrafficReception => {
    const packetIndex = Math.floor(index / 2)
    const packet = packetExamples[packetMix[(bucketIndex + packetIndex * 7) % packetMix.length]]
    const route = routes[(bucketIndex + packetIndex) % routes.length]
    const region = regions[(bucketIndex + index) % regions.length]
    const observers = observersByRegion[region]
    return {
      id: `reception-${bucketIndex}-${index}`,
      code: (0xa41000 + bucketIndex * 256 + packetIndex).toString(16).toUpperCase(),
      packetType: packet.key === 'unavailable' ? undefined : packet.key,
      route: packet.key === 'unavailable' ? 'unavailable' : route,
      scope: packet.key !== 'unavailable' && route.startsWith('transport') && packetIndex % 3 === 0 ? '#baltics' : undefined,
      summary: packet.summary,
      region,
      observer: observers[(Math.floor(index / regions.length) + bucketIndex) % observers.length],
      arrivedAt: new Date(bucketStart + Math.floor(index * interval / count)).toISOString(),
    }
  })
}).reverse()

export function buildTrafficBuckets({ from, to }: HistogramRange, rows: readonly PacketTableRow[]): readonly HistogramBucket[] {
  const lower = Date.parse(from)
  const duration = Date.parse(to) - lower
  const bucketSize = bucketSizes.find(size => duration % size === 0 && duration / size <= maxBuckets) ?? duration
  const buckets = Array.from({ length: duration / bucketSize }, (_, index) => ({
    from: new Date(lower + index * bucketSize).toISOString(),
    to: new Date(lower + (index + 1) * bucketSize).toISOString(),
    counts: {} as Record<string, number>,
  }))
  for (const { arrivedAt, packetType } of rows) {
    const bucket = buckets[Math.floor((Date.parse(arrivedAt) - lower) / bucketSize)]
    if (!bucket) continue
    const key = packetType ?? 'unavailable'
    bucket.counts[key] = (bucket.counts[key] ?? 0) + 1
  }
  return buckets
}

export const trafficBuckets = buildTrafficBuckets({ from: new Date(start).toISOString(), to: trafficReferenceTime }, trafficRows)

export const trafficStatusRows: Readonly<Partial<Record<number, PacketTableStatusRow>>> = {
  0: { id: 'fixture-live', status: 'live' },
  5: { id: 'fixture-resumed', status: 'resumed', occurredAt: '2026-11-20T13:59:00.000Z' },
  12: { id: 'fixture-connection-lost', status: 'lost-connection', occurredAt: '2026-11-20T13:57:45.000Z' },
}
