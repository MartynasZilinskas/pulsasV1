import { Button } from '@ui-sdk/react/button'
import { Collapsible, CollapsiblePanel, CollapsibleTrigger } from '@ui-sdk/react/collapsible'
import { createResizableController, Resizable, ResizableHandle, ResizablePanel } from '@ui-sdk/react/resizable'
import { useCallback, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { MapPreview } from '../../components/map/map-preview'
import { mapCatalogue } from '../kitchen-sink/map-catalogue'
import { mapShowcaseCenter } from '../kitchen-sink/map-fixtures'
import type { PacketTableRow } from '../../components/packets/packet-table'
import { MapActivePanel, type ActiveMapContext } from './map-active-panel'
import { PacketTrafficSpecimens } from '../kitchen-sink/packet-traffic-specimens'
import '../kitchen-sink/kitchen-sink.css'
import '../kitchen-sink/map-specimens.css'
import './map-page.css'

const example = mapCatalogue.nodes.find(({ id }) => id === 'regional-nodes')!

export default function MapPage() {
  const [nodesOpen, setNodesOpen] = useState(() => matchMedia('(min-width: 48rem)').matches)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [layersOpen, setLayersOpen] = useState(false)
  const [active, setActive] = useState<ActiveMapContext>({ kind: 'visible' })
  const [packetsOpen, setPacketsOpen] = useState(true)
  const [panels] = useState(createResizableController)
  const packetsToggle = useRef<HTMLButtonElement>(null)
  const packetsBody = useRef<HTMLDivElement>(null)
  const packetsHeader = useRef<HTMLElement>(null)
  const mapView = useRef<HTMLDivElement>(null)
  const [collapsedHeight, setCollapsedHeight] = useState(48)
  const [minimumPacketsHeight, setMinimumPacketsHeight] = useState<number | null>(null)
  const [minimumMapHeight, setMinimumMapHeight] = useState<number | null>(null)
  const [clustered, setClustered] = useState(false)
  const selectedId = active.kind === 'node' ? active.id : null
  const [focusRequest, setFocusRequest] = useState(0)
  const [viewportNodes, setViewportNodes] = useState<readonly string[] | null>(null)
  const overlapOrigin = useRef<HTMLCanvasElement | null>(null)
  const scene = useMemo(() => ({ ...example.scene, clustered }), [clustered])

  function changeNodesOpen(open: boolean) {
    if (open && matchMedia('(max-width: 47.99rem)').matches) {
      setSettingsOpen(false)
      setLayersOpen(false)
    }
    setNodesOpen(open)
  }

  function changeSettingsOpen(open: boolean) {
    if (open && matchMedia('(max-width: 47.99rem)').matches) setNodesOpen(false)
    setSettingsOpen(open)
  }

  function changeLayersOpen(open: boolean) {
    if (open && matchMedia('(max-width: 47.99rem)').matches) setNodesOpen(false)
    setLayersOpen(open)
  }

  useLayoutEffect(() => {
    const narrow = matchMedia('(max-width: 47.99rem)')
    const adaptPanels = () => { if (narrow.matches && (settingsOpen || layersOpen)) setNodesOpen(false) }
    narrow.addEventListener('change', adaptPanels)
    adaptPanels()
    return () => narrow.removeEventListener('change', adaptPanels)
  }, [settingsOpen, layersOpen])

  const measureNodesHeader = useCallback((header: HTMLElement | null) => {
    const panel = header?.closest<HTMLElement>('.map-workspace-nodes')
    if (!header || !panel) return
    const measure = () => {
      const height = header.getBoundingClientRect().height
      if (height > 0) panel.style.setProperty('--map-panel-header-height', `${height}px`)
    }
    const observer = new ResizeObserver(measure)
    observer.observe(header)
    measure()
    return () => observer.disconnect()
  }, [])

  useLayoutEffect(() => {
    const header = packetsHeader.current
    if (!header) return
    const measure = () => setCollapsedHeight(Math.ceil(header.getBoundingClientRect().height))
    const observer = new ResizeObserver(measure)
    observer.observe(header)
    measure()
    return () => observer.disconnect()
  }, [])

  useLayoutEffect(() => {
    const header = packetsHeader.current
    const body = packetsBody.current
    const scroller = body?.querySelector<HTMLElement>('[data-packet-table-scroll]')
    const tableHeader = scroller?.querySelector('thead')
    if (!packetsOpen || !header || !body || !scroller || !tableHeader) return
    const measure = () => {
      const row = scroller.querySelector<HTMLElement>('[data-reception-id]')
      if (!row || body.hidden || row.getBoundingClientRect().height === 0) return
      const scrollbarHeight = scroller.offsetHeight - scroller.clientHeight
      setMinimumPacketsHeight(Math.ceil(header.getBoundingClientRect().height
        + tableHeader.getBoundingClientRect().height
        + row.getBoundingClientRect().height + scrollbarHeight))
    }
    const observer = new ResizeObserver(measure)
    observer.observe(header)
    observer.observe(tableHeader)
    observer.observe(scroller)
    const row = scroller.querySelector('[data-reception-id]')
    if (row) observer.observe(row)
    measure()
    return () => observer.disconnect()
  }, [packetsOpen])

  useLayoutEffect(() => {
    const view = mapView.current
    const controls = view?.querySelector<HTMLElement>('.map-preview-workspace-controls')
    const rail = controls?.querySelector<HTMLElement>('.map-preview-workspace-rail')
    const button = rail?.querySelector('button')
    const group = view?.parentElement
    const handle = group?.querySelector('.map-workspace-resize')
    if (!view || !controls || !rail || !button || !group || !handle) return
    const measure = () => {
      const availableHeight = group.getBoundingClientRect().height - handle.getBoundingClientRect().height
      const chromeHeight = view.getBoundingClientRect().height - controls.getBoundingClientRect().height
      const railOffset = parseFloat(getComputedStyle(rail).marginBlockStart)
      // Leave one pixel for the browser's integer scroll offset at fractional rail edges.
      setMinimumMapHeight(Math.ceil(Math.max(availableHeight * 0.25,
        chromeHeight + railOffset + button.getBoundingClientRect().height + 1)))
    }
    const observer = new ResizeObserver(measure)
    for (const element of [view, controls, rail, button, group]) observer.observe(element)
    measure()
    return () => observer.disconnect()
  }, [nodesOpen])

  function select(id: string) {
    setActive({ kind: 'node', id })
    changeNodesOpen(true)
    setFocusRequest(value => value + 1)
    overlapOrigin.current = null
  }

  function clearSelection() {
    setActive({ kind: 'visible' })
    overlapOrigin.current = null
  }

  function selectPacket(row: PacketTableRow) {
    setActive({ kind: 'packet', row })
    changeNodesOpen(true)
    overlapOrigin.current = null
  }

  function pick(ids: readonly string[]) {
    changeNodesOpen(true)
    if (ids.length === 1) select(ids[0])
    else {
      const origin = document.activeElement
      overlapOrigin.current = origin instanceof HTMLCanvasElement ? origin : null
      setActive({ kind: 'cluster', ids })
    }
  }

  function exitOverlap(restoreFocus: boolean) {
    const origin = overlapOrigin.current
    setActive({ kind: 'visible' })
    overlapOrigin.current = null
    if (restoreFocus && origin?.isConnected) origin.focus()
  }

  return (
    <section className="map-workspace ks-page" aria-label="Map workspace" data-packets-open={packetsOpen}>
      <h1 className="ks-visually-hidden">Map</h1>
      <Resizable className="map-workspace-split" orientation="vertical" controller={panels} step={2} largeStep={10}
        onLayoutChange={() => {
          const open = !panels.isCollapsed('incoming-packets')
          if (!open && packetsBody.current?.contains(document.activeElement)) packetsToggle.current?.focus()
          setPacketsOpen(open)
        }}>
      <ResizablePanel className="map-workspace-view" ref={mapView} defaultSize={60} minSize={minimumMapHeight === null ? 25 : `${minimumMapHeight}px`}>
        <MapPreview presentation="workspace" sceneId={example.id} label="Regional fixture map" center={mapShowcaseCenter}
          settingsOpen={settingsOpen} onSettingsOpenChange={changeSettingsOpen}
          layersOpen={layersOpen} onLayersOpenChange={changeLayersOpen}
          scene={scene} selectedId={selectedId} focusRequest={focusRequest} regional
          onPick={pick} onClearSelection={clearSelection} onClusterToggle={setClustered} onViewportNodesChange={setViewportNodes} />
        <Collapsible className="map-workspace-nodes" open={nodesOpen} onOpenChange={changeNodesOpen}>
          <CollapsibleTrigger className="map-preview-button map-workspace-node-toggle" aria-label={nodesOpen ? 'Hide nodes' : 'Show nodes'}>
            <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
              <path d={nodesOpen ? 'm15 4-8 8 8 8' : 'm9 4 8 8-8 8'} />
            </svg>
          </CollapsibleTrigger>
          <CollapsiblePanel className="map-workspace-node-panel" id="map-active-panel" keepMounted>
            <MapActivePanel anchors={example.anchors} viewportIds={viewportNodes} active={active} headerRef={measureNodesHeader}
              onSelectNode={select} onClear={clearSelection} onExitCluster={exitOverlap} />
          </CollapsiblePanel>
        </Collapsible>
      </ResizablePanel>
      <ResizableHandle className="map-workspace-resize" aria-label="Resize incoming packets"><span aria-hidden="true" /></ResizableHandle>
      <ResizablePanel className="map-workspace-packets" id="incoming-packets" defaultSize={40} minSize={minimumPacketsHeight === null ? undefined : `${minimumPacketsHeight}px`} maxSize={75} collapsible collapsedSize={`${collapsedHeight}px`}>
        <header className="map-workspace-packet-header" ref={packetsHeader}>
          <Button className="map-preview-button map-workspace-packet-toggle" ref={packetsToggle} aria-label={packetsOpen ? 'Hide packets' : 'Show packets'} aria-expanded={packetsOpen} aria-controls="incoming-packets-body"
            onClick={() => {
              if (packetsOpen) panels.collapse('incoming-packets')
              else panels.expand('incoming-packets')
            }}>
            <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
              <path d={packetsOpen ? 'm4 8 8 8 8-8' : 'm4 16 8-8 8 8'} />
            </svg>
          </Button>
          <div className="map-workspace-packet-context">
            <h2>Incoming packets</h2>
            <p>Fixture receptions · not map-filtered<span className="ks-visually-hidden"> · Receptions are not filtered by map position.</span></p>
          </div>
        </header>
        <div className="map-workspace-packet-body" id="incoming-packets-body" ref={packetsBody} hidden={!packetsOpen}>
          <PacketTrafficSpecimens presentation="workspace" inspection={{
            selectedId: active.kind === 'packet' ? active.row.id : undefined,
            controls: 'map-active-panel', onSelect: selectPacket,
          }} />
        </div>
      </ResizablePanel>
      </Resizable>
    </section>
  )
}
