import type { NodeRole } from '../../components/nodes/node-role'

type MapAnchor = {
  id: string
  name: string
  role: NodeRole
  coordinates: readonly [number, number]
  publicKey: string
}

export const mapShowcaseCenter = [23.9036, 54.8985] as const

function anchor(id: string, name: string, role: NodeRole, coordinates: MapAnchor['coordinates'], publicKey: string): MapAnchor {
  return { id, name, role, coordinates, publicKey }
}

// Static Beacon KUN snapshot; ranked by the legacy 100 km Kaunas-centred selection.
export const mapAnchors = [
  anchor('9b904ae6-8341-487e-9c6d-86a4a90f4070', 'LT-KM ROOT ED', 'REPEATER', [23.89257, 54.91074], 'ed1c6d0c1b7f14f6e9ca4bf38eec674c266afdf6d29c7bd5d066aa7ff09daf86'),
  anchor('f99f399f-167e-4ac3-ba2a-c950bcf92a89', 'LT-KM Vilijampolė FF', 'REPEATER', [23.871102, 54.908078], '7d8e31121d000425201b2b3951b14af28d822e2b21988c363f61175982ceec04'),
  anchor('fd24c30e-606e-453c-96af-082301039c12', 'LT-KM Panerys C14A 🦗', 'REPEATER', [23.908625, 54.919662], 'c14ada007e10acf2518b61ed3bf54239d04cf616ff48a5e6735bb90afe86b46b'),
  anchor('c321b85c-5075-41c5-8cfd-8df26014d884', 'LT-KM Dainava 61 🪖', 'REPEATER', [23.939816, 54.913632], '61d14fdbc6d6b69de44c76de4327f0f4a00ecb3748c68a57a069450034edf838'),
  anchor('d70c89ec-c853-4fa3-8f1f-c5a878499ed6', 'v78', 'CLIENT', [23.914698, 54.870263], '2b786b91c73b3a338307df5cbf3479a4932ea1f0f0f286c156261105fe53fed3'),
  anchor('9861a8dd-3bf3-4a49-a9a5-4d9ef627fb3b', 'LY3PGO', 'CLIENT', [23.886824, 54.926492], '8d45949f8d61a8a342d1041fc890700871d6c9d61f011cdc60c13d39e8587721'),
] as const

export const unlocatedMapAnchor = {
  id: '21fcc03f-36e0-4052-9088-0d69d46b0c69',
  name: 'myhost wio',
  role: 'CLIENT',
  publicKey: '9f308ed88c576593f65ce1b82e32696b3d80fb90824fbf5f944212f445e42ba9',
  coordinates: null,
} as const
