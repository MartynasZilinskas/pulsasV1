import { bindDrag, dragThreshold } from '@ui-sdk/core'
import { Popover, PopoverClose, PopoverContent, PopoverDescription, PopoverTitle, createPopoverController } from '@ui-sdk/react/popover'
import { useEffect, useId, useMemo, useRef, useState, type PointerEvent, type ReactNode } from 'react'
import { createTimestampFormatter } from './timestamp-format'
import './time-histogram.css'

export type HistogramRange = { from: string; to: string }
export type HistogramBucket = HistogramRange & { counts: Readonly<Record<string, number>> }
export type HistogramSeries = { key: string; label: string; color: string }

type BucketSpan = [first: number, last: number]

const countsFormat = new Intl.NumberFormat()

export function TimeHistogram({ buckets, series, measure, unit, timeZone = 'UTC', timeZoneLabel = timeZone, draftRange, appliedRange, onRangeZoom, renderRangeSummary, rangeFocusFallback }: {
  buckets: readonly HistogramBucket[]
  series: readonly HistogramSeries[]
  measure: string
  unit: string
  timeZone?: string
  timeZoneLabel?: string
  draftRange?: HistogramRange | null
  appliedRange?: HistogramRange | null
  onRangeZoom: (range: HistogramRange) => void
  renderRangeSummary: (range: HistogramRange) => ReactNode
  rangeFocusFallback: () => HTMLElement | null
}) {
  const heading = useId()
  const stopDrag = useRef<(() => void) | null>(null)
  const [hovered, setHovered] = useState<number | null>(null)
  const [brush, setBrush] = useState<BucketSpan | null>(null)
  const [selection, setSelection] = useState<HistogramRange | null>(null)
  const [confirmation] = useState(() => createPopoverController())
  const returnFocus = useRef<HTMLElement | null>(null)
  const format = useMemo(() => histogramFormats(timeZone, buckets), [timeZone, buckets])

  useEffect(() => () => { stopDrag.current?.() }, [])

  const rangeFor = (first: number, last: number): HistogramRange => ({ from: buckets[first]!.from, to: buckets[last]!.to })

  const beginBrush = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType !== 'mouse' || !event.isPrimary || event.button !== 0 || buckets.length === 0) return
    event.preventDefault()
    const plot = event.currentTarget
    const start = pointerBucket(plot, event.clientX, buckets.length)
    stopDrag.current?.()
    setHovered(null)
    setBrush([start, start])
    let end = start
    stopDrag.current = bindDrag(plot, event.nativeEvent, {
      threshold: dragThreshold,
      cancelOnEscape: true,
      onMove: ({ x }) => {
        end = pointerBucket(plot, x, buckets.length)
        setBrush([Math.min(start, end), Math.max(start, end)])
      },
      onEnd: (_event, canceled) => {
        stopDrag.current = null
        setBrush(null)
        setHovered(null)
        if (canceled) return
        const first = Math.min(start, end)
        const last = Math.max(start, end)
        const active = plot.ownerDocument.activeElement
        if (!active?.closest('.time-histogram-confirm')) {
          returnFocus.current = active instanceof HTMLElement && active !== plot.ownerDocument.body ? active : null
        }
        setSelection(rangeFor(first, last))
        void confirmation.open({ anchor: {
          getBoundingClientRect() {
            const { left, top, width, height } = plot.getBoundingClientRect()
            const binWidth = width / buckets.length
            return new DOMRect(left + first * binWidth, top, (last - first + 1) * binWidth, height)
          },
        } })
      },
    })
  }

  if (buckets.length === 0) {
    return <figure className="time-histogram" aria-labelledby={heading}>
      <figcaption id={heading}>{measure} <span>· {unit} · {timeZoneLabel}</span></figcaption>
      <p className="time-histogram-empty">No activity buckets for this period.</p>
    </figure>
  }

  const domainEnd = Date.parse(buckets.at(-1)!.to)
  const tickPositions = [...new Set([0, 0.25, 0.5, 0.75, 1].map((fraction) => Math.floor(buckets.length * fraction)))]
  const totals = buckets.map(({ counts }) => series.reduce((sum, { key }) => sum + (counts[key] ?? 0), 0))
  const peak = totals.reduce((max, total) => Math.max(max, total), 1)
  const hover = hovered !== null && !brush && !selection ? buckets[hovered] : undefined
  const preview = brush ? rangeFor(...brush) : selection ?? draftRange
  const range = preview ?? appliedRange
  const left = range ? bucketPosition(buckets, Date.parse(range.from)) : 0
  const right = range ? bucketPosition(buckets, Date.parse(range.to)) : 0

  return (
    <figure className="time-histogram" aria-labelledby={heading}>
      <figcaption id={heading}>{measure} <span>· {unit} · {timeZoneLabel}</span></figcaption>
      <div className="time-histogram-chart">
        <div className="time-histogram-plot" data-histogram-plot aria-hidden="true" onPointerDown={beginBrush}
          onPointerMove={(event) => {
            if (event.pointerType !== 'mouse' || event.buttons !== 0 || stopDrag.current) return
            setHovered(pointerBucket(event.currentTarget, event.clientX, buckets.length))
          }} onPointerLeave={() => setHovered(null)}>
          {buckets.map(({ from, counts }, index) => (
            <div className="time-histogram-bin" data-histogram-bucket={from} key={from}
              data-inspected={hover && index === hovered ? '' : undefined}>
              {series.map(({ key, color }) => counts[key] > 0 && (
                <span key={key} style={{ backgroundColor: color, height: `${100 * counts[key] / peak}%` }} />
              ))}
            </div>
          ))}
          {right > left && <span className="time-histogram-brush" data-applied={preview ? undefined : ''}
            style={{ left: `${left}%`, width: `${right - left}%` }} />}
        </div>
        <div className="time-histogram-axis" aria-hidden="true">
          {tickPositions.map((index) => (
            <span key={index} style={{ left: `${100 * index / buckets.length}%` }}>
              {format.axis.format(index === buckets.length ? domainEnd : Date.parse(buckets[index]!.from))}
            </span>
          ))}
        </div>
        {hover && hovered !== null && <div className="time-histogram-inspect" aria-hidden="true"
          style={{ left: `${100 * (hovered + 0.5) / buckets.length}%` }} data-align={hovered < buckets.length / 2 ? 'after' : 'before'}>
          <BucketBreakdown bucket={hover} series={series} unit={unit} formatter={format.detail} />
        </div>}
      </div>
      <Popover controller={confirmation} modal={false} onOpenChange={(open) => {
        if (!open) setSelection(null)
      }}>
        <PopoverContent className="time-histogram-confirm" side="top" sideOffset={5} collisionPadding={8}
          finalFocus={() => returnFocus.current?.isConnected ? returnFocus.current : rangeFocusFallback()}>
          <PopoverTitle>Selected time range</PopoverTitle>
          {selection && <PopoverDescription>{renderRangeSummary(selection)} <span className="time-histogram-confirm-zone">{timeZoneLabel}</span></PopoverDescription>}
          <div className="time-histogram-confirm-actions">
            <PopoverClose>Cancel</PopoverClose>
            <PopoverClose data-variant="primary" onClick={() => { if (selection) onRangeZoom(selection) }}>Zoom</PopoverClose>
          </div>
        </PopoverContent>
      </Popover>
    </figure>
  )
}

