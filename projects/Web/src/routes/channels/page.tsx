'use client'

import { Button } from '@ui-sdk/react/button'
import { createFocusRecovery } from '@ui-sdk/core'
import { useLayoutEffect, useRef, useState } from 'react'
import { ChannelDirectoryItem } from '../../components/channels/channel-directory-item'
import { ChannelKindIcon } from '../../components/channels/channel-kind-icon'
import { ChannelMessageRow } from '../../components/channels/channel-message-row'
import { NodeAvatar } from '../../components/nodes/node-avatar'
import { PacketTypeBadge } from '../../components/packets/packet-type-badge'
import { RouteBadge } from '../../components/packets/route-badge'
import { SectionError } from '../../components/ui/generic-states'
import { channelFixtures, channelObserverCount, channelReferenceTime, type ChannelFixture, type ChannelMessageFixture, type ChannelPacketFixture } from './channel-fixtures'
import './page.css'

function ExactTime({ value }: { value: string | null }) {
  return value ? <time dateTime={value}>{new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'long', timeZone: 'UTC' }).format(Date.parse(value))}</time> : <>Unknown</>
}

function PacketEvidence({ packet, showTransport }: { packet: ChannelPacketFixture; showTransport: boolean }) {
  return <article className="channel-packet" aria-label={`Wire variant ${packet.code}`}>
    <div className="channel-packet-heading"><h4><code>{packet.code}</code></h4>{showTransport && <RouteBadge route={packet.route} scope={packet.scope ?? undefined} />}</div>
    <dl className="channel-evidence-facts">
      <div><dt>Arrived</dt><dd><ExactTime value={packet.arrivedAt} /></dd></div>
      <div><dt>Observers</dt><dd>{packet.observers.join(', ') || 'None'}</dd></div>
      <div><dt>{packet.route === 'direct' || packet.route === 'transportDirect' ? 'Remaining route' : 'Flood history'}</dt><dd><code className="channel-packet-prefixes">{packet.path === null ? 'Unavailable' : packet.path.length ? packet.path.join(' → ') : 'Empty path'}</code></dd></div>
      <div><dt>Prefix width</dt><dd>{packet.prefixWidth} {packet.prefixWidth === 1 ? 'byte' : 'bytes'}</dd></div>
    </dl>
  </article>
}

function MessageEvidence({ message, onClose, returnFocus }: { message: ChannelMessageFixture; onClose: () => void; returnFocus: () => HTMLElement | null }) {
  const root = useRef<HTMLElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const firstPacket = message.packets[0]
  const commonTransport = firstPacket && message.packets.every(packet => packet.route === firstPacket.route && packet.scope === firstPacket.scope) ? firstPacket : null
  const [recovery] = useState(() => createFocusRecovery({ fallback: returnFocus }))
  useLayoutEffect(() => {
    if (root.current) return recovery.bind(root.current, { key: message.id, props: {} })
  }, [message.id, recovery])
  useLayoutEffect(() => {
    let active = true
    // Strict Mode rebinds recovery before its queued focus restoration completes.
    queueMicrotask(() => { if (active) heading.current?.focus({ preventScroll: true }) })
    return () => { active = false }
  }, [message.id])
  return <section ref={root} id="channel-message-evidence" className="channels-evidence" aria-labelledby="channels-evidence-title">
    <header className="channels-pane-header"><h2 id="channels-evidence-title" ref={heading} tabIndex={-1}>Packet evidence</h2><Button className="channels-control" aria-label="Close packet evidence" onClick={onClose}>Close <span aria-hidden="true">×</span></Button></header>
    <div className="channels-scroll channel-evidence-scroll" tabIndex={0} aria-label="Packet evidence scroll area">
      <div className="channel-evidence-summary">
        <div className="channel-evidence-sender"><NodeAvatar role="NONE" size="marker" /><div><h3>{message.sender ?? 'Unknown sender'}</h3><p>Claimed sender</p></div></div>
        <div className="channel-evidence-badges"><PacketTypeBadge code="0x05" passive />{commonTransport && <RouteBadge route={commonTransport.route} scope={commonTransport.scope ?? undefined} />}</div>
        <p className="channel-evidence-text">{message.text}</p>
        <dl className="channel-evidence-facts"><div><dt>Sent</dt><dd><ExactTime value={message.sentAt} /></dd></div><div><dt>First arrived</dt><dd><ExactTime value={message.firstArrivedAt} /></dd></div></dl>
        <p className="channel-evidence-counts">{message.packets.length} packet {message.packets.length === 1 ? 'variant' : 'variants'} · {channelObserverCount(message)} distinct {channelObserverCount(message) === 1 ? 'observer' : 'observers'}</p>
      </div>
      <div className="channel-evidence-packets"><h3>Wire variants</h3><p className="channel-evidence-note">Same message; distinct exact packets. Observer hearings are grouped per variant.</p>{message.packets.map(packet => <PacketEvidence packet={packet} showTransport={!commonTransport} key={packet.id} />)}<p className="channel-evidence-note">Path prefixes are not verified node identities.</p></div>
    </div>
  </section>
}

