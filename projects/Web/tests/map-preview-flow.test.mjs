import assert from 'node:assert/strict'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

test('a moving packet trail updates on each available display frame', async (t) => {
  const vite = await createServer({
    root: fileURLToPath(new URL('../', import.meta.url)),
    configFile: false,
    server: { middlewareMode: true, hmr: false },
    optimizeDeps: { noDiscovery: true },
  })
  try {
    const { playPreviewFlow } = await vite.ssrLoadModule('/src/components/map/map-preview-flow.ts')
    let now = 0
    let tick
    t.mock.method(performance, 'now', () => now)
    globalThis.requestAnimationFrame = (callback) => { tick = callback; return 1 }
    globalThis.cancelAnimationFrame = () => { tick = undefined }
    globalThis.window = globalThis
    t.after(() => {
      delete globalThis.requestAnimationFrame
      delete globalThis.cancelAnimationFrame
      delete globalThis.window
    })
    const trailEnds = []
    const stop = playPreviewFlow({
      async setData({ features }) {
        const trail = features.find(({ properties }) => properties.kind === 'trail')
        if (trail) trailEnds.push(trail.geometry.coordinates.at(-1)[0])
      },
    }, [{ id: 'route', stops: [{ id: 'a', coordinates: [23, 54] }, { id: 'b', coordinates: [24, 54] }] }],
    { mode: 'once', hopMs: 1400, reduced: false }, () => assert.fail('Playback ended early'), assert.fail)
    try {
      for (let frame = 1; frame <= 60; frame++) {
        now = frame * 1000 / 60
        tick(now)
      }
      assert.equal(trailEnds.length, 60)
      assert.ok(trailEnds.every((value, index) => index === 0 || value > trailEnds[index - 1]))
      assert.equal(trailEnds.at(-1), 23 + 1000 / 1400)
    } finally {
      stop()
    }
    assert.equal(tick, undefined)
  } finally {
    await vite.close()
  }
})
