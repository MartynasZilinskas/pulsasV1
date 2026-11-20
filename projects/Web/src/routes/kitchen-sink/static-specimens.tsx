import { ChannelKindIcon } from '../../components/channels/channel-kind-icon'
import { MeshBrandMark } from '../../components/mesh-brand-mark'

const channels = [
  { kind: 'public', label: 'Public channel', description: 'The standard Public channel.' },
  { kind: 'hashtag', label: 'Hashtag channel', description: 'Uses a shared key derived from the exact hashtag.' },
  { kind: 'shared-key', label: 'Shared-key channel', description: 'Uses an explicitly supplied shared key.' },
] as const
const surfaces = [
  { theme: 'light', label: 'Light surface' },
  { theme: 'dark', label: 'Dark surface' },
] as const

export function ChannelKindSpecimens() {
  return (
    <section className="ks-section" aria-labelledby="ks-channels-heading">
      <div className="ks-section-heading">
        <h2 id="ks-channels-heading">Channel kind icons</h2>
        <p>The glyphs identify the channel kind and key origin, not who can read its messages.</p>
      </div>
      <ul className="ks-channel-gallery">
        {channels.map(({ kind, label, description }) => (
          <li className="ks-channel" data-kind={kind} key={kind}>
            <div className="ks-channel-heading">
              <h3>{label}</h3>
              <p>{description}</p>
            </div>
            {surfaces.map(({ theme, label }) => (
              <figure className="ks-surface ks-channel-surface" data-theme={theme} key={theme}>
                <figcaption>{label}</figcaption>
                <ul className="ks-glyph-specimens">
                  {[16, 20, 24].map((size) => (
                    <li key={size}>
                      <ChannelKindIcon kind={kind} size={size} />
                      <code>{size}px</code>
                    </li>
                  ))}
                </ul>
              </figure>
            ))}
          </li>
        ))}
      </ul>
    </section>
  )
}

export function MeshBrandSpecimens() {
  return (
    <section className="ks-section" aria-labelledby="ks-mesh-heading">
      <div className="ks-section-heading">
        <h2 id="ks-mesh-heading">Mesh icon</h2>
        <p>The static Pulsas mark used in the header, shown on light and dark surfaces and at actual icon sizes.</p>
      </div>
      <div className="ks-brand-specimens">
        <div className="ks-surfaces">
          {surfaces.map(({ theme, label }) => (
            <figure className="ks-surface ks-mark-surface" data-theme={theme} key={theme}>
              <figcaption>{label}</figcaption>
              <MeshBrandMark size={72} />
            </figure>
          ))}
        </div>
        <div className="ks-brand-sizes">
          <h3>Actual icon sizes</h3>
          <ul className="ks-glyph-specimens">
            {[16, 24, 48].map((size) => (
              <li key={size}>
                <span><MeshBrandMark size={size} /></span>
                <code>{size}px</code>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  )
}
