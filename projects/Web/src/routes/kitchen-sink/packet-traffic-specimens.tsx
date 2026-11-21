import { Button } from '@ui-sdk/react/button'
import { Drawer, DrawerBackdrop, DrawerClose, DrawerContent, DrawerDescription, DrawerTitle } from '@ui-sdk/react/drawer'
import { useId, useMemo, useRef, useState, useSyncExternalStore, type KeyboardEvent } from 'react'
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
const inlineDetailsQuery = '(min-width: 64rem)'
const detailsTitle = 'Reception details'
const detailsHint = 'Use the Up and Down arrow keys to browse loaded receptions.'
const closeIcon = <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
  <path d="m6 6 12 12M6 18 18 6" />
</svg>

function subscribeToDetailsLayout(notify: () => void) {
  const media = window.matchMedia(inlineDetailsQuery)
  media.addEventListener('change', notify)
  return () => media.removeEventListener('change', notify)
}

function isInlineDetailsLayout() {
  return window.matchMedia(inlineDetailsQuery).matches
}

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

  const row = scroller.querySelector<HTMLTableRowElement>(`tr[data-activatable][data-reception-id="${CSS.escape(id)}"]`)
  if (!row) return scroller

  const { top, bottom, left, right, height } = row.getBoundingClientRect()
  const frame = scroller.getBoundingClientRect()
  const headerBottom = row.closest('table')?.tHead?.getBoundingClientRect().bottom ?? frame.top
  const viewport = scroller.ownerDocument.documentElement
  return height > 0 && top >= Math.max(0, frame.top, headerBottom) &&
    bottom <= Math.min(frame.top + scroller.clientHeight, viewport.clientHeight) &&
    Math.max(left, frame.left, 0) < Math.min(right, frame.left + scroller.clientWidth, viewport.clientWidth)
    ? row : scroller
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

function ReceptionDetails({ drawer, title, hint, row, announcement, filterNote, previous, next, close }: {
  drawer: boolean
  title: string
  hint: string
  row?: PacketTableRow
  announcement?: string
  filterNote?: string
  previous?: () => void
  next?: () => void
  close: () => void
}) {
  const Title = drawer ? DrawerTitle : 'h3'
  const Hint = drawer ? DrawerDescription : 'p'

  return <>
    <header className="ks-reception-details-header">
      <div>
        <Title id={title}>{detailsTitle}</Title>
        <Hint id={hint} className="ks-reception-details-hint">{detailsHint}</Hint>
      </div>
      {drawer
        ? <DrawerClose className="ks-reception-details-close" aria-label="Close reception details">{closeIcon}</DrawerClose>
        : <Button className="ks-reception-details-close" aria-label="Close reception details" onClick={close}>{closeIcon}</Button>}
    </header>
    {drawer && <div className="ks-reception-browse" data-drawer-no-drag="">
      <Button className="ks-reception-step" disabled={!previous} focusableWhenDisabled onClick={previous}>Previous</Button>
      <Button className="ks-reception-step" disabled={!next} focusableWhenDisabled onClick={next}>Next</Button>
    </div>}
    {drawer && <div className="ks-visually-hidden" role="status">{announcement}</div>}
    {row && <div className="ks-reception-details-scroll">
      {filterNote && <p className="ks-reception-filter-note">{filterNote}</p>}
      <ReceptionFacts row={row} />
    </div>}
  </>
}

