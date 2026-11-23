'use client'

import { Accordion, AccordionHeader, AccordionItem, AccordionPanel, AccordionTrigger } from '@ui-sdk/react/accordion'
import { Button } from '@ui-sdk/react/button'
import { Collapsible, CollapsiblePanel, CollapsibleTrigger } from '@ui-sdk/react/collapsible'
import { useId } from 'react'
import type { FixturePacketOverview } from '../../routes/map/packet-overview-fixtures'
import { PacketTypeBadge } from './packet-type-badge'
import type { PacketTableRow } from './packet-table'
import { RouteBadge } from './route-badge'
import '../ui/badge.css'
import './reception-packet-overview.css'

type ExactPacket = NonNullable<FixturePacketOverview['packet']>

const timestamp = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: 'UTC',
})

function Time({ value }: { value: string | null }) {
  return value ? <time dateTime={value}>{timestamp.format(Date.parse(value))} UTC</time> : <>Unknown</>
}

function PacketPath({ path }: { path: ExactPacket['path'] }) {
  const title = path.kind === 'flood' ? 'Flood history'
    : path.kind === 'direct' ? 'Remaining route' : path.kind === 'trace' ? 'Trace data' : 'Path unavailable'
  const nodePrefixes = path.kind === 'flood' || path.kind === 'direct'

  return <section className="reception-packet-section" aria-label={title}>
    <div className="reception-packet-section-heading"><h3>{title}</h3><span className="reception-packet-state">{path.status}</span></div>
    {path.kind === 'trace' ? <p className="reception-packet-note">SNR samples, not a node path.</p>
      : path.kind === 'unavailable' ? <p className="reception-packet-note">Route not decoded.</p>
        : <>
          <dl className="reception-packet-facts reception-packet-path-meta">
            <div><dt>Prefix width</dt><dd>{path.prefixWidth === null ? 'Unknown' : `${path.prefixWidth} ${path.prefixWidth === 1 ? 'byte' : 'bytes'}`}</dd></div>
            {path.assessedAt && <div><dt>Assessed</dt><dd><Time value={path.assessedAt} /></dd></div>}
          </dl>
        </>}
    {nodePrefixes && (path.hops.length === 0
      ? <p className="reception-packet-note">{path.status === 'Empty' ? 'No path prefixes.'
        : path.status === 'Unavailable' ? 'No assessment recorded; prefixes unknown.'
          : 'Prefixes unavailable.'}</p>
      : <Accordion className="reception-packet-prefixes" aria-label="Packet prefix evidence" multiple>
        {path.hops.map((hop, index) => <AccordionItem key={hop.id} value={hop.id} className="reception-packet-hop">
          <AccordionHeader render={<h4 />}>
            <AccordionTrigger className="reception-packet-hop-trigger" aria-label={`Inspect prefix ${index + 1}: ${hop.prefix}, ${hop.outcome}, ${hop.candidates.length} candidates`}>
              <span className="reception-packet-hop-number">{index + 1}</span>{' '}
              <code>{hop.prefix}</code>{' '}
              <span className="reception-packet-hop-summary">{hop.outcome}{' '}<span>{hop.candidates.length} {hop.candidates.length === 1 ? 'candidate' : 'candidates'}</span></span>
              <svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="m6 9 6 6 6-6" /></svg>
            </AccordionTrigger>
          </AccordionHeader>
          <AccordionPanel className="reception-packet-hop-details">
            {hop.candidates.length === 0 ? <p className="reception-packet-note">No matching candidates.</p>
              : <ul className="reception-packet-candidates">
                {hop.candidates.map(candidate => <li key={candidate.id} data-assessment-selected={candidate.selected ? '' : undefined}>
                  <div className="reception-packet-candidate-heading"><strong>{candidate.name}</strong>{' '}
                    <span className="ks-badge" data-tone={candidate.confidence === 'Strong' ? 'green' : 'amber'}>{candidate.confidence}</span></div>
                  <p className="reception-packet-candidate-selection">{candidate.selected ? 'Assessment pick' : 'Alternative'}</p>
                  <code className="reception-packet-public-key">{candidate.publicKey}</code>
                  <p className="reception-packet-note">{candidate.reasons.join(' · ')}<br />Support: {candidate.supportCount}</p>
                </li>)}
              </ul>}
          </AccordionPanel>
        </AccordionItem>)}
      </Accordion>)}
  </section>
}

