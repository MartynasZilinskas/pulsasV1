import type { FilterSpecification, Map as MapLibre, StyleSpecification, SymbolLayerSpecification } from 'maplibre-gl'
import { nodeRoles, type NodeRole } from '../nodes/node-role'

export const markerLayer = 'preview-marker'
export const gapLayer = 'preview-gap'
export const pathLayer = 'preview-path'
export const weakPathLayer = 'preview-path-weak'
export const clusterLayer = 'preview-cluster'
export const roleLayer = 'preview-role'
export const labelLayer = 'preview-name'
export const raisedLayer = 'preview-raised'
export const hopLayer = 'preview-hop-number'
export const ambiguousLayer = 'preview-ambiguous'
export const chosenSource = 'preview-chosen'
export const flowSource = 'preview-flow'
export const flowLayers = ['preview-flow-trail', 'preview-flow-head', 'preview-flow-arrival', 'preview-flow-schematic']
export const previewId = 'previewId'
export const roleZoom = 9
export const labelZoom = 12
export const empty: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features: [] }

const strongColor = '#165e47'
const weakColor = '#8b5719'
const flowColor = '#a24319'
const ink = '#24342c'
const white = '#fff'
const roleImageSize = 36
const isWeak = ['==', ['get', 'confidence'], 'weak'] satisfies FilterSpecification
const isGap = ['==', ['get', 'presentation'], 'gap'] satisfies FilterSpecification
const isNode = ['all', ['!', isGap], ['!', ['has', 'point_count']]] satisfies FilterSpecification
const roleColors = { ROOM_SERVER: '#225e45', REPEATER: '#594480', CLIENT: '#30587e', SENSOR: '#74531c', NONE: '#54635b' } satisfies Record<NodeRole, string>
const textPaint = { 'text-color': ink, 'text-halo-color': white, 'text-halo-width': 1.7 }
const nameLayout: SymbolLayerSpecification['layout'] = {
  'text-field': ['get', 'name'], 'text-font': ['sans-serif'], 'text-size': 12,
  'text-anchor': 'top', 'text-offset': [0, 1.25], 'text-max-width': 14, 'text-padding': 4,
}

