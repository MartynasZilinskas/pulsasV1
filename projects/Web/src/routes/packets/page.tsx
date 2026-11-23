import { PacketTrafficSpecimens } from '../kitchen-sink/packet-traffic-specimens'
import './page.css'

export default function PacketsPage() {
  return <section className="packets-page" aria-label="Packets workspace">
    <h1 className="ks-visually-hidden">Packets</h1>
    <PacketTrafficSpecimens presentation="page" />
  </section>
}
