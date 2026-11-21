import { isISODate } from '@ui-sdk/core/date'
import { Button } from '@ui-sdk/react/button'
import { Calendar, CalendarGrid, CalendarHeading, CalendarNext, CalendarPrevious, type CalendarValue } from '@ui-sdk/react/calendar'
import { Collapsible, CollapsiblePanel, CollapsibleTrigger } from '@ui-sdk/react/collapsible'
import { Drawer, DrawerBackdrop, DrawerClose, DrawerContent, DrawerDescription, DrawerTitle } from '@ui-sdk/react/drawer'
import { Field, FieldDescription, FieldError, FieldLabel } from '@ui-sdk/react/field'
import { Input } from '@ui-sdk/react/input'
import { Popover, PopoverClose, PopoverContent, PopoverDescription, PopoverTitle, PopoverTrigger } from '@ui-sdk/react/popover'
import { useId, useMemo, useRef, useState, useSyncExternalStore, type FormEvent, type KeyboardEvent } from 'react'
import { flushSync } from 'react-dom'
import { PacketTable, type PacketTableRow } from '../../components/packets/packet-table'
import { PacketTypeBadge, type PacketTypeCode } from '../../components/packets/packet-type-badge'
import { RouteBadge } from '../../components/packets/route-badge'
import { SearchableMultiSelect } from '../../components/ui/searchable-multi-select'
import { TimeHistogram, type HistogramRange } from '../../components/ui/time-histogram'
import { createTimestampFormatter } from '../../components/ui/timestamp-format'
import { formatArrivalDateTime, parseArrivalTime, withArrivalDate } from './arrival-time-range'
import { usePacketTrafficDemo, type TrafficFilterGroup } from './use-packet-traffic-demo'
import './packet-traffic-specimens.css'

const count = new Intl.NumberFormat()
const timeZone = 'Europe/Vilnius'
const timeZoneLabel = 'Vilnius'
const offsetFormatter = new Intl.DateTimeFormat('en', { timeZone, timeZoneName: 'shortOffset' })
const timestamp = createTimestampFormatter(timeZone)
const inlineDetailsQuery = '(min-width: 64rem)'
const filtersRailQuery = '(min-width: 72rem)'
const detailsTitle = 'Reception details'
const detailsHint = 'Use the Up and Down arrow keys to browse loaded receptions.'
const closeIcon = <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
  <path d="m6 6 12 12M6 18 18 6" />
</svg>

function subscribeToTrafficLayout(notify: () => void) {
  const layouts = [inlineDetailsQuery, filtersRailQuery].map((query) => window.matchMedia(query))
  for (const layout of layouts) layout.addEventListener('change', notify)
  return () => { for (const layout of layouts) layout.removeEventListener('change', notify) }
}

function trafficLayout() {
  if (window.matchMedia(filtersRailQuery).matches) return 'rail'
  return window.matchMedia(inlineDetailsQuery).matches ? 'inspector' : 'compact'
}

function RangeSummary({ range }: { range: HistogramRange }) {
  const values = rangeValues(range)
  const [fromDay] = values.from.split(' ')
  const [toDay, toTime] = values.to.split(' ')
  const offset = (instant: string) => offsetFormatter.formatToParts(Date.parse(instant)).find(({ type }) => type === 'timeZoneName')?.value
  const fromOffset = offset(range.from)
  const toOffset = offset(range.to)
  const showOffsets = fromOffset !== toOffset
  return <><time dateTime={range.from}>{values.from}{showOffsets && ` ${fromOffset}`}</time>
    {' – '}<time dateTime={range.to}>{fromDay === toDay ? toTime : values.to}{showOffsets && ` ${toOffset}`}</time></>
}

function rangeValues(range: HistogramRange | null): HistogramRange {
  const format = (instant: string) => formatArrivalDateTime(instant, timeZone)
  return range ? { from: format(range.from), to: format(range.to) } : { from: '', to: '' }
}

