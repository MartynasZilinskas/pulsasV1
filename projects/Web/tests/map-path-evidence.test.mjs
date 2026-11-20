import assert from 'node:assert/strict'
import test from 'node:test'
import { evidenceCandidateKey, pathScene } from '../src/components/map/map-path-evidence.ts'

const anchor = (id, coordinates) => ({ id, name: id, coordinates, role: 'REPEATER', publicKey: id })
const strong = anchor => ({ anchor, confidence: 'Strong' })
const start = { prefix: 'abc123', matches: [strong(anchor('a', [23, 54]))] }
const end = { prefix: 'def456', matches: [strong(anchor('b', [25, 54]))] }

test('a strong unlocated match leaves a numbered schematic gap even with a located weak alternative', () => {
  const middle = { prefix: 'deadbe', matches: [strong(anchor('unlocated', null)), { anchor: anchor('weak', [23.8, 55]), confidence: 'Weak' }] }
  const scene = pathScene([start, middle, end])
  const gap = scene.markers.find(({ presentation }) => presentation === 'gap')
  assert.deepEqual(gap, { id: 'gap-2', coordinates: [24, 54], label: '2', prefix: 'deadbe', presentation: 'gap' })
  assert.ok(scene.markers.some(({ id }) => id === 'weak'))
  assert.ok(scene.markers.every(({ id }) => id !== 'unlocated'))
  assert.deepEqual(scene.paths.map(({ coordinates, confidence }) => ({ coordinates, confidence })), [
    { coordinates: [[23, 54], [24, 54]], confidence: 'weak' },
    { coordinates: [[24, 54], [25, 54]], confidence: 'weak' },
  ])
})

test('candidate visibility cannot resolve an ambiguous raw hop or erase every located candidate', () => {
  const middle = { prefix: 'ab1212', matches: [strong(anchor('first', [24, 54])), strong(anchor('second', [24, 55]))] }
  const hops = [start, middle, end]
  const hidden = new Set([evidenceCandidateKey(1, 'second')])
  const visible = pathScene(hops, hidden)
  assert.equal(visible.markers.some(({ id }) => id === 'second'), false)
  assert.equal(visible.markers.find(({ id }) => id === 'first').ambiguous, true)
  assert.deepEqual(visible.paths.map(({ confidence }) => confidence), ['weak', 'weak'])

  hidden.add(evidenceCandidateKey(1, 'first'))
  assert.deepEqual(pathScene(hops, hidden), pathScene(hops))
})
