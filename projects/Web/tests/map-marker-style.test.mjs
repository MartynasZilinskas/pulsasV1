import assert from 'node:assert/strict'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
import { createExpression, validateStyleMin } from '@maplibre/maplibre-gl-style-spec'

test('selected markers stay compact across the dot threshold while unselected roles begin at zoom 9 and names at zoom 12', async () => {
  const vite = await createServer({ root: fileURLToPath(new URL('../', import.meta.url)), configFile: false, server: { middlewareMode: true, hmr: false }, optimizeDeps: { noDiscovery: true } })
  try {
    const { offlineStyle } = await vite.ssrLoadModule('/src/components/map/map-preview-style.ts')
    const evaluate = (input, zoom, properties) => {
      if (!Array.isArray(input) || typeof input[0] !== 'string') return input
      const expression = createExpression(input, 'test')
      assert.equal(expression.result, 'success')
      return expression.value.evaluate({ zoom }, { type: 'Point', properties })
    }
    const stable = {
      'preview-role-face': ['paint', ['circle-radius', 'circle-stroke-width']],
      'preview-confidence-ring': ['paint', ['circle-radius']],
      'preview-raised-confidence-ring': ['paint', ['circle-radius']],
      'preview-raised-confidence-ink': ['paint', ['circle-radius']],
      'preview-raised-ordinary-ring': ['paint', ['circle-radius']],
      'preview-raised-role-face': ['paint', ['circle-radius', 'circle-stroke-width']],
      'preview-hop-number': ['layout', ['text-offset']],
      'preview-ambiguous': ['layout', ['text-offset']],
      'preview-raised-hop': ['layout', ['text-offset']],
      'preview-raised-ambiguous': ['layout', ['text-offset']],
    }
    for (const theme of ['light', 'dark']) {
      const style = offlineStyle(theme)
      assert.deepEqual(validateStyleMin(style), [])
      const layer = id => style.layers.find(layer => layer.id === id)
      for (const properties of [{}, { confidence: 'strong' }, { confidence: 'weak', ambiguous: true }]) {
        for (const [id, [group, keys]] of Object.entries(stable)) {
          for (const key of keys) {
            const value = layer(id)[group][key]
            const expected = evaluate(value, 12.5, properties)
            for (const zoom of [9, 9.1, 11.5, 12, 16]) assert.deepEqual(evaluate(value, zoom, properties), expected, `${id} ${key} at ${zoom}`)
          }
        }
        for (const zoom of [8.5, 8.99, 9, 9.5, 12.5]) {
          const evidence = 'confidence' in properties
          const visibleCircles = [
            ['preview-raised-role-face', 9, 1],
            ...(evidence
              ? [['preview-raised-confidence-ring', 15, 4], ['preview-raised-confidence-ink', 14, 3]]
              : [['preview-raised-ordinary-ring', 12.5, 2]]),
          ]
          for (const [id, radius, stroke] of visibleCircles) {
            const circle = layer(id)
            assert.ok((circle.minzoom ?? 0) <= zoom, `${id} visible at ${zoom}`)
            assert.equal(evaluate(circle.paint['circle-radius'], zoom, properties), radius, `${id} radius at ${zoom}`)
            assert.equal(evaluate(circle.paint['circle-stroke-width'], zoom, properties), stroke, `${id} stroke at ${zoom}`)
            if (circle.filter) assert.equal(evaluate(circle.filter, zoom, properties), true)
          }
          const hitArea = layer('preview-raised').paint
          assert.equal(evaluate(hitArea['circle-radius'], zoom, properties), evidence ? 18 : 22)
          assert.equal(evaluate(hitArea['circle-stroke-width'], zoom, properties), evidence ? 0 : 4)
          assert.equal(evaluate(hitArea['circle-opacity'], zoom, properties), 0)
          assert.equal(evaluate(hitArea['circle-stroke-opacity'], zoom, properties), 0)
          assert.equal(evaluate(layer('preview-raised-ordinary-ring').filter, zoom, properties), !evidence)
          assert.equal(evaluate(layer('preview-raised-confidence-ink').filter, zoom, properties), evidence)
          if (evidence) {
            assert.equal(evaluate(layer('preview-raised-confidence-ink').paint['circle-stroke-color'], zoom, properties),
              theme === 'dark' ? (properties.confidence === 'weak' ? '#ffca82' : '#92e4bc') : (properties.confidence === 'weak' ? '#8b5719' : '#165e47'))
            assert.deepEqual(evaluate(layer('preview-raised-hop').layout['text-offset'], zoom, properties), [0, -15.5 / 12])
            assert.deepEqual(evaluate(layer('preview-raised-ambiguous').layout['text-offset'], zoom, properties), [15.5 / 14, 0])
          }
          assert.deepEqual(evaluate(layer('preview-raised-label').layout['text-offset'], zoom, properties),
            [0, (zoom < 9 ? 26 : evidence ? 22 : 20) / 12])
        }
      }
      assert.equal(layer('preview-role').minzoom, 9)
      assert.equal(layer('preview-role-face').minzoom, 9)
      const colors = { roleInk: '#574184', roleFill: '#f0eaf9', roleDarkInk: '#dec8ff', roleDarkFill: '#3a2e50', roleImage: 'node-role-REPEATER' }
      for (const id of ['preview-role-face', 'preview-raised-role-face']) {
        for (const zoom of id === 'preview-raised-role-face' ? [8.5, 9.5, 12.5] : [12.5]) {
          assert.equal(evaluate(layer(id).paint['circle-color'], zoom, colors), theme === 'dark' ? colors.roleDarkFill : colors.roleFill)
          assert.equal(evaluate(layer(id).paint['circle-stroke-color'], zoom, colors), theme === 'dark' ? colors.roleDarkInk : colors.roleInk)
        }
      }
      for (const id of ['preview-role', 'preview-raised-role']) {
        assert.equal(evaluate(layer(id).layout['icon-image'], 12.5, colors), `node-role-REPEATER${theme === 'dark' ? '-dark' : ''}`)
      }
      for (const id of ['preview-role', 'preview-raised-role']) {
        for (const zoom of id === 'preview-raised-role' ? [8.5, 9.5, 12.5, 18] : [9, 11.5, 12.5, 18]) {
          assert.ok((layer(id).minzoom ?? 0) <= zoom)
          for (const role of ['CLIENT', 'ROOM_SERVER', 'REPEATER', 'SENSOR', 'NONE']) {
            assert.equal(evaluate(layer(id).layout['icon-size'], zoom, { roleImage: `node-role-${role}` }), ['CLIENT', 'ROOM_SERVER'].includes(role) ? 0.8 : 1)
          }
        }
      }
      assert.equal(layer('preview-name').minzoom, 12)
      assert.equal(layer('preview-evidence-name').minzoom, undefined)
      assert.equal(evaluate(layer('preview-marker').paint['circle-radius'], 8.5, {}), 5)
      assert.equal(evaluate(layer('preview-marker').paint['circle-radius'], 8.5, { confidence: 'weak' }), 6)
      assert.equal(evaluate(layer('preview-confidence-ring').paint['circle-radius'], 8.5, { confidence: 'weak' }), 10)
      assert.equal(evaluate(layer('preview-marker').paint['circle-opacity'], 8.5, {}), 1)
      assert.equal(evaluate(layer('preview-marker').paint['circle-opacity'], 9, {}), 0)
      assert.deepEqual(layer('preview-raised-label').layout['icon-text-fit-padding'], [0, 6, 2, 6])
      const evidence = layer('preview-evidence-name').layout
      assert.equal(evidence['text-variable-anchor-offset'], undefined)
      assert.equal(evidence['text-anchor'], 'top')
      assert.equal(evidence['text-allow-overlap'] ?? false, false)
      assert.deepEqual(evaluate(layer('preview-name').layout['text-offset'], 12.5, {}), [0, 15 / 12])
      assert.deepEqual(evaluate(layer('preview-raised-label').layout['text-offset'], 12.5, {}), [0, 20 / 12])
    }
  } finally {
    await vite.close()
  }
})
