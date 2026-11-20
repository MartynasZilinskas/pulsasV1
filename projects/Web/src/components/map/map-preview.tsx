import { Button } from '@ui-sdk/react/button'
import { LngLatBounds, Map as MapLibre, type GeoJSONSource, type MapGeoJSONFeature } from 'maplibre-gl'
import { useEffect, useEffectEvent, useId, useRef, useState, type ReactNode } from 'react'
import { playPreviewFlow, type FlowPlayback, type PreviewFlow } from './map-preview-flow'
import {
  ambiguousLayer, chosenSource, clusterLayer, empty, flowLayers, flowSource, gapLayer, hopLayer, labelLayer, labelZoom,
  loadPreviewImages, markerLayer, offlineStyle, pathLayer, previewId, previewRoles, raisedLayer, roleArtwork, roleLayer, roleZoom, weakPathLayer,
} from './map-preview-style'
import { NodeRoleIcon, type NodeRole } from '../nodes/node-role'
import './maplibre-worker'
import 'maplibre-gl/dist/maplibre-gl.css'
import './map-preview.css'

type Coordinates = readonly [longitude: number, latitude: number]
type Marker = {
  id: string; coordinates: Coordinates; label: string; name?: string; role?: NodeRole; prefix?: string
  presentation?: 'node' | 'gap'; confidence?: 'strong' | 'weak'; ambiguous?: boolean
}
type Path = { id: string; coordinates: readonly Coordinates[]; confidence?: 'strong' | 'weak' }

export type MapPreviewScene = {
  markers: readonly Marker[]; paths: readonly Path[]; zoom?: number; clustered?: boolean
}

type MapPreviewProps = {
  sceneId: string; label: string; center: Coordinates; scene: MapPreviewScene
  selectedId: string | null; focusRequest: number
  onPick: (ids: readonly string[]) => void
  onClusterToggle?: (clustered: boolean) => void
  flow?: readonly PreviewFlow[]; playback?: FlowPlayback | null; onFlowFinish?: () => void
  renderControls?: (ready: boolean) => ReactNode
}

function renderedIds(features: readonly MapGeoJSONFeature[]): string[] {
  const ids = new Set<string>()
  for (const { properties } of features) {
    const id: unknown = properties[previewId]
    if (typeof id === 'string') ids.add(id)
  }
  return [...ids]
}

function markerFeature(marker: Marker): GeoJSON.Feature<GeoJSON.Point> {
  const { id, coordinates, label, name, role, confidence, ambiguous, presentation, prefix } = marker
  return {
    type: 'Feature', geometry: { type: 'Point', coordinates: [...coordinates] },
    properties: {
      [previewId]: id, name: name ?? label, presentation: presentation ?? 'node',
      ...(role && roleArtwork(role)), ...(confidence && { confidence, hopLabel: label }),
      ambiguous: ambiguous ?? false, gapLabel: prefix ? `${label} · ${prefix}` : label,
      raisedLabel: `${confidence ? `${label} · ` : ''}${name ?? label}${ambiguous ? ' · ?' : ''}`,
    },
  }
}

function fitScene(map: MapLibre, scene: MapPreviewScene, center: Coordinates, selectedId: string | null) {
  if (scene.zoom !== undefined) {
    const marker = scene.markers.find(({ id }) => id === selectedId) ?? scene.markers[0]
    map.jumpTo({ center: [...(marker?.coordinates ?? center)], zoom: scene.zoom })
    return
  }
  const points = [...scene.markers.map(({ coordinates }) => coordinates), ...scene.paths.flatMap(({ coordinates }) => coordinates)]
  if (points.length === 0) {
    map.jumpTo({ center: [...center], zoom: 11 })
    return
  }
  const bounds = new LngLatBounds()
  for (const point of points) bounds.extend([...point])
  const padding = Math.min(64, Math.floor(map.getContainer().clientWidth * 0.22))
  map.fitBounds(bounds, { padding, maxZoom: 14.5, duration: 0 })
}

function visibleClusters(map: MapLibre) {
  const clusters = new globalThis.Map<number, { id: number; count: number; coordinates: Coordinates }>()
  for (const { properties, geometry } of map.queryRenderedFeatures({ layers: [clusterLayer] })) {
    if (geometry.type !== 'Point') continue
    const id = Number(properties.cluster_id), count = Number(properties.point_count)
    const [longitude, latitude] = geometry.coordinates
    clusters.set(id, { id, count, coordinates: [longitude, latitude] })
  }
  return [...clusters.values()].sort((a, b) => b.count - a.count || a.id - b.id)
}

