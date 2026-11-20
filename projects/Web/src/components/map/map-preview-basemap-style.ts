import type { LayerSpecification, Map as MapLibre, StyleSpecification } from 'maplibre-gl'

export type MapPreviewTheme = 'light' | 'dark'

const provider = 'https://tiles.openfreemap.org'
const styleNames = { light: 'positron', dark: 'dark' } satisfies Record<MapPreviewTheme, string>
const prefix = 'ofm-'
const sources = {
  'ofm-openmaptiles': { type: 'vector', url: `${provider}/planet` },
  'ofm-ne2_shaded': { type: 'raster', tiles: [`${provider}/natural_earth/ne2sr/{z}/{x}/{y}.png`], tileSize: 256, maxzoom: 6 },
} satisfies StyleSpecification['sources']

function providerLayers(data: StyleSpecification): LayerSpecification[] {
  if (data?.version !== 8 || !Array.isArray(data.layers) || !data.layers.length) {
    throw new Error('OpenFreeMap did not return a MapLibre style.')
  }
  return data.layers.flatMap((layer): LayerSpecification[] => {
    if (typeof layer?.id !== 'string') throw new Error('OpenFreeMap layer has no ID.')
    const id = prefix + layer.id
    if (layer.type === 'background') return [{ ...layer, id }]
    if (!('source' in layer) || !Object.hasOwn(sources, prefix + layer.source)) {
      throw new Error(`Unexpected OpenFreeMap source for ${id}.`)
    }
    const source = prefix + layer.source
    if (layer.type === 'symbol') {
      if (!layer.layout?.['text-field']) return []
      // Keep glyphs local to avoid a provider font or sprite failure affecting the evidence overlay.
      const layout = { ...layer.layout, 'text-font': ['sans-serif'] }
      for (const key of Object.keys(layout)) if (key.startsWith('icon-')) delete layout[key as keyof typeof layer.layout]
      return [{ ...layer, id, source, layout }]
    }
    if (layer.type === 'fill') {
      const paint = { ...layer.paint }
      delete paint['fill-pattern']
      return [{ ...layer, id, source, paint }]
    }
    if (layer.type === 'line' || layer.type === 'raster') return [{ ...layer, id, source }]
    throw new Error(`Unsupported OpenFreeMap layer type for ${id}.`)
  })
}

export async function loadBasemap(theme: MapPreviewTheme, signal: AbortSignal): Promise<StyleSpecification> {
  const requestSignal = AbortSignal.any([signal, AbortSignal.timeout(12000)])
  const response = await fetch(`${provider}/styles/${styleNames[theme]}`, { signal: requestSignal, redirect: 'error', credentials: 'omit' })
  if (!response.ok) throw new Error(`OpenFreeMap style request failed (${response.status}).`)
  return { version: 8, sources, layers: providerLayers(await response.json()) }
}

function removeBasemapLayers(map: MapLibre) {
  for (const { id } of map.getStyle().layers) if (id.startsWith(prefix)) map.removeLayer(id)
}

export function clearBasemap(map: MapLibre) {
  removeBasemapLayers(map)
  for (const id of Object.keys(sources)) if (map.getSource(id)) map.removeSource(id)
}

export function installBasemap(map: MapLibre, style: StyleSpecification) {
  removeBasemapLayers(map)
  const before = map.getStyle().layers[0]?.id
  try {
    for (const [id, source] of Object.entries(style.sources)) if (!map.getSource(id)) map.addSource(id, source)
    for (const layer of style.layers) {
      map.addLayer(layer, before)
      if (!map.getLayer(layer.id)) throw new Error(`Could not install OpenFreeMap layer ${layer.id}.`)
    }
  } catch (error) {
    clearBasemap(map)
    throw error
  }
}

export function isBasemapError(event: { sourceId?: string; error: unknown }) {
  if (event.sourceId?.startsWith(prefix)) return true
  const error = event.error
  return typeof error === 'object' && error !== null && 'url' in error && typeof error.url === 'string' && error.url.startsWith(`${provider}/`)
}

export function restrictMapRequest(path: string) {
  const url = new URL(path, window.location.href)
  if (url.origin !== provider && url.origin !== window.location.origin) {
    throw new Error('Unexpected map resource origin.')
  }
  return { url: url.href, credentials: 'same-origin' as const }
}
