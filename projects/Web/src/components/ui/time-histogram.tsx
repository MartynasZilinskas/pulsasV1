import { bindDrag, dragThreshold } from '@ui-sdk/core'
import { Button } from '@ui-sdk/react/button'
import { Popover, PopoverClose, PopoverContent, PopoverTitle, PopoverTrigger } from '@ui-sdk/react/popover'
import { Select, SelectContent, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@ui-sdk/react/select'
import { useEffect, useId, useMemo, useRef, useState, type PointerEvent } from 'react'
import { createTimestampFormatter } from './timestamp-format'
import './time-histogram.css'

export type HistogramRange = { from: string; to: string }
export type HistogramBucket = HistogramRange & { counts: Readonly<Record<string, number>> }
export type HistogramSeries = { key: string; label: string; color: string }

type BucketSpan = [first: number, last: number]

const countsFormat = new Intl.NumberFormat()

export function TimeHistogram({ buckets, series, measure, unit, timeZone = 'UTC', timeZoneLabel = timeZone, appliedRange = null, onRangeSelect }: {
  buckets: readonly HistogramBucket[]
  series: readonly HistogramSeries[]
  measure: string
  unit: string
  timeZone?: string
  timeZoneLabel?: string
  appliedRange?: HistogramRange | null
  onRangeSelect: (range: HistogramRange) => void
}) {
  const heading = useId()
  const picker = useRef<HTMLButtonElement>(null)
  const plot = useRef<HTMLDivElement>(null)
  const stopDrag = useRef<(() => void) | null>(null)
  const [hovered, setHovered] = useState<number | null>(null)
  const [brush, setBrush] = useState<BucketSpan | null>(null)
  const [staged, setStaged] = useState<HistogramRange | null>(null)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [first, setFirst] = useState<string | null>(null)
  const [last, setLast] = useState<string | null>(null)
  const format = useMemo(() => histogramFormats(timeZone, buckets), [timeZone, buckets])
  const choices = useMemo(() => buckets.map(({ from, to }) => ({
    value: from,
    label: `${format.choice.format(Date.parse(from))} – ${format.choice.format(Date.parse(to))}`,
  })), [buckets, format])

  useEffect(() => () => { stopDrag.current?.() }, [])

  const getSpan = (range: HistogramRange | null): BucketSpan | null => {
    if (!range) return null
    const start = buckets.findIndex(({ from }) => from === range.from)
    const end = buckets.findIndex(({ to }) => to === range.to)
    return start >= 0 && end >= start ? [start, end] : null
  }
  const stagedSpan = getSpan(staged)
  const highlighted = brush ?? stagedSpan ?? getSpan(appliedRange)
  const firstIndex = buckets.findIndex(({ from }) => from === first)
  const lastIndex = buckets.findIndex(({ from }) => from === last)
  const rangeFor = (start: number, end: number): HistogramRange => ({ from: buckets[start]!.from, to: buckets[end]!.to })

  const beginBrush = (event: PointerEvent<HTMLDivElement>) => {
    if (!event.isPrimary || event.button !== 0 || buckets.length === 0) return
    const plot = event.currentTarget
    const start = pointerBucket(plot, event.clientX, buckets.length)
    stopDrag.current?.()
    setHovered(null)
    setStaged(null)
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
        if (!canceled) setStaged(rangeFor(Math.min(start, end), Math.max(start, end)))
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
  const hover = hovered !== null && !brush && !stagedSpan && !pickerOpen ? buckets[hovered] : undefined
  const pickerSpan = firstIndex >= 0 && lastIndex >= firstIndex ? rangeFor(firstIndex, lastIndex) : null
  const selectionAnchor = stagedSpan && {
    getBoundingClientRect: () => {
      const bounds = plot.current?.getBoundingClientRect()
      if (!bounds) return new DOMRect()
      const width = bounds.width / buckets.length
      return new DOMRect(bounds.left + stagedSpan[0] * width, bounds.top, (stagedSpan[1] - stagedSpan[0] + 1) * width, bounds.height)
    },
  }

  function breakdown(start: number, end: number) {
    return series.map(({ key, label, color }) => ({
      key, label, color,
      count: buckets.slice(start, end + 1).reduce((sum, bucket) => sum + (bucket.counts[key] ?? 0), 0),
    }))
  }

  return (
    <figure className="time-histogram" aria-labelledby={heading}>
      <figcaption id={heading}>{measure} <span>· {unit} · {timeZoneLabel}</span></figcaption>
      <div className="time-histogram-picker">
        <Popover open={pickerOpen} onOpenChange={(open) => {
          if (open) {
            const [start, end] = stagedSpan ?? getSpan(appliedRange) ?? [0, 0]
            setFirst(buckets[start]!.from)
            setLast(buckets[end]!.from)
            setStaged(null)
            setHovered(null)
          }
          setPickerOpen(open)
        }}>
          <PopoverTrigger ref={picker} className="time-histogram-button">Choose time range</PopoverTrigger>
          <PopoverContent className="time-histogram-panel" side="bottom" align="end" sideOffset={4} collisionPadding={8}>
            <PopoverTitle className="time-histogram-panel-title">Time range ({timeZoneLabel})</PopoverTitle>
            <div className="time-histogram-fields">
              <BucketSelect label="First bucket" choices={choices} value={first} onChange={setFirst} />
              <BucketSelect label="Last bucket" choices={choices} value={last} onChange={setLast} />
            </div>
            {pickerSpan ? <BucketBreakdown range={pickerSpan} counts={breakdown(firstIndex, lastIndex)} unit={unit} formatter={format.detail} />
              : <p className="time-histogram-hint">Last bucket must be the same as or later than the first.</p>}
            <div className="time-histogram-actions">
              <PopoverClose className="time-histogram-button">Close</PopoverClose>
              <Button className="time-histogram-button" data-variant="primary" disabled={!pickerSpan} onClick={() => {
                setPickerOpen(false)
                setStaged(pickerSpan)
              }}>Stage range</Button>
            </div>
          </PopoverContent>
        </Popover>
      </div>
      <div className="time-histogram-chart">
        <div className="time-histogram-plot" ref={plot} data-histogram-plot aria-hidden="true" onPointerDown={beginBrush}
          onPointerMove={(event) => {
            if (event.pointerType !== 'mouse' || stopDrag.current) return
            setHovered(pointerBucket(event.currentTarget, event.clientX, buckets.length))
          }} onPointerLeave={() => setHovered(null)}>
          {buckets.map(({ from, counts }, index) => (
            <div className="time-histogram-bin" data-histogram-bucket={from} key={from}
              data-dimmed={highlighted && (index < highlighted[0] || index > highlighted[1]) ? '' : undefined}
              data-inspected={hover && index === hovered ? '' : undefined}>
              {series.map(({ key, color }) => counts[key] > 0 && (
                <span key={key} style={{ backgroundColor: color, height: `${100 * counts[key] / peak}%` }} />
              ))}
            </div>
          ))}
          {highlighted && <span className="time-histogram-brush" data-applied={!brush && !stagedSpan ? '' : undefined}
            style={{ left: `${100 * highlighted[0] / buckets.length}%`, width: `${100 * (highlighted[1] - highlighted[0] + 1) / buckets.length}%` }} />}
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
          <BucketBreakdown range={hover} counts={breakdown(hovered, hovered)} unit={unit} formatter={format.detail} />
        </div>}
      </div>
      <Popover open={stagedSpan !== null && !pickerOpen} onOpenChange={(open) => { if (!open) setStaged(null) }}>
        <PopoverContent className="time-histogram-panel" anchor={selectionAnchor} side="top" sideOffset={3}
          collisionPadding={8} finalFocus={() => picker.current}>
          <PopoverTitle className="time-histogram-panel-title">Staged time range ({timeZoneLabel})</PopoverTitle>
          {stagedSpan && staged && <BucketBreakdown range={staged} counts={breakdown(...stagedSpan)} unit={unit} formatter={format.detail} />}
          <div className="time-histogram-actions">
            <PopoverClose className="time-histogram-button">Cancel</PopoverClose>
            <Button className="time-histogram-button" data-variant="primary" disabled={!stagedSpan} onClick={() => {
              if (stagedSpan && staged) onRangeSelect(staged)
              setStaged(null)
            }}>Zoom</Button>
          </div>
        </PopoverContent>
      </Popover>
    </figure>
  )
}

function histogramFormats(timeZone: string, buckets: readonly HistogramBucket[]) {
  const options: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }
  const detail = createTimestampFormatter(timeZone)
  if (buckets.length === 0) {
    const clock = new Intl.DateTimeFormat(undefined, options)
    return { axis: clock, choice: clock, detail }
  }
  const start = Date.parse(buckets[0]!.from)
  const end = Date.parse(buckets.at(-1)!.to)
  const hasSubMinuteBounds = buckets.some(({ from, to }) => Date.parse(from) % 60_000 !== 0 || Date.parse(to) % 60_000 !== 0)
  const day = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeZone })
  if (day.format(start) !== day.format(end)) {
    options.month = 'short'
    options.day = 'numeric'
  }
  if (hasSubMinuteBounds || end - start < 5 * 60_000) options.second = '2-digit'
  const offset = (instant: number) => detail.formatToParts(instant).find(({ type }) => type === 'timeZoneName')?.value
  const axis = new Intl.DateTimeFormat(undefined, options)
  const choice = offset(start) === offset(end) ? axis : new Intl.DateTimeFormat(undefined, { ...options, timeZoneName: 'shortOffset' })
  return { axis, choice, detail }
}

