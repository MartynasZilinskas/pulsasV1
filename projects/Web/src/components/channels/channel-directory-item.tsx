'use client'

import { Button } from '@ui-sdk/react/button'
import type { Ref } from 'react'
import { ChannelKindIcon, type ChannelKind } from './channel-kind-icon'
import './channel-browser.css'

export function ChannelDirectoryItem({ name, kind, selected, latestArrival, onSelect, ref }: {
  name: string
  kind: ChannelKind
  selected: boolean
  latestArrival: string | null
  onSelect: () => void
  ref?: Ref<HTMLButtonElement>
}) {
  return <Button ref={ref} className="channel-directory-item" aria-label={`Browse ${name}`} aria-current={selected ? 'true' : undefined} onClick={onSelect}>
    <ChannelKindIcon kind={kind} />
    <span className="channel-directory-label">
      <span className="channel-directory-name">{name}</span>
      <span className="channel-directory-arrival">{latestArrival ? <>Arrived <time dateTime={latestArrival}>{new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' }).format(Date.parse(latestArrival))} UTC</time></> : 'No messages'}</span>
    </span>
    {selected && <span className="channel-directory-selected" aria-hidden="true">›</span>}
  </Button>
}
