import { Input } from '@ui-sdk/react/input'
import { createListboxController, Listbox, ListboxItem, ListboxItemIndicator } from '@ui-sdk/react/listbox'
import { useCallback, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react'
import type { EvidenceAnchor } from '../../components/map/map-path-evidence'
import { NodeAvatar } from '../../components/nodes/node-avatar'
import { nodeRoles } from '../../components/nodes/node-role'
import { LucideGlyph } from '../../components/ui/lucide-glyph'

const rowHeight = 48
const overscan = 4

type Props = {
  matches: readonly EvidenceAnchor[]
  title: string
  hideTitle?: boolean
  searchable?: boolean
  header?: ReactNode
  searchLabel: string
  listLabel: string
  query: string
  onQueryChange: (query: string) => void
  input?: RefObject<HTMLInputElement | null>
  count: string | null
  status: string
  feedback: string | null
  visibleStatus?: string
  description?: string
  action?: ReactNode
  selectedId: string | null
  onSelect: (id: string) => void
}

export function MapNodeList({ matches, title, hideTitle = false, searchable = true, header, searchLabel, listLabel, query, onQueryChange, input, count, status, feedback, visibleStatus, description, action, selectedId, onSelect }: Props) {
  const headingId = useId()
  const descriptionId = useId()
  const localSearch = useRef<HTMLInputElement>(null)
  const search = input ?? localSearch
  const section = useRef<HTMLElement>(null)
  const scroller = useRef<HTMLDivElement | null>(null)
  const [controller] = useState(() => createListboxController<string>())
  const [viewport, setViewport] = useState({ top: 0, height: 0 })
  const [highlightedId, setHighlightedId] = useState<string | null>(null)
  const items = useMemo(() => matches.map(({ id, name }) => ({ value: id, label: name })), [matches])
  const indexById = useMemo(() => new Map(items.map(({ value }, index) => [value, index])), [items])
  const highlight = highlightedId === null ? -1 : indexById.get(highlightedId) ?? -1

  const readViewport = useCallback(() => {
    const element = scroller.current
    if (!element) return
    const { scrollTop: top, clientHeight: height } = element
    setViewport(previous => previous.top === top && previous.height === height ? previous : { top, height })
  }, [])

  const attachScroller = useCallback((element: HTMLDivElement | null) => {
    if (!element) return
    scroller.current = element
    const observer = new ResizeObserver(readViewport)
    observer.observe(element)
    readViewport()
    return () => {
      observer.disconnect()
      if (element.contains(document.activeElement)) (search.current ?? section.current)?.focus()
      scroller.current = null
    }
  }, [readViewport, search])

  function revealRow(index: number) {
    const element = scroller.current
    if (!element || index < 0) return
    const top = index * rowHeight
    if (top < element.scrollTop) element.scrollTop = top
    else if (top + rowHeight > element.scrollTop + element.clientHeight) {
      element.scrollTop = top + rowHeight - element.clientHeight
    }
    readViewport()
  }

  function highlightRow(index: number) {
    const id = matches[index]?.id
    if (id === undefined) return
    controller.highlight(id)
    setHighlightedId(id)
    revealRow(index)
  }

  function onListFocus(event: React.FocusEvent<HTMLDivElement>) {
    if (!event.currentTarget.matches(':focus-visible')) return
    const element = scroller.current
    if (!element) return
    const first = Math.min(matches.length - 1, Math.floor(element.scrollTop / rowHeight))
    const last = Math.ceil((element.scrollTop + element.clientHeight) / rowHeight) - 1
    const activeId = controller.getHighlighted() ?? selectedId
    const active = activeId === null ? -1 : indexById.get(activeId) ?? -1
    highlightRow(active >= first && active <= last ? active : first)
  }

  function onListKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (event.defaultPrevented || event.nativeEvent.isComposing || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return
    if (event.key === 'Home' || event.key === 'End') {
      // A boundary key still needs to scroll when the virtual highlight is already at that boundary.
      event.preventDefault()
      highlightRow(event.key === 'Home' ? 0 : matches.length - 1)
    } else if ((event.key === 'Enter' || event.key === ' ') && selectedId !== null && controller.getHighlighted() === selectedId) {
      event.preventDefault()
      onSelect(selectedId)
    }
  }

  useLayoutEffect(() => {
    if (highlightedId !== null && highlight < 0) {
      setHighlightedId(null)
      if (scroller.current?.contains(document.activeElement)) (search.current ?? section.current)?.focus()
    }
    readViewport()
  }, [matches.length, highlight, highlightedId, readViewport, search])

  const start = Math.min(matches.length, Math.max(0, Math.floor(viewport.top / rowHeight) - overscan))
  const end = Math.min(matches.length, Math.ceil((viewport.top + viewport.height) / rowHeight) + overscan)
  const indexes = Array.from({ length: end - start }, (_, index) => start + index)
  // Keep the active descendant mounted even when the user scrolls past it.
  if (highlight >= 0 && highlight < start) indexes.unshift(highlight)
  if (highlight >= end) indexes.push(highlight)

  return (
    <section ref={section} tabIndex={-1} className="ks-map-visible" aria-labelledby={headingId}>
      {header !== undefined ? <><h4 id={headingId} className="ks-visually-hidden">{title}</h4>{header}</> : <header className="ks-map-visible-header" data-title-hidden={hideTitle ? '' : undefined}>
        <div className={hideTitle && count === null && !action ? 'ks-visually-hidden' : 'ks-map-visible-heading'}>
          <h4 id={headingId} className={hideTitle ? 'ks-visually-hidden' : undefined}>{title}</h4>
          {count !== null && <span className="ks-map-visible-count" aria-hidden="true">{count}</span>}
          {action}
        </div>
        {searchable && <Input ref={search} type="search" className="ks-map-visible-search" aria-label={searchLabel} aria-describedby={description ? descriptionId : undefined}
          placeholder="Search by name or ID" autoComplete="off" spellCheck={false} value={query} onValueChange={(value) => {
            if (scroller.current) scroller.current.scrollTop = 0
            readViewport()
            setHighlightedId(null)
            onQueryChange(value)
          }} />}
        {description && <p id={descriptionId} className="ks-visually-hidden">{description}</p>}
        {visibleStatus && <p className="ks-map-visible-summary" aria-hidden="true">{visibleStatus}</p>}
      </header>}
      <p className={feedback ? 'ks-map-visible-status' : 'ks-visually-hidden'} role="status" aria-atomic="true">{status}</p>
      {matches.length > 0 && <Listbox ref={attachScroller} className="ks-map-visible-scroll" aria-label={listLabel}
        items={items} value={selectedId !== null && indexById.has(selectedId) ? selectedId : null}
        activation="manual" virtualFocus keepInView={false} controller={controller}
        onFocus={onListFocus} onKeyDown={onListKeys} onScroll={readViewport}
        onHighlight={(id, { index }) => { setHighlightedId(id); revealRow(index) }}
        onValueChange={(id, { reason }) => { if (id !== null && id !== selectedId && reason === 'item-press') onSelect(id) }}>
        <div className="ks-map-visible-track" role="presentation" style={{ height: matches.length * rowHeight }}>
          {indexes.map(index => {
            const { id, name, role } = matches[index]
            return <ListboxItem key={id} value={id} label={name} index={index}
              className="ks-map-visible-row" title={`${name} · ${nodeRoles[role].label} · ${id}`}
              onClick={() => { if (id === selectedId) onSelect(id) }}
              style={{ top: index * rowHeight, height: rowHeight }}>
              <span className="ks-map-visible-avatar">
                <NodeAvatar role={role} size="marker" />
                <ListboxItemIndicator className="ks-map-visible-selected" aria-hidden="true"><LucideGlyph name="check" /></ListboxItemIndicator>
              </span>
              <span className="ks-map-visible-details">
                <strong>{name}</strong>
                <code>{id}</code>
              </span>
              <span className="ks-map-visible-role">{nodeRoles[role].label}</span>
            </ListboxItem>
          })}
        </div>
      </Listbox>}
    </section>
  )
}
