import type { MapPreviewScene } from './map-preview'
import type { NodeRole } from '../nodes/node-role'

type Position = readonly [longitude: number, latitude: number]
export type EvidenceAnchor = { id: string; name: string; role: NodeRole; coordinates: Position | null; publicKey: string }
export type EvidenceMatch = { anchor: EvidenceAnchor; confidence: 'Strong' | 'Weak' }
export type EvidenceHop = { prefix: string; matches: readonly EvidenceMatch[] }

type Marker = MapPreviewScene['markers'][number]
type Line = MapPreviewScene['paths'][number]
type LocatedMatch = EvidenceMatch & { anchor: EvidenceAnchor & { coordinates: Position } }
type DrawableHop = { index: number; matches: readonly LocatedMatch[]; solid: boolean }

export const evidenceCandidateKey = (hopIndex: number, anchorId: string) => `${hopIndex}:${anchorId}`

const located = (match: EvidenceMatch): match is LocatedMatch => match.anchor.coordinates !== null

function drawableCandidates(step: EvidenceHop, index: number, hiddenKeys: ReadonlySet<string>) {
  const locatedMatches = step.matches.filter(located)
  const shown = locatedMatches.filter(({ anchor }) => !hiddenKeys.has(evidenceCandidateKey(index, anchor.id)))
  // If an earlier choice no longer leaves any located candidate, fall back to all.
  const visible = shown.length ? shown : locatedMatches
  const strong = step.matches.filter(({ confidence }) => confidence === 'Strong')
  const selected = strong.length === 1 ? strong[0] : undefined
  // Use the raw strong matches by default. Display filters cannot resolve a raw ambiguity.
  const matches = visible.length === locatedMatches.length && strong.length ? strong.filter(located) : visible
  return { visible, selected, matches, solid: matches.length === 1 && matches[0] === selected }
}

function centerOf(matches: readonly LocatedMatch[]): Position {
  return [matches.reduce((sum, match) => sum + match.anchor.coordinates[0], 0) / matches.length,
    matches.reduce((sum, match) => sum + match.anchor.coordinates[1], 0) / matches.length]
}

function addSchematicBridge(markers: Map<string, Marker>, hops: readonly EvidenceHop[], from: DrawableHop, to: DrawableHop, connect: (from: Position, to: Position, solid: boolean) => void) {
  const start = centerOf(from.matches), end = centerOf(to.matches)
  let last: Position | undefined
  for (let index = from.index + 1; index < to.index; index += 1) {
    const fraction = (index - from.index) / (to.index - from.index)
    const coordinates: Position = [start[0] + (end[0] - start[0]) * fraction, start[1] + (end[1] - start[1]) * fraction]
    const gapId = `gap-${index + 1}`
    markers.set(gapId, { id: gapId, coordinates, label: String(index + 1), prefix: hops[index].prefix, presentation: 'gap' })
    if (last) connect(last, coordinates, false)
    else for (const match of from.matches) connect(match.anchor.coordinates, coordinates, false)
    last = coordinates
  }
  if (last) for (const match of to.matches) connect(last, match.anchor.coordinates, false)
}

export function pathScene(hops: readonly EvidenceHop[], hiddenKeys: ReadonlySet<string> = new Set(), id = 'path'): MapPreviewScene {
  const markers = new Map<string, Marker>()
  const paths: Line[] = []
  let previous: DrawableHop | undefined
  function connect(from: Position, to: Position, solid: boolean) {
    if (from[0] !== to[0] || from[1] !== to[1]) paths.push({ id: paths.length === 0 ? id : `${id}-${paths.length + 1}`, coordinates: [from, to], confidence: solid ? 'strong' : 'weak' })
  }
  for (const [index, step] of hops.entries()) {
    const { visible, selected, matches, solid } = drawableCandidates(step, index, hiddenKeys)
    for (const match of visible) {
      const { anchor } = match
      const existing = markers.get(anchor.id)
      markers.set(anchor.id, {
        id: anchor.id, coordinates: anchor.coordinates, name: anchor.name, role: anchor.role,
        label: existing ? `${existing.label}, ${index + 1}` : String(index + 1),
        confidence: existing?.confidence === 'strong' || match === selected ? 'strong' : 'weak',
        ambiguous: Boolean(existing?.ambiguous || step.matches.length > 1),
      })
    }
    if (!matches.length) continue
    const current = { index, matches, solid }
    if (previous && previous.index === index - 1) {
      for (const from of previous.matches) for (const to of matches) connect(from.anchor.coordinates, to.anchor.coordinates, previous.solid && solid)
    } else if (previous) {
      addSchematicBridge(markers, hops, previous, current, connect)
    }
    previous = current
  }
  return { markers: [...markers.values()], paths }
}
