import type { MapPreviewScene } from '../../components/map/map-preview'
import { pathScene, type EvidenceAnchor as Anchor, type EvidenceHop, type EvidenceMatch as Match } from '../../components/map/map-path-evidence'
import type { NodeRole } from '../../components/nodes/node-role'
import type { PacketPathHop } from '../../components/packets/packet-path-chain'
import { mapAnchors, mapShowcaseCenter, unlocatedMapAnchor } from './map-fixtures'

type Position = readonly [number, number]
type Marker = MapPreviewScene['markers'][number]
type Flow = { id: string; stops: readonly { id: string; name: string; coordinates: Position; schematic?: boolean }[] }

export type MapExample = {
  id: string; label: string; description: string; scene: MapPreviewScene; anchors: readonly Anchor[]
  hops?: readonly PacketPathHop[]; evidence?: readonly EvidenceHop[]; flows?: readonly Flow[]; selectedId?: string
}

const [a, b, c, d] = mapAnchors
const [longitude, latitude] = mapShowcaseCenter
const roleSamples: readonly { name: string; role: NodeRole }[] = [
  { name: 'Room server', role: 'ROOM_SERVER' }, { name: 'Repeater', role: 'REPEATER' }, { name: 'Client', role: 'CLIENT' },
  { name: 'Sensor', role: 'SENSOR' }, { name: 'Unknown', role: 'NONE' }, { name: 'Reserved role fallback', role: 'NONE' },
  { name: 'Missing role fallback', role: 'NONE' },
]

function specimen({ name, role }: (typeof roleSamples)[number], index: number, coordinates: Position): Anchor {
  const label = index < 5 ? `Specimen ${index + 1} · ${name}` : `Specimen · ${name}`
  return { id: `specimen-${index}`, name: label, role, coordinates, publicKey: '' }
}

function mapNodes(anchors: readonly Anchor[]): Marker[] {
  return anchors.flatMap(({ id, name, role, coordinates }) => coordinates ? [{ id, coordinates, label: name, name, role }] : [])
}

function nodes(id: string, label: string, description: string, anchors: readonly Anchor[], options: Pick<MapPreviewScene, 'zoom' | 'clustered'> = {}, selectedId?: string): MapExample {
  return { id, label, description, anchors, scene: { markers: mapNodes(anchors), paths: [], ...options }, selectedId }
}

const nodeExamples: readonly MapExample[] = [
  nodes('dots', 'Distant dots', 'Role-coloured points below zoom 9.', mapAnchors, { zoom: 8.5 }),
  nodes('roles', 'Role glyphs', 'Shared node role artwork from zoom 9.', mapAnchors, { zoom: 10.5 }),
  nodes('labels', 'Labels & collision', 'Names appear from zoom 12. A hidden label does not hide its marker.', mapAnchors, { zoom: 12.5 }),
  nodes('selected', 'Raised selection', 'The selected node stays above other markers and labels.', mapAnchors, { zoom: 13 }, a.id),
  nodes('clusters', 'Clusters & counts', 'Nearby nodes group into counted circles. Select a cluster to expand it.', mapAnchors, { zoom: 8, clustered: true }),
  nodes('role-specimens', 'Every role & fallback', 'Generated specimens for the shared role artwork and both unknown-role fallbacks.',
    roleSamples.map((role, index) => specimen(role, index, [longitude + (index % 3 - 1) * .014, latitude + (1 - Math.floor(index / 3)) * .01]))),
  nodes('overlap', 'Overlapping markers', 'Eight specimens share one advertised coordinate. Pick the overlap to spread them out.',
    Array.from({ length: 8 }, (_, index) => ({ ...specimen(roleSamples[index % roleSamples.length], index, mapShowcaseCenter),
      name: `Specimen ${index + 1} · ${roleSamples[index % roleSamples.length].name}` }))),
  nodes('near-overlap', 'Near-overlapping markers', 'Distinct advertised positions overlap on screen. Display connectors keep each anchor separate.',
    roleSamples.slice(0, 3).map((role, index) => specimen(role, index, [longitude + index * .00012, latitude])), { zoom: 13 }),
  nodes('dense-overlap', 'Dense overlapping markers', 'A bounded chooser keeps every node available when a fan would be crowded.',
    Array.from({ length: 16 }, (_, index) => ({ ...specimen(roleSamples[index % roleSamples.length], index, mapShowcaseCenter),
      name: `Dense specimen ${index + 1} · ${roleSamples[index % roleSamples.length].name}` }))),
  nodes('long-label', 'Long node label', 'Inspect placement and wrapping without changing the advertised names of the snapshot nodes.',
    [{ ...specimen(roleSamples[0], 0, mapShowcaseCenter), name: 'Specimen · A deliberately long node label to inspect wrapping, collision placement and selected-label visibility' }]),
]

