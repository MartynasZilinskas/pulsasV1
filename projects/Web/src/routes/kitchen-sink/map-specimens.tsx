import { Button } from '@ui-sdk/react/button'
import { Checkbox, CheckboxIndicator } from '@ui-sdk/react/checkbox'
import { Collapsible, CollapsiblePanel, CollapsibleTrigger } from '@ui-sdk/react/collapsible'
import { Select, SelectContent, SelectItem, SelectItemIndicator, SelectLabel, SelectTrigger, SelectValue } from '@ui-sdk/react/select'
import { Slider, SliderControl, SliderIndicator, SliderThumb, SliderTrack, SliderValue } from '@ui-sdk/react/slider'
import { useEffect, useMemo, useState } from 'react'
import { MapPreview } from '../../components/map/map-preview'
import type { FlowPlayback } from '../../components/map/map-preview-flow'
import { NodeAvatar } from '../../components/nodes/node-avatar'
import { nodeRoles } from '../../components/nodes/node-role'
import { PacketPathChain } from '../../components/packets/packet-path-chain'
import { LucideGlyph } from '../../components/ui/lucide-glyph'
import { mapCatalogue, pathScene, type MapExample } from './map-catalogue'
import { mapShowcaseCenter } from './map-fixtures'

const modes = [
  { id: 'nodes', label: 'Node states' }, { id: 'paths', label: 'Path evidence' }, { id: 'flow', label: 'Packet flow' },
] as const
type Mode = (typeof modes)[number]['id']

function ScenarioSelector({ examples, selected, choose }: { examples: readonly MapExample[]; selected: string; choose: (id: string) => void }) {
  const items = examples.map(({ id, label }) => ({ value: id, label }))
  return (
    <aside className="ks-map-catalogue" aria-label="Scenario catalogue">
      <div className="ks-map-scenario-select">
        <Select items={items} value={selected} onValueChange={(id) => { if (id !== null) choose(id) }}>
          <SelectLabel className="ks-field-label">Scenario</SelectLabel>
          <SelectTrigger className="ks-select-trigger ks-map-select-trigger">
            <SelectValue />
            <svg className="ks-glyph" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" aria-hidden="true"><path d="m6 9 6 6 6-6" /></svg>
          </SelectTrigger>
          <SelectContent className="ks-select-content" align="start" sideOffset={5}>
            {items.map(({ value, label }) => <SelectItem className="ks-select-item ks-map-select-item" key={value} value={value} label={label}>
              <span>{label}</span><SelectItemIndicator className="ks-select-indicator" aria-hidden="true"><LucideGlyph name="check" /></SelectItemIndicator>
            </SelectItem>)}
          </SelectContent>
        </Select>
      </div>
      <div className="ks-map-scenario-list">
        <h3>Scenarios</h3>
        <ol>
          {examples.map(({ id, label }, index) => <li key={id}>
            <Button aria-current={id === selected ? 'true' : undefined} onClick={() => choose(id)}>
              <span aria-hidden="true">{String(index + 1).padStart(2, '0')}</span>{label}
            </Button>
          </li>)}
        </ol>
      </div>
    </aside>
  )
}