export function ReceptionPacketOverview({ overview, onSelectReception }: {
  overview: FixturePacketOverview
  onSelectReception: (row: PacketTableRow) => void
}) {
  const { reception, packet, otherHearings } = overview
  const hearingContext = useId()
  const { row } = reception
  const hearings = otherHearings.filter(hearing => hearing.row.id !== row.id)

  return <section className="reception-packet-overview" aria-label="Packet overview" data-overview-reception={row.id}>
    <p className="reception-packet-fixture">Synthetic fixture</p>
    <section className="reception-packet-section" aria-label="Reception">
      <h3>Reception</h3>
      <dl className="reception-packet-facts">
        <div><dt>Observer</dt><dd>{reception.observer}</dd></div>
        <div><dt>Broker</dt><dd>{reception.broker}</dd></div>
        <div><dt>Heard</dt><dd><Time value={reception.heardAt} /></dd></div>
        <div><dt>Arrived</dt><dd><Time value={row.arrivedAt} /></dd></div>
        <div><dt>Signal</dt><dd>RSSI {reception.rssi === null ? 'unknown' : `${reception.rssi} dBm`}<br />SNR {reception.snr === null ? 'unknown' : `${reception.snr} dB`}</dd></div>
        <div><dt>Region</dt><dd>{row.region}</dd></div>
      </dl>
    </section>
    <section className="reception-packet-section" aria-label="Exact packet">
      <div className="reception-packet-section-heading"><h3>Exact packet</h3><PacketTypeBadge code={row.packetType} passive /></div>
      {packet ? <>
        <dl className="reception-packet-facts">
          <div><dt>Code</dt><dd><code>{packet.code}</code></dd></div>
          <div><dt>Transport</dt><dd><RouteBadge route={row.route} /></dd></div>
          <div><dt>Scope</dt><dd>{row.scope ?? 'No scope'}</dd></div>
          {packet.facts.map(fact => <div key={fact.label}><dt>{fact.label}</dt><dd>{fact.value}</dd></div>)}
        </dl>
      </> : <p className="reception-packet-note">Packet not decoded.</p>}
    </section>
    {packet && <PacketPath path={packet.path} />}
    <section className="reception-packet-section" aria-label="Other hearings">
      <h3>Other hearings</h3>
      {hearings.length === 0 ? <p className="reception-packet-note">None.</p> : <>
        <Collapsible>
        <CollapsibleTrigger className="reception-packet-hearings-toggle">Browse {hearings.length} hearings</CollapsibleTrigger>
        <CollapsiblePanel>
        <ul className="reception-packet-hearings">
          {hearings.map(hearing => <li key={hearing.row.id}>
            <Button className="reception-packet-hearing" aria-label={`Open hearing at ${hearing.observer}: ${hearing.row.code}, ${hearing.row.arrivedAt}`}
              aria-describedby={`${hearingContext}-${hearing.row.id}`} onClick={() => onSelectReception(hearing.row)}>
              <span className="reception-packet-hearing-title">{hearing.observer}<span aria-hidden="true">→</span></span>{' '}
              <code>{hearing.row.code}</code>
            </Button>
            <p className="reception-packet-hearing-context" id={`${hearingContext}-${hearing.row.id}`}>{hearing.samePacket ? 'Same exact packet' : 'Different packet · shared payload'}<br />
              {hearing.row.region} · <Time value={hearing.row.arrivedAt} /></p>
          </li>)}
        </ul>
        </CollapsiblePanel>
        </Collapsible>
      </>}
    </section>
  </section>
}
