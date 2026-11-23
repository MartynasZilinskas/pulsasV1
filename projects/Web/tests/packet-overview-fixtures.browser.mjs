import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`

test('fixture overview preserves reception, wire-packet and shared-payload boundaries', async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' })
  const context = await browser.newContext({ serviceWorkers: 'block' })
  const unexpected = []
  await context.routeWebSocket('**/*', socket => socket.close())
  await context.route('**/*', route => {
    const target = new URL(route.request().url())
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)) {
      unexpected.push(target.href)
      return route.abort()
    }
    return route.continue()
  })
  try {
    const page = await context.newPage()
    assert.equal((await page.goto(`${origin}/health`)).status(), 200)
    const result = await page.evaluate(async () => {
      const { getPacketOverview } = await import('/src/routes/map/packet-overview-fixtures.ts')
      const { trafficRows } = await import('/src/routes/kitchen-sink/packet-traffic-fixtures.ts')
      const failures = new Set()
      const packets = new Map()
      const coverage = new Set()
      for (const row of trafficRows) {
        const overview = getPacketOverview(row)
        if (overview.reception.row.id !== row.id || overview.reception.observer !== row.observer) failures.add('reception identity')
        if (!overview.packet) {
          coverage.add('undecoded')
          if (overview.otherHearings.length) failures.add('undecoded relationships')
          continue
        }
        const { packet } = overview
        const signature = JSON.stringify(packet)
        if (packets.has(packet.id) && packets.get(packet.id) !== signature) failures.add('exact packet changed between receptions')
        packets.set(packet.id, signature)
        coverage.add(packet.path.kind)
        coverage.add(packet.path.status)
        if (packet.path.kind !== 'trace' && packet.path.status === 'Unavailable') {
          coverage.add('missing-assessment')
          if (packet.path.assessedAt !== null || packet.path.hops.length) failures.add('fabricated assessment')
        }
        if (packet.path.kind === 'trace' && packet.path.hops.length) failures.add('trace bytes interpreted as path')
        for (const hop of packet.path.hops) {
          coverage.add(hop.outcome)
          const selected = hop.candidates.filter(candidate => candidate.selected)
          if (hop.outcome === 'Resolved' ? selected.length !== 1 : selected.length !== 0) failures.add('selection contradicts outcome')
          for (const candidate of hop.candidates) {
            if (!candidate.publicKey.startsWith(hop.prefix) || candidate.publicKey.length !== 64) failures.add('candidate prefix')
          }
        }
        const ids = new Set()
        for (const other of overview.otherHearings) {
          if (other.row.id === row.id || ids.has(other.row.id)) failures.add('duplicate/self hearing')
          ids.add(other.row.id)
          const target = getPacketOverview(other.row)
          if (target.packet?.payloadId !== packet.payloadId) failures.add('unrelated payload')
          if (other.samePacket !== (target.packet?.id === packet.id)) failures.add('incorrect packet relationship')
          coverage.add(other.samePacket ? 'same-packet' : 'different-packet')
        }
        if (overview.reception.rssi === null && overview.reception.snr === null) coverage.add('missing-signal')
      }
      return { failures: [...failures], coverage: [...coverage] }
    })
    assert.deepEqual(result.failures, [])
    for (const expected of ['undecoded', 'trace', 'flood', 'direct', 'Empty', 'Resolved', 'Ambiguous', 'Unresolved', 'same-packet', 'different-packet', 'missing-signal', 'missing-assessment']) {
      assert.ok(result.coverage.includes(expected), `missing representative fixture: ${expected}`)
    }
    assert.deepEqual(unexpected, [])
  } finally {
    await context.close()
    await browser.close()
  }
})
