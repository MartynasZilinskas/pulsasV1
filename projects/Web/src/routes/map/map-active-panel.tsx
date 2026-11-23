import { Button } from '@ui-sdk/react/button'
import { useLayoutEffect, useMemo, useRef, useState, type Ref } from 'react'
import type { EvidenceAnchor } from '../../components/map/map-path-evidence'
import { NodeAvatar } from '../../components/nodes/node-avatar'
import { nodeRoles } from '../../components/nodes/node-role'
import type { PacketTableRow } from '../../components/packets/packet-table'
import { ReceptionPacketOverview } from '../../components/packets/reception-packet-overview'
import { MapNodeFinder } from '../kitchen-sink/map-node-finder'
import { MapNodeList } from '../kitchen-sink/map-node-list'
import { MapVisibleNodes } from '../kitchen-sink/map-visible-nodes'
import { getPacketOverview } from './packet-overview-fixtures'

export type ActiveMapContext =
  | { kind: 'visible' }
  | { kind: 'cluster'; ids: readonly string[] }
  | { kind: 'node'; id: string }
  | { kind: 'packet'; row: PacketTableRow }

type Props = {
  anchors: readonly EvidenceAnchor[]
  viewportIds: readonly string[] | null
  active: ActiveMapContext
  headerRef?: Ref<HTMLElement>
  onSelectNode: (id: string) => void
  onSelectReception: (row: PacketTableRow) => void
  onClear: () => void
  onExitCluster: (restoreFocus: boolean) => void
}

const formatCount = new Intl.NumberFormat('en')

function ClusterNodes({ anchors, ids, onSelectNode }: {
  anchors: readonly EvidenceAnchor[]
  ids: readonly string[]
  onSelectNode: (id: string) => void
}) {
  const [query, setQuery] = useState('')
  const input = useRef<HTMLInputElement>(null)
  const candidates = useMemo(() => {
    const byId = new Map(anchors.map(anchor => [anchor.id, anchor]))
    return [...new Set(ids)].flatMap(id => {
      const anchor = byId.get(id)
      return anchor ? [anchor] : []
    })
  }, [anchors, ids])
  const term = query.trim().toLowerCase()
  const matches = useMemo(() => candidates.filter(({ name, id }) => name.toLowerCase().includes(term)
    || id.toLowerCase().includes(term)), [candidates, term])

  useLayoutEffect(() => { input.current?.focus() }, [])

  const count = formatCount.format(matches.length)
  return <MapNodeList title="Cluster nodes" hideTitle matches={matches} query={query} onQueryChange={setQuery} input={input}
    searchLabel="Search cluster nodes" listLabel="Nodes in cluster" count={count}
    status={`${count} ${matches.length === 1 ? 'node' : 'nodes'}${term ? ' matching' : ''} in this cluster.`}
    feedback={matches.length === 0 ? 'No matching nodes in this cluster.' : null}
    description="Search only the clicked cluster by name or node ID. Press Escape to return to visible nodes."
    selectedId={null} onSelect={onSelectNode} />
}