export function MapPreview({ sceneId, label, center, scene, selectedId, focusRequest, onPick, onClusterToggle, flow, playback, onFlowFinish, renderControls }: MapPreviewProps) {
  const container = useRef<HTMLDivElement>(null)
  const artwork = useRef<HTMLDivElement>(null)
  const framing = useRef<{ map: MapLibre; sceneId: string } | null>(null)
  const lastFocus = useRef(0)
  const descriptionId = useId()
  const [attempt, setAttempt] = useState(0)
  const [loadedMap, setLoadedMap] = useState<MapLibre | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [zoom, setZoom] = useState(11)
  const [clusters, setClusters] = useState<ReturnType<typeof visibleClusters>>([])
  const [clusterError, setClusterError] = useState(false)
  const pick = useEffectEvent((ids: readonly string[]) => onPick(ids))
  const finishFlow = useEffectEvent(() => onFlowFinish?.())
  const initialFrame = useEffectEvent((map: MapLibre) => fitScene(map, scene, center, selectedId))
  const [longitude, latitude] = center

  async function expandCluster(map: MapLibre, id: number, coordinates: Coordinates, control?: HTMLElement) {
    const scene = framing.current
    try {
      const zoom = await map.getSource<GeoJSONSource>('markers')?.getClusterExpansionZoom(id)
      if (zoom === undefined || framing.current !== scene || !map.getSource('markers')) return
      map.jumpTo({ center: [...coordinates], zoom })
      if (document.activeElement === control) map.getCanvas().focus({ preventScroll: true })
    } catch {
      if (map.getSource('markers')) setClusterError(true)
    }
  }

  useEffect(() => {
    const element = container.current
    const roleArtwork = artwork.current
    if (!element || !roleArtwork) return
    let map: MapLibre
    try {
      map = new MapLibre({
        container: element, style: offlineStyle(), center: [longitude, latitude], zoom: 11,
        minZoom: 4, maxZoom: 18, scrollZoom: false, dragRotate: false, touchPitch: false,
        cooperativeGestures: true, attributionControl: false, renderWorldCopies: false, fadeDuration: 0,
      })
    } catch (error) {
      console.error('Map preview could not initialize.', error)
      setStatus('error')
      return
    }

    let disposed = false
    let failed = false
    const showMapError = () => {
      if (disposed || failed) return
      failed = true
      setLoadedMap(null)
      setStatus('error')
      finishFlow()
    }
    const fail = (error: unknown) => {
      if (disposed || failed) return
      console.error('Map preview could not render.', error)
      showMapError()
    }
    map.keyboard.disableRotation()
    map.touchZoomRotate.disableRotation()
    map.on('error', ({ error }) => fail(error))
    map.on('webglcontextlost', showMapError)
    map.on('load', () => {
      void loadPreviewImages(roleArtwork).then((install) => {
        if (disposed || failed) return
        install(map)
        setLoadedMap(map)
      }).catch(fail)
    })
    map.on('zoomend', () => setZoom(map.getZoom()))
    for (const layer of [clusterLayer, markerLayer, raisedLayer]) {
      map.on('mouseenter', layer, () => { map.getCanvas().classList.add('map-preview-pickable') })
      map.on('mouseleave', layer, () => { map.getCanvas().classList.remove('map-preview-pickable') })
    }
    map.on('click', ({ point: { x, y } }) => {
      if (failed || !map.getLayer(markerLayer)) return
      const box: [[number, number], [number, number]] = [[x - 7, y - 7], [x + 7, y + 7]]
      const cluster = map.queryRenderedFeatures(box, { layers: [clusterLayer] })[0]
      if (cluster?.geometry.type === 'Point') {
        const [longitude, latitude] = cluster.geometry.coordinates
        void expandCluster(map, Number(cluster.properties.cluster_id), [longitude, latitude])
        return
      }
      const ids = renderedIds(map.queryRenderedFeatures(box, { layers: [markerLayer, raisedLayer] }))
      if (ids.length) pick(ids)
    })
    return () => { disposed = true; map.remove() }
  }, [attempt, longitude, latitude])

  useEffect(() => {
    if (!loadedMap) return
    const canvas = loadedMap.getCanvas()
    canvas.setAttribute('aria-label', label)
    canvas.setAttribute('aria-describedby', descriptionId)
  }, [loadedMap, label, descriptionId])

  useEffect(() => {
    if (!loadedMap) return
    const markers = loadedMap.getSource<GeoJSONSource>('markers')
    const paths = loadedMap.getSource<GeoJSONSource>('paths')
    if (!markers || !paths) return
    let obsolete = false
    let committed = false
    setClusterError(false)
    const reportRenderedScene = () => {
      if (!committed) return
      const visible = scene.clustered ? visibleClusters(loadedMap) : []
      setClusters(visible)
      const query = (...layers: string[]) => renderedIds(loadedMap.queryRenderedFeatures({ layers }))
      const weakPathIds = query(weakPathLayer), strongPathIds = query(pathLayer)
      const hopLabels = Object.fromEntries(loadedMap.queryRenderedFeatures({ layers: [hopLayer] }).map(({ properties }) => [properties[previewId], properties.hopLabel]))
      loadedMap.getContainer().dispatchEvent(new CustomEvent('map-preview-rendered', {
        bubbles: true, detail: {
          sceneId, markerIds: query(markerLayer, raisedLayer), pathIds: [...new Set([...strongPathIds, ...weakPathIds])],
          gapIds: query(gapLayer), strongPathIds, weakPathIds, roleIds: query(roleLayer), labelIds: query(labelLayer),
          raisedIds: query(raisedLayer), ambiguousIds: query(ambiguousLayer), clusterCounts: visible.map(({ count }) => count), hopLabels,
        },
      }))
      setStatus('ready')
    }

    loadedMap.on('idle', reportRenderedScene)
    if (framing.current?.map !== loadedMap || framing.current.sceneId !== sceneId) {
      framing.current = { map: loadedMap, sceneId }
      initialFrame(loadedMap)
    }
    const clustered = scene.clustered ?? false
    const updates = [markers.setData({ type: 'FeatureCollection', features: scene.markers.map(markerFeature) })]
    if (markers.getClusterOptions().cluster !== clustered) updates.push(markers.setClusterOptions({ cluster: clustered }))
    updates.push(paths.setData({
      type: 'FeatureCollection', features: scene.paths.map(({ id, coordinates, confidence }) => ({
        type: 'Feature', properties: { [previewId]: id, confidence: confidence ?? 'strong' },
        geometry: { type: 'LineString', coordinates: coordinates.map((point) => [...point]) },
      })),
    }))
    void Promise.all(updates).then(() => {
      if (obsolete) return
      committed = true
      loadedMap.triggerRepaint()
    }).catch((error: unknown) => {
      if (obsolete) return
      console.error('Could not load the offline example.', error)
      setLoadedMap(null)
      setStatus('error')
    })
    return () => { obsolete = true; loadedMap.off('idle', reportRenderedScene) }
  }, [loadedMap, sceneId, scene])

  useEffect(() => {
    if (!loadedMap) return
    const chosen = loadedMap.getSource<GeoJSONSource>(chosenSource)
    if (!chosen) return
    const selected = scene.markers.find(({ id }) => id === selectedId)
    loadedMap.setFilter(hopLayer, ['all', ['has', 'confidence'], ['!=', ['get', 'presentation'], 'gap'], ['!=', ['get', previewId], selectedId]])
    loadedMap.setFilter(ambiguousLayer, ['all', ['==', ['get', 'ambiguous'], true], ['!=', ['get', previewId], selectedId]])
    loadedMap.setFilter(labelLayer, ['all', ['!', ['has', 'point_count']], ['!=', ['get', 'presentation'], 'gap'], ['!=', ['get', previewId], selectedId]])
    void chosen.setData(selected ? { type: 'FeatureCollection', features: [markerFeature(selected)] } : empty).catch(() => {
      // MapLibre reports source errors through the map's error handler.
    })
    if (selected && focusRequest !== lastFocus.current) {
      loadedMap.jumpTo({ center: [...selected.coordinates], zoom: Math.max(labelZoom + 0.5, loadedMap.getZoom()) })
    }
    lastFocus.current = focusRequest
  }, [loadedMap, scene, selectedId, focusRequest])

  useEffect(() => {
    if (!loadedMap || status !== 'ready' || !flow || !playback || document.hidden) return
    const source = loadedMap.getSource<GeoJSONSource>(flowSource)
    if (!source) return
    return playPreviewFlow(source, flow, playback, finishFlow, (error) => {
      console.error('The packet flow could not play.', error)
      finishFlow()
    })
  }, [loadedMap, status, flow, playback])

  useEffect(() => {
    if (!loadedMap || !flow) return
    const flowIds = new Set(flow.map(({ id }) => id))
    let previous: string | undefined
    const reportFlow = () => {
      const routes = new globalThis.Map<string, Set<string>>()
      for (const { properties } of loadedMap.queryRenderedFeatures({ layers: flowLayers })) {
        const { route, kind } = properties
        if (typeof route !== 'string' || typeof kind !== 'string') continue
        if (!flowIds.has(route)) return
        const kinds = routes.get(route) ?? new Set<string>()
        kinds.add(kind)
        routes.set(route, kinds)
      }
      const activity = [...routes.keys()].sort().map((routeId) => ({ routeId, kinds: [...routes.get(routeId)!].sort() }))
      const signature = JSON.stringify(activity)
      if (signature === previous) return
      previous = signature
      loadedMap.getContainer().dispatchEvent(new CustomEvent('map-preview-flow-rendered', {
        bubbles: true, detail: { sceneId, routes: activity },
      }))
    }
    loadedMap.on('render', reportFlow)
    return () => { loadedMap.off('render', reportFlow) }
  }, [loadedMap, flow, sceneId])

  const ready = status === 'ready'
  function retry() {
    setLoadedMap(null)
    setStatus('loading')
    setAttempt((value) => value + 1)
  }

  return (
    <div className="map-preview" data-map-state={status}>
      <div hidden ref={artwork}>{previewRoles.map((role) => <span key={role}><NodeRoleIcon role={role} /></span>)}</div>
      <div className="map-preview-toolbar">
        <span className="map-preview-zoom">{ready ? `Zoom ${zoom.toFixed(1)} · ${zoom < roleZoom ? 'Dots' : zoom < labelZoom ? 'Roles' : 'Names'}` : 'Map controls'}</span>
        {onClusterToggle && <Button className="map-preview-button" aria-pressed={scene.clustered ?? false} disabled={!ready} onClick={() => onClusterToggle(!scene.clustered)}>Clustering</Button>}
        <Button className="map-preview-button" disabled={!ready} onClick={() => { if (loadedMap) fitScene(loadedMap, scene, center, selectedId) }}>Fit example</Button>
        <Button className="map-preview-button map-preview-zoom-button" aria-label="Zoom out" disabled={!ready || zoom <= 4} onClick={() => { loadedMap?.zoomOut({ duration: 0 }) }}>−</Button>
        <Button className="map-preview-button map-preview-zoom-button" aria-label="Zoom in" disabled={!ready || zoom >= 18} onClick={() => { loadedMap?.zoomIn({ duration: 0 }) }}>+</Button>
      </div>
      {renderControls?.(ready)}
      <div className="map-preview-stage">
        <div className="map-preview-canvas" ref={container} hidden={status === 'error'} />
        <span className="map-preview-note">Offline · No basemap</span>
        <div className="map-preview-status" role="status">
          {status === 'loading' && <p>Loading offline map…</p>}
          {status === 'error' && <p>The map couldn’t load. The example details are still available.</p>}
        </div>
        {status === 'error' && <Button className="map-preview-button map-preview-retry" onClick={retry}>Retry map</Button>}
      </div>
      <p className="map-preview-help" id={descriptionId}>Drag or use arrow keys to pan. On touch, use two fingers. Use the map controls or + / − to zoom.</p>
      {ready && scene.clustered && (clusters.length > 0 || clusterError) && <div className="map-preview-clusters">
        {clusters.map(({ id, count, coordinates }, index) => <Button key={id} className="map-preview-button" onClick={(event) => { if (loadedMap) void expandCluster(loadedMap, id, coordinates, event.currentTarget) }}>
          Expand {clusters.length > 1 ? `cluster ${index + 1}` : 'cluster'} · {count} nodes
        </Button>)}
        {clusterError && <p role="status">This cluster couldn’t expand. Turn off clustering to see individual nodes.</p>}
      </div>}
    </div>
  )
}
