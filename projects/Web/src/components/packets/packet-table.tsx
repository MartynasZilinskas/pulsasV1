import { Button } from '@ui-sdk/react/button'
import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'
import { flushSync } from 'react-dom'
import { LucideGlyph, type LucideGlyphName } from '../ui/lucide-glyph'
import { createTimestampFormatter } from '../ui/timestamp-format'
import { PacketTypeBadge, type PacketTypeCode } from './packet-type-badge'
import { RouteBadge, type RouteKind } from './route-badge'
import './packet-table.css'

export type PacketTableRow = {
  id: string
  code: string
  packetType?: PacketTypeCode
  route: RouteKind
  scope?: string
  summary: string
  region: string
  arrivedAt: string
}

export type PacketTableStatusRow = {
  id: string
  status: 'live' | 'resumed' | 'lost-connection'
  occurredAt?: string
}

const statusPresentation = {
  live: { label: 'Live packets', icon: 'radio', tone: 'green' },
  resumed: { label: 'Resumed', icon: 'check', tone: 'green' },
  'lost-connection': { label: 'Lost connection', icon: 'circle-dashed', tone: 'amber' },
} satisfies Record<PacketTableStatusRow['status'], { label: string; icon: LucideGlyphName; tone: string }>

const columns = ['Code', 'Type', 'Transport', 'Summary', 'Region', 'Age']
const rowHeight = 40
const overscan = 6
const loadAhead = 16 * rowHeight
const clickSlop = 6
const relative = new Intl.RelativeTimeFormat(undefined, { style: 'narrow', numeric: 'auto' })

function isStatusRow(row: PacketTableRow | PacketTableStatusRow): row is PacketTableStatusRow {
  return 'status' in row
}

function rowTabStop(row: HTMLTableRowElement, position: 'first' | 'last') {
  const rowStop = row.tabIndex >= 0 ? row : undefined
  if (position === 'first' && rowStop) return rowStop
  const buttons = row.querySelectorAll<HTMLButtonElement>('button:not(:disabled):not([tabindex="-1"])')
  return buttons[position === 'first' ? 0 : buttons.length - 1] ?? rowStop
}

function ageLabel(timestamp: string, referenceTime: string) {
  const seconds = Math.max(0, Math.floor((Date.parse(referenceTime) - Date.parse(timestamp)) / 1000))
  if (seconds < 60) return relative.format(-seconds, 'second')
  if (seconds < 3600) return relative.format(-Math.floor(seconds / 60), 'minute')
  if (seconds < 86400) return relative.format(-Math.floor(seconds / 3600), 'hour')
  return relative.format(-Math.floor(seconds / 86400), 'day')
}