export function MapActivePanel({ anchors, viewportIds, active, headerRef, onSelectNode, onSelectReception, onClear, onExitCluster }: Props) {
  const [finding, setFinding] = useState(false)
  const [query, setQuery] = useState('')
  const [focusRequest, setFocusRequest] = useState(0)
  const [pointerInteraction, setPointerInteraction] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  const findButton = useRef<HTMLButtonElement>(null)
  const clearButton = useRef<HTMLButtonElement>(null)
  const panel = useRef<HTMLElement>(null)
  const body = useRef<HTMLDivElement>(null)
  const overviewHeading = useRef<HTMLHeadingElement>(null)
  const focusWasInside = useRef(false)
  const receptionNavigation = useRef(false)
  const searching = active.kind === 'visible' && finding
  const node = active.kind === 'node' ? anchors.find(({ id }) => id === active.id) : undefined
  const title = active.kind === 'packet' ? 'Packet overview'
    : active.kind === 'node' ? node?.name ?? 'Node unavailable'
      : active.kind === 'cluster' ? 'Cluster nodes' : searching ? 'Find node' : 'Visible nodes'
  const receptionId = active.kind === 'packet' ? active.row.id : null

  useLayoutEffect(() => {
    if (receptionId && body.current) body.current.scrollTop = 0
  }, [receptionId])

  useLayoutEffect(() => {
    if (active.kind !== 'visible') {
      setFinding(false)
      setQuery('')
    }
    if (receptionNavigation.current || ((active.kind === 'node' || active.kind === 'packet') && focusWasInside.current
      && !panel.current?.contains(document.activeElement))) overviewHeading.current?.focus({ preventScroll: true })
    receptionNavigation.current = false
  }, [active])

  useLayoutEffect(() => {
    if (focusRequest === 0) return
    if (searching) input.current?.focus()
    else findButton.current?.focus()
  }, [focusRequest, searching])

  function selectNode(id: string) {
    setFinding(false)
    setQuery('')
    onSelectNode(id)
  }

  function clear() {
    setFinding(false)
    setQuery('')
    setFocusRequest(value => value + 1)
    if (active.kind === 'cluster') onExitCluster(false)
    else onClear()
  }

  function renderHeader(count: string | null = null) {
    return <header className="map-active-header" ref={headerRef}>
      {node && <NodeAvatar role={node.role} size="marker" />}
      <h2 ref={overviewHeading} tabIndex={-1} data-pointer-focus={pointerInteraction ? '' : undefined}>{title}{count !== null && <span className="map-active-count" aria-hidden="true">{count}</span>}</h2>
      {active.kind === 'visible' && !searching ? <Button ref={findButton} className="map-preview-button map-active-action" aria-label="Find node" title="Find node" onClick={() => {
        setFinding(true)
        setFocusRequest(value => value + 1)
      }}>
        <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 4 4" />
        </svg>
      </Button> : <Button ref={clearButton} className="map-preview-button map-active-action" aria-label="Clear selection" title="Clear selection" onClick={clear}>
        <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <path d="m6 6 12 12M6 18 18 6" />
        </svg>
      </Button>}
    </header>
  }

  const showingVisible = active.kind === 'visible' && !searching
  return <section className="map-active-panel" data-visible-list={showingVisible ? '' : undefined} ref={panel} aria-label="What's active"
    onPointerDownCapture={() => { setPointerInteraction(true) }}
    onKeyDownCapture={() => { setPointerInteraction(false) }}
    onFocusCapture={() => { focusWasInside.current = true }}
    onBlurCapture={event => {
      if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) focusWasInside.current = false
    }} onKeyDown={event => {
    if (active.kind !== 'cluster' || event.key !== 'Escape' || event.defaultPrevented || event.nativeEvent.isComposing) return
    event.preventDefault()
    event.stopPropagation()
    onExitCluster(true)
  }}>
    {!showingVisible && renderHeader()}
    <div className="map-active-body" ref={body}>
      {active.kind === 'visible' && (searching
        ? <MapNodeFinder presentation="compact" hideTitle anchors={anchors} overlap={[]} selectedId={null} input={input}
          query={query} onQueryChange={setQuery} onSelect={selectNode} onClearSelection={onClear} onExitOverlap={clear} />
        : <MapVisibleNodes searchable={false} renderHeader={renderHeader} anchors={anchors} ids={viewportIds} selectedId={null} onSelect={selectNode} />)}
      {active.kind === 'cluster' && <ClusterNodes key={active.ids.join('\n')} anchors={anchors} ids={active.ids} onSelectNode={selectNode} />}
      {active.kind === 'node' && <section className="map-active-overview" aria-label="Node overview">
        {node ? <>
          <p className="map-active-fixture">Fixture node</p>
          <dl className="map-active-node-facts">
            <div><dt>Node ID</dt><dd><code>{node.id}</code></dd></div>
            <div><dt>Role</dt><dd>{nodeRoles[node.role].label}</dd></div>
            <div><dt>Coordinates</dt><dd>{node.coordinates
              ? `${node.coordinates[1].toFixed(5)}, ${node.coordinates[0].toFixed(5)}` : 'No map location'}</dd></div>
          </dl>
        </> : <p role="status">This node is not available in the fixture.</p>}
      </section>}
      {active.kind === 'packet' && <ReceptionPacketOverview key={active.row.id} overview={getPacketOverview(active.row)}
        onSelectReception={row => {
          receptionNavigation.current = true
          onSelectReception(row)
        }} />}
    </div>
  </section>
}
