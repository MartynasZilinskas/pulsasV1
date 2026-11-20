import { PacketTypeBadge, type PacketTypeCode } from '../../components/packets/packet-type-badge'

const packetCodes = [
  '0x00', '0x01', '0x02', '0x03', '0x04', '0x05', '0x06', '0x07',
  '0x08', '0x09', '0x0A', '0x0B', '0x0C', '0x0D', '0x0E', '0x0F',
] satisfies readonly PacketTypeCode[]

const presentations = [
  { id: 'icons', label: 'Icon + text', icons: true },
  { id: 'text', label: 'Text only', icons: false },
] as const

const samplePackets = [
  { type: '0x04', summary: 'Hilltop repeater · node announcement' },
  { type: '0x02', summary: 'A1 → B2 · encrypted contents unavailable' },
  { type: '0x07', summary: 'Sender prefix 6A → B2' },
  { type: '0x0C', summary: 'Reserved payload type' },
] as const

export function PacketTypeSpecimens() {
  return (
    <section aria-labelledby="ks-type-heading" className="ks-packet-types">
      <h3 id="ks-type-heading">Packet type</h3>
      <p>Same labels and colors. Hover, focus or tap a badge for its protocol name and code.</p>
      <div className="ks-type-comparison">
        {presentations.map(({ id, label, icons }) => (
          <section className="ks-type-panel" aria-labelledby={`ks-type-${id}`} key={id}>
            <h4 id={`ks-type-${id}`}>{label}</h4>
            <ul className="ks-badge-list">
              {packetCodes.map((code) => (
                <li key={code}><PacketTypeBadge code={code} icons={icons} /></li>
              ))}
              <li><PacketTypeBadge icons={icons} /></li>
            </ul>
          </section>
        ))}
      </div>
      <div className="ks-type-density">
        <h4>In compact packet rows</h4>
        <div className="ks-type-comparison">
          {presentations.map(({ id, label, icons }) => (
            <div className="ks-type-panel ks-packet-table" key={id}>
              <table>
                <caption>{label} · sample packets</caption>
                <thead><tr><th scope="col">Type</th><th scope="col">Summary</th></tr></thead>
                <tbody>
                  {samplePackets.map(({ type, summary }) => (
                    <tr key={type}>
                      <td><PacketTypeBadge code={type} icons={icons} /></td>
                      <td>{summary}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}
