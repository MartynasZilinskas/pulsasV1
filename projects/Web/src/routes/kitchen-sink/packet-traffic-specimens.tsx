import { Button } from '@ui-sdk/react/button'
import { Drawer, DrawerBackdrop, DrawerClose, DrawerContent, DrawerTitle } from '@ui-sdk/react/drawer'
import { useId, useRef, useState } from 'react'
import { PacketTable, type PacketTableRow } from '../../components/packets/packet-table'
import { PacketTypeBadge } from '../../components/packets/packet-type-badge'
import { RouteBadge } from '../../components/packets/route-badge'
import { TimeHistogram, type HistogramRange } from '../../components/ui/time-histogram'
import { createTimestampFormatter } from '../../components/ui/timestamp-format'
import { usePacketTrafficDemo } from './use-packet-traffic-demo'
import './packet-traffic-specimens.css'

const count = new Intl.NumberFormat()
const timeZone = 'Europe/Vilnius'
const timeZoneLabel = 'Vilnius'
const clockOptions = { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone } as const
const calendarOptions = { month: 'short', day: 'numeric' } as const
const clock = new Intl.DateTimeFormat(undefined, clockOptions)
const clockWithSeconds = new Intl.DateTimeFormat(undefined, { ...clockOptions, second: '2-digit' })
const calendarClock = new Intl.DateTimeFormat(undefined, { ...clockOptions, ...calendarOptions })
const calendarClockWithSeconds = new Intl.DateTimeFormat(undefined, { ...clockOptions, ...calendarOptions, second: '2-digit' })
const localDay = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeZone })
const timestamp = createTimestampFormatter(timeZone)

function rangeFormatter({ from, to }: HistogramRange) {
  const start = new Date(from)
  const end = new Date(to)
  const showSeconds = start.getUTCSeconds() !== 0 || end.getUTCSeconds() !== 0
  if (localDay.format(start) !== localDay.format(end)) return showSeconds ? calendarClockWithSeconds : calendarClock
  return showSeconds ? clockWithSeconds : clock
}

function RangeTime({ instant, formatter }: { instant: string; formatter: Intl.DateTimeFormat }) {
  const value = Date.parse(instant)
  const detail = timestamp.format(value)
  return <time dateTime={instant} title={detail}>
    <span aria-hidden="true">{formatter.format(value)}</span><span className="ks-visually-hidden">{detail}</span>
  </time>
}

function receptionReturnTarget(workspace: HTMLElement | null, id?: string) {
  const scroller = workspace?.querySelector<HTMLElement>('[data-packet-table-scroll]')
  if (!scroller || !id) return scroller

  const button = scroller.querySelector<HTMLButtonElement>(`[data-reception-id="${CSS.escape(id)}"] [data-packet-code]`)
  if (!button || button.disabled) return scroller

  const { top, bottom, left, right, width, height } = button.getBoundingClientRect()
  const frame = scroller.getBoundingClientRect()
  const headerBottom = scroller.querySelector('thead')?.getBoundingClientRect().bottom ?? frame.top
  return width > 0 && height > 0 && top >= Math.max(0, frame.top, headerBottom) &&
    bottom <= Math.min(frame.top + scroller.clientHeight, document.documentElement.clientHeight) &&
    left >= Math.max(0, frame.left) && right <= Math.min(frame.left + scroller.clientWidth, document.documentElement.clientWidth)
    ? button : scroller
}

function ReceptionFacts({ row }: { row: PacketTableRow }) {
  return (
    <dl className="ks-reception-facts" data-drawer-no-drag="">
      <div><dt>Code</dt><dd><code>{row.code}</code></dd></div>
      <div><dt>Region</dt><dd>{row.region}</dd></div>
      <div><dt>Type</dt><dd><PacketTypeBadge code={row.packetType} /></dd></div>
      <div><dt>Transport</dt><dd><RouteBadge route={row.route} scope={row.scope} /></dd></div>
      <div className="ks-reception-fact-wide">
        <dt>Arrival ({timeZoneLabel})</dt>
        <dd><time dateTime={row.arrivedAt}>{timestamp.format(Date.parse(row.arrivedAt))}</time></dd>
      </div>
      <div className="ks-reception-summary ks-reception-fact-wide"><dt>Summary</dt><dd>{row.summary}</dd></div>
    </dl>
  )
}

export function PacketTrafficSpecimens() {
  const heading = useId()
  const workspace = useRef<HTMLDivElement>(null)
  const { buckets, series, totalReceptions, loadedReceptions, rows, range, setRange, hasMore, isLoadingMore, loadMore, resetKey, referenceTime } = usePacketTrafficDemo()
  const [details, setDetails] = useState<{ row: PacketTableRow; open: boolean }>()
  const rangeClock = range ? rangeFormatter(range) : clock

  function applyRange(next: HistogramRange | null) {
    if (next?.from === range?.from && next?.to === range?.to) return
    setRange(next)
  }

  return (
    <section className="ks-section ks-packet-traffic" aria-labelledby={heading}>
      <div className="ks-section-heading">
        <h2 id={heading}>Packet traffic</h2>
        <p>24 hours · {count.format(totalReceptions)} sample receptions. Zoom the histogram to filter the table. Select a reception to view its details.</p>
      </div>
      <div className="ks-traffic-workspace" ref={workspace}>
        <div className="ks-traffic-overview">
          <TimeHistogram buckets={buckets} series={series} measure="Receptions over time" unit="receptions" timeZone={timeZone} timeZoneLabel={timeZoneLabel}
            onRangeSelect={applyRange} />
          <div className="ks-traffic-context">
            <span className="ks-traffic-count">{count.format(loadedReceptions)} loaded</span>
            <div className="ks-traffic-range">
              <p role="status" aria-label="Active time range">
                {range ? <>Filtered: <RangeTime instant={range.from} formatter={rangeClock} /> – <RangeTime instant={range.to} formatter={rangeClock} /> {timeZoneLabel}</> : 'All sample times'}
              </p>
              <Button className="ks-traffic-clear" aria-label="Clear time range" disabled={!range} focusableWhenDisabled onClick={() => applyRange(null)}>Clear</Button>
            </div>
          </div>
        </div>
        <PacketTable rows={rows} referenceTime={referenceTime} timeZone={timeZone} timeZoneLabel={timeZoneLabel} resetKey={resetKey} hasMore={hasMore} isLoadingMore={isLoadingMore}
          loadMore={loadMore} selectedId={details?.open ? details.row.id : undefined} onActivate={(row) => setDetails({ row, open: true })} />
      </div>
      <Drawer side="end" modal open={details?.open ?? false} onOpenChange={(open) => {
        if (!open) setDetails((current) => current?.open ? { ...current, open } : current)
      }}>
        <DrawerBackdrop className="ks-reception-drawer-backdrop" />
        <DrawerContent className="ks-reception-drawer" initialFocus="content" finalFocus={() => receptionReturnTarget(workspace.current, details?.row.id)}>
          <header className="ks-reception-drawer-header">
            <DrawerTitle>Reception details</DrawerTitle>
            <DrawerClose className="ks-reception-drawer-close" aria-label="Close reception details">
              <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <path d="m6 6 12 12M6 18 18 6" />
              </svg>
            </DrawerClose>
          </header>
          {details && <div className="ks-reception-drawer-scroll"><ReceptionFacts row={details.row} /></div>}
        </DrawerContent>
      </Drawer>
    </section>
  )
}