export function PacketTable({ rows, referenceTime, timeZone = 'UTC', timeZoneLabel = timeZone, resetKey, hasMore, isLoadingMore, loadMore, selectedId, onActivate, onNavigate, selectionControls, isLoading = false, error, retry }: {
  rows: readonly (PacketTableRow | PacketTableStatusRow)[]
  referenceTime: string
  timeZone?: string
  timeZoneLabel?: string
  resetKey: string
  hasMore: boolean
  isLoadingMore: boolean
  loadMore: () => void
  selectedId?: string
  onActivate?: (row: PacketTableRow) => void
  onNavigate?: (row: PacketTableRow) => void
  selectionControls?: string
  isLoading?: boolean
  error?: string
  retry?: () => void
}) {
  const caption = useId()
  const scroller = useRef<HTMLDivElement>(null)
  const head = useRef<HTMLTableSectionElement>(null)
  const tail = useRef<HTMLDivElement>(null)
  const tailAction = useRef<HTMLButtonElement>(null)
  const lastRequested = useRef<{ key: string; count: number } | null>(null)
  const loading = useRef(false)
  const previousResetKey = useRef(resetKey)
  const previouslyLoading = useRef(isLoadingMore)
  const committedSelectedId = useRef(selectedId)
  const [viewport, setViewport] = useState({ top: 0, height: 440 })
  const [focusedId, setFocusedId] = useState<string | null>(null)
  const timestamp = useMemo(() => createTimestampFormatter(timeZone), [timeZone])
  const clock = useMemo(() => new Intl.DateTimeFormat(undefined, {
    timeZone, hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }), [timeZone])
  const packetCount = useMemo(() => rows.reduce((count, row) => isStatusRow(row) ? count : count + 1, 0), [rows])

  const readViewport = useCallback(() => {
    const root = scroller.current
    if (!root) return
    const { scrollTop: top, clientHeight: height } = root
    setViewport((previous) => previous.top === top && previous.height === height ? previous : { top, height })
  }, [])

  const scrollRowIntoView = useCallback((root: HTMLDivElement, index: number) => {
    const headHeight = head.current?.offsetHeight ?? 0
    const rowTop = index * rowHeight
    if (rowTop < root.scrollTop) root.scrollTop = rowTop
    else if (rowTop + rowHeight + headHeight > root.scrollTop + root.clientHeight) {
      root.scrollTop = rowTop + rowHeight + headHeight - root.clientHeight
    }
  }, [])

  useLayoutEffect(() => {
    if (previousResetKey.current !== resetKey) {
      previousResetKey.current = resetKey
      if (scroller.current) scroller.current.scrollTop = 0
      lastRequested.current = null
      loading.current = false
      setFocusedId(null)
    }
    readViewport()
  }, [resetKey, readViewport])

  useLayoutEffect(() => {
    const previous = committedSelectedId.current
    committedSelectedId.current = selectedId
    const root = scroller.current
    if (!root || previous === undefined || selectedId === undefined || previous === selectedId) return
    const index = rows.findIndex((row) => !isStatusRow(row) && row.id === selectedId)
    if (index < 0) return
    scrollRowIntoView(root, index)
    readViewport()
  }, [readViewport, rows, scrollRowIntoView, selectedId])

  useEffect(() => {
    const root = scroller.current
    if (!root) return
    const resize = new ResizeObserver(readViewport)
    resize.observe(root)
    return () => resize.disconnect()
  }, [readViewport])

  useLayoutEffect(() => {
    if (!isLoadingMore && (previouslyLoading.current || error || lastRequested.current?.count !== packetCount)) {
      loading.current = false
    }
    previouslyLoading.current = isLoadingMore
  }, [error, isLoadingMore, packetCount])

  const requestMore = useCallback((automatic: boolean) => {
    if (!hasMore || isLoading || isLoadingMore || error || loading.current) return
    if (automatic && lastRequested.current?.key === resetKey && lastRequested.current.count === packetCount) return
    lastRequested.current = { key: resetKey, count: packetCount }
    loading.current = true
    loadMore()
  }, [error, hasMore, isLoading, isLoadingMore, loadMore, resetKey, packetCount])

  useEffect(() => {
    const root = scroller.current
    const marker = tail.current
    if (!root || !marker || !hasMore || isLoading || isLoadingMore || error) return
    const check = () => {
      if (root.clientHeight > 0 && marker.getBoundingClientRect().top <= root.getBoundingClientRect().top + root.clientHeight + loadAhead) requestMore(true)
    }
    const observer = new IntersectionObserver(check, { root, rootMargin: `0px 0px ${loadAhead}px 0px` })
    const resize = new ResizeObserver(check)
    observer.observe(marker)
    resize.observe(root)
    check()
    return () => { observer.disconnect(); resize.disconnect() }
  }, [error, hasMore, isLoading, isLoadingMore, requestMore])

  const start = Math.min(rows.length, Math.max(0, Math.floor(viewport.top / rowHeight) - overscan))
  const end = Math.min(rows.length, Math.ceil((viewport.top + viewport.height) / rowHeight) + overscan)
  const focusedIndex = focusedId === null ? -1 : rows.findIndex((row) => !isStatusRow(row) && row.id === focusedId)
  const indexes = Array.from({ length: end - start }, (_, index) => start + index)
  // Keep a focused row mounted even when it leaves the visible window.
  if (focusedIndex >= 0 && !indexes.includes(focusedIndex)) {
    if (focusedIndex < start) indexes.unshift(focusedIndex)
    else indexes.push(focusedIndex)
  }

  function nextPacketIndex(from: number, direction: 1 | -1) {
    for (let index = from + direction; index >= 0 && index < rows.length; index += direction) {
      if (!isStatusRow(rows[index]!)) return index
    }
    return -1
  }

  function focusRow(index: number, position: 'first' | 'last') {
    const root = scroller.current
    const row = rows[index]
    if (!root || !row || isStatusRow(row)) return
    scrollRowIntoView(root, index)
    flushSync(() => { setFocusedId(row.id); readViewport() })
    const reception = root.querySelector<HTMLTableRowElement>(`tr[data-reception-id][data-row-index="${index}"]`)
    if (!reception) return
    const target = rowTabStop(reception, position)
    target?.focus({ preventScroll: true })
    if (target && target !== reception) {
      const focus = target.getBoundingClientRect()
      const frame = root.getBoundingClientRect()
      if (focus.left < frame.left) root.scrollLeft += focus.left - frame.left - 4
      if (focus.right > frame.right) root.scrollLeft += focus.right - frame.right + 4
    }
  }

  function onTableKeys(event: KeyboardEvent<HTMLDivElement>) {
    const target = event.target
    if (!(target instanceof Element)) return
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return
    const row = target.closest<HTMLTableRowElement>('tr[data-reception-id]')
    if (event.key === 'Tab') {
      const backward = event.shiftKey
      let from: number
      if (!backward && target === scroller.current) from = Math.floor(scroller.current.scrollTop / rowHeight) - 1
      else if (backward && target === tailAction.current) from = rows.length
      else if (row && target === rowTabStop(row, backward ? 'first' : 'last')) from = Number(row.dataset.rowIndex)
      else return
      const next = nextPacketIndex(from, backward ? -1 : 1)
      if (next >= 0) {
        event.preventDefault()
        focusRow(next, backward ? 'last' : 'first')
      }
      return
    }
    if (row && target === row && !event.shiftKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      const next = nextPacketIndex(Number(row.dataset.rowIndex), event.key === 'ArrowUp' ? -1 : 1)
      if (next >= 0) {
        event.preventDefault()
        focusRow(next, 'first')
        const nextRow = rows[next]
        if (nextRow && !isStatusRow(nextRow)) onNavigate?.(nextRow)
      }
    }
  }

  const spacer = (count: number) => count > 0 && <tr aria-hidden="true" className="packet-reception-table-spacer">
    <td colSpan={columns.length} style={{ height: count * rowHeight }} />
  </tr>
  let cursor = 0

  return (
    <div className="packet-reception-table-scroll" data-packet-table-scroll ref={scroller} tabIndex={0} role="region" aria-label="Reception list scroll area"
      onScroll={readViewport} onKeyDown={onTableKeys} onFocusCapture={(event) => {
        const row = (event.target as Element).closest<HTMLTableRowElement>('[data-reception-id]')
        setFocusedId(row?.dataset.receptionId ?? null)
      }} onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setFocusedId(null)
      }}>
      <table className="packet-reception-table" aria-labelledby={caption} aria-rowcount={rows.length ? rows.length + 1 : undefined}>
        <caption className="ks-visually-hidden" id={caption}>Packet receptions</caption>
        <colgroup><col className="packet-col-code" /><col className="packet-col-type" /><col className="packet-col-route" />
          <col /><col className="packet-col-region" /><col className="packet-col-age" /></colgroup>
        <thead ref={head}>
          <tr>{columns.map((title) => <th scope="col" key={title}>{title}</th>)}</tr>
        </thead>
        <tbody>
          {indexes.map((index) => {
            const row = rows[index]!
            const gap = index - cursor
            cursor = index + 1
            if (isStatusRow(row)) return <PacketStatusEntry key={`status:${row.id}`} leading={spacer(gap)} row={row} index={index}
              clock={clock} timestamp={timestamp} timeZoneLabel={timeZoneLabel} />
            return <PacketTableEntry key={`packet:${row.id}`} leading={spacer(gap)} row={row} index={index} referenceTime={referenceTime}
              arrivalLabel={`${timestamp.format(Date.parse(row.arrivedAt))} (${timeZoneLabel})`} selected={selectedId === row.id}
              onActivate={onActivate} selectionControls={selectionControls} />
          })}
          {spacer(rows.length - cursor)}
          {!rows.length && !hasMore && !isLoading && !error && <tr className="packet-reception-table-message">
            <td colSpan={columns.length}>No receptions match the current filters.</td>
          </tr>}
        </tbody>
      </table>
      {(rows.length > 0 || isLoading || hasMore || error) && <div className="packet-reception-table-tail" ref={tail}>
        {error ? <><span role="alert">{error}</span>{retry && <Button ref={tailAction} className="packet-reception-table-action" onClick={retry}>Retry</Button>}</> :
          isLoading ? <span role="status">Loading receptions…</span> :
            <Button ref={tailAction} className="packet-reception-table-action" aria-label={hasMore ? 'Load older receptions' : undefined}
              disabled={isLoadingMore || !hasMore} focusableWhenDisabled={isLoadingMore || !hasMore}
              onClick={() => requestMore(false)}>
              {isLoadingMore ? 'Loading older receptions…' : hasMore ? 'Load older receptions' : 'End of available history.'}
            </Button>}
        <span className="ks-visually-hidden" role="status">{isLoadingMore && 'Loading older receptions…'}</span>
      </div>}
    </div>
  )
}

