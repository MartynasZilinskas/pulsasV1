import { Button } from '@ui-sdk/react/button'
import { Input } from '@ui-sdk/react/input'
import { useId, useMemo, type RefObject } from 'react'
import type { EvidenceAnchor } from '../../components/map/map-path-evidence'
import { NodeAvatar } from '../../components/nodes/node-avatar'
import { nodeRoles } from '../../components/nodes/node-role'

const resultLimit = 8
const countFormat = new Intl.NumberFormat('en')

type Props = {
  anchors: readonly EvidenceAnchor[]
  overlap: readonly string[]
  selectedId: string | null
  input: RefObject<HTMLInputElement | null>
  query: string
  onQueryChange: (query: string) => void
  onSelect: (id: string) => void
  onClearSelection: () => void
  onExitOverlap: (restoreCanvasFocus: boolean) => void
}

export function MapNodeFinder({ anchors, overlap, selectedId, input, query, onQueryChange, onSelect, onClearSelection, onExitOverlap }: Props) {
  const inputId = useId()
  const helpId = useId()
  const byId = useMemo(() => new Map(anchors.map(anchor => [anchor.id, {
    anchor, name: anchor.name.toLowerCase(), id: anchor.id.toLowerCase(),
  }])), [anchors])
  const inOverlap = overlap.length > 0
  const term = query.trim().toLowerCase()
  const selected = selectedId === null ? undefined : byId.get(selectedId)?.anchor
  const { results, count } = useMemo(() => {
    if (!term && !inOverlap) return { results: [], count: 0 }
    const exact: EvidenceAnchor[] = []
    const partial: EvidenceAnchor[] = []
    let exactId: EvidenceAnchor | undefined
    let count = 0
    const candidates = inOverlap ? overlap.map(id => byId.get(id)) : byId.values()
    for (const candidate of candidates) {
      if (!candidate) continue
      const { anchor, name, id } = candidate
      if (!name.includes(term) && !id.includes(term)) continue
      count += 1
      if (id === term) exactId = anchor
      else if (name === term) {
        if (exact.length < resultLimit) exact.push(anchor)
      } else if (partial.length < resultLimit) partial.push(anchor)
    }
    return { results: [...(exactId ? [exactId] : []), ...exact, ...partial].slice(0, resultLimit), count }
  }, [byId, inOverlap, overlap, term])

  const resultStatus = !term && !inOverlap ? ''
    : count === 0 ? 'No matching nodes.'
      : count > resultLimit ? `Showing ${resultLimit} of ${countFormat.format(count)} ${term ? 'matches' : 'nodes'}. Refine your search to see fewer results.`
        : `${count} ${term ? (count === 1 ? 'matching node' : 'matching nodes') : 'overlapping nodes'}.`

  return (
    <section className="ks-map-finder" aria-label="Map node finder" onKeyDown={event => {
      if (!inOverlap || event.key !== 'Escape' || event.defaultPrevented || event.nativeEvent.isComposing) return
      event.preventDefault()
      event.stopPropagation()
      onExitOverlap(true)
    }}>
      <div className="ks-map-finder-search">
        <div className="ks-map-finder-heading">
          <label className="ks-field-label" htmlFor={inputId}>Find node</label>
          {inOverlap && <Button className="map-preview-button" onClick={() => onExitOverlap(false)}>Search all nodes</Button>}
        </div>
        <Input className="ks-map-finder-input" id={inputId} ref={input} type="search" value={query}
          aria-describedby={helpId} autoComplete="off" spellCheck={false} onValueChange={onQueryChange} />
        <p id={helpId} className="ks-map-inspector-note">
          {inOverlap ? `Search these ${countFormat.format(overlap.length)} overlapping nodes by name or node ID. Press Escape to search all nodes.`
            : `Search all ${countFormat.format(anchors.length)} nodes by name or node ID.`}
        </p>
        <p className="ks-map-inspector-note" role="status" aria-atomic="true">{resultStatus}</p>
        {results.length > 0 && <ul className="ks-map-finder-results" aria-label="Node search results">
          {results.map(({ id, name, role }) => <li key={id}>
            <Button className="ks-map-anchor" aria-current={id === selectedId ? 'true' : undefined} onClick={() => onSelect(id)}>
              <NodeAvatar role={role} size="marker" />
              <span className="ks-map-anchor-details">
                <strong>{name}</strong>
                <span>{nodeRoles[role].label} · <code>{id}</code></span>
              </span>
            </Button>
          </li>)}
        </ul>}
      </div>
      <section className="ks-map-finder-selection" aria-label="Selected node">
        <h4>Selection</h4>
        <p className="ks-map-finder-selected-name" role="status" aria-atomic="true">
          {selected ? <>Selected node: <strong>{selected.name}</strong></> : 'No node selected.'}
        </p>
        {selected && <>
          <p className="ks-map-inspector-note">{nodeRoles[selected.role].label} · {selected.coordinates
            ? `${selected.coordinates[1].toFixed(5)}, ${selected.coordinates[0].toFixed(5)}` : 'No map location'}</p>
          <p className="ks-map-inspector-note">Node ID: <code>{selected.id}</code></p>
          <Button className="map-preview-button" onClick={onClearSelection}>Clear selection</Button>
        </>}
      </section>
    </section>
  )
}
