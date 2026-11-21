import type { EvidenceAnchor } from '../../components/map/map-path-evidence'
import type { PreviewFlow } from '../../components/map/map-preview-flow'
import { nodeRoles, type NodeRole } from '../../components/nodes/node-role'

const hubs = [
  ['Reykjavík', -21.94, 64.15], ['Akureyri', -18.09, 65.68], ['Egilsstaðir', -14.4, 65.27],
  ['Copenhagen', 12.57, 55.68], ['Aarhus', 10.2, 56.16], ['Aalborg', 9.92, 57.05],
  ['Oslo', 10.75, 59.91], ['Bergen', 5.32, 60.39], ['Trondheim', 10.4, 63.43], ['Tromsø', 18.96, 69.65], ['Honningsvåg', 25.97, 70.98],
  ['Malmö', 13, 55.61], ['Gothenburg', 11.97, 57.71], ['Stockholm', 18.07, 59.33], ['Umeå', 20.26, 63.83], ['Kiruna', 20.23, 67.86],
  ['Helsinki', 24.95, 60.17], ['Turku', 22.27, 60.45], ['Kuopio', 27.68, 62.89], ['Oulu', 25.47, 65.01], ['Inari', 27.03, 68.91],
  ['Tallinn', 24.75, 59.44], ['Tartu', 26.73, 58.38], ['Narva', 28.19, 59.38],
  ['Riga', 24.11, 56.95], ['Liepāja', 21.01, 56.51], ['Daugavpils', 26.53, 55.87],
  ['Vilnius', 25.28, 54.69], ['Kaunas', 23.9, 54.9], ['Šiauliai', 23.31, 55.93],
  ['Gdańsk', 18.65, 54.35], ['Warsaw', 21.01, 52.23], ['Wrocław', 17.04, 51.11], ['Kraków', 19.94, 50.06], ['Zakopane', 19.95, 49.3],
] as const

const roles = Object.keys(nodeRoles) as NodeRole[]

function makeRegionalAnchors(): readonly EvidenceAnchor[] {
  let seed = 41078
  const random = () => (seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0) / 2 ** 32

  return Array.from({ length: 5000 }, (_, index) => {
    const number = String(index + 1).padStart(4, '0')
    // Thirty-six nodes deliberately share one location so overlap exceeds a page of search results.
    const [city, longitude, latitude] = index < 36 ? ['Klaipėda', 21.14, 55.71] : hubs[(index - 36) % hubs.length]
    const spread = index < 36 ? 0 : index % 4 === 0 ? 0.48 : 0.028
    return {
      id: `regional-${number}`, name: `${city} ${number}`, role: roles[index % roles.length], publicKey: '',
      coordinates: [longitude + (random() - 0.5) * spread, latitude + (random() - 0.5) * spread],
    }
  })
}

export const regionalMapAnchors = makeRegionalAnchors()

const regionalStops = regionalMapAnchors.flatMap(({ id, coordinates }) => coordinates ? [{ id, coordinates }] : [])

export function nextRegionalFlow(id: string): PreviewFlow {
  const stops = [regionalStops[Math.floor(Math.random() * regionalStops.length)]]
  while (stops.length < 3) {
    let index = Math.floor(Math.random() * regionalStops.length)
    // Colocated fixture nodes must not turn a packet hop into a zero-length trail.
    while (stops.some(({ coordinates: [longitude, latitude] }) =>
      longitude === regionalStops[index].coordinates[0] && latitude === regionalStops[index].coordinates[1])) {
      index = (index + 1) % regionalStops.length
    }
    stops.push(regionalStops[index])
  }
  return { id, stops }
}
