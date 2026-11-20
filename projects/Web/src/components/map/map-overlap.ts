export type ScreenNode = { id: string; x: number; y: number; radius: number }
export type ScreenOverlap = { id: string; nodes: ScreenNode[]; x: number; y: number }

// A screen-space index, not a geographic cluster. Repeated evidence appearances count once.
export function screenOverlaps(points: readonly ScreenNode[]): ScreenOverlap[] {
  const nodes = [...new Map(points.map(node => [node.id, node])).values()].sort((a, b) => a.id.localeCompare(b.id))
  const parents = nodes.map((_, i) => i)
  const root = (i: number): number => parents[i] === i ? i : (parents[i] = root(parents[i]))
  const cells = new Map<string, number[]>()
  const cellSize = nodes.reduce((size, node) => Math.max(size, node.radius * 2), 20)
  const positions = new Map<string, number>()
  nodes.forEach((node, i) => {
    const position = `${node.x}:${node.y}:${node.radius}`
    const duplicate = positions.get(position)
    if (duplicate !== undefined) { parents[i] = root(duplicate); return }
    positions.set(position, i)
    const x = Math.floor(node.x / cellSize), y = Math.floor(node.y / cellSize)
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) {
      for (const j of cells.get(`${x + dx}:${y + dy}`) ?? []) {
        const other = nodes[j]
        if (Math.hypot(node.x - other.x, node.y - other.y) < node.radius + other.radius) parents[root(i)] = root(j)
      }
    }
    const key = `${x}:${y}`
    const cell = cells.get(key) ?? []
    cell.push(i)
    cells.set(key, cell)
  })
  const groups = new Map<number, ScreenNode[]>()
  nodes.forEach((node, i) => { const key = root(i); const group = groups.get(key) ?? []; group.push(node); groups.set(key, group) })
  return [...groups.values()].filter(group => group.length > 1).map(nodes => ({
    id: JSON.stringify(nodes.map(node => node.id)), nodes,
    x: nodes.reduce((sum, node) => sum + node.x, 0) / nodes.length,
    y: nodes.reduce((sum, node) => sum + node.y, 0) / nodes.length,
  }))
}

export function overlapFan(group: ScreenOverlap, width: number, height: number) {
  if (group.nodes.length > 8 || width < 200 || height < 200) return null
  const radius = group.nodes.length === 2 ? 52 : group.nodes.length > 6 ? 88 : 72
  if (width < radius * 2 + 50 || height < radius * 2 + 83) return null
  const cx = Math.max(radius + 25, Math.min(width - radius - 25, group.x))
  const cy = Math.max(radius + 28, Math.min(height - radius - 55, group.y))
  return group.nodes.map((node, i) => {
    const angle = -Math.PI / 2 + i * 2 * Math.PI / group.nodes.length
    return { ...node, anchorX: node.x, anchorY: node.y, x: cx + Math.cos(angle) * radius, y: cy + Math.sin(angle) * radius }
  })
}