function PacketStatusEntry({ leading, row, index, clock, timestamp, timeZoneLabel }: {
  leading: ReactNode
  row: PacketTableStatusRow
  index: number
  clock: Intl.DateTimeFormat
  timestamp: Intl.DateTimeFormat
  timeZoneLabel: string
}) {
  const { label, icon, tone } = statusPresentation[row.status]
  const instant = row.occurredAt ? Date.parse(row.occurredAt) : undefined
  const absolute = instant === undefined ? undefined : `${timestamp.format(instant)} (${timeZoneLabel})`
  return <>{leading}<tr className="packet-feed-status" data-feed-status={row.status} data-tone={tone} data-row-index={index} aria-rowindex={index + 2}>
    <td colSpan={columns.length}>
      <div className="packet-feed-status-content">
        <LucideGlyph name={icon} />
        <strong>{label}</strong>
        {instant !== undefined && <time dateTime={row.occurredAt} title={absolute}>
          <span aria-hidden="true">{clock.format(instant)}</span><span className="ks-visually-hidden">{absolute}</span>
        </time>}
      </div>
    </td>
  </tr></>
}

function isPrimaryUnmodifiedClick(event: MouseEvent) {
  return !event.defaultPrevented && event.button === 0 && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey
}

function passiveRowTarget(row: HTMLTableRowElement, target: EventTarget) {
  if (target === row) return row
  if (!(target instanceof Element) || !row.contains(target) || target.closest('button, [data-slot="tooltip-content"], .packet-reception-copy')) return null
  const cell = target.closest('td')
  return cell?.parentElement === row ? cell : null
}

