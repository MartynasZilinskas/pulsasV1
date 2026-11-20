'use client'

import { createPopoverController, Popover, PopoverClose, PopoverContent, PopoverDescription, PopoverTitle, PopoverTrigger } from '@ui-sdk/react/popover'
import { createTooltipController, Tooltip, TooltipContent, TooltipTrigger } from '@ui-sdk/react/tooltip'
import { useState } from 'react'
import { PublicKey } from '../public-key'
import '../ui/badge.css'
import './packet-path-chain.css'

export type PacketPathCandidate = {
  name: string
  publicKey: string
  confidence: 'Strong' | 'Weak'
  reason: string
}

export type PacketPathHop = { id: string; prefix: string; candidates: readonly PacketPathCandidate[] }
export type PacketPathIndicator = 'squares' | 'dots' | 'corner' | 'underline' | 'ambiguity' | 'count' | 'count-horizontal'
export type PacketPathSelection = { hop: PacketPathHop; hopNumber: number; candidate: PacketPathCandidate }

type HopContext = { hop: PacketPathHop; number: number; context?: string }
type Inspector = ReturnType<typeof createPopoverController<HopContext>>
type HopTooltip = ReturnType<typeof createTooltipController<string>>
type OnSelect = (selection: PacketPathSelection) => void

function hopModel({ candidates }: PacketPathHop) {
  const total = candidates.length
  const strong = candidates.filter(({ confidence }) => confidence === 'Strong').length
  const weak = total - strong
  const weakMarks = Math.min(3, strong ? weak : Math.max(0, weak - 1))
  const strongMarks = Math.min(3 - weakMarks, Math.max(0, strong - 1))
  return {
    total, strong, weak,
    marks: [...Array.from({ length: weakMarks }, () => 'Weak'), ...Array.from({ length: strongMarks }, () => 'Strong')],
    tone: strong ? 'green' : weak ? 'amber' : 'neutral',
    state: total > 1 ? 'Ambiguous' : total === 0 ? 'Missing' : strong ? 'Resolved' : 'Tentative',
  }
}

function HopTrigger({ entry, indicator, tooltip, inspector }: { entry: HopContext; indicator: PacketPathIndicator; tooltip: HopTooltip; inspector?: Inspector }) {
  const { hop, number, context } = entry
  const { total, strong, weak, marks, tone, state } = hopModel(hop)
  const showsCount = indicator === 'count' || indicator === 'count-horizontal'
  const counts = `${strong} strong · ${weak} weak`

  return (
    <TooltipTrigger
      controller={tooltip}
      payload={`${hop.prefix} · ${state} · ${counts}`}
      render={<PopoverTrigger controller={inspector} payload={inspector ? entry : undefined} />}
      className="ks-path-hop"
      data-indicator={indicator}
      data-state={state.toLowerCase()}
      data-tone={tone}
      aria-label={`Inspect hop ${number}: ${hop.prefix}, ${state}, ${total} ${total === 1 ? 'candidate' : 'candidates'}${context ? ` · ${context}` : ''}`}
      aria-description={counts}
    >
      {total > 1 && !showsCount && (
        <span className="ks-hop-marks" aria-hidden="true">
          {indicator === 'ambiguity' ? '≋' : marks.map((confidence, index) => (
            <span key={`${confidence}-${index}`} data-confidence={confidence.toLowerCase()} data-tone={confidence === 'Strong' ? 'green' : 'amber'} />
          ))}
        </span>
      )}
      <code className="ks-hop-prefix">{hop.prefix}</code>
      {showsCount && total > 1 && <span className="ks-hop-count" aria-hidden="true">+{total - 1}</span>}
    </TooltipTrigger>
  )
}

function HopDetails({ entry, onSelect }: { entry: HopContext; onSelect: OnSelect }) {
  const { hop, number } = entry
  const { total, strong, weak } = hopModel(hop)
  return (
    <>
      <div className="ks-path-header">
        <PopoverTitle render={<h3 />} className="ks-path-title">Hop {number} · {hop.prefix}</PopoverTitle>
        <PopoverDescription className="ks-path-detail">
          {total === 0 ? 'No candidates for this prefix.' : `${strong} strong · ${weak} weak`}
        </PopoverDescription>
      </div>
      {total > 0 && (
        <ul className="ks-path-candidates">
          {hop.candidates.map((item) => (
            <li key={item.publicKey}>
              <PopoverClose className="ks-candidate" aria-label={`Select ${item.name}`} aria-description={`${item.confidence} confidence. ${item.reason}. Public key: ${item.publicKey}`} onClick={() => onSelect({ hop, hopNumber: number, candidate: item })}>
                {/* Flex and grid gaps don't separate words in the button's textContent. */}
                <span className="ks-candidate-heading">
                  <span>{item.name}</span>{' '}
                  <span className="ks-badge" data-tone={item.confidence === 'Strong' ? 'green' : 'amber'}>{item.confidence}</span>
                </span>{' '}
                <PublicKey value={item.publicKey} />{' '}
                <span className="ks-path-detail">{item.reason}</span>
              </PopoverClose>
            </li>
          ))}
        </ul>
      )}
      {/* Keep Close after the candidates in tab order, even though it sits in the header. */}
      <PopoverClose className="ks-path-dismiss">
        <svg className="ks-glyph" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true" focusable="false">
          <path d="m6 6 12 12M18 6 6 18" />
        </svg>
        <span className="ks-visually-hidden">Close</span>
      </PopoverClose>
    </>
  )
}

export function PacketPathChain({ hops, indicator = 'count-horizontal', label, onSelect }: {
  hops: readonly PacketPathHop[]
  indicator?: PacketPathIndicator
  label: string
  onSelect: OnSelect
}) {
  const [inspector] = useState(() => createPopoverController<HopContext>())
  const [tooltip] = useState(() => createTooltipController<string>())
  // Small paths retain individually addressable agent popovers. Longer paths
  // share one inspector to bound registrations independently of hop count.
  const shared = hops.length > 4

  return (
    <>
      <ol className="ks-hop-chain" aria-label={label}>
        {hops.map((hop, index) => {
          const entry = { hop, number: index + 1, context: shared ? label : undefined }
          return (
            <li key={hop.id}>
              {shared ? <HopTrigger entry={entry} indicator={indicator} tooltip={tooltip} inspector={inspector} /> : (
                <Popover>
                  <HopTrigger entry={entry} indicator={indicator} tooltip={tooltip} />
                  <PopoverContent className="ks-path-popover" align="start" sideOffset={6} collisionPadding={8}>
                    <HopDetails entry={entry} onSelect={onSelect} />
                  </PopoverContent>
                </Popover>
              )}
            </li>
          )
        })}
      </ol>
      {shared && (
        <Popover controller={inspector}>
          <PopoverContent<HopContext> className="ks-path-popover" align="start" sideOffset={6} collisionPadding={8} keepMounted>
            {({ payload }) => payload
              ? <HopDetails entry={payload} onSelect={onSelect} />
              : <PopoverTitle render={<h3 />}>{label} hop details</PopoverTitle>}
          </PopoverContent>
        </Popover>
      )}
      <Tooltip controller={tooltip} type="description">
        <TooltipContent<string> className="ks-badge-tooltip" aria-label="Packet hop summary" data-agent-hidden collisionPadding={8}>{({ payload }) => payload}</TooltipContent>
      </Tooltip>
    </>
  )
}