function arrivalDate(value: string) {
  const date = value.trim().split(/[ T]/, 1)[0]
  return isISODate(date) ? date : undefined
}

function readBound(value: string): { instant: string } | { message: string } {
  if (!value.trim()) return { message: 'Enter a date and time.' }
  if (isISODate(value.trim())) return { message: 'Enter a time after the date.' }
  const result = parseArrivalTime(value, timeZone)
  if ('instant' in result) return result
  switch (result.error) {
    case 'invalid': return { message: 'Enter a valid date and time.' }
    case 'nonexistent': return { message: 'This time is skipped when the clocks change. Choose another time.' }
    case 'ambiguous': return { message: 'This time occurs twice when the clocks change. Choose another time.' }
  }
}

function validateArrivalRange(values: HistogramRange) {
  const from = readBound(values.from)
  const to = readBound(values.to)
  if ('message' in from || 'message' in to) return {
    fromError: 'message' in from ? from.message : undefined,
    toError: 'message' in to ? to.message : undefined,
  }
  if (Date.parse(from.instant) >= Date.parse(to.instant)) return { toError: 'To must be later than From.' }
  return { range: { from: from.instant, to: to.instant } }
}

function ArrivalCalendar({ dates, month, onDatesChange }: {
  dates: CalendarValue<'range'>
  month: string
  onDatesChange: (from: string, to: string) => void
}) {
  return <Calendar className="ks-arrival-calendar" aria-label="Arrival dates" mode="range" value={dates} defaultMonth={month}
    locale="lt-LT" timeZone={timeZone} weekdayFormat="short" fixedWeeks onValueChange={(next) => {
      if (Array.isArray(next) && next[0] && next[1]) onDatesChange(next[0], next[1])
    }}>
    <div className="ks-arrival-month">
      <CalendarPrevious className="ks-arrival-month-step" type="button">
        <svg aria-hidden="true" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="m10 4-4 4 4 4" /></svg>
      </CalendarPrevious>
      <CalendarHeading className="ks-arrival-month-title" lang="lt-LT" render={<h3 />} />
      <CalendarNext className="ks-arrival-month-step" type="button">
        <svg aria-hidden="true" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="m6 4 4 4-4 4" /></svg>
      </CalendarNext>
    </div>
    <CalendarGrid lang="lt-LT" />
    <p className="ks-arrival-note">Pick two dates. For a single-day range, pick the same date twice.</p>
  </Calendar>
}

