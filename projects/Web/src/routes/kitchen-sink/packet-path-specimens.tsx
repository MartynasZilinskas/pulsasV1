import { Select, SelectContent, SelectItem, SelectItemIndicator, SelectLabel, SelectTrigger, SelectValue } from '@ui-sdk/react/select'
import { useState } from 'react'
import { PacketPathChain, type PacketPathIndicator, type PacketPathSelection } from '../../components/packets/packet-path-chain'
import { LucideGlyph } from '../../components/ui/lucide-glyph'
import { comparisonHops, densityPaths, indicators, shortPath } from './packet-path-fixtures'

export function PacketPathSpecimens() {
  const [indicator, setIndicator] = useState<PacketPathIndicator>('count-horizontal')
  const [selection, setSelection] = useState('')
  const selectCandidate = ({ hop, hopNumber, candidate }: PacketPathSelection, context?: string) => {
    setSelection(`Selected ${candidate.name} for hop ${hopNumber} (${hop.prefix})${context ? ` in ${context}` : ''}.`)
  }

  return (
    <section className="ks-section" aria-labelledby="ks-path-heading">
      <div className="ks-section-heading">
        <h2 id="ks-path-heading">Packet path chains</h2>
        <p>Hover or focus a hop for candidate counts, or open it to inspect candidates. The sample hop counts show display density, not protocol limits.</p>
      </div>
      <div className="ks-path-picker">
        <Select<PacketPathIndicator> items={[...indicators]} value={indicator} onValueChange={(value) => { if (value !== null) setIndicator(value) }}>
          <SelectLabel className="ks-field-label">Indicator variant</SelectLabel>
          <SelectTrigger className="ks-select-trigger"><SelectValue /></SelectTrigger>
          <SelectContent className="ks-select-content" align="start" sideOffset={5}>
            {indicators.map(({ value, label }) => (
              <SelectItem className="ks-select-item" key={value} value={value} label={label}>
                <span>{label}</span>
                <SelectItemIndicator className="ks-select-indicator" aria-hidden="true"><LucideGlyph name="check" /></SelectItemIndicator>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="ks-path-board" data-path-examples>
        <div className="ks-path-example">
          <div className="ks-path-caption"><h3>Short path</h3><span>1-byte prefixes · 3 hops</span></div>
          <PacketPathChain hops={shortPath} indicator={indicator} label="Short path" onSelect={selectCandidate} />
        </div>
        {densityPaths.map(({ width, hops }) => {
          const label = `${width}, ${hops.length} hops`
          return (
            <div className="ks-path-example" key={width}>
              <div className="ks-path-caption"><h3>{width}</h3><span>{hops.length} hops</span></div>
              <PacketPathChain hops={hops} indicator={indicator} label={label} onSelect={(value) => selectCandidate(value, label)} />
            </div>
          )
        })}
        <div className="ks-path-example">
          <div className="ks-path-caption"><h3>Reserved prefix width</h3><span>No hops supplied</span></div>
        </div>
      </div>
      <section className="ks-path-comparison" aria-labelledby="ks-path-comparison-heading">
        <h3 id="ks-path-comparison-heading">Compare indicators</h3>
        <p>Green: at least one strong candidate. Amber: only weak candidates. Gray: no candidates. Marks show up to three alternatives, weak first; +N counts all alternatives.</p>
        <div className="ks-path-board">
          {indicators.map(({ value, label }) => (
            <div className="ks-path-example" data-path-comparison={value} key={value}>
              <div className="ks-path-caption"><h4>{label}</h4></div>
              <PacketPathChain hops={comparisonHops} indicator={value} label={label} onSelect={(selected) => selectCandidate(selected, label)} />
            </div>
          ))}
        </div>
      </section>
      <p className="ks-path-status" role="status" aria-label="Packet path selection" aria-atomic="true">{selection}</p>
    </section>
  )
}
