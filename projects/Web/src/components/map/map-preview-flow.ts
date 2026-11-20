import type { GeoJSONSource } from 'maplibre-gl'
import { empty } from './map-preview-style'

type Coordinates = readonly [longitude: number, latitude: number]
export type PreviewFlow = {
  id: string
  stops: readonly { id: string; coordinates: Coordinates; schematic?: boolean }[]
}

export type FlowPlayback = { mode: 'once' | 'loop'; hopMs: number; reduced: boolean }

const lingerMs = 1050
const ringMs = 720

type Activity = { kind: 'head' | 'trail' | 'arrival' | 'schematic'; route: string; opacity: number; radius?: number }

function point(coordinates: Coordinates, properties: Activity): GeoJSON.Feature<GeoJSON.Point, Activity> {
  return { type: 'Feature', properties, geometry: { type: 'Point', coordinates: [...coordinates] } }
}

function activityFrame(flows: readonly PreviewFlow[], age: number, { hopMs, reduced }: FlowPlayback): GeoJSON.FeatureCollection {
  const features: GeoJSON.Feature[] = []
  for (const { id: route, stops } of flows) {
    const last = stops.length - 1
    const progress = reduced ? last : Math.min(last, age / hopMs)
    const fade = reduced ? 1 : Math.max(0, 1 - Math.max(0, age - last * hopMs) / lingerMs)
    if (fade === 0) continue

    for (let index = 0; index < progress; index += 1) {
      const from = stops[index].coordinates, to = stops[index + 1].coordinates
      const fraction = Math.min(1, progress - index)
      const position: Coordinates = [from[0] + (to[0] - from[0]) * fraction, from[1] + (to[1] - from[1]) * fraction]
      features.push({ type: 'Feature', properties: { kind: 'trail', route, opacity: fade * (reduced ? 0.8 : Math.max(0.16, 1 - (progress - index) / 3)) }, geometry: { type: 'LineString', coordinates: [[...from], [...position]] } })
      if (!reduced && index === Math.ceil(progress) - 1 && progress < last) features.push(point(position, { kind: 'head', route, opacity: 1 }))
    }

    stops.forEach(({ coordinates, schematic }, index) => {
      if (schematic) {
        features.push(point(coordinates, { kind: 'schematic', route, opacity: fade }))
        return
      }
      if (index === 0 && last > 0) return
      const sinceArrival = age - index * hopMs
      if (reduced || (sinceArrival >= 0 && sinceArrival < ringMs)) {
        features.push(point(coordinates, { kind: 'arrival', route, opacity: reduced ? 0.8 : 1 - sinceArrival / ringMs, radius: reduced ? 19 : 15 + 13 * sinceArrival / ringMs }))
      }
    })
  }
  return { type: 'FeatureCollection', features }
}

export function playPreviewFlow(source: GeoJSONSource, flows: readonly PreviewFlow[], playback: FlowPlayback, onFinish: () => void, onError: (error: unknown) => void) {
  const start = performance.now()
  const travelMs = Math.max(0, ...flows.map(({ stops }) => stops.length - 1)) * playback.hopMs
  const duration = (playback.reduced ? 0 : travelMs) + lingerMs
  let frame = 0
  let disposed = false
  const write = (data: GeoJSON.FeatureCollection) => {
    void source.setData(data).catch((error: unknown) => { if (!disposed) onError(error) })
  }

  function finish() {
    write(empty)
    onFinish()
  }

  function tick(now: number) {
    const age = Math.max(0, now - start)
    if (playback.mode === 'once' && age >= duration) {
      finish()
      return
    }
    write(activityFrame(flows, age % duration, playback))
    frame = requestAnimationFrame(tick)
  }

  write(activityFrame(flows, 0, playback))
  const expiry = playback.reduced && playback.mode === 'once' ? window.setTimeout(finish, duration) : undefined
  if (!playback.reduced) frame = requestAnimationFrame(tick)
  return () => {
    disposed = true
    cancelAnimationFrame(frame)
    window.clearTimeout(expiry)
    write(empty)
  }
}