function histogramFormats(timeZone: string, buckets: readonly HistogramBucket[]) {
  const options: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }
  const detail = createTimestampFormatter(timeZone)
  if (buckets.length === 0) return { axis: new Intl.DateTimeFormat(undefined, options), detail }
  const start = Date.parse(buckets[0]!.from)
  const end = Date.parse(buckets.at(-1)!.to)
  const hasSubMinuteBounds = buckets.some(({ from, to }) => Date.parse(from) % 60_000 !== 0 || Date.parse(to) % 60_000 !== 0)
  const day = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeZone })
  if (day.format(start) !== day.format(end)) {
    options.month = 'short'
    options.day = 'numeric'
  }
  if (hasSubMinuteBounds || end - start < 5 * 60_000) options.second = '2-digit'
  return { axis: new Intl.DateTimeFormat(undefined, options), detail }
}

function bucketPosition(buckets: readonly HistogramBucket[], instant: number) {
  const index = buckets.findIndex(({ to }) => instant < Date.parse(to))
  if (index < 0) return 100
  const { from, to } = buckets[index]!
  const lower = Date.parse(from)
  const fraction = Math.max(0, (instant - lower) / (Date.parse(to) - lower))
  return (index + fraction) / buckets.length * 100
}

function pointerBucket(plot: HTMLElement, x: number, length: number) {
  const { left, width } = plot.getBoundingClientRect()
  return Math.max(0, Math.min(length - 1, Math.floor((x - left) / width * length)))
}

function BucketBreakdown({ bucket, series, unit, formatter }: {
  bucket: HistogramBucket
  series: readonly HistogramSeries[]
  unit: string
  formatter: Intl.DateTimeFormat
}) {
  const counts = series.map((item) => ({ ...item, count: bucket.counts[item.key] ?? 0 })).filter(({ count }) => count > 0)
  const total = counts.reduce((sum, { count }) => sum + count, 0)
  return <div className="time-histogram-breakdown">
    <dl className="time-histogram-bounds">
      <dt>From</dt><dd><time dateTime={bucket.from}>{formatter.format(Date.parse(bucket.from))}</time></dd>
      <dt>Until</dt><dd><time dateTime={bucket.to}>{formatter.format(Date.parse(bucket.to))}</time></dd>
    </dl>
    <strong>{countsFormat.format(total)} {unit}</strong>
    {total > 0 && <dl className="time-histogram-counts">
      {counts.map(({ key, label, color, count }) => <div key={key}>
        <dt><span style={{ backgroundColor: color }} />{label}</dt><dd>{countsFormat.format(count)}</dd>
      </div>)}
    </dl>}
  </div>
}
