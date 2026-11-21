import { useEffect, useMemo, useState } from 'react'
import type { PacketTableRow, PacketTableStatusRow } from '../../components/packets/packet-table'
import type { HistogramRange } from '../../components/ui/time-histogram'
import { trafficBuckets, trafficReferenceTime, trafficRows, trafficSeries, trafficStatusRows, type TrafficReception } from './packet-traffic-fixtures'

export type TrafficFilterKey = 'packetType' | 'scope' | 'observer' | 'region'
export type TrafficFilterGroup = { key: TrafficFilterKey; label: string; options: readonly { value: string; label: string }[] }
type TrafficFilterValues = Record<TrafficFilterKey, readonly string[]>
type TrafficPage = { range: HistogramRange | null; filterValues: TrafficFilterValues; hashQuery: string; limit: number; loading: boolean }
const pageSize = 32
const emptyFilters: TrafficFilterValues = { packetType: [], scope: [], observer: [], region: [] }

const filterValue = {
  packetType: (row) => row.packetType ?? 'unavailable',
  scope: (row) => row.scope ?? 'unscoped',
  observer: (row) => row.observer,
  region: (row) => row.region,
} satisfies Record<TrafficFilterKey, (row: TrafficReception) => string>

function fixtureOptions(key: 'scope' | 'observer' | 'region') {
  return [...new Set(trafficRows.map(filterValue[key]))].sort().map(value => ({
    value, label: value === 'unscoped' ? 'No scope' : value,
  }))
}

const filterGroups: readonly TrafficFilterGroup[] = [
  { key: 'packetType', label: 'Packet type', options: trafficSeries.map(({ key, label }) => ({ value: key, label })) },
  { key: 'scope', label: 'Scope', options: fixtureOptions('scope') },
  { key: 'observer', label: 'Observer', options: fixtureOptions('observer') },
  { key: 'region', label: 'Region', options: fixtureOptions('region') },
]

const normalizeHashQuery = (query: string) => query.trim().toUpperCase()

export function usePacketTrafficDemo() {
  const [page, setPage] = useState<TrafficPage>({ range: null, filterValues: emptyFilters, hashQuery: '', limit: pageSize, loading: false })
  const { range, filterValues, hashQuery } = page
  const hash = normalizeHashQuery(hashQuery)
  const activeFilterCount = Number(!!range) + filterGroups.filter(({ key }) => filterValues[key].length > 0).length
  const isFiltered = activeFilterCount > 0 || hash !== ''
  const matchingRows = useMemo(() => {
    const activeGroups = filterGroups.filter(({ key }) => filterValues[key].length > 0)
    if (!range && !activeGroups.length && !hash) return trafficRows
    return trafficRows.filter(row => (!range || (row.arrivedAt >= range.from && row.arrivedAt < range.to)) &&
      (!hash || row.code.toUpperCase().includes(hash)) &&
      activeGroups.every(({ key }) => filterValues[key].includes(filterValue[key](row))))
  }, [range, filterValues, hash])
  const receptions = useMemo(() => matchingRows.slice(0, page.limit), [matchingRows, page.limit])
  const rows = useMemo(() => isFiltered ? receptions : receptions.flatMap<PacketTableRow | PacketTableStatusRow>((row, index) => {
    const status = trafficStatusRows[index]
    return status ? [status, row] : [row]
  }), [isFiltered, receptions])
  const hasMore = receptions.length < matchingRows.length

  useEffect(() => {
    if (!page.loading) return
    const timeout = window.setTimeout(() => {
      setPage((current) => current === page ? { ...current, limit: current.limit + pageSize, loading: false } : current)
    }, 240)
    return () => window.clearTimeout(timeout)
  }, [page])

  return {
    buckets: trafficBuckets,
    series: trafficSeries,
    totalReceptions: trafficRows.length,
    loadedReceptions: receptions.length,
    rows,
    range,
    setRange: (next: HistogramRange | null) => setPage((current) =>
      current.range?.from === next?.from && current.range?.to === next?.to
        ? current : { ...current, range: next, limit: pageSize, loading: false }),
    hashQuery,
    setHashQuery: (next: string) => setPage((current) => normalizeHashQuery(next) === normalizeHashQuery(current.hashQuery)
      ? { ...current, hashQuery: next }
      : { ...current, hashQuery: next, limit: pageSize, loading: false }),
    filterGroups,
    filterValues,
    setFilterValues: (key: TrafficFilterKey, values: readonly string[]) => setPage((current) => ({
      ...current, filterValues: { ...current.filterValues, [key]: values }, limit: pageSize, loading: false,
    })),
    activeFilterCount,
    matchesReception: (id: string) => !isFiltered || matchingRows.some(row => row.id === id),
    hasMore,
    isLoadingMore: page.loading,
    loadMore: () => setPage((current) => current === page && hasMore && !current.loading ? { ...current, loading: true } : current),
    resetKey: JSON.stringify([range, filterValues, hash]),
    referenceTime: trafficReferenceTime,
  }
}
