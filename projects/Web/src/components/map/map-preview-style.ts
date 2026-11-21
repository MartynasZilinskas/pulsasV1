import type { ExpressionSpecification, FilterSpecification, Map as MapLibre, StyleSpecification, SymbolLayerSpecification } from 'maplibre-gl'
import { nodeRoles, type NodeRole } from '../nodes/node-role'
import type { MapPreviewTheme } from './map-preview-basemap-style'

export const markerLayer = 'preview-marker'
export const gapLayer = 'preview-gap'
export const gapHopLayer = 'preview-gap-hop'
export const pathLayer = 'preview-path'
export const weakPathHaloLayer = 'preview-path-weak-halo'
export const weakPathLayer = 'preview-path-weak'
export const clusterLayer = 'preview-cluster'
export const roleLayer = 'preview-role'
export const labelLayer = 'preview-name'
export const evidenceLabelLayer = 'preview-evidence-name'
export const raisedLayer = 'preview-raised'
export const raisedLabelLayer = 'preview-raised-label'
export const raisedHopLayer = 'preview-raised-hop'
export const raisedAmbiguousLayer = 'preview-raised-ambiguous'
export const hopLayer = 'preview-hop-number'
export const ambiguousLayer = 'preview-ambiguous'
export const chosenSource = 'preview-chosen'
export const flowSource = 'preview-flow'
export const flowLayers = ['preview-flow-trail', 'preview-flow-head', 'preview-flow-arrival', 'preview-flow-schematic']
export const previewId = 'previewId'
export const roleZoom = 9
export const labelZoom = 12
export const empty: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features: [] }

const palettes = {
  light: { strong: '#165e47', weak: '#8b5719', flow: '#a24319', ink: '#24342c', casing: '#ffffff', gap: '#fffaf1' },
  dark: { strong: '#92e4bc', weak: '#ffca82', flow: '#ff9f78', ink: '#edf4ee', casing: '#17211f', gap: '#2b2318' },
}
const white = '#fff'
const rolePixelRatio = 3
const roleGlyphSize = 18
const roleBadgeSize = 32
const nameSize = 12
const roleBadgeLayout: SymbolLayerSpecification['layout'] = {
  'symbol-z-order': 'source',
  'icon-allow-overlap': true, 'icon-ignore-placement': true,
}
const isWeak = ['==', ['get', 'confidence'], 'weak'] satisfies FilterSpecification
const isGap = ['==', ['get', 'presentation'], 'gap'] satisfies FilterSpecification
const isNode = ['all', ['!', isGap], ['!', ['has', 'point_count']]] satisfies FilterSpecification
const isEvidence = ['has', 'confidence'] satisfies FilterSpecification
const isCandidate = ['all', isNode, isEvidence] satisfies FilterSpecification
const isOrdinaryNode = ['all', isNode, ['!', isEvidence]] satisfies FilterSpecification
const isAmbiguous = ['==', ['get', 'ambiguous'], true] satisfies FilterSpecification

function roleBadgeImage(theme: MapPreviewTheme, includeConfidence: boolean): ExpressionSpecification {
  const role = ['coalesce', ['get', 'roleImage'], roleImageId('NONE')] satisfies ExpressionSpecification
  const suffix = theme === 'dark' ? '-dark' : ''
  return includeConfidence
    ? ['concat', role, ['case', isEvidence, ['case', isWeak, '-weak', '-strong'], ''], suffix]
    : ['concat', role, suffix]
}

const nameLayout: SymbolLayerSpecification['layout'] = {
  'text-field': ['get', 'name'], 'text-font': ['sans-serif'], 'text-size': nameSize,
  'text-anchor': 'top', 'text-max-width': 14, 'text-padding': 4,
  'text-offset': [0, 15 / 12],
}
const evidenceNameLayout: SymbolLayerSpecification['layout'] = {
  'text-field': ['get', 'name'], 'text-font': ['sans-serif'], 'text-size': nameSize,
  'text-anchor': 'top', 'text-offset': ['step', ['zoom'], ['literal', [0, 1.65]], roleZoom, ['literal', [0, 20 / 12]]],
  'text-justify': 'center', 'text-max-width': 14, 'text-padding': 3,
}
const hopLayout: SymbolLayerSpecification['layout'] = {
  'text-field': ['get', 'hopLabel'], 'text-font': ['sans-serif'], 'text-size': 12,
  'text-anchor': 'center', 'text-offset': ['step', ['zoom'],
    ['literal', [0, -11.25 / 12]], roleZoom, ['literal', [0, -13.75 / 12]]],
  'text-allow-overlap': true,
}
const ambiguityLayout: SymbolLayerSpecification['layout'] = {
  'text-field': '?', 'text-font': ['sans-serif'], 'text-size': 14,
  'text-offset': ['step', ['zoom'],
    ['literal', [11.25 / 14, 0]], roleZoom, ['literal', [13.75 / 14, 0]]],
  'text-allow-overlap': true,
}
const lineWidth = 4
const casingWidth = 8
const possibleDashes = [8, 6]

