import assert from 'node:assert/strict'
import test from 'node:test'
import { createServer } from 'vite'

test('screen overlaps deduplicate stable nodes, include near neighbours and leave distant nodes alone', async () => {
  const vite = await createServer({ configFile: false, server: { middlewareMode: true, hmr: false }, optimizeDeps: { noDiscovery: true } })
  try {
    const { screenOverlaps, overlapFan } = await vite.ssrLoadModule('/src/components/map/map-overlap.ts')
    const node = (id, x, y = 0) => ({ id, x, y, radius: 10 })
    const points = [node('b', 0), node('a', 0), node('a', 0), node('c', 19), node('far', 80)]
    const [group] = screenOverlaps(points)
    assert.deepEqual(group.nodes.map(n => n.id), ['a', 'b', 'c'])
    assert.equal(screenOverlaps(points.toReversed())[0].id, group.id)
    for (const x of [0, 160, 320]) for (const y of [0, 200, 400]) {
      const fan = overlapFan({ ...group, x, y }, 320, 400)
      for (const n of fan) { assert.ok(n.x >= 22 && n.x <= 298); assert.ok(n.y >= 22 && n.y <= 378) }
      assert.equal(new Set(fan.map(n => `${n.x}:${n.y}`)).size, 3)
    }
    const eight = { ...group, nodes: Array.from({ length: 8 }, (_, i) => node(String(i), 0)) }
    for (const [width, height] of [[800, 380], [286, 338]]) {
      const fan = overlapFan(eight, width, height)
      assert.equal(fan?.length, 8)
      assert.equal(new Set(fan.map(n => `${n.x}:${n.y}`)).size, 8)
      for (const a of fan) {
        assert.ok(a.x >= 22 && a.x <= width - 22 && a.y >= 22 && a.y <= height - 22)
        for (const b of fan) if (a.id !== b.id) assert.ok(Math.abs(a.x - b.x) >= 44 || Math.abs(a.y - b.y) >= 44)
      }
    }
    assert.equal(overlapFan({ ...eight, nodes: [...eight.nodes, node('ninth', 0)] }, 800, 400), null)
    assert.equal(overlapFan(group, 180, 400), null)
    assert.equal(overlapFan(group, 320, 200), null)
    const dense = screenOverlaps(Array.from({ length: 10000 }, (_, i) => node(`dense-${i}`, 0)))
    assert.equal(dense.length, 1)
    assert.equal(dense[0].nodes.length, 10000)
  } finally { await vite.close() }
})
