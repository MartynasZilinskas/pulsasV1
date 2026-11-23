import { Button } from '@ui-sdk/react/button'
import { Radio, RadioGroup } from '@ui-sdk/react/radio'
import { MapWorkspaceControls } from './map-workspace-controls'
import { MapOverlap } from './map-overlap-view'
import { screenOverlaps, type ScreenOverlap } from './map-overlap'
import { LngLatBounds, Map as MapLibre, Popup, type GeoJSONSource, type MapGeoJSONFeature } from 'maplibre-gl'
import { useEffect, useEffectEvent, useId, useRef, useState, type ReactNode } from 'react'
import { clearBasemap, installBasemap, isBasemapError, loadBasemap, restrictMapRequest, type MapPreviewTheme } from './map-preview-basemap-style'
import { playPreviewFlow, type FlowPlayback, type PreviewFlowInput } from './map-preview-flow'
import {
  ambiguousLayer, applyMapPreviewTheme, chosenSource, clusterLayer, empty, evidenceLabelLayer, excludeRaisedLabels, flowLayers, flowSource, gapHopLayer, gapLayer, hopLayer,
  labelLayer, labelZoom, loadPreviewImages, markerLayer, offlineStyle, pathLayer, previewId, previewRoles, raisedAmbiguousLayer,
  raisedHopLayer, raisedLabelLayer, raisedLayer, roleImageId, roleLayer, roleZoom, weakPathHaloLayer, weakPathLayer, type PreviewRoleColors,
} from './map-preview-style'
import { NodeRoleIcon, nodeRoles, type NodeRole } from '../nodes/node-role'
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
  regional?: boolean
  onPick: (ids: readonly string[]) => void
  onClearSelection?: () => void
  onClusterToggle?: (clustered: boolean) => void
  onViewportNodesChange?: (ids: readonly string[] | null) => void
  flow?: PreviewFlowInput; playback?: FlowPlayback | null; onFlowFinish?: () => void
  renderControls?: (ready: boolean) => ReactNode
  presentation?: 'specimen' | 'workspace'
  settingsOpen?: boolean
  onSettingsOpenChange?: (open: boolean) => void
  layersOpen?: boolean
  onLayersOpenChange?: (open: boolean) => void
}

function renderedIds(features: readonly MapGeoJSONFeature[]): string[] {
  const ids = new Set<string>()
  for (const { properties } of features) {
    const id: unknown = properties[previewId]
    if (typeof id === 'string') ids.add(id)
  }
  return [...ids]
}

