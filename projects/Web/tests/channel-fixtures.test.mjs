import assert from 'node:assert/strict'
import test from 'node:test'
import { channelFixtures, channelObserverCount, channelReferenceTime } from '../src/routes/channels/channel-fixtures.ts'

test('channel fixtures distinguish exact packet variants from distinct observers', () => {
  const message = channelFixtures[0].messages[0]
  assert.equal(message.packets.length, 2)
  assert.equal(channelObserverCount(message), 3)
  assert.equal(channelObserverCount({ ...message, packets: [...message.packets, message.packets[0]] }), 3)
  assert.notEqual(message.packets[0].id, message.packets[1].id)
  assert.ok(channelFixtures.some(channel => channel.messages.length === 0))
})

test('fixture timelines are chronological with explicit unavailable and empty path states', () => {
  const ids = new Set()
  for (const channel of channelFixtures) {
    let previous = -Infinity
    for (const message of channel.messages) {
      assert.ok(!ids.has(message.id))
      ids.add(message.id)
      const arrived = Date.parse(message.firstArrivedAt)
      assert.ok(arrived >= previous && arrived <= channelReferenceTime)
      previous = arrived
      assert.equal(message.firstArrivedAt, message.packets[0].arrivedAt)
      assert.equal(new Set(message.packets.map(packet => packet.route)).size, 1, 'Path variants of each fixture transmission retain its transport')
      assert.ok(message.sentAt === null || Date.parse(message.sentAt) <= arrived)
    }
  }
  const packets = channelFixtures.flatMap(channel => channel.messages.flatMap(message => message.packets))
  assert.ok(packets.some(packet => packet.path === null))
  assert.ok(packets.some(packet => packet.path?.length === 0))
  assert.ok(packets.some(packet => packet.path?.length > 0))
  assert.ok(packets.some(packet => packet.route === 'direct'))
  assert.ok(packets.some(packet => packet.route === 'flood'))
})