type ReadingAnchor = { id: string; offset: number }

function ChannelThread({ channel, channelId, evidence, onInspect, onBack }: {
  channel: ChannelFixture | undefined
  channelId: string | null
  evidence: ChannelMessageFixture | null
  onInspect: (message: ChannelMessageFixture, trigger: HTMLElement) => void
  onBack: () => void
}) {
  const [visibleCount, setVisibleCount] = useState(20)
  const [atBottom, setAtBottom] = useState(true)
  const scroll = useRef<HTMLDivElement>(null)
  const title = useRef<HTMLHeadingElement>(null)
  const anchor = useRef<ReadingAnchor | null>(null)
  const following = useRef(true)
  const initialized = useRef(false)
  const historyReady = useRef(false)
  const previousScrollTop = useRef(0)
  const historyIntent = useRef(false)
  const lastRequestedCount = useRef<number | null>(null)
  const touchY = useRef<number | null>(null)
  const messages = channel?.messages.slice(-visibleCount) ?? []
  const hasOlder = Boolean(channel && visibleCount < channel.messages.length)

  function requestOlder() {
    const element = scroll.current
    if (!historyIntent.current || !historyReady.current || !initialized.current || !element?.clientHeight || evidence || !hasOlder || element.scrollTop > 120 || lastRequestedCount.current === visibleCount) return
    historyIntent.current = false
    lastRequestedCount.current = visibleCount
    rememberPosition()
    following.current = false
    setVisibleCount(count => count + 20)
    return true
  }

  function rememberPosition() {
    const element = scroll.current
    if (!element?.clientHeight) return
    const top = element.getBoundingClientRect().top
    const first = [...element.querySelectorAll<HTMLElement>('[data-message-id]')].find(row => row.getBoundingClientRect().bottom > top)
    anchor.current = first ? { id: first.dataset.messageId!, offset: first.getBoundingClientRect().top - top } : null
  }

  function reconcilePosition() {
    const element = scroll.current
    if (!element?.clientHeight) return
    if (!initialized.current || following.current) {
      element.scrollTop = element.scrollHeight
      initialized.current = true
    } else if (anchor.current) {
      const saved = anchor.current
      const row = [...element.querySelectorAll<HTMLElement>('[data-message-id]')].find(item => item.dataset.messageId === saved.id)
      if (row) element.scrollTop += row.getBoundingClientRect().top - element.getBoundingClientRect().top - saved.offset
    }
    const bottom = element.scrollHeight - element.scrollTop - element.clientHeight < 24
    setAtBottom(bottom)
    previousScrollTop.current = element.scrollTop
    historyIntent.current = false
    rememberPosition()
  }

  useLayoutEffect(() => {
    if (channelId) title.current?.focus({ preventScroll: true })
    const element = scroll.current
    if (!element) return
    const observer = new ResizeObserver(reconcilePosition)
    observer.observe(element)
    // Router scroll restoration runs after mount; a new channel still starts at latest.
    const frame = requestAnimationFrame(() => {
      following.current = true
      reconcilePosition()
      historyReady.current = true
    })
    return () => { historyReady.current = false; observer.disconnect(); cancelAnimationFrame(frame) }
  }, [])

  useLayoutEffect(() => {
    reconcilePosition()
  }, [visibleCount, evidence?.id])

  return <section className="channels-thread" aria-labelledby="channels-thread-title">
    <header className="channels-pane-header">
      {channelId && <Button className="channels-control channels-mobile-back" aria-label="Back to channels" onClick={onBack}><span aria-hidden="true">←</span> Channels</Button>}
      {channel && <ChannelKindIcon kind={channel.kind} />}
      <h2 id="channels-thread-title" ref={title} tabIndex={-1}>{channel?.name ?? (channelId ? 'Unavailable channel' : 'Messages')}</h2>
      {channel && <span className="channels-fixture">Sample messages</span>}
    </header>
    <div ref={scroll} className="channels-scroll channels-message-scroll" tabIndex={0} aria-label="Message timeline" onScroll={() => {
      const element = scroll.current!
      const upward = element.scrollTop < previousScrollTop.current
      following.current = element.scrollHeight - element.scrollTop - element.clientHeight < 24
      setAtBottom(following.current)
      rememberPosition()
      previousScrollTop.current = element.scrollTop
      if (upward) {
        historyIntent.current = true
        requestOlder()
      }
    }} onWheel={event => {
      historyIntent.current = event.deltaY < 0
      if (historyIntent.current) requestOlder()
    }} onTouchStart={event => { touchY.current = event.touches[0]?.clientY ?? null }} onTouchMove={event => {
      const y = event.touches[0]?.clientY
      if (y !== undefined && touchY.current !== null) historyIntent.current = y > touchY.current
      touchY.current = y ?? null
      if (historyIntent.current) requestOlder()
    }} onKeyDown={event => {
      if (['ArrowUp', 'PageUp', 'Home'].includes(event.key)) {
        historyIntent.current = true
        if (requestOlder()) event.preventDefault()
      }
    }}>
      <div className={`channels-message-content${messages.length ? '' : ' channels-message-content-empty'}`}>
        {!channelId ? <p className="channels-empty">Select a channel</p> : !channel ? <SectionError title="Channel unavailable" description="Choose another channel from the directory." onRetry={onBack} retryLabel="Back to channels" /> : !messages.length ? <p className="channels-empty">No messages in this channel</p> : <>
          <div className="channels-history" role="status">{hasOlder ? `${messages.length} of ${channel.messages.length}` : 'Beginning of history'}</div>
          <ol className="channels-messages" aria-label={`${channel.name} messages`}>{messages.map(message => <li key={message.id} data-message-id={message.id}><ChannelMessageRow sender={message.sender} text={message.text} arrival={message.firstArrivedAt} referenceTime={channelReferenceTime} variants={message.packets.length} observers={channelObserverCount(message)} selected={evidence?.id === message.id} evidenceId="channel-message-evidence" onSelect={trigger => {
            rememberPosition()
            following.current = false
            onInspect(message, trigger)
          }} /></li>)}</ol>
        </>}
      </div>
    </div>
    {!atBottom && messages.length > 0 && <div className="channels-latest"><Button className="channels-control" aria-label="Latest messages" onClick={() => { following.current = true; scroll.current!.scrollTop = scroll.current!.scrollHeight; setAtBottom(true); scroll.current?.focus({ preventScroll: true }); rememberPosition() }}>↓ Latest</Button></div>}
  </section>
}

