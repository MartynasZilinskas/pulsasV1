import type { ChannelKind } from '../../components/channels/channel-kind-icon'
import type { RouteKind } from '../../components/packets/route-badge'

export type ChannelPacketFixture = {
  readonly id: string
  readonly code: string
  readonly route: RouteKind
  readonly scope: string | null
  readonly path: readonly string[] | null
  readonly prefixWidth: 1 | 2 | 3
  readonly observers: readonly string[]
  readonly arrivedAt: string
}

export type ChannelMessageFixture = {
  readonly id: string
  readonly sender: string | null
  readonly text: string
  readonly sentAt: string | null
  readonly firstArrivedAt: string
  readonly packets: readonly ChannelPacketFixture[]
}

export type ChannelFixture = {
  readonly id: string
  readonly name: string
  readonly kind: ChannelKind
  readonly messages: readonly ChannelMessageFixture[]
}

export const channelReferenceTime = Date.parse('2026-11-20T14:00:00Z')

const senders = ['Mantas', 'Ieva', 'Kaunas / portable', 'Rūta', 'Jonas', null] as const
const messages = [
  'Labas rytas! Anyone receiving from Kaunas?',
  'Receiving clearly near the river. SNR 8 dB.',
  'Moving the antenna upstairs for the afternoon test.',
  'Vilnius → Kaunas test received. Thanks!',
  'Portable node is back online.',
  'Testing the western route. Can someone confirm?',
  'Klaipėda received 👍',
  'Battery at 84%. Staying on the hill until sunset.',
  'Next check at 16:00 local time.\nPlease keep the channel clear during the test.',
  'Antenna adjusted; the link looks much better now.',
  'All good here. Thanks for the reports!',
  'Packet text stays text: <img src=x onerror=alert(1)>',
] as const

function createMessage(channel: string, index: number, scope: string | null): ChannelMessageFixture {
  const instant = channelReferenceTime - (48 - index) * 4 * 60_000
  const arrivedAt = new Date(instant).toISOString()
  const route: RouteKind = index % 5 === 0 ? 'direct' : 'flood'
  const variantCount = index % 4 === 0 ? 2 : 1
  return {
    id: `${channel}-message-${index}`,
    sender: senders[index % senders.length],
    text: messages[index % messages.length],
    sentAt: index % 13 === 0 ? null : new Date(instant - 12_000).toISOString(),
    firstArrivedAt: arrivedAt,
    packets: Array.from({ length: variantCount }, (_, variant) => ({
      id: `${channel}-packet-${index}-${variant}`,
      code: (0xA47000 + index * 2 + variant).toString(16).toUpperCase(),
      route,
      scope,
      path: index % 11 === 0 ? null : variant ? ['B7', 'C2'] : index % 7 === 0 ? [] : ['4A', 'B7', 'C2'],
      prefixWidth: 1,
      observers: variant ? ['Vilnius 1'] : index % 3 === 0 ? ['Kaunas 1', 'Kaunas 2'] : ['Kaunas 1'],
      arrivedAt: new Date(instant + variant * 3_000).toISOString(),
    })),
  }
}

export const channelFixtures: readonly ChannelFixture[] = [
  { id: 'public', name: 'Public', kind: 'public', messages: Array.from({ length: 48 }, (_, index) => createMessage('public', index, null)) },
  { id: 'baltics', name: '#baltics', kind: 'hashtag', messages: Array.from({ length: 34 }, (_, index) => createMessage('baltics', index, '#baltics')) },
  { id: 'kaunas', name: '#kaunas', kind: 'hashtag', messages: Array.from({ length: 9 }, (_, index) => createMessage('kaunas', index, '#kaunas')) },
  { id: 'field-test', name: 'Field test', kind: 'shared-key', messages: [createMessage('field-test', 46, null), createMessage('field-test', 47, null)] },
  { id: 'quiet', name: '#off-grid', kind: 'hashtag', messages: [] },
]

export function channelObserverCount(message: ChannelMessageFixture): number {
  return new Set(message.packets.flatMap(packet => packet.observers)).size
}