const match = (anchor: Anchor, confidence: Match['confidence'] = 'Strong'): Match => ({ anchor, confidence })
const hop = (anchor: Anchor, confidence?: Match['confidence']): EvidenceHop => ({ prefix: anchor.publicKey.slice(0, 6), matches: [match(anchor, confidence)] })
const gap = (prefix: string): EvidenceHop => ({ prefix, matches: [] })
const alternative = (first: Match['confidence'], second: Match['confidence']): EvidenceHop => ({ prefix: b.publicKey.slice(0, 6), matches: [match(b, first), match(c, second)] })
const positionBetween = (from: Position, to: Position, fraction: number): Position => [from[0] + (to[0] - from[0]) * fraction, from[1] + (to[1] - from[1]) * fraction]

function evidence(id: string, label: string, description: string, steps: readonly EvidenceHop[]): MapExample {
  return {
    id, label, description, evidence: steps, scene: pathScene(steps, undefined, id === 'strong' ? 'single-strong' : id),
    anchors: [...new Map(steps.flatMap(({ matches }) => matches.map(({ anchor }) => [anchor.id, anchor] as const))).values()],
    hops: steps.map(({ prefix, matches }, index) => ({ id: `${id}-${index + 1}`, prefix, candidates: matches.map(({ anchor, confidence }) => ({
      name: anchor.name, publicKey: anchor.publicKey, confidence, reason: anchor.coordinates ? 'Generated prefix match' : 'Generated prefix match · no map location',
    })) })),
  }
}

const pathExamples: readonly MapExample[] = [
  evidence('strong', 'Single strong', 'One strong candidate per hop. A solid line connects the located candidates.', [hop(a), hop(b)]),
  evidence('weak', 'Single weak', 'Weak candidates are uncertain. The link is dashed.', [hop(a, 'Weak'), hop(b, 'Weak')]),
  evidence('weak-alternatives', 'Multiple weak', 'Weak alternatives remain visible and uncertain.', [hop(a), alternative('Weak', 'Weak'), hop(d)]),
  evidence('strong-alternatives', 'Multiple strong', 'Several strong candidates still mean ambiguity, not confirmed traversal.', [hop(a), alternative('Strong', 'Strong'), hop(d)]),
  evidence('mixed', 'Strong + weak', 'The unique strong candidate takes precedence; the weak alternative remains visible.', [hop(a), alternative('Strong', 'Weak'), hop(d)]),
  evidence('missing', 'Missing middle hop', 'A dashed prefix box marks a schematic gap between located hops.', [hop(a), gap('e1e1e1'), hop(b)]),
  evidence('consecutive-missing', 'Consecutive missing hops', 'Both gaps retain their prefix and hop number.', [hop(a), gap('e1e1e1'), gap('e2e2e2'), hop(b)]),
  evidence('repeat', 'Repeated node', 'A revisited candidate shows every hop number on the same marker.', [hop(a), hop(b), hop(a)]),
  evidence('unlocated', 'Candidate without location', 'The candidate stays in the chain. The dashed box is schematic, not its location.', [hop(a), hop(unlocatedMapAnchor, 'Weak'), hop(b)]),
  evidence('empty', 'Empty raw path', 'No hops were recorded. The map does not invent endpoints.', []),
  evidence('unresolved', 'Only unresolved prefix', 'The raw prefix remains visible. With no located neighbours, nothing is plotted.', [gap('e1e1e1')]),
  evidence('exterior-missing', 'Missing outer hops', 'Leading and trailing prefixes remain in the raw chain without invented positions.', [gap('e1e1e1'), hop(a), gap('e2e2e2')]),
]

function flow(id: string, label: string, description: string, routes: readonly (readonly Flow['stops'][number][])[]): MapExample {
  const allIds = new Set(routes.flatMap(stops => stops.filter(stop => !stop.schematic).map(stop => stop.id)))
  const anchors = mapAnchors.filter(({ id }) => allIds.has(id))
  return { id, label, description, anchors, scene: { markers: mapNodes(anchors), paths: [] },
    flows: routes.map((stops, index) => ({ id: `${id}-${index + 1}`, stops })) }
}

const flowExamples: readonly MapExample[] = [
  flow('route', 'Moving packet & trail', 'A travelling packet leaves a dashed trail and marks arrivals at the nodes.', [[a, b, c]]),
  flow('arrival', 'Single-node arrival', 'An arrival ring without a route or travelling head.', [[a]]),
  flow('branch', 'Shared prefix & branches', 'Two preview packets share the first leg and branch to separate nodes.', [[a, b, c], [a, b, d]]),
  flow('concurrent', 'Concurrent arrivals', 'Two preview packets travel in opposite directions independently.', [[a, b], [b, a]]),
  flow('revisit', 'Revisit & repeated arrival', 'A preview packet visits another node and returns to its first node.', [[a, b, a]]),
  flow('schematic', 'Schematic unresolved stop', 'The midpoint is schematic and does not receive a node arrival ring.',
    [[a, { id: 'schematic', name: 'Schematic unresolved stop', coordinates: positionBetween(a.coordinates, b.coordinates, .5), schematic: true }, b]]),
]

export const mapCatalogue = { nodes: nodeExamples, paths: pathExamples, flow: flowExamples }
