'use client'

import { Button } from '@ui-sdk/react/button'
import { useId, type Ref } from 'react'
import './channel-browser.css'

export function ChannelMessageRow({ sender, text, arrival, referenceTime, variants, observers, selected, onSelect, evidenceId, ref }: {
  sender: string | null
  text: string
  arrival: string
  referenceTime: number
  variants: number
  observers: number
  selected: boolean
  evidenceId?: string
  onSelect: (trigger: HTMLElement) => void
  ref?: Ref<HTMLButtonElement>
}) {
  const description = useId()
  const minutes = Math.round((Date.parse(arrival) - referenceTime) / 60_000)
  const [amount, unit] = Math.abs(minutes) < 60 ? [minutes, 'minute'] as const : Math.abs(minutes) < 1440 ? [Math.round(minutes / 60), 'hour'] as const : [Math.round(minutes / 1440), 'day'] as const
  return <Button ref={ref} className="channel-message-row" aria-label={`Inspect message from ${sender ?? 'unknown sender'} at ${new Date(arrival).toISOString()}`} aria-describedby={`${description}-counts ${description}`} aria-expanded={evidenceId ? selected : undefined} aria-pressed={evidenceId ? undefined : selected} aria-controls={selected ? evidenceId : undefined} onClick={event => onSelect(event.currentTarget)}>
    <span className="channel-message-top">
      <span className="channel-message-sender">{sender ?? 'Unknown sender'}</span>{' '}
      <span id={`${description}-counts`} className="channel-message-counts">{variants} {variants === 1 ? 'variant' : 'variants'} · {observers} {observers === 1 ? 'observer' : 'observers'}</span>{' '}
      <time className="channel-message-age" dateTime={arrival} title={`First arrived ${new Date(arrival).toUTCString()}`}>{new Intl.RelativeTimeFormat('en', { numeric: 'auto', style: 'narrow' }).format(amount, unit)}</time>
    </span>
    <span id={description} className="channel-message-text">{text}</span>
  </Button>
}