export function offlineStyle(): StyleSpecification {
  return {
    version: 8,
    sources: {
      markers: { type: 'geojson', promoteId: previewId, data: empty, cluster: false, clusterRadius: 45, clusterMaxZoom: 12 },
      paths: { type: 'geojson', data: empty },
      [chosenSource]: { type: 'geojson', data: empty },
      [flowSource]: { type: 'geojson', data: empty },
    },
    layers: [
      { id: 'path-halo', source: 'paths', type: 'line', filter: ['!', isWeak], layout: { 'line-cap': 'round' }, paint: { 'line-color': white, 'line-width': 8 } },
      { id: pathLayer, source: 'paths', type: 'line', filter: ['!', isWeak], layout: { 'line-cap': 'round' }, paint: { 'line-color': strongColor, 'line-width': 4 } },
      { id: weakPathLayer, source: 'paths', type: 'line', filter: isWeak, paint: { 'line-color': weakColor, 'line-width': 4, 'line-dasharray': [2, 1.5] } },
      { id: 'preview-flow-trail', source: flowSource, type: 'line', filter: ['==', ['get', 'kind'], 'trail'], paint: {
        'line-color': flowColor, 'line-opacity': ['get', 'opacity'], 'line-width': 3, 'line-dasharray': [2, 1.5],
      } },
      { id: clusterLayer, source: 'markers', type: 'circle', filter: ['has', 'point_count'], paint: {
        'circle-radius': 17, 'circle-color': strongColor, 'circle-stroke-color': white, 'circle-stroke-width': 2,
      } },
      { id: 'preview-cluster-count', source: 'markers', type: 'symbol', filter: ['has', 'point_count'], layout: {
        'text-field': ['get', 'point_count_abbreviated'], 'text-font': ['sans-serif'], 'text-size': 13, 'text-allow-overlap': true, 'text-ignore-placement': true,
      }, paint: { 'text-color': white } },
      { id: markerLayer, source: 'markers', type: 'circle', filter: isNode, paint: {
        'circle-radius': ['step', ['zoom'], ['case', ['has', 'confidence'], 13, 5], 9, 13],
        'circle-color': ['coalesce', ['get', 'color'], strongColor],
        'circle-stroke-color': ['case', ['==', ['get', 'confidence'], 'weak'], '#d79532', white],
        'circle-stroke-width': 2,
      } },
      { id: roleLayer, source: 'markers', type: 'symbol', minzoom: roleZoom, filter: ['all', isNode, ['has', 'roleImage']], layout: {
        'icon-image': ['get', 'roleImage'], 'icon-allow-overlap': true, 'icon-ignore-placement': true,
      } },
      { id: labelLayer, source: 'markers', type: 'symbol', minzoom: labelZoom, filter: isNode, layout: nameLayout, paint: textPaint },
      { id: hopLayer, source: 'markers', type: 'symbol', filter: ['all', isNode, ['has', 'confidence']], layout: {
        'text-field': ['get', 'hopLabel'], 'text-font': ['sans-serif'], 'text-size': 12,
        'text-anchor': 'bottom', 'text-offset': [0, -1.3], 'text-allow-overlap': true, 'text-ignore-placement': true,
      }, paint: textPaint },
      { id: ambiguousLayer, source: 'markers', type: 'symbol', filter: ['all', isNode, ['==', ['get', 'ambiguous'], true]], layout: {
        'text-field': '?', 'text-font': ['sans-serif'], 'text-size': 14,
        'text-offset': [1.25, 0], 'text-allow-overlap': true, 'text-ignore-placement': true,
      }, paint: { ...textPaint, 'text-color': weakColor } },
      { id: gapLayer, source: 'markers', type: 'symbol', filter: isGap, layout: {
        'icon-image': 'schematic-box', 'icon-allow-overlap': true, 'icon-ignore-placement': true,
        'text-field': ['get', 'gapLabel'], 'text-font': ['sans-serif'], 'text-size': 11,
        'text-allow-overlap': true, 'text-ignore-placement': true,
      }, paint: { 'text-color': weakColor } },
      { id: raisedLayer, source: chosenSource, type: 'circle', paint: {
        'circle-radius': 16, 'circle-color': ['coalesce', ['get', 'color'], strongColor],
        'circle-stroke-color': white, 'circle-stroke-width': 4,
      } },
      { id: 'preview-raised-role', source: chosenSource, type: 'symbol', layout: {
        'icon-image': ['get', 'roleImage'], 'icon-allow-overlap': true, 'icon-ignore-placement': true,
      } },
      { id: 'preview-raised-label', source: chosenSource, type: 'symbol', layout: {
        ...nameLayout, 'text-field': ['get', 'raisedLabel'], 'text-allow-overlap': true, 'text-ignore-placement': true,
      }, paint: textPaint },
      { id: 'preview-flow-arrival', source: flowSource, type: 'circle', filter: ['==', ['get', 'kind'], 'arrival'], paint: {
        'circle-radius': ['get', 'radius'], 'circle-opacity': 0, 'circle-stroke-color': flowColor,
        'circle-stroke-width': 3, 'circle-stroke-opacity': ['get', 'opacity'],
      } },
      { id: 'preview-flow-schematic', source: flowSource, type: 'symbol', filter: ['==', ['get', 'kind'], 'schematic'], layout: {
        'icon-image': 'schematic-box', 'icon-size': 0.45, 'icon-allow-overlap': true, 'icon-ignore-placement': true,
      }, paint: { 'icon-opacity': ['get', 'opacity'] } },
      { id: 'preview-flow-head', source: flowSource, type: 'circle', filter: ['==', ['get', 'kind'], 'head'], paint: {
        'circle-radius': 5, 'circle-color': flowColor, 'circle-stroke-color': white, 'circle-stroke-width': 2,
      } },
    ],
  }
}

export function roleArtwork(role: NodeRole) {
  return { roleImage: `node-role-${role}`, color: roleColors[role] }
}

export const previewRoles = Object.keys(nodeRoles) as NodeRole[]

export async function loadPreviewImages(artwork: HTMLDivElement) {
  const images = await Promise.all(previewRoles.map(async (role, index) => {
    const svg = (artwork.children[index].firstElementChild as SVGSVGElement).cloneNode(true) as SVGSVGElement
    svg.setAttribute('width', String(roleImageSize))
    svg.setAttribute('height', String(roleImageSize))
    svg.setAttribute('color', 'white')
    const image = new Image()
    image.src = `data:image/svg+xml,${encodeURIComponent(new XMLSerializer().serializeToString(svg))}`
    await image.decode()
    return { id: roleArtwork(role).roleImage, image }
  }))
  return (map: MapLibre) => {
    for (const { id, image } of images) map.addImage(id, image, { pixelRatio: 2 })
    const canvas = document.createElement('canvas')
    canvas.width = 126
    canvas.height = 52
    const context = canvas.getContext('2d')!
    context.fillStyle = '#fffaf1'
    context.strokeStyle = weakColor
    context.lineWidth = 3
    context.setLineDash([6, 4])
    context.beginPath()
    context.roundRect(2, 2, 122, 48, 8)
    context.fill()
    context.stroke()
    map.addImage('schematic-box', context.getImageData(0, 0, 126, 52), { pixelRatio: 2 })
  }
}