function pointerBucket(plot: HTMLElement, x: number, length: number) {
  const { left, width } = plot.getBoundingClientRect()
  return Math.max(0, Math.min(length - 1, Math.floor((x - left) / width * length)))
}

function BucketSelect({ label, choices, value, onChange }: {
  label: string
  choices: { value: string; label: string }[]
  value: string | null
  onChange: (value: string | null) => void
}) {
  return <div className="time-histogram-field">
    <Select items={choices} value={value} onValueChange={onChange}>
      <SelectLabel>{label}</SelectLabel>
      <SelectTrigger className="time-histogram-button time-histogram-select"><SelectValue /></SelectTrigger>
      <SelectContent className="time-histogram-options" align="start" sideOffset={4} collisionPadding={8}>
        {choices.map(({ value, label }) => <SelectItem key={value} value={value} label={label}>{label}</SelectItem>)}
      </SelectContent>
    </Select>
  </div>
}

function BucketBreakdown({ range, counts, unit, formatter }: {
  range: HistogramRange
  counts: readonly (HistogramSeries & { count: number })[]
  unit: string
  formatter: Intl.DateTimeFormat
}) {
  const total = counts.reduce((sum, series) => sum + series.count, 0)
  return <div className="time-histogram-breakdown">
    <dl className="time-histogram-bounds">
      <dt>From</dt><dd><time dateTime={range.from}>{formatter.format(Date.parse(range.from))}</time></dd>
      <dt>Until</dt><dd><time dateTime={range.to}>{formatter.format(Date.parse(range.to))}</time></dd>
    </dl>
    <strong>{countsFormat.format(total)} {unit}</strong>
    {total > 0 && <dl className="time-histogram-counts">
      {counts.filter(({ count }) => count > 0).map(({ key, label, color, count }) => <div key={key}>
        <dt><span style={{ backgroundColor: color }} />{label}</dt><dd>{countsFormat.format(count)}</dd>
      </div>)}
    </dl>}
  </div>
}