export function offlineStyle(theme: MapPreviewTheme = 'light'): StyleSpecification {
  const { strong: strongColor, weak: weakColor, flow: flowColor, ink, casing } = palettes[theme]
  const roleInk = ['coalesce', ['get', theme === 'dark' ? 'roleDarkInk' : 'roleInk'], strongColor] satisfies ExpressionSpecification
  const roleFill = ['coalesce', ['get', theme === 'dark' ? 'roleDarkFill' : 'roleFill'], strongColor] satisfies ExpressionSpecification
  const roleEdge = ['coalesce', ['get', theme === 'dark' ? 'roleDarkInk' : 'roleInk'], white] satisfies ExpressionSpecification
  const textPaint = { 'text-color': ink, 'text-halo-color': casing, 'text-halo-width': 1.7 }
  const nameOpacity = ['case', ['boolean', ['feature-state', 'nameOutsideViewport'], false], 0, 1] satisfies ExpressionSpecification
  const confidenceInk = ['case', isWeak, weakColor, strongColor] as const
  return {
    version: 8,
    transition: { duration: 0, delay: 0 },
    sources: {
      markers: { type: 'geojson', promoteId: previewId, data: empty, cluster: false, clusterRadius: 45, clusterMaxZoom: 12 },
      paths: { type: 'geojson', data: empty },
      [chosenSource]: { type: 'geojson', promoteId: previewId, data: empty },
      [flowSource]: { type: 'geojson', data: empty },
    },
    layers: [
      // Dash units are multiples of stroke width; the casing and ink share the same pixel pattern.
      { id: weakPathHaloLayer, source: 'paths', type: 'line', filter: isWeak, layout: { 'line-cap': 'butt', 'line-join': 'round' }, paint: {
        'line-color': casing, 'line-width': casingWidth, 'line-dasharray': possibleDashes.map((length) => length / casingWidth),
      } },
      { id: weakPathLayer, source: 'paths', type: 'line', filter: isWeak, layout: { 'line-cap': 'butt', 'line-join': 'round' }, paint: {
        'line-color': weakColor, 'line-width': lineWidth, 'line-dasharray': possibleDashes.map((length) => length / lineWidth),
      } },
      { id: 'path-halo', source: 'paths', type: 'line', filter: ['!', isWeak], layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': casing, 'line-width': casingWidth } },
      { id: pathLayer, source: 'paths', type: 'line', filter: ['!', isWeak], layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': strongColor, 'line-width': lineWidth } },
      { id: 'preview-flow-trail', source: flowSource, type: 'line', filter: ['==', ['get', 'kind'], 'trail'], paint: {
        'line-color': flowColor, 'line-opacity': ['get', 'opacity'], 'line-width': 3, 'line-dasharray': [2, 1.5],
      } },
      { id: clusterLayer, source: 'markers', type: 'circle', filter: ['has', 'point_count'], paint: {
        'circle-radius': 17, 'circle-color': strongColor, 'circle-stroke-color': casing, 'circle-stroke-width': 2,
      } },
      { id: 'preview-cluster-count', source: 'markers', type: 'symbol', filter: ['has', 'point_count'], layout: {
        'text-field': ['get', 'point_count_abbreviated'], 'text-font': ['sans-serif'], 'text-size': 13, 'text-allow-overlap': true, 'text-ignore-placement': true,
      }, paint: { 'text-color': theme === 'dark' ? casing : white } },
      { id: 'preview-confidence-ring', source: 'markers', type: 'circle', maxzoom: roleZoom, filter: isCandidate, paint: {
        'circle-radius': 10, 'circle-color': casing,
        'circle-stroke-color': [...confidenceInk], 'circle-stroke-width': 2.5,
      } },
      // Keep generous hit areas in the role band, where a separate layer draws the smaller face.
      { id: markerLayer, source: 'markers', type: 'circle', filter: isNode, paint: {
        'circle-radius': ['step', ['zoom'], ['case', isEvidence, 6, 5], roleZoom, ['case', isEvidence, 16, 18]],
        'circle-color': ['step', ['zoom'], roleInk, roleZoom, roleFill],
        'circle-stroke-color': ['step', ['zoom'], white, roleZoom, roleEdge],
        'circle-stroke-width': ['case', isEvidence, 0, 2],
        'circle-opacity': ['step', ['zoom'], 1, roleZoom, 0], 'circle-stroke-opacity': ['step', ['zoom'], 1, roleZoom, 0],
      } },
      { id: roleLayer, source: 'markers', type: 'symbol', minzoom: roleZoom, filter: isNode,
        layout: { ...roleBadgeLayout, 'icon-image': roleBadgeImage(theme, true) } },
      { id: labelLayer, source: 'markers', type: 'symbol', minzoom: labelZoom, filter: isOrdinaryNode, layout: nameLayout, paint: { ...textPaint, 'text-opacity': nameOpacity } },
      { id: evidenceLabelLayer, source: 'markers', type: 'symbol', filter: isCandidate, layout: evidenceNameLayout, paint: { ...textPaint, 'text-opacity': nameOpacity } },
      { id: hopLayer, source: 'markers', type: 'symbol', filter: isCandidate, layout: hopLayout, paint: { ...textPaint, 'text-color': [...confidenceInk] } },
      { id: ambiguousLayer, source: 'markers', type: 'symbol', filter: ['all', isCandidate, isAmbiguous], layout: ambiguityLayout, paint: { ...textPaint, 'text-color': weakColor } },
      { id: gapLayer, source: 'markers', type: 'symbol', filter: isGap, layout: {
        'icon-image': `schematic-box-${theme}`, 'icon-size': 0.84, 'icon-allow-overlap': true,
        'text-field': ['get', 'prefix'], 'text-font': ['monospace'], 'text-size': 11,
        'text-anchor': 'center', 'text-allow-overlap': true,
      }, paint: { 'text-color': weakColor } },
      { id: gapHopLayer, source: 'markers', type: 'symbol', filter: isGap, layout: {
        ...hopLayout, 'text-anchor': 'bottom', 'text-offset': [0, -1.12],
      }, paint: { ...textPaint, 'text-color': weakColor } },
      { id: 'preview-flow-arrival', source: flowSource, type: 'circle', filter: ['==', ['get', 'kind'], 'arrival'], paint: {
        'circle-radius': ['step', ['zoom'], ['get', 'radius'], roleZoom, ['+', ['number', ['get', 'radius']], 5]],
        'circle-opacity': 0, 'circle-stroke-color': flowColor,
        'circle-stroke-width': 3, 'circle-stroke-opacity': ['get', 'opacity'],
      } },
      { id: 'preview-flow-schematic', source: flowSource, type: 'symbol', filter: ['==', ['get', 'kind'], 'schematic'], layout: {
        'icon-image': `schematic-box-${theme}`, 'icon-size': 0.45, 'icon-allow-overlap': true, 'icon-ignore-placement': true,
      }, paint: { 'icon-opacity': ['get', 'opacity'] } },
      { id: 'preview-flow-head', source: flowSource, type: 'circle', filter: ['==', ['get', 'kind'], 'head'], paint: {
        'circle-radius': 5, 'circle-color': flowColor, 'circle-stroke-color': casing, 'circle-stroke-width': 2,
      } },
      { id: 'preview-raised-confidence-ring', source: chosenSource, type: 'circle', filter: isEvidence, paint: {
        'circle-radius': 15, 'circle-color': casing, 'circle-stroke-color': casing, 'circle-stroke-width': 4,
      } },
      { id: 'preview-raised-confidence-ink', source: chosenSource, type: 'circle', filter: isEvidence, paint: {
        'circle-radius': 14, 'circle-color': casing, 'circle-stroke-color': [...confidenceInk], 'circle-stroke-width': 3,
      } },
      { id: raisedLayer, source: chosenSource, type: 'circle', paint: {
        'circle-radius': ['case', isEvidence, 18, 22],
        'circle-color': ['step', ['zoom'], roleFill, labelZoom, casing],
        'circle-stroke-color': roleEdge, 'circle-stroke-width': ['case', isEvidence, 0, 4],
        // Retain the selected hit area without covering the compact confidence ring.
        'circle-opacity': 0,
        'circle-stroke-opacity': 0,
      } },
      { id: 'preview-raised-ordinary-ring', source: chosenSource, type: 'circle', filter: ['!', isEvidence], paint: {
        'circle-radius': 12.5, 'circle-color': casing, 'circle-stroke-color': roleEdge, 'circle-stroke-width': 2,
      } },
      { id: 'preview-raised-role', source: chosenSource, type: 'symbol',
        layout: { ...roleBadgeLayout, 'icon-image': roleBadgeImage(theme, false) } },
      { id: raisedLabelLayer, source: chosenSource, type: 'symbol', layout: {
        // MapLibre infers weight from the first family; extending the generic sans-serif name makes Canvas reject its font string.
        ...nameLayout, 'text-font': ['Arial Semibold', 'sans-serif'],
        'icon-image': `selected-nameplate-${theme}`, 'icon-text-fit': 'both', 'icon-text-fit-padding': [0, 6, 2, 6],
        'icon-allow-overlap': true, 'icon-ignore-placement': true,
        'text-offset': ['step', ['zoom'], ['literal', [0, 26 / 12]], roleZoom,
          ['case', isEvidence, ['literal', [0, 22 / 12]], ['literal', [0, 20 / 12]]]],
      }, paint: { 'text-color': theme === 'light' ? white : palettes.light.ink, 'text-opacity': nameOpacity, 'icon-opacity': nameOpacity } },
      { id: raisedHopLayer, source: chosenSource, type: 'symbol', filter: isEvidence, layout: {
        ...hopLayout, 'text-offset': [0, -15.5 / 12],
      }, paint: { ...textPaint, 'text-color': [...confidenceInk] } },
      { id: raisedAmbiguousLayer, source: chosenSource, type: 'symbol', filter: ['all', isEvidence, isAmbiguous], layout: {
        ...ambiguityLayout, 'text-offset': [15.5 / 14, 0],
      }, paint: { ...textPaint, 'text-color': weakColor } },
    ],
  }
}

export function applyMapPreviewTheme(map: MapLibre, theme: MapPreviewTheme) {
  const layers = offlineStyle(theme).layers
  const colorProperties = ['background-color', 'fill-color', 'line-color', 'circle-color', 'circle-stroke-color', 'text-color', 'text-halo-color'] as const
  for (const layer of layers) {
    if (!('paint' in layer)) continue
    const paint = layer.paint as Partial<Record<(typeof colorProperties)[number], string | ExpressionSpecification>> | undefined
    for (const property of colorProperties) {
      if (paint && property in paint) map.setPaintProperty(layer.id, property, paint[property])
    }
  }
  map.setLayoutProperty(gapLayer, 'icon-image', `schematic-box-${theme}`)
  map.setLayoutProperty('preview-flow-schematic', 'icon-image', `schematic-box-${theme}`)
  map.setLayoutProperty(raisedLabelLayer, 'icon-image', `selected-nameplate-${theme}`)
  map.setLayoutProperty(roleLayer, 'icon-image', roleBadgeImage(theme, true))
  map.setLayoutProperty('preview-raised-role', 'icon-image', roleBadgeImage(theme, false))
}

export function excludeRaisedLabels(map: MapLibre, selectedId: string | null, overlapIds: readonly string[] = [], expandedIds: readonly string[] = []) {
  const notSelected = ['!=', ['get', previewId], selectedId] satisfies FilterSpecification
  const notStacked = ['!', ['in', ['get', previewId], ['literal', overlapIds]]] satisfies FilterSpecification
  for (const layer of offlineStyle().layers) {
    if (!('source' in layer) || ![chosenSource, 'markers'].includes(String(layer.source)) || [markerLayer, raisedLayer, clusterLayer, 'preview-cluster-count', gapLayer, gapHopLayer].includes(layer.id)) continue
    const exclusions: FilterSpecification[] = layer.source === chosenSource
      ? [['!', ['in', ['get', previewId], ['literal', expandedIds]]]]
      : [notStacked]
    if ([labelLayer, evidenceLabelLayer, hopLayer, ambiguousLayer].includes(layer.id)) exclusions.push(notSelected)
    map.setFilter(layer.id, ['all', ...(layer.filter ? [layer.filter] : []), ...exclusions] as FilterSpecification)
  }
}

export function roleImageId(role: NodeRole) {
  return `node-role-${role}`
}

export const previewRoles = Object.keys(nodeRoles) as NodeRole[]
export type PreviewRoleColors = ReadonlyMap<NodeRole, { roleInk: string; roleFill: string; roleDarkInk: string; roleDarkFill: string }>

export async function loadPreviewImages(artwork: HTMLDivElement) {
  const images = await Promise.all(previewRoles.map(async (role, index) => {
    const element = artwork.children[index]
    const style = getComputedStyle(element)
    const roleInk = style.getPropertyValue('--ks-tone-light-ink').trim()
    const roleFill = style.getPropertyValue('--ks-tone-light-fill').trim()
    const roleDarkInk = style.getPropertyValue('--ks-tone-dark-ink').trim()
    const roleDarkFill = style.getPropertyValue('--ks-tone-dark-fill').trim()
    const svg = (element.firstElementChild as SVGSVGElement).cloneNode(true) as SVGSVGElement
    svg.setAttribute('width', String(roleGlyphSize * rolePixelRatio))
    svg.setAttribute('height', String(roleGlyphSize * rolePixelRatio))
    const variants = await Promise.all([roleInk, roleDarkInk].map(async (color) => {
      const variant = svg.cloneNode(true) as SVGSVGElement
      variant.setAttribute('color', color)
      const image = new Image()
      image.src = `data:image/svg+xml,${encodeURIComponent(new XMLSerializer().serializeToString(variant))}`
      await image.decode()
      return image
    }))
    return { role, variants, colors: { roleInk, roleFill, roleDarkInk, roleDarkFill } }
  }))
  function install(map: MapLibre) {
    const canvas = document.createElement('canvas')
    const context = canvas.getContext('2d')!
    canvas.width = canvas.height = roleBadgeSize * rolePixelRatio
    context.scale(rolePixelRatio, rolePixelRatio)
    const center = roleBadgeSize / 2
    function disk(radius: number, color: string) {
      context.fillStyle = color
      context.beginPath()
      context.arc(center, center, radius, 0, 2 * Math.PI)
      context.fill()
    }
    for (const { role, variants, colors } of images) {
      const glyphSize = roleGlyphSize * (role === 'CLIENT' || role === 'ROOM_SERVER' ? 0.8 : 1)
      const glyphStart = center - glyphSize / 2
      for (const theme of ['light', 'dark'] as const) {
        const dark = theme === 'dark'
        const palette = palettes[theme]
        for (const confidence of ['', 'strong', 'weak'] as const) {
          context.clearRect(0, 0, roleBadgeSize, roleBadgeSize)
          // Composite the whole badge so a foreground face and ring mask whatever is below them.
          if (confidence) {
            disk(15, palette[confidence])
            disk(12.5, palette.casing)
          }
          disk(10, dark ? colors.roleDarkInk : colors.roleInk)
          disk(9, dark ? colors.roleDarkFill : colors.roleFill)
          context.drawImage(variants[dark ? 1 : 0], glyphStart, glyphStart, glyphSize, glyphSize)
          const id = `${roleImageId(role)}${confidence ? `-${confidence}` : ''}${dark ? '-dark' : ''}`
          map.addImage(id, context.getImageData(0, 0, canvas.width, canvas.height), { pixelRatio: rolePixelRatio })
        }
      }
    }
    canvas.width = 126
    canvas.height = 52
    context.lineWidth = 3
    context.setLineDash([6, 4])
    context.beginPath()
    context.roundRect(2, 2, 122, 48, 8)
    for (const [theme, colors] of Object.entries(palettes)) {
      context.clearRect(0, 0, canvas.width, canvas.height)
      context.fillStyle = colors.gap
      context.strokeStyle = colors.weak
      context.fill()
      context.stroke()
      map.addImage(`schematic-box-${theme}`, context.getImageData(0, 0, 126, 52), { pixelRatio: 2 })
    }
    canvas.width = canvas.height = 32
    for (const theme of ['light', 'dark'] as const) {
      context.clearRect(0, 0, 32, 32)
      context.fillStyle = theme === 'light' ? palettes.light.ink : palettes.dark.ink
      context.beginPath()
      context.roundRect(0, 0, 32, 32, 8)
      context.fill()
      map.addImage(`selected-nameplate-${theme}`, context.getImageData(0, 0, 32, 32), {
        pixelRatio: 2, content: [0, 0, 32, 32], stretchX: [[8, 24]], stretchY: [[8, 24]],
      })
    }
  }
  return { install, roleColors: new Map(images.map(({ role, colors }) => [role, colors])) }
}
