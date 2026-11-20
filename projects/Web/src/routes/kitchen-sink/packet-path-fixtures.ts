import type { PacketPathCandidate, PacketPathHop, PacketPathIndicator } from '../../components/packets/packet-path-chain'

export const indicators = [
  { value: 'squares', label: 'Squares' },
  { value: 'dots', label: 'Edge dots' },
  { value: 'corner', label: 'Corner pips' },
  { value: 'underline', label: 'Segmented underline' },
  { value: 'ambiguity', label: 'Ambiguity mark' },
  { value: 'count', label: 'Extra count · vertical' },
  { value: 'count-horizontal', label: 'Extra count · horizontal (default)' },
] as const satisfies readonly { value: PacketPathIndicator; label: string }[]

function candidate(prefix: string, suffix: number, confidence: PacketPathCandidate['confidence'], name = `Candidate ${suffix}`): PacketPathCandidate {
  return {
    name,
    publicKey: prefix + suffix.toString(16).toUpperCase().padStart(64 - prefix.length, '0'),
    confidence,
    reason: confidence === 'Strong' ? 'Prefix match · Neighbor support' : 'Prefix match only',
  }
}

function exampleHop(index: number, prefix: string, strong: number, weak: number): PacketPathHop {
  return {
    id: `${prefix}-${index + 1}`,
    prefix,
    candidates: [
      ...Array.from({ length: strong }, (_, i) => candidate(prefix, i + 1, 'Strong')),
      ...Array.from({ length: weak }, (_, i) => candidate(prefix, strong + i + 1, 'Weak')),
    ],
  }
}

const comparisonCounts = [[1, 0], [1, 1], [1, 2], [1, 3], [1, 6], [0, 3], [0, 0], [3, 1]] as const
export const comparisonHops = comparisonCounts.map(([strong, weak], index) => (
  exampleHop(index, (0xaa + index).toString(16).toUpperCase(), strong, weak)
))

function densityFixture(count: number, bytes: number) {
  return Array.from({ length: count }, (_, index) => {
    const pattern = index % 8
    const prefix = (0xaa + index % 12).toString(16).toUpperCase().repeat(bytes)
    if (pattern === 2) return exampleHop(index, prefix, 0, 0)
    if (pattern === 3) return exampleHop(index, prefix, 0, 2)
    if (pattern === 4) return exampleHop(index, prefix, 1 + index % 4, 0)
    return exampleHop(index, prefix, 1, pattern === 1 ? 5 : index % 4)
  })
}

export const densityPaths = [
  { width: '1-byte prefixes', hops: densityFixture(64, 1) },
  { width: '2-byte prefixes', hops: densityFixture(32, 2) },
  { width: '3-byte prefixes', hops: densityFixture(16, 3) },
]

export const shortPath: PacketPathHop[] = [
  { id: '2C', prefix: '2C', candidates: [candidate('2C', 1, 'Strong', 'Hilltop repeater')] },
  { id: 'B4', prefix: 'B4', candidates: [candidate('B4', 1, 'Strong', 'North relay'), candidate('B4', 2, 'Weak', 'Riverside relay')] },
  { id: 'E7', prefix: 'E7', candidates: [] },
]