function TrafficFilterSection({ group, values, onValuesChange }: {
  group: TrafficFilterGroup
  values: readonly string[]
  onValuesChange: (values: readonly string[]) => void
}) {
  const selectionStatus = useId()

  return <Collapsible className="ks-traffic-filter-section">
    <CollapsibleTrigger className="ks-traffic-filter-heading" aria-describedby={values.length ? selectionStatus : undefined}>
      <span>{group.label}</span>
      {values.length > 0 && <span className="ks-traffic-filter-selected" id={selectionStatus} aria-hidden="true">{count.format(values.length)} selected</span>}
      <svg className="ks-traffic-filter-chevron" aria-hidden="true" focusable="false" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
        <path d="m4 6 4 4 4-4" />
      </svg>
    </CollapsibleTrigger>
    <CollapsiblePanel className="ks-traffic-filter-options">
      <SearchableMultiSelect label={`${group.label} options`} searchLabel={`Search ${group.label.toLowerCase()}`}
        options={group.options} value={values} onValueChange={onValuesChange}
        renderOptionLabel={group.key === 'packetType'
          ? ({ value }) => <PacketTypeBadge code={value === 'unavailable' ? undefined : value as PacketTypeCode} passive />
          : undefined} />
    </CollapsiblePanel>
  </Collapsible>
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
      <div><dt>Transport</dt><dd><RouteBadge route={row.route} /></dd></div>
      <div className="ks-reception-fact-wide">
        <dt>Scope</dt>
        <dd>{row.scope ? <code data-tone="green">{row.scope}</code> : <span className="ks-reception-no-scope">No scope</span>}</dd>
      </div>
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
      <Title id={title}>{detailsTitle}</Title>
      {drawer
        ? <DrawerClose className="ks-reception-details-close" aria-label="Close reception details">{closeIcon}</DrawerClose>
        : <Button className="ks-reception-details-close" aria-label="Close reception details" onClick={close}>{closeIcon}</Button>}
    </header>
    <Hint id={hint} className="ks-visually-hidden">{detailsHint}</Hint>
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
  const rangeStatus = useId()
  const activeFilterStatus = useId()
  const detailsId = useId()
  const title = useId()
  const hint = useId()
  const layout = useSyncExternalStore(subscribeToTrafficLayout, trafficLayout, () => 'compact' as const)
  const inline = layout !== 'compact'
  const workspace = useRef<HTMLDivElement>(null)
  const inspector = useRef<HTMLElement>(null)
  const detailsButton = useRef<HTMLButtonElement>(null)
  const filtersToggle = useRef<HTMLButtonElement>(null)
  const editTrigger = useRef<HTMLButtonElement>(null)
  const fromInput = useRef<HTMLInputElement>(null)
  const toInput = useRef<HTMLInputElement>(null)
  const {
    buckets, series, totalReceptions, loadedReceptions, rows, range, setRange, hasMore, isLoadingMore, loadMore, resetKey, referenceTime,
    filterGroups, filterValues, setFilterValues, activeFilterCount, matchesReception, hashQuery, setHashQuery,
  } = usePacketTrafficDemo()
  const [filtersVisible, setFiltersVisible] = useState(true)
  const filterRail = filtersVisible && layout === 'rail'
  const [editorOpen, setEditorOpen] = useState(false)
  const [edits, setEdits] = useState<HistogramRange | null>(null)
  const [submitted, setSubmitted] = useState(false)
  const [details, setDetails] = useState<{ row: PacketTableRow; open: boolean; returnToDetailsButton: boolean; announcement?: string }>()
  const appliedValues = useMemo(() => rangeValues(range), [range])
  const values = edits ?? appliedValues
  const fromDate = arrivalDate(values.from)
  const toDate = arrivalDate(values.to)
  const calendarDates: CalendarValue<'range'> = fromDate && toDate && fromDate <= toDate ? [fromDate, toDate] : null
  const referenceMonth = formatArrivalDateTime(referenceTime, timeZone).slice(0, 7)
  const validation = useMemo(() => validateArrivalRange(values), [values])
  const fromError = submitted ? validation.fromError : undefined
  const toError = submitted ? validation.toError : undefined
  const receptions = useMemo(() => rows.filter((row): row is PacketTableRow => !('status' in row)), [rows])
  const selected = details?.open ? details.row : undefined
  const selectedIndex = selected ? receptions.findIndex((row) => row.id === selected.id) : -1
  const previous = selectedIndex > 0 ? receptions[selectedIndex - 1] : undefined
  const next = selectedIndex >= 0 ? receptions[selectedIndex + 1] : undefined
  const filterNote = selected && !matchesReception(selected.id)
    ? 'Outside the current filter'
    : selected && selectedIndex < 0 ? 'Not in the loaded receptions' : undefined
  const announcement = selected ? filterNote ?? details?.announcement : undefined
  const showActiveFilterCount = !filtersVisible && activeFilterCount > 0
  const filterToggleDescription = showActiveFilterCount ? (range ? `${activeFilterStatus} ${rangeStatus}` : activeFilterStatus) : undefined

  function updateDraft(bound: keyof HistogramRange, value: string) {
    setEdits((current) => ({ ...(current ?? appliedValues), [bound]: value }))
  }

  function updateDraftDates(from: string, to: string) {
    setEdits((current) => {
      const draft = current ?? appliedValues
      return { from: withArrivalDate(draft.from, from, '00:00'), to: withArrivalDate(draft.to, to, '23:59:59') }
    })
  }

  function resetDraft() {
    setEdits(null)
    setSubmitted(false)
  }

  function applyRange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!validation.range) {
      flushSync(() => setSubmitted(true))
      const invalid = validation.fromError ? fromInput : toInput
      invalid.current?.focus()
      return
    }
    commitRange(validation.range)
  }

  function commitRange(next: HistogramRange | null) {
    setRange(next)
    resetDraft()
    setEditorOpen(false)
  }

  function selectReception(row: PacketTableRow, returnToDetailsButton = false) {
    const index = receptions.indexOf(row)
    const announcement = index >= 0
      ? `Reception ${index + 1} of ${receptions.length}: ${row.code}, ${row.region}, ${timestamp.format(Date.parse(row.arrivedAt))}`
      : undefined
    setDetails({ row, open: true, returnToDetailsButton, announcement })
  }

  function browseReception(row: PacketTableRow) {
    selectReception(row, !inline && details?.returnToDetailsButton)
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
      browseReception(row)
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
        <p>24 hours · {count.format(totalReceptions)} sample receptions. Filter the list or select a reception to view its details.</p>
      </div>
      <Collapsible className="ks-traffic-layout" data-filter-rail={filterRail ? '' : undefined} open={filtersVisible} onOpenChange={(open) => {
        setFiltersVisible(open)
        if (!open) setEditorOpen(false)
      }}>
        <CollapsiblePanel className="ks-traffic-filters" role="region" aria-label="Packet traffic filters" tabIndex={filtersVisible ? 0 : undefined} keepMounted>
          {filterRail && <h3 className="ks-traffic-filters-heading">Filters</h3>}
          {filtersVisible && <div className="ks-arrival-filter">
            <Popover presentation="auto" open={editorOpen} onOpenChange={(open, { reason }) => {
              setEditorOpen(open)
              if (!open && (reason === 'close-press' || reason === 'escape-key' || reason === 'trigger-press')) resetDraft()
            }}>
              <PopoverTrigger className="ks-arrival-trigger" ref={editTrigger} aria-label="Edit arrival time" aria-describedby={rangeStatus}>
                <span>Arrival time</span>
                <span className="ks-arrival-summary" aria-hidden="true">{range ? <RangeSummary range={range} /> : 'All times'}</span>
              </PopoverTrigger>
              <PopoverContent className="ks-arrival-editor" align="start" side={filterRail ? 'end' : 'bottom'} sideOffset={5} collisionPadding={8} initialFocus={() => fromInput.current}>
                <PopoverTitle>Arrival time</PopoverTitle>
                <PopoverDescription className="ks-arrival-note">{timeZone} · YYYY-MM-DD HH:mm (24-hour; seconds optional). The calendar fills missing times. You can edit them.</PopoverDescription>
                <form className="ks-arrival-form" onSubmit={applyRange} noValidate>
                  <Field name="from" className="ks-arrival-bound" invalid={fromError ? true : undefined}>
                    <FieldLabel>From</FieldLabel>
                    <Input ref={fromInput} required spellCheck={false} autoComplete="off" value={values.from} onValueChange={(value) => updateDraft('from', value)} />
                    {fromError && <FieldError className="ks-arrival-error" match>{fromError}</FieldError>}
                  </Field>
                  <Field name="to" className="ks-arrival-bound" invalid={toError ? true : undefined}>
                    <FieldLabel>To</FieldLabel>
                    <Input ref={toInput} required spellCheck={false} autoComplete="off" value={values.to} onValueChange={(value) => updateDraft('to', value)} />
                    <FieldDescription className="ks-arrival-note">Receptions at the To time are excluded.</FieldDescription>
                    {toError && <FieldError className="ks-arrival-error" match>{toError}</FieldError>}
                  </Field>
                  <ArrivalCalendar dates={calendarDates} month={(fromDate ?? toDate)?.slice(0, 7) ?? referenceMonth} onDatesChange={updateDraftDates} />
                  <div className="ks-arrival-actions">
                    <PopoverClose className="ks-traffic-clear">Cancel</PopoverClose>
                    <Button className="ks-arrival-apply" type="submit">Apply range</Button>
                  </div>
                </form>
              </PopoverContent>
            </Popover>
            {range && <Button className="ks-traffic-clear" aria-label="Clear time range" onClick={() => {
              commitRange(null)
              editTrigger.current?.focus()
            }}>Clear</Button>}
          </div>}
          {filtersVisible && filterGroups.map(group => <TrafficFilterSection key={group.key} group={group} values={filterValues[group.key] ?? []}
            onValuesChange={values => setFilterValues(group.key, values)} />)}
        </CollapsiblePanel>
        <div className="ks-traffic-workspace" ref={workspace} data-inspecting={inline && selected ? '' : undefined}>
          <div className="ks-traffic-list" onKeyDown={onListKeys}>
            <div className="ks-traffic-overview">
              <div className="ks-traffic-hash-control">
                <svg aria-hidden="true" focusable="false" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
                  <circle cx="7" cy="7" r="4.25" />
                  <path d="m10.25 10.25 3 3" />
                </svg>
                <Input type="search" aria-label="Search by hash" placeholder="Search by hash..." value={hashQuery} onValueChange={setHashQuery}
                  autoComplete="off" autoCapitalize="none" spellCheck={false} />
              </div>
              <TimeHistogram buckets={buckets} series={series} measure="Receptions over time" unit="receptions" timeZone={timeZone} timeZoneLabel={timeZoneLabel}
                draftRange={editorOpen && edits ? validation.range : null} appliedRange={range} onRangeZoom={commitRange}
                renderRangeSummary={(selection) => <RangeSummary range={selection} />} rangeFocusFallback={() => editTrigger.current ?? filtersToggle.current} />
              <div className="ks-traffic-context">
                <CollapsibleTrigger className="ks-traffic-filter-toggle" ref={filtersToggle} aria-describedby={filterToggleDescription}>
                  <svg aria-hidden="true" focusable="false" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.25" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="1.5" y="2.5" width="13" height="11" rx="2" />
                    <path d="M5.5 2.5v11" />
                    <path d={filtersVisible ? 'm10.5 6-2 2 2 2' : 'm8.5 6 2 2-2 2'} />
                  </svg>
                  {filtersVisible ? 'Hide filters' : 'Show filters'}
                </CollapsibleTrigger>
                <span className="ks-traffic-brush-hint">Click or drag to select a range</span>
                <p className="ks-visually-hidden" id={rangeStatus} role="status" aria-label="Active time range">
                  {range ? <RangeSummary range={range} /> : 'All times'}
                </p>
                <div className="ks-traffic-count-context">
                  {showActiveFilterCount && <span className="ks-traffic-filter-active" id={activeFilterStatus}>
                    {count.format(activeFilterCount)} active {activeFilterCount === 1 ? 'filter' : 'filters'}
                  </span>}
                  <span className="ks-traffic-count">{count.format(loadedReceptions)} loaded</span>
                </div>
                {!inline && details && <Button className="ks-traffic-clear" ref={detailsButton} hidden={details.open} aria-haspopup="dialog"
                  onClick={() => selectReception(details.row, true)}>Go to details</Button>}
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
      </Collapsible>
      {inline && <div className="ks-visually-hidden" role="status">{announcement}</div>}
      {!inline && <Drawer side="end" modal open={details?.open ?? false} onOpenChange={(open) => {
        if (!open) dismissDetails()
      }}>
        <DrawerBackdrop className="ks-reception-drawer-backdrop" />
        <DrawerContent className="ks-reception-details ks-reception-drawer" initialFocus="content"
          finalFocus={() => (details?.returnToDetailsButton ? detailsButton.current : null) ?? receptionReturnTarget(workspace.current, details?.row.id)}
          onKeyDown={onDetailsKeys}>
          <ReceptionDetails drawer title={title} hint={hint} row={details?.row} announcement={announcement} filterNote={filterNote}
            previous={previous && (() => browseReception(previous))} next={next && (() => browseReception(next))} close={dismissDetails} />
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