export default function ChannelsPage({ channelId, onSelectChannel }: { channelId: string | null; onSelectChannel: (id: string | null) => void }) {
  const channel = channelFixtures.find(item => item.id === channelId)
  const [selection, setSelection] = useState<{ channel: string; message: string } | null>(null)
  const evidence = selection?.channel === channelId ? channel?.messages.find(message => message.id === selection.message) ?? null : null
  const evidenceTrigger = useRef<HTMLElement | null>(null)
  const channelTriggers = useRef(new Map<string, HTMLButtonElement>())
  const previousChannel = useRef(channelId)

  useLayoutEffect(() => {
    const prior = previousChannel.current
    previousChannel.current = channelId
    if (prior !== channelId) {
      setSelection(null)
      if (!channelId && prior) channelTriggers.current.get(prior)?.focus({ preventScroll: true })
    }
  }, [channelId])

  function closeEvidence() {
    setSelection(null)
  }

  return <div className="channels-workspace" data-selected={Boolean(channelId)} data-evidence={Boolean(evidence)} role="region" aria-label="Channels workspace" onKeyDown={event => {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    if (evidence) { event.stopPropagation(); closeEvidence() }
    else if (channelId && window.matchMedia('(max-width: 47.999rem)').matches) { event.stopPropagation(); onSelectChannel(null) }
  }}>
    <section className="channels-directory" aria-labelledby="channels-directory-title">
      <header className="channels-pane-header"><h1 id="channels-directory-title">Channels</h1><span className="channels-fixture" title="Synthetic fixture data. No live connection.">Fixture</span></header>
      <nav className="channels-scroll" aria-label="Channel directory"><ul className="channels-directory-list">{channelFixtures.map(item => <li key={item.id}><ChannelDirectoryItem name={item.name} kind={item.kind} selected={channelId === item.id} latestArrival={item.messages.at(-1)?.firstArrivedAt ?? null} ref={element => { if (element) channelTriggers.current.set(item.id, element); else channelTriggers.current.delete(item.id) }} onSelect={() => onSelectChannel(item.id)} /></li>)}</ul></nav>
    </section>
    <ChannelThread key={channelId ?? 'none'} channel={channel} channelId={channelId} evidence={evidence} onInspect={(message, trigger) => { evidenceTrigger.current = trigger; setSelection({ channel: channelId!, message: message.id }) }} onBack={() => onSelectChannel(null)} />
    {evidence && <MessageEvidence message={evidence} onClose={closeEvidence} returnFocus={() => evidenceTrigger.current} />}
  </div>
}