export default function MapSpecimens() {
  const [mode, setMode] = useState<Mode>('paths')
  const [scenarios, setScenarios] = useState({ nodes: 'labels', paths: 'strong', flow: 'route' })
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [focusRequest, setFocusRequest] = useState(0)
  const [overlap, setOverlap] = useState<readonly string[]>([])
  const [hidden, setHidden] = useState<ReadonlySet<string>>(() => new Set())
  const [clusterOverride, setClusterOverride] = useState<boolean | undefined>(undefined)
  const [playback, setPlayback] = useState<FlowPlayback | null>(null)
  const [hopMs, setHopMs] = useState(1400)
  const [forceReduced, setForceReduced] = useState(false)
  const [systemReduced, setSystemReduced] = useState(() => matchMedia('(prefers-reduced-motion: reduce)').matches)
  const examples = mapCatalogue[mode]
  const example = examples.find(({ id }) => id === scenarios[mode])!
  const selected = example.anchors.find(({ id }) => id === selectedId)
  const reduced = systemReduced || forceReduced
  const pathId = example.id === 'strong' ? 'single-strong' : example.id
  const scene = useMemo(() => {
    const current = hidden.size && example.evidence ? pathScene(example.evidence, hidden, pathId) : example.scene
    return clusterOverride === undefined ? current : { ...current, clustered: clusterOverride }
  }, [clusterOverride, example, hidden, pathId])

  useEffect(() => {
    const media = matchMedia('(prefers-reduced-motion: reduce)')
    const motion = () => { setSystemReduced(media.matches); setPlayback(null) }
    const visibility = () => { if (document.hidden) setPlayback(null) }
    media.addEventListener('change', motion)
    document.addEventListener('visibilitychange', visibility)
    return () => { media.removeEventListener('change', motion); document.removeEventListener('visibilitychange', visibility) }
  }, [])

  function reset(next: MapExample) {
    setSelectedId(next.selectedId ?? null)
    setOverlap([])
    setClusterOverride(undefined)
    setHidden(new Set())
    setPlayback(null)
  }

  function chooseMode(id: Mode) {
    if (mode === id) return
    reset(mapCatalogue[id].find((example) => example.id === scenarios[id])!)
    setMode(id)
  }

  function chooseScenario(id: string) {
    if (example.id === id) return
    reset(examples.find((example) => example.id === id)!)
    setScenarios((previous) => ({ ...previous, [mode]: id }))
  }

  function select(id: string, focus: boolean) {
    setOverlap([])
    setSelectedId(id)
    if (focus) setFocusRequest((value) => value + 1)
  }

  function focusAnchor(anchor: MapExample['anchors'][number], hopIndex?: number) {
    if (!anchor.coordinates) { select(anchor.id, false); return }
    const matches = example.evidence?.flatMap(({ matches }, index) => (
      (hopIndex === undefined || hopIndex === index) && matches.some((match) => match.anchor.id === anchor.id) ? [`${index}:${anchor.id}`] : []
    ))
    if (matches?.some((key) => hidden.has(key))) {
      const next = new Set(hidden)
      for (const key of matches) next.delete(key)
      setHidden(next)
    }
    select(anchor.id, true)
  }

  function changeCandidate(key: string, anchorId: string, visible: boolean) {
    const next = new Set(hidden)
    if (visible) next.delete(key)
    else {
      next.add(key)
      if (selectedId === anchorId) setSelectedId(null)
    }
    setHidden(next)
    setOverlap([])
  }

  return (
    <div className="ks-map-workspace">
      <div className="ks-map-modes" role="group" aria-label="Map example category">
        {modes.map(({ id, label }) => <Button className="map-preview-button" key={id} aria-pressed={id === mode} onClick={() => chooseMode(id)}>
          {label}<span aria-hidden="true">{mapCatalogue[id].length}</span>
        </Button>)}
      </div>
      <div className="ks-map-workbench">
        <ScenarioSelector examples={examples} selected={example.id} choose={chooseScenario} />
        <div className="ks-map-main">
          <div className="ks-map-example-heading">
            <p className="ks-map-eyebrow">{modes.find(({ id }) => id === mode)?.label}</p>
            <h3>{example.label}</h3>
            <p>{example.description}</p>
          </div>
          <MapPreview
            sceneId={example.id} label={`${example.label} fixture map`} center={mapShowcaseCenter} scene={scene}
            selectedId={selectedId} focusRequest={focusRequest}
            onPick={(ids) => { if (ids.length === 1) select(ids[0], false); else setOverlap(ids) }}
            onClusterToggle={mode === 'nodes' ? setClusterOverride : undefined}
            flow={example.flows} playback={playback} onFlowFinish={() => setPlayback(null)}
            renderControls={mode !== 'flow' ? undefined : (ready) => (
              <div className="ks-map-playback">
                <div className="ks-map-playback-actions">
                  <Button className="map-preview-button" disabled={!ready} onClick={() => setPlayback({ mode: 'once', hopMs, reduced })}>Play once</Button>
                  <Button className="map-preview-button ks-map-loop" disabled={!ready} onClick={() => setPlayback(playback?.mode === 'loop' ? null : { mode: 'loop', hopMs, reduced })}>
                    {playback?.mode === 'loop' ? 'Pause & clear' : 'Loop preview'}
                  </Button>
                  <Button className="map-preview-button" disabled={!ready || playback === null} onClick={() => setPlayback(null)}>Clear</Button>
                </div>
                <p className="ks-map-playback-status" role="status">{!playback ? 'No preview running.' : `${playback.mode === 'loop' ? 'Looping preview' : 'Playing once'}${playback.reduced ? ' · static marks' : ' · animated packet'}.`}</p>
              </div>
            )}
          />
          <div className="ks-map-inspector">
            {example.hops && <div className="ks-map-path">
              <h4>Raw hop chain</h4>
              <p className="ks-map-inspector-note">Generated evidence. Map filters do not change the reported candidates.</p>
              {example.hops.length ? <PacketPathChain key={`hops-${example.id}`} hops={example.hops} label={`${example.label} hop chain`} onSelect={({ hopNumber, candidate }) => {
                const match = example.evidence?.[hopNumber - 1]?.matches.find(({ anchor }) => anchor.publicKey === candidate.publicKey)
                if (match) focusAnchor(match.anchor, hopNumber - 1)
              }} /> : <p className="ks-map-inspector-note">No hops recorded.</p>}
              {scene.paths.length > 0 && <ul className="ks-map-path-key" aria-label="Path map key">
                {scene.paths.some(({ confidence }) => confidence !== 'weak') && <li><span aria-hidden="true" />Strong connection</li>}
                {scene.paths.some(({ confidence }) => confidence === 'weak') && <li><span className="ks-map-possible-line" aria-hidden="true" />Uncertain connection</li>}
                {scene.markers.some(({ presentation }) => presentation === 'gap') && <li><span className="ks-map-gap-symbol" aria-hidden="true" />Schematic gap</li>}
              </ul>}
              {example.evidence?.some(({ matches }) => matches.length > 1) && <Collapsible className="ks-map-filters" key={`filters-${example.id}`}>
                <CollapsibleTrigger className="map-preview-button">Show candidates on map</CollapsibleTrigger>
                <CollapsiblePanel className="ks-map-filters-panel">
                  {example.evidence.map(({ matches, prefix }, index) => matches.length > 1 && (
                    <fieldset key={`${index}:${prefix}`}>
                      <legend>Hop {index + 1} · <code>{prefix}</code></legend>
                      {matches.filter(({ anchor }) => anchor.coordinates).map(({ anchor, confidence }) => {
                        const key = `${index}:${anchor.id}`
                        return <label className="ks-checkbox-row" key={key}>
                          <Checkbox className="ks-checkbox" checked={!hidden.has(key)} onCheckedChange={(checked) => changeCandidate(key, anchor.id, checked === true)}>
                            <CheckboxIndicator aria-hidden="true"><LucideGlyph name="check" /></CheckboxIndicator>
                          </Checkbox>
                          <span>{anchor.name} · {confidence}</span>
                        </label>
                      })}
                    </fieldset>
                  ))}
                  <Button className="map-preview-button" disabled={!hidden.size} onClick={() => setHidden(new Set())}>Reset candidate visibility</Button>
                </CollapsiblePanel>
              </Collapsible>}
            </div>}
            {example.flows && <div className="ks-map-flow-details">
              <h4>Preview stops</h4>
              <ol className="ks-map-flow-routes">
                {example.flows.map(({ id, stops }, index) => <li key={id}>
                  {example.flows!.length > 1 && <strong>Route {index + 1}</strong>}
                  <ol>{stops.map(({ id, name, schematic }, index) => <li key={`${index}:${id}`}>{name}{schematic && ' · not a node location'}</li>)}</ol>
                </li>)}
              </ol>
              <div className="ks-map-motion-options">
                <Slider className="ks-map-speed" min={300} max={3000} step={100} value={hopMs}
                  format={(value) => `${(value / 1000).toFixed(1)} seconds`}
                  onValueChange={(value) => { setHopMs(value); setPlayback(null) }}>
                  <div className="ks-map-speed-label">Time per hop <SliderValue /></div>
                  <SliderControl className="ks-map-speed-control">
                    <SliderTrack className="ks-map-speed-track">
                      <SliderIndicator className="ks-map-speed-indicator" />
                      <SliderThumb className="ks-map-speed-thumb" aria-label="Time per hop" />
                    </SliderTrack>
                  </SliderControl>
                </Slider>
                <label className="ks-checkbox-row">
                  <Checkbox className="ks-checkbox" checked={reduced} disabled={systemReduced} onCheckedChange={(checked) => { setForceReduced(checked === true); setPlayback(null) }}>
                    <CheckboxIndicator aria-hidden="true"><LucideGlyph name="check" /></CheckboxIndicator>
                  </Checkbox>
                  Reduced-motion preview
                </label>
                {systemReduced && <p className="ks-map-inspector-note">Your system requests reduced motion. Playback uses static marks.</p>}
              </div>
            </div>}
            {example.anchors.length > 0 && <div className="ks-map-anchor-panel">
              <h4>{mode === 'paths' ? 'Path candidates' : 'Fixture anchors'}</h4>
              <ul className="ks-map-anchors">
                {example.anchors.map((anchor) => {
                  const { id, name, role, coordinates } = anchor
                  const marker = scene.markers.find((marker) => marker.id === id)
                  const content = <>
                    <NodeAvatar role={role} size="marker" />
                    <span className="ks-map-anchor-details">
                      <strong>{name}</strong>
                      <span>{nodeRoles[role].label} · {coordinates ? `${coordinates[1].toFixed(5)}, ${coordinates[0].toFixed(5)}` : 'No map location'}</span>
                      {mode === 'paths' && marker && <span>Hop {marker.label} · {marker.ambiguous ? 'Ambiguous' : marker.confidence === 'weak' ? 'Tentative' : 'Resolved'}</span>}
                      {mode === 'paths' && coordinates && !marker && <span>Hidden on map</span>}
                    </span>
                  </>
                  return <li key={id}>{coordinates ? (
                    <Button className="ks-map-anchor" aria-current={selectedId === id ? 'true' : undefined}
                      data-overlap={overlap.includes(id) || undefined} onClick={() => focusAnchor(anchor)}>{content}</Button>
                  ) : <div className="ks-map-anchor-static">{content}</div>}</li>
                })}
              </ul>
              <div className="ks-map-selection-row">
                <p className="ks-map-selection" role="status" aria-atomic="true">
                  {overlap.length ? `${overlap.length} markers overlap. Choose an anchor from the list.` : selected ? (selected.coordinates ? `Selected on map: ${selected.name}` : `${selected.name} has no map location.`) : 'Choose an anchor on the map or in this list.'}
                </p>
                {selected && <Button className="map-preview-button" onClick={() => { setSelectedId(null); setOverlap([]) }}>Clear selection</Button>}
              </div>
            </div>}
          </div>
        </div>
      </div>
    </div>
  )
}