function markerFeature(marker: Marker, roleColors: PreviewRoleColors): GeoJSON.Feature<GeoJSON.Point> {
  const { id, coordinates, label, name, role, confidence, ambiguous, presentation, prefix } = marker
  return {
    type: 'Feature', geometry: { type: 'Point', coordinates: [...coordinates] },
    properties: {
      [previewId]: id, name: name ?? label, presentation: presentation ?? 'node',
      ...(role && { roleImage: roleImageId(role), ...roleColors.get(role) }), ...(confidence && { confidence, hopLabel: label }),
      ...(presentation === 'gap' && { prefix, hopLabel: label }), ambiguous: ambiguous ?? false,
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

function viewportNodeIds(map: MapLibre, markers: readonly Marker[]): string[] {
  const { clientWidth: width, clientHeight: height } = map.getCanvas()
  const ids = new Set<string>()
  // Use advertised positions, not rendered features, so clustering and occlusion don't change membership.
  for (const { id, coordinates, presentation } of markers) {
    if (presentation === 'gap') continue
    const { x, y } = map.project([coordinates[0], coordinates[1]])
    if (x >= 0 && y >= 0 && x <= width && y <= height) ids.add(id)
  }
  return [...ids]
}

const fixtureReportLayers = [
  markerLayer, raisedLayer, pathLayer, weakPathLayer, weakPathHaloLayer, gapLayer, gapHopLayer,
  roleLayer, labelLayer, evidenceLabelLayer, raisedLabelLayer, hopLayer, raisedHopLayer, ambiguousLayer, raisedAmbiguousLayer,
]
const minZoom = 0
const maxZoom = 18

function frameDiagnosticsEnabled() {
  return new URLSearchParams(window.location.search).has('mapDiagnostics')
}

export function MapPreview({ sceneId, label, center, scene, selectedId, focusRequest, regional = false, onPick, onClearSelection, onClusterToggle, onViewportNodesChange, flow, playback, onFlowFinish, renderControls, presentation = 'specimen', settingsOpen, onSettingsOpenChange, layersOpen, onLayersOpenChange }: MapPreviewProps) {
  const container = useRef<HTMLDivElement>(null)
  const artwork = useRef<HTMLDivElement>(null)
  const framing = useRef<{ map: MapLibre; sceneId: string } | null>(null)
  const basemapErrors = useRef<{ map: MapLibre; installing: boolean; fail: (error: unknown) => void } | null>(null)
  const clusterSignature = useRef('[]')
  const lastFocus = useRef(0)
  const zoomTarget = useRef<number | null>(null)
  const descriptionId = useId()
  const [attempt, setAttempt] = useState(0)
  const [localSettingsOpen, setLocalSettingsOpen] = useState(false)
  const [localLayersOpen, setLocalLayersOpen] = useState(false)
  const [loadedPreview, setLoadedPreview] = useState<{ map: MapLibre; roleColors: PreviewRoleColors } | null>(null)
  const loadedMap = loadedPreview?.map ?? null
  const roleColors = loadedPreview?.roleColors
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [theme, setTheme] = useState<MapPreviewTheme>(() => presentation === 'workspace' && document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light')
  const [basemap, setBasemap] = useState<{ map: MapLibre; theme: MapPreviewTheme; attempt: number; status: 'ready' | 'error' } | null>(null)
  const [basemapAttempt, setBasemapAttempt] = useState(0)
  const [zoom, setZoom] = useState(11)
  const [clusters, setClusters] = useState<ReturnType<typeof visibleClusters>>([])
  const [clusterError, setClusterError] = useState(false)
  const [centerPickMissed, setCenterPickMissed] = useState(false)
  const [spiderfyEnabled, setSpiderfyEnabled] = useState(false)
  const spiderfy = !regional && spiderfyEnabled
  const [overlaps, setOverlaps] = useState<ScreenOverlap[]>([])
  const [expandedOverlap, setExpandedOverlap] = useState<string | null>(null)
  const overlapGroups = useRef<ScreenOverlap[]>([])
  const openOverlap = useRef<((id: string) => void) | null>(null)
  const pick = useEffectEvent((ids: readonly string[]) => {
    const overlap = spiderfy && overlapGroups.current.find(group => group.nodes.some(node => ids.includes(node.id)))
    if (overlap && openOverlap.current) openOverlap.current(overlap.id)
    else onPick(ids)
  })
  const clearPick = useEffectEvent(() => { setExpandedOverlap(null); onClearSelection?.() })
  const finishFlow = useEffectEvent(() => onFlowFinish?.())
  const initialFrame = useEffectEvent((map: MapLibre) => fitScene(map, scene, center, selectedId))
  const reportViewportNodes = useEffectEvent((ids: readonly string[] | null) => onViewportNodesChange?.(ids))
  const trackViewportNodes = onViewportNodesChange !== undefined
  const [longitude, latitude] = center

  useEffect(() => {
    if (presentation !== 'workspace') return
    const root = document.documentElement
    const observer = new MutationObserver(() => setTheme(root.dataset.theme === 'dark' ? 'dark' : 'light'))
    observer.observe(root, { attributes: true, attributeFilter: ['data-theme'] })
    return () => observer.disconnect()
  }, [presentation])

  async function expandCluster(map: MapLibre, id: number, coordinates: Coordinates, control?: HTMLElement) {
    const scene = framing.current
    try {
      const source = map.getSource<GeoJSONSource>('markers')
      if (presentation === 'workspace' && source) {
        const leaves = await source.getClusterLeaves(id, Infinity, 0)
        if (framing.current !== scene || map.getSource('markers') !== source) return
        const ids = leaves.flatMap(feature => typeof feature.properties?.[previewId] === 'string' ? [feature.properties[previewId] as string] : [])
        if (ids.length) pick([...new Set(ids)])
        return
      }
      const zoom = await source?.getClusterExpansionZoom(id)
      if (zoom === undefined || framing.current !== scene || !map.getSource('markers')) return
      map.easeTo({ center: [...coordinates], zoom: Math.max(zoom, map.getZoom(), zoomTarget.current ?? minZoom), duration: 420 })
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
        minZoom, maxZoom, scrollZoom: presentation === 'workspace', dragRotate: false, touchPitch: false,
        cooperativeGestures: presentation !== 'workspace', attributionControl: { compact: true }, renderWorldCopies: false, fadeDuration: 0,
        transformRequest: restrictMapRequest,
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
      setLoadedPreview(null)
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
    map.on('error', (event) => {
      const activeBasemap = basemapErrors.current?.map === map ? basemapErrors.current : null
      const sourceId = 'sourceId' in event && typeof event.sourceId === 'string' ? event.sourceId : undefined
      // Synchronous style validation errors from installBasemap have no source ID or request URL.
      if (activeBasemap?.installing || isBasemapError({ sourceId, error: event.error })) {
        activeBasemap?.fail(event.error)
        return
      }
      fail(event.error)
    })
    map.on('webglcontextlost', showMapError)
    map.on('load', () => {
      void loadPreviewImages(roleArtwork).then(({ install, roleColors }) => {
        if (disposed || failed) return
        install(map)
        setLoadedPreview({ map, roleColors })
      }).catch(fail)
    })
    map.on('zoomend', () => { zoomTarget.current = null; setZoom(map.getZoom()) })
    const pickLayers = [markerLayer, raisedLayer]
    for (const layer of [clusterLayer, ...pickLayers]) {
      map.on('mouseenter', layer, () => { map.getCanvas().classList.add('map-preview-pickable') })
      map.on('mouseleave', layer, () => { map.getCanvas().classList.remove('map-preview-pickable') })
    }
    function pickAt(x: number, y: number) {
      if (failed || !map.getLayer(markerLayer)) return false
      const box: [[number, number], [number, number]] = [[x - 9, y - 9], [x + 9, y + 9]]
      const cluster = map.queryRenderedFeatures(box, { layers: [clusterLayer] })[0]
      if (cluster?.geometry.type === 'Point') {
        const [longitude, latitude] = cluster.geometry.coordinates
        void expandCluster(map, Number(cluster.properties.cluster_id), [longitude, latitude])
        return true
      }
      const ids = renderedIds(map.queryRenderedFeatures(box, { layers: pickLayers }))
      if (ids.length) pick(ids)
      return ids.length > 0
    }
    map.on('click', ({ point: { x, y } }) => {
      setCenterPickMissed(false)
      if (!pickAt(x, y)) clearPick()
    })
    const canvas = map.getCanvas()
    const pickCenter = (event: KeyboardEvent) => {
      if (event.key !== 'Enter' || event.defaultPrevented || event.repeat) return
      event.preventDefault()
      setCenterPickMissed(!pickAt(canvas.clientWidth / 2, canvas.clientHeight / 2))
    }
    const clearPickMiss = () => setCenterPickMissed(false)
    canvas.addEventListener('keydown', pickCenter)
    canvas.addEventListener('blur', clearPickMiss)
    map.on('movestart', clearPickMiss)
    return () => {
      disposed = true
      zoomTarget.current = null
      canvas.removeEventListener('keydown', pickCenter)
      canvas.removeEventListener('blur', clearPickMiss)
      map.remove()
    }
  }, [attempt, longitude, latitude, presentation])

  useEffect(() => {
    if (!loadedMap) return
    const attribution = loadedMap.getContainer().querySelector<HTMLDetailsElement>('.maplibregl-ctrl-attrib')
    const summary = attribution?.querySelector<HTMLElement>('summary')
    if (!attribution || !summary) return
    let hovered = false
    let hoverOpened = false
    const setOpen = (open: boolean) => {
      attribution.classList.toggle('maplibregl-compact-show', open)
      attribution.open = open
    }
    const synchronize = () => {
      // MapLibre collapses its compact class on drag; keep native disclosure semantics in sync.
      const open = attribution.classList.contains('maplibregl-compact-show')
      if (!open && attribution.contains(document.activeElement) && document.activeElement !== summary) summary.focus({ preventScroll: true })
      if (attribution.open !== open) attribution.open = open
    }
    const enter = (event: PointerEvent) => {
      if (event.pointerType !== 'mouse' || !matchMedia('(hover: hover)').matches) return
      hovered = true
      if (!attribution.open) { hoverOpened = true; setOpen(true) }
    }
    const closeHover = () => {
      if (!hoverOpened || hovered || (attribution.contains(document.activeElement) && document.activeElement !== summary)) return
      hoverOpened = false
      setOpen(false)
    }
    const leave = () => { hovered = false; closeHover() }
    const focusOut = () => { queueMicrotask(closeHover) }
    const toggle = () => { hoverOpened = false }
    const escape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented || !attribution.open) return
      event.preventDefault()
      event.stopPropagation()
      hoverOpened = false
      setOpen(false)
      summary.focus({ preventScroll: true })
    }
    const observer = new MutationObserver(synchronize)
    observer.observe(attribution, { attributes: true, attributeFilter: ['class', 'open'] })
    synchronize()
    attribution.addEventListener('pointerenter', enter)
    attribution.addEventListener('pointerleave', leave)
    attribution.addEventListener('focusout', focusOut)
    attribution.addEventListener('keydown', escape)
    summary.addEventListener('click', toggle)
    return () => {
      observer.disconnect()
      attribution.removeEventListener('pointerenter', enter)
      attribution.removeEventListener('pointerleave', leave)
      attribution.removeEventListener('focusout', focusOut)
      attribution.removeEventListener('keydown', escape)
      summary.removeEventListener('click', toggle)
    }
  }, [loadedMap])

  useEffect(() => {
    if (loadedMap) applyMapPreviewTheme(loadedMap, theme)
  }, [loadedMap, theme])

  useEffect(() => {
    if (!loadedMap) return

    const controller = new AbortController()
    const cancel = () => controller.abort()
    let failed = false
    let sources: string[] | undefined
    const reportReady = () => {
      if (controller.signal.aborted || failed || !sources?.every((id) => loadedMap.isSourceLoaded(id))) return
      loadedMap.off('render', reportReady)
      setBasemap({ map: loadedMap, theme, attempt: basemapAttempt, status: 'ready' })
    }
    const fail = (error: unknown) => {
      if (controller.signal.aborted || failed) return
      failed = true
      console.error('The OpenFreeMap basemap could not load.', error)
      // Source events can fire while MapLibre is updating its style or tiles.
      queueMicrotask(() => {
        if (controller.signal.aborted) return
        clearBasemap(loadedMap)
        loadedMap.off('render', reportReady)
        setBasemap({ map: loadedMap, theme, attempt: basemapAttempt, status: 'error' })
      })
    }
    const errors = { map: loadedMap, installing: false, fail }
    basemapErrors.current = errors
    loadedMap.on('webglcontextlost', cancel)
    loadedMap.on('remove', cancel)
    loadedMap.on('render', reportReady)
    void loadBasemap(theme, controller.signal).then((style) => {
      if (controller.signal.aborted || failed) return
      errors.installing = true
      try {
        installBasemap(loadedMap, style)
        sources = Object.keys(style.sources)
      } finally {
        errors.installing = false
      }
      loadedMap.triggerRepaint()
    }).catch(fail)
    return () => {
      cancel()
      if (basemapErrors.current === errors) basemapErrors.current = null
      loadedMap.off('webglcontextlost', cancel)
      loadedMap.off('remove', cancel)
      loadedMap.off('render', reportReady)
    }
  }, [loadedMap, theme, basemapAttempt])

  useEffect(() => {
    if (!loadedMap) return
    const canvas = loadedMap.getCanvas()
    canvas.setAttribute('aria-label', label)
    canvas.setAttribute('aria-describedby', descriptionId)
  }, [loadedMap, label, descriptionId])

  useEffect(() => {
    if (!loadedMap || expandedOverlap) return
    const canvas = loadedMap.getCanvas()
    const popup = new Popup({ closeButton: false, closeOnClick: true, closeOnMove: true, focusAfterOpen: false,
      offset: 18, maxWidth: `${Math.max(100, Math.min(260, canvas.clientWidth - 24))}px`, className: 'map-preview-node-hint' })
    let previous = ''
    const hide = () => { previous = ''; popup.remove() }
    const show = (x: number, y: number) => {
      if (loadedMap.isMoving()) { hide(); return }
      const features = loadedMap.queryRenderedFeatures([[x - 2, y - 2], [x + 2, y + 2]], { layers: [markerLayer, raisedLayer] })
      const ids = renderedIds(features)
      const group = spiderfy && overlapGroups.current.find(group => group.nodes.some(node => ids.includes(node.id)))
      if (group) { hide(); return }
      const feature = features[0]
      if (!ids.length || feature?.geometry.type !== 'Point') { hide(); return }
      const text = ids.length > 1 ? 'Multiple nodes here · Select to choose a name' : String(feature.properties.name)
      const key = ids.join(':')
      if (key === previous && popup.isOpen()) return
      previous = key
      const [longitude, latitude] = feature.geometry.coordinates
      popup.setText(text).setLngLat([longitude, latitude]).addTo(loadedMap)
    }
    const hover = (event: { point: { x: number; y: number }; originalEvent: MouseEvent }) => {
      if (!matchMedia('(any-hover: hover)').matches) return
      show(event.point.x, event.point.y)
    }
    const focus = () => show(canvas.clientWidth / 2, canvas.clientHeight / 2)
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') hide() }
    loadedMap.on('mousemove', hover)
    loadedMap.on('mouseout', hide)
    loadedMap.on('movestart', hide)
    loadedMap.on('click', hide)
    canvas.addEventListener('focus', focus)
    canvas.addEventListener('blur', hide)
    canvas.addEventListener('keydown', escape)
    return () => {
      hide(); loadedMap.off('mousemove', hover); loadedMap.off('mouseout', hide)
      loadedMap.off('movestart', hide); loadedMap.off('click', hide)
      canvas.removeEventListener('focus', focus); canvas.removeEventListener('blur', hide); canvas.removeEventListener('keydown', escape)
    }
  }, [loadedMap, scene, expandedOverlap, spiderfy])

  useEffect(() => {
    setExpandedOverlap(null)
    setOverlaps([])
    overlapGroups.current = []
    if (!loadedMap) return
    excludeRaisedLabels(loadedMap, selectedId)
    if (!spiderfy) return
    let dirty = true
    let signature = ''
    const invalidate = () => { dirty = true }
    const collapse = () => { setExpandedOverlap(null); invalidate() }
    const update = () => {
      if (!dirty || !loadedMap.isSourceLoaded('markers')) return
      dirty = false
      const { clientWidth: width, clientHeight: height } = loadedMap.getCanvas()
      const points = loadedMap.queryRenderedFeatures({ layers: [markerLayer] }).flatMap(feature => {
        if (feature.geometry.type !== 'Point') return []
        const point = loadedMap.project(feature.geometry.coordinates as [number, number])
        if (point.x < 0 || point.y < 0 || point.x > width || point.y > height) return []
        return [{ id: String(feature.properties[previewId]), x: point.x, y: point.y,
          radius: loadedMap.getZoom() < roleZoom ? 6 : feature.properties.confidence ? 15 : 10 }]
      })
      const groups = screenOverlaps(points)
      const next = JSON.stringify(groups)
      if (signature === next) return
      signature = next
      overlapGroups.current = groups
      setOverlaps(groups)
    }
    loadedMap.on('render', update)
    loadedMap.on('sourcedata', invalidate)
    loadedMap.on('move', invalidate)
    loadedMap.on('movestart', collapse)
    loadedMap.on('resize', collapse)
    return () => {
      loadedMap.off('render', update); loadedMap.off('sourcedata', invalidate)
      loadedMap.off('move', invalidate); loadedMap.off('movestart', collapse); loadedMap.off('resize', collapse)
      overlapGroups.current = []
    }
  }, [loadedMap, scene, spiderfy])

  const overlapMembership = JSON.stringify(spiderfy ? overlaps.map(group => ({ id: group.id, ids: group.nodes.map(node => node.id) })) : [])
  useEffect(() => {
    if (!loadedMap) return
    const groups = JSON.parse(overlapMembership) as { id: string; ids: string[] }[]
    excludeRaisedLabels(loadedMap, selectedId, groups.flatMap(group => group.ids),
      groups.find(group => group.id === expandedOverlap)?.ids ?? [])
  }, [loadedMap, selectedId, overlapMembership, expandedOverlap])

  useEffect(() => {
    if (!loadedMap || !selectedId || !onClearSelection || expandedOverlap) return
    const canvas = loadedMap.getCanvas()
    const clearSelection = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented || event.repeat || event.target !== canvas) return
      event.preventDefault()
      event.stopPropagation()
      setCenterPickMissed(false)
      onClearSelection()
    }
    canvas.addEventListener('keydown', clearSelection)
    return () => canvas.removeEventListener('keydown', clearSelection)
  }, [loadedMap, selectedId, onClearSelection, expandedOverlap])

  useEffect(() => {
    if (!loadedMap || regional) return
    const layers = [labelLayer, evidenceLabelLayer, raisedLabelLayer]
    let hidden = new Map<string, { source: string; id: string }>()
    const fitNames = () => {
      const { clientWidth: width, clientHeight: height } = loadedMap.getCanvas()
      const edges: [[number, number], [number, number]][] = [
        [[0, 0], [1, height]], [[width - 1, 0], [width, height]],
        [[0, 0], [width, 1]], [[0, height - 1], [width, height]],
      ]
      const next = new Map<string, { source: string; id: string }>()
      for (const edge of edges) {
        for (const feature of loadedMap.queryRenderedFeatures(edge, { layers })) {
          const id = String(feature.properties[previewId])
          const source = feature.source
          next.set(`${source}:${id}`, { source, id })
        }
      }
      // Opacity does not remove symbols from hit queries, so a clipped name can be restored without relayout or source rewrites.
      for (const [key, feature] of hidden) if (!next.has(key)) loadedMap.setFeatureState(feature, { nameOutsideViewport: false })
      for (const [key, feature] of next) if (!hidden.has(key)) loadedMap.setFeatureState(feature, { nameOutsideViewport: true })
      hidden = next
    }
    loadedMap.on('render', fitNames)
    return () => {
      loadedMap.off('render', fitNames)
      for (const feature of hidden.values()) loadedMap.removeFeatureState(feature, 'nameOutsideViewport')
    }
  }, [loadedMap, regional])

  useEffect(() => {
    if (!loadedMap || !roleColors) return
    const markers = loadedMap.getSource<GeoJSONSource>('markers')
    const paths = loadedMap.getSource<GeoJSONSource>('paths')
    if (!markers || !paths) return
    let obsolete = false
    let committed = false
    let reportedReady = false
    let previousReport: string | undefined
    const diagnostics = frameDiagnosticsEnabled()
    const listClusters = Boolean(scene.clustered && !regional)
    setClusterError(false)
    const reportRenderedScene = () => {
      // Playback changes its source continuously; neither it nor the basemap gates fixture readiness.
      if (!committed || !['markers', 'paths', chosenSource].every((id) => loadedMap.isSourceLoaded(id))) return
      if (!reportedReady) {
        reportedReady = true
        setStatus('ready')
      }
      const visible = scene.clustered && (listClusters || diagnostics) ? visibleClusters(loadedMap) : []
      if (!regional) {
        const nextClusters = JSON.stringify(visible)
        if (nextClusters !== clusterSignature.current) {
          clusterSignature.current = nextClusters
          setClusters(visible)
        }
      }
      if (!diagnostics) {
        if (!listClusters) loadedMap.off('render', reportRenderedScene)
        return
      }
      const features = new Map<string, MapGeoJSONFeature[]>()
      for (const feature of loadedMap.queryRenderedFeatures({ layers: fixtureReportLayers })) {
        if ([labelLayer, evidenceLabelLayer, raisedLabelLayer].includes(feature.layer.id)
          && loadedMap.getFeatureState({ source: feature.source, id: String(feature.properties[previewId]) }).nameOutsideViewport) continue
        const layer = features.get(feature.layer.id)
        if (layer) layer.push(feature)
        else features.set(feature.layer.id, [feature])
      }
      const query = (...layers: string[]) => renderedIds(layers.flatMap((layer) => features.get(layer) ?? [])).sort()
      const weakPathIds = query(weakPathLayer), strongPathIds = query(pathLayer)
      const renderedLabels = (field: string, ...layers: string[]) => Object.fromEntries(
        layers.flatMap((layer) => features.get(layer) ?? [])
          .map(({ properties }) => [String(properties[previewId]), properties[field]] as const)
          .sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0),
      )
      const hopLabels = renderedLabels('hopLabel', hopLayer, raisedHopLayer)
      const evidenceLabelIds = query(evidenceLabelLayer)
      const detail = {
        sceneId, zoom: Math.round(loadedMap.getZoom() * 100) / 100,
        markerIds: query(markerLayer, raisedLayer), pathIds: [...new Set([...strongPathIds, ...weakPathIds])],
        gapIds: query(gapLayer), gapPrefixes: renderedLabels('prefix', gapLayer), gapHopLabels: renderedLabels('hopLabel', gapHopLayer),
        strongPathIds, weakPathIds, weakPathHaloIds: query(weakPathHaloLayer), roleIds: query(roleLayer), labelIds: query(labelLayer, evidenceLabelLayer), evidenceLabelIds,
        raisedIds: query(raisedLayer), selectedNameplateIds: query(raisedLabelLayer), ambiguousIds: query(ambiguousLayer, raisedAmbiguousLayer), clusterCounts: visible.map(({ count }) => count), hopLabels,
      }
      const signature = JSON.stringify(detail)
      if (signature === previousReport) return
      previousReport = signature
      loadedMap.getContainer().dispatchEvent(new CustomEvent('map-preview-rendered', {
        bubbles: true, detail,
      }))
    }

    loadedMap.on('render', reportRenderedScene)
    if (framing.current?.map !== loadedMap || framing.current.sceneId !== sceneId) {
      framing.current = { map: loadedMap, sceneId }
      initialFrame(loadedMap)
    }
    const clustered = scene.clustered ?? false
    const updates = [markers.setData({ type: 'FeatureCollection', features: scene.markers.map((marker) => markerFeature(marker, roleColors)) })]
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
      console.error('Could not load the map example.', error)
      setLoadedPreview(null)
      setStatus('error')
    })
    return () => { obsolete = true; loadedMap.off('render', reportRenderedScene) }
  }, [loadedMap, roleColors, sceneId, scene, regional])

  useEffect(() => {
    if (!loadedMap || !roleColors) return
    const chosen = loadedMap.getSource<GeoJSONSource>(chosenSource)
    if (!chosen) return
    const selected = scene.markers.find(({ id }) => id === selectedId)
    void chosen.setData(selected ? { type: 'FeatureCollection', features: [markerFeature(selected, roleColors)] } : empty).catch(() => {
      // MapLibre reports source errors through the map's error handler.
    })
    if (selected && focusRequest !== lastFocus.current) {
      const zoom = Math.max(labelZoom + (regional ? 1 : 0.5), loadedMap.getZoom(), zoomTarget.current ?? minZoom)
      loadedMap.easeTo({ center: [...selected.coordinates], zoom, duration: 420 })
    }
    lastFocus.current = focusRequest
  }, [loadedMap, roleColors, scene, selectedId, focusRequest, regional])

  useEffect(() => {
    if (!trackViewportNodes) return
    if (!loadedMap || status !== 'ready') {
      reportViewportNodes(null)
      return
    }

    let previous: readonly string[] | undefined
    const update = () => {
      if (loadedMap.isMoving()) return
      const ids = viewportNodeIds(loadedMap, scene.markers)
      const last = previous
      if (last?.length === ids.length && ids.every((id, index) => id === last[index])) return
      previous = ids
      reportViewportNodes(ids)
    }
    // MapLibre emits moveend after a resize when idle, or when the ongoing movement settles.
    loadedMap.on('moveend', update)
    update()
    return () => { loadedMap.off('moveend', update) }
  }, [loadedMap, status, scene.markers, trackViewportNodes])

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
    if (!loadedMap || !flow || !frameDiagnosticsEnabled()) return
    const flowIds = typeof flow === 'function' ? null : new Set(flow.map(({ id }) => id))
    let previous: string | undefined
    const reportFlow = () => {
      const routes = new globalThis.Map<string, Set<string>>()
      for (const { properties } of loadedMap.queryRenderedFeatures({ layers: flowLayers })) {
        const { route, kind } = properties
        if (typeof route !== 'string' || typeof kind !== 'string') continue
        if (flowIds && !flowIds.has(route)) return
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
  const basemapStatus = status === 'error' ? 'unavailable'
    : basemap && basemap.map === loadedMap && basemap.theme === theme && basemap.attempt === basemapAttempt ? basemap.status
      : 'loading'
  function retry() {
    setLoadedPreview(null)
    setStatus('loading')
    setAttempt((value) => value + 1)
  }

  function changeZoom(direction: -1 | 1) {
    if (!loadedMap) return
    const previous = zoomTarget.current ?? loadedMap.getZoom()
    const next = Math.max(minZoom, Math.min(maxZoom, previous + direction))
    if (next === previous) return
    zoomTarget.current = next
    loadedMap.zoomTo(next, { duration: 320, easeId: 'preview-zoom' })
  }

  const zoomLevel = zoom < roleZoom
    ? `Dots · below ${roleZoom}`
    : zoom < labelZoom ? `Role glyphs · ${roleZoom}–${labelZoom}` : `Names · ${labelZoom}+`

  const controls = <>
        <span className="map-preview-zoom">{ready ? `Zoom ${zoom.toFixed(1)} · ${zoomLevel}` : 'Map controls'}</span>
        <RadioGroup className="map-preview-theme" aria-label="Map theme" value={theme}
          onValueChange={(value) => { if (value === 'light' || value === 'dark') setTheme(value) }}>
          <Radio className="map-preview-button" value="light">Light</Radio>
          <Radio className="map-preview-button" value="dark">Dark</Radio>
        </RadioGroup>
        {(!regional || onClusterToggle) && <div className="map-preview-control-pair">
          {onClusterToggle && <Button className="map-preview-button" aria-pressed={scene.clustered ?? false} disabled={!ready} onClick={() => onClusterToggle(!scene.clustered)}>Clustering</Button>}
          {!regional && <Button className="map-preview-button" aria-pressed={spiderfy} disabled={!ready} onClick={() => setSpiderfyEnabled(value => !value)}>Spiderfy</Button>}
        </div>}
        <Button className="map-preview-button" disabled={!ready} onClick={() => { if (loadedMap) fitScene(loadedMap, scene, center, selectedId) }}>{regional ? 'Fit all nodes' : 'Fit example'}</Button>
        {presentation === 'specimen' && <div className="map-preview-control-pair">
          <Button className="map-preview-button map-preview-zoom-button" aria-label="Zoom out" disabled={!ready || zoom <= minZoom} onClick={() => changeZoom(-1)}>−</Button>
          <Button className="map-preview-button map-preview-zoom-button" aria-label="Zoom in" disabled={!ready || zoom >= maxZoom} onClick={() => changeZoom(1)}>+</Button>
        </div>}
  </>
  const instructions = `Drag or use arrow keys to pan. On touch, use two fingers. Use the map controls or + / − to zoom. Select a node to zoom in. Press Enter on the map to select a node or ${presentation === 'workspace' ? 'list the nodes in a cluster' : 'expand a cluster'} at the crosshair.${onClearSelection ? ' Press Escape on the map to clear selection.' : ''}`

  return (
    <div className="map-preview" data-presentation={presentation} data-map-state={status} data-map-theme={theme} data-basemap-state={basemapStatus}>
      <div hidden ref={artwork}>
        {previewRoles.map((role) => <span key={role} data-tone={nodeRoles[role].tone}><NodeRoleIcon role={role} /></span>)}
      </div>
      {presentation === 'specimen' ? <div className="map-preview-toolbar">{controls}</div> : <MapWorkspaceControls
        open={settingsOpen ?? localSettingsOpen} onOpenChange={open => {
          if (open) (onLayersOpenChange ?? setLocalLayersOpen)(false)
          const change = onSettingsOpenChange ?? setLocalSettingsOpen
          change(open)
        }} layersOpen={layersOpen ?? localLayersOpen} onLayersOpenChange={open => {
          if (open) (onSettingsOpenChange ?? setLocalSettingsOpen)(false)
          const change = onLayersOpenChange ?? setLocalLayersOpen
          change(open)
        }}
        theme={theme} onThemeChange={setTheme} clustered={scene.clustered ?? false} onClusterChange={onClusterToggle}
        ready={ready} canZoomIn={zoom < maxZoom} canZoomOut={zoom > minZoom} onZoom={changeZoom}
        onFit={() => { if (loadedMap) fitScene(loadedMap, scene, center, selectedId) }} />}
      {renderControls?.(ready)}
      <div className="map-preview-stage">
        <div className="map-preview-canvas" ref={container} hidden={status === 'error'} />
        {ready && spiderfy && loadedMap && <MapOverlap map={loadedMap} groups={overlaps} markers={scene.markers} selectedId={selectedId}
          expanded={expandedOverlap} onExpandedChange={setExpandedOverlap} openRef={openOverlap} theme={theme}
          onPick={(id) => { setExpandedOverlap(null); onPick([id]) }} />}
        {ready && <span className="map-preview-crosshair" aria-hidden="true">+</span>}
        <div className="map-preview-status" role="status">
          {status === 'loading' && <p>Loading map example…</p>}
          {status === 'error' && <p>The map couldn’t load. The example details are still available.</p>}
          {ready && centerPickMissed && <p>No node or cluster at the crosshair. Pan the map or use the lists below.</p>}
        </div>
        {status === 'error' && <Button className="map-preview-button map-preview-retry" onClick={retry}>Retry map</Button>}
      </div>
      {(presentation === 'specimen' || basemapStatus === 'error') && <div className="map-preview-map-meta">
        <p role="status">{basemapStatus === 'ready' ? 'Live basemap · OpenFreeMap'
          : basemapStatus === 'loading' ? 'Loading OpenFreeMap basemap…'
            : basemapStatus === 'error' ? 'Basemap unavailable · Showing map evidence only' : null}</p>
        {basemapStatus === 'error' && <Button className="map-preview-button" onClick={() => setBasemapAttempt((value) => value + 1)}>Retry basemap</Button>}
      </div>}
      <p className={presentation === 'specimen' ? 'map-preview-help' : 'ks-visually-hidden'} id={descriptionId}>{instructions}</p>
      {ready && scene.clustered && !regional && (clusters.length > 0 || clusterError) && <div className="map-preview-clusters">
        {clusters.map(({ id, count, coordinates }, index) => <Button key={id} className="map-preview-button" onClick={(event) => { if (loadedMap) void expandCluster(loadedMap, id, coordinates, event.currentTarget) }}>
          Expand {clusters.length > 1 ? `cluster ${index + 1}` : 'cluster'} · {count} nodes
        </Button>)}
        {clusterError && <p role="status">This cluster couldn’t expand. Turn off clustering to see individual nodes.</p>}
      </div>}
      {ready && regional && clusterError && <p className="map-preview-help" role="status">This cluster couldn’t expand. Turn off clustering or use Find node.</p>}
    </div>
  )
}
