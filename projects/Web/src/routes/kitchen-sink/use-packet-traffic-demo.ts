import { useEffect, useMemo, useState } from 'react'
import type { PacketTableRow, PacketTableStatusRow } from '../../components/packets/packet-table'
import type { HistogramRange } from '../../components/ui/time-histogram'
import { buildTrafficBuckets, trafficBuckets, trafficReferenceTime, trafficRows, trafficSeries, trafficStatusRows } from './packet-traffic-fixtures'

type TrafficPage = { range: HistogramRange | null; limit: number; loading: boolean }
const pageSize = 32

export function usePacketTrafficDemo() {
  const [page, setPage] = useState<TrafficPage>({ range: null, limit: pageSize, loading: false })
  const { range } = page
  const matchingRows = useMemo(() => range
    ? trafficRows.filter(({ arrivedAt }) => arrivedAt >= range.from && arrivedAt < range.to)
    : trafficRows, [range])
  const buckets = useMemo(() => range ? buildTrafficBuckets(range, matchingRows) : trafficBuckets, [range, matchingRows])
  const receptions = useMemo(() => matchingRows.slice(0, page.limit), [matchingRows, page.limit])
  const rows = useMemo(() => range ? receptions : receptions.flatMap<PacketTableRow | PacketTableStatusRow>((row, index) => {
    const status = trafficStatusRows[index]
    return status ? [status, row] : [row]
  }), [range, receptions])
  const hasMore = receptions.length < matchingRows.length

  useEffect(() => {
    if (!page.loading) return
    const timeout = window.setTimeout(() => {
      setPage((current) => current === page ? { ...current, limit: current.limit + pageSize, loading: false } : current)
    }, 240)
    return () => window.clearTimeout(timeout)
  }, [page])

  return {
    buckets,
    series: trafficSeries,
    totalReceptions: trafficRows.length,
    loadedReceptions: receptions.length,
    rows,
    range,
    setRange: (next: HistogramRange | null) => setPage((current) =>
      current.range?.from === next?.from && current.range?.to === next?.to
        ? current : { range: next, limit: pageSize, loading: false }),
    hasMore,
    isLoadingMore: page.loading,
    loadMore: () => setPage((current) => current === page && hasMore && !current.loading ? { ...current, loading: true } : current),
    resetKey: range ? `${range.from}/${range.to}` : 'all',
    referenceTime: trafficReferenceTime,
  }
}