export function PacketTrafficSpecimens() {
  const heading = useId()
  const detailsId = useId()
  const title = useId()
  const hint = useId()
  const inline = useSyncExternalStore(subscribeToDetailsLayout, isInlineDetailsLayout, () => false)
  const workspace = useRef<HTMLDivElement>(null)
  const inspector = useRef<HTMLElement>(null)
  const { buckets, series, totalReceptions, loadedReceptions, rows, range, setRange, hasMore, isLoadingMore, loadMore, resetKey, referenceTime } = usePacketTrafficDemo()
  const [details, setDetails] = useState<{ row: PacketTableRow; open: boolean; announcement?: string }>()
  const receptions = useMemo(() => rows.filter((row): row is PacketTableRow => !('status' in row)), [rows])
  const selected = details?.open ? details.row : undefined
  const selectedIndex = selected ? receptions.findIndex((row) => row.id === selected.id) : -1
  const previous = selectedIndex > 0 ? receptions[selectedIndex - 1] : undefined
  const next = selectedIndex >= 0 ? receptions[selectedIndex + 1] : undefined
  const filterNote = selected && range && (selected.arrivedAt < range.from || selected.arrivedAt >= range.to)
    ? 'Outside the current filter'
    : selected && selectedIndex < 0 ? 'Not in the loaded receptions' : undefined
  const announcement = selected ? filterNote ?? details?.announcement : undefined
  const rangeClock = range ? rangeFormatter(range) : clock

  function applyRange(next: HistogramRange | null) {
    if (next?.from === range?.from && next?.to === range?.to) return
    setRange(next)
  }

  function selectReception(row: PacketTableRow) {
    const index = receptions.indexOf(row)
    const announcement = `Reception ${index + 1} of ${receptions.length}: ${row.code}, ${row.region}, ${timestamp.format(Date.parse(row.arrivedAt))}`
    setDetails({ row, open: true, announcement })
  }

  function dismissDetails() {
    setDetails((current) => current?.open ? { ...current, open: false } : current)
  }

  function closeInspector() {
    const owner = inspector.current?.ownerDocument
    if (owner && (owner.activeElement === owner.body || inspector.current?.contains(owner.activeElement))) {
      const target = receptionReturnTarget(workspace.current, selected?.id)
      target?.focus({ preventScroll: target.hasAttribute('data-reception-id') })
    }
    dismissDetails()
  }

  function onDetailsKeys(event: KeyboardEvent<HTMLElement>) {
    if (!selected || !isPlainKey(event)) return
    if (inline && event.key === 'Escape' && !isOpenTooltipTrigger(event.target)) {
      event.preventDefault()
      closeInspector()
      return
    }
    const row = event.key === 'ArrowUp' ? previous : event.key === 'ArrowDown' ? next : undefined
    if (row) {
      event.preventDefault()
      selectReception(row)
    }
  }

  function onListKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (!inline || !selected || event.key !== 'Escape' || !isPlainKey(event) || isOpenTooltipTrigger(event.target)) return
    if (!(event.target instanceof Element) || !event.target.closest('[data-packet-table-scroll]')) return
    event.preventDefault()
    dismissDetails()
  }

  return (
    <section className="ks-section ks-packet-traffic" aria-labelledby={heading}>
      <div className="ks-section-heading">
        <h2 id={heading}>Packet traffic</h2>
        <p>24 hours · {count.format(totalReceptions)} sample receptions. Zoom the histogram to filter the table. Select a reception to view its details.</p>
      </div>
      <div className="ks-traffic-workspace" ref={workspace} data-inspecting={inline && selected ? '' : undefined}>
        <div className="ks-traffic-list" onKeyDown={onListKeys}>
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
              {inline && selected && <Button className="ks-traffic-clear" aria-controls={detailsId} onClick={() => inspector.current?.focus()}>Go to reception details</Button>}
            </div>
          </div>
          <PacketTable rows={rows} referenceTime={referenceTime} timeZone={timeZone} timeZoneLabel={timeZoneLabel} resetKey={resetKey} hasMore={hasMore} isLoadingMore={isLoadingMore}
            loadMore={loadMore} selectedId={selected?.id} onActivate={selectReception} onNavigate={inline && selected ? selectReception : undefined}
            selectionControls={inline ? detailsId : undefined} />
        </div>
        {inline && selected && <section className="ks-reception-details ks-reception-inspector" id={detailsId} ref={inspector} tabIndex={-1}
          aria-labelledby={title} aria-describedby={hint} onKeyDown={onDetailsKeys}>
          <ReceptionDetails drawer={false} title={title} hint={hint} row={selected} filterNote={filterNote} close={closeInspector} />
        </section>}
      </div>
      {inline && <div className="ks-visually-hidden" role="status">{announcement}</div>}
      {!inline && <Drawer side="end" modal open={details?.open ?? false} onOpenChange={(open) => {
        if (!open) dismissDetails()
      }}>
        <DrawerBackdrop className="ks-reception-drawer-backdrop" />
        <DrawerContent className="ks-reception-details ks-reception-drawer" initialFocus="content" finalFocus={() => receptionReturnTarget(workspace.current, details?.row.id)}
          onKeyDown={onDetailsKeys}>
          <ReceptionDetails drawer title={title} hint={hint} row={details?.row} announcement={announcement} filterNote={filterNote}
            previous={previous && (() => selectReception(previous))} next={next && (() => selectReception(next))} close={dismissDetails} />
        </DrawerContent>
      </Drawer>}
    </section>
  )
}

function isPlainKey(event: KeyboardEvent<HTMLElement>) {
  return !event.defaultPrevented && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey
}

function isOpenTooltipTrigger(target: EventTarget) {
  return target instanceof Element && target.closest('[data-slot="tooltip-trigger"][data-open]') !== null
}
