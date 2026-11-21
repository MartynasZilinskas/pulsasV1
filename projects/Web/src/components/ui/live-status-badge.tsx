import { useEffect, useState } from 'react'
import './badge.css'
import './live-status-badge.css'

type LiveStatusBadgeProps = { paused?: boolean } & (
  | { state: 'live'; staleSince?: never }
  | { state: 'stale'; staleSince: number }
)

const second = { name: 'second', suffix: 's', seconds: 1 } as const
const largerUnits = [
  { name: 'day', suffix: 'd', seconds: 86_400 },
  { name: 'hour', suffix: 'h', seconds: 3_600 },
  { name: 'minute', suffix: 'm', seconds: 60 },
] as const

function readElapsedDuration(staleSince: number) {
  const elapsedSeconds = Math.max(0, Math.floor((Date.now() - staleSince) / 1_000))
  const unit = largerUnits.find(({ seconds }) => elapsedSeconds >= seconds) ?? second
  return { count: Math.floor(elapsedSeconds / unit.seconds), unit }
}

function StaleElapsedTime({ staleSince, paused }: { staleSince: number; paused?: boolean }) {
  const [{ count, unit }, setDuration] = useState(() => readElapsedDuration(staleSince))

  useEffect(() => {
    if (paused) return

    function refresh() {
      const next = readElapsedDuration(staleSince)
      setDuration((current) => current.count === next.count && current.unit === next.unit ? current : next)
    }

    refresh()
    const interval = window.setInterval(refresh, 1_000)
    return () => window.clearInterval(interval)
  }, [staleSince, paused])

  const dateTime = `${unit.suffix === 'd' ? 'P' : 'PT'}${count}${unit.suffix.toUpperCase()}`

  return (
    <time className="ks-live-status-time" role="timer" aria-live="off" dateTime={dateTime} aria-label={`for ${count} ${unit.name}${count === 1 ? '' : 's'}`}>
      {count}{unit.suffix}
    </time>
  )
}

export function LiveStatusBadge(props: LiveStatusBadgeProps) {
  const isLive = props.state === 'live'

  return (
    <span className="ks-badge ks-live-status-badge" data-state={props.state} data-tone={isLive ? 'green' : 'orange'} data-paused={props.paused ? '' : undefined}>
      {isLive ? (
        <><span className="ks-live-status-dot" aria-hidden="true" />Live</>
      ) : (
        <>Stale {Number.isFinite(props.staleSince) && <StaleElapsedTime key={props.staleSince} staleSince={props.staleSince} paused={props.paused} />}</>
      )}
    </span>
  )
}