function PacketTableEntry({ leading, row, index, referenceTime, arrivalLabel, selected, onActivate, selectionControls }: {
  leading: ReactNode
  row: PacketTableRow
  index: number
  referenceTime: string
  arrivalLabel: string
  selected: boolean
  onActivate?: (row: PacketTableRow) => void
  selectionControls?: string
}) {
  const press = useRef<{ target: HTMLTableRowElement | HTMLTableCellElement; pointerId: number; x: number; y: number } | null>(null)
  const cancelPress = () => { press.current = null }

  return <>{leading}<tr className="packet-reception-row" data-reception-id={row.id} data-row-index={index}
    data-selected={selected ? '' : undefined} data-activatable={onActivate ? '' : undefined} aria-rowindex={index + 2}
    tabIndex={onActivate ? 0 : undefined} aria-current={selected || undefined} aria-controls={selected ? selectionControls : undefined}
    aria-description={onActivate ? 'Press Enter or Space to view reception details.' : undefined}
    onKeyDown={onActivate && ((event) => {
      if (event.target !== event.currentTarget || event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return
      if (event.key !== 'Enter' && event.key !== ' ') return
      event.preventDefault()
      if (!event.repeat) onActivate(row)
    })}
    onPointerDown={onActivate && ((event) => {
      const target = passiveRowTarget(event.currentTarget, event.target)
      press.current = target && event.isPrimary && isPrimaryUnmodifiedClick(event)
        ? { target, pointerId: event.pointerId, x: event.clientX, y: event.clientY }
        : null
    })}
    onPointerMove={onActivate && ((event) => {
      const down = press.current
      if (down?.pointerId === event.pointerId && Math.hypot(event.clientX - down.x, event.clientY - down.y) > clickSlop) cancelPress()
    })}
    onPointerLeave={onActivate && ((event) => {
      // Touch fires pointerleave on release before the synthesized click.
      if (event.pointerType === 'mouse') cancelPress()
    })}
    onPointerCancel={onActivate && cancelPress}
    onDragStart={onActivate && cancelPress}
    onContextMenu={onActivate && cancelPress}
    onClick={onActivate && ((event) => {
      const down = press.current
      cancelPress()
      const target = passiveRowTarget(event.currentTarget, event.target)
      if (!target || !isPrimaryUnmodifiedClick(event)) return
      if (event.detail !== 0) {
        if (!down || down.target !== target || Math.hypot(event.clientX - down.x, event.clientY - down.y) > clickSlop) return
        const selection = event.currentTarget.ownerDocument.getSelection()
        if (selection && !selection.isCollapsed && selection.getRangeAt(0).intersectsNode(event.currentTarget)) return
      }
      event.currentTarget.focus({ preventScroll: true })
      onActivate(row)
    })}>
    <td><code className="packet-reception-code" title={row.code}>{row.code}</code></td>
    <td className="packet-reception-type"><PacketTypeBadge code={row.packetType} /></td>
    <td><RouteBadge route={row.route} scope={row.scope} /></td>
    <td><span className="packet-reception-summary packet-reception-copy" title={row.summary}>{row.summary}</span></td>
    <td><span className="packet-reception-summary" title={row.region}>{row.region}</span></td>
    <td><time dateTime={row.arrivedAt} title={arrivalLabel} className="packet-reception-age">
      {ageLabel(row.arrivedAt, referenceTime)}<span className="ks-visually-hidden"> · {arrivalLabel}</span>
    </time></td>
  </tr></>
}
