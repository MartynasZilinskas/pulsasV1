'use client'

import { useState } from 'react'
import { ChannelDirectoryItem } from '../../components/channels/channel-directory-item'
import { ChannelMessageRow } from '../../components/channels/channel-message-row'
import { channelFixtures, channelObserverCount, channelReferenceTime } from '../channels/channel-fixtures'
import './channel-specimens.css'

export function ChannelSpecimens() {
  const [channelId, setChannelId] = useState('field-test')
  const [messageId, setMessageId] = useState<string | null>(null)
  const channel = channelFixtures.find(item => item.id === channelId)!
  return <section className="ks-section" aria-labelledby="ks-chat-heading">
    <div className="ks-section-heading"><h2 id="ks-chat-heading">Channel browser</h2><p>Compact directory and message rows. Synthetic messages; counts distinguish packet variants from observers.</p></div>
    <div className="ks-channel-specimens">
      <div className="ks-channel-directory"><h3>Directory</h3>{channelFixtures.map(item => <ChannelDirectoryItem key={item.id} name={item.name} kind={item.kind} latestArrival={item.messages.at(-1)?.firstArrivedAt ?? null} selected={channelId === item.id} onSelect={() => { setChannelId(item.id); setMessageId(null) }} />)}</div>
      <div className="ks-channel-messages"><h3>{channel.name}</h3>{channel.messages.slice(-3).map(message => <ChannelMessageRow key={message.id} sender={message.sender} text={message.text} arrival={message.firstArrivedAt} referenceTime={channelReferenceTime} variants={message.packets.length} observers={channelObserverCount(message)} selected={message.id === messageId} onSelect={() => setMessageId(current => current === message.id ? null : message.id)} />)}{!channel.messages.length && <p className="ks-channel-empty">No messages in this channel</p>}<p className="ks-channel-selection" role="status">{messageId ? 'Message selected' : 'No message selected'}</p></div>
    </div>
    <div className="ks-channel-long-content"><h3>Long-content specimen</h3><ChannelMessageRow sender="Kaunas / portable field station with a long claimed sender name" text="Synthetic diagnostic token: 0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF" arrival="2026-11-20T13:56:00Z" referenceTime={channelReferenceTime} variants={1} observers={2} selected={messageId === 'long-content'} onSelect={() => setMessageId(current => current === 'long-content' ? null : 'long-content')} /></div>
  </section>
}
