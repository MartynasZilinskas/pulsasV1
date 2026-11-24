'use client'

import { createFocusRecovery } from '@ui-sdk/core'
import { Button } from '@ui-sdk/react/button'
import { Progress, ProgressIndicator, ProgressLabel, ProgressTrack } from '@ui-sdk/react/progress'
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import './theme.css'
import './generic-states.css'

type LoadingStateProps = {
  delayMs?: number
  label?: string
  className?: string
}

function LoadingIndicator({ delayMs = 300, label = 'Loading…' }: LoadingStateProps) {
  const [elapsed, setElapsed] = useState(false)
  const visible = delayMs <= 0 || elapsed

  useEffect(() => {
    if (delayMs <= 0) return
    const timer = window.setTimeout(() => setElapsed(true), delayMs)
    return () => window.clearTimeout(timer)
  }, [delayMs])

  return (
    <>
      <span className="ks-visually-hidden" role="status" aria-label={visible ? label : undefined} aria-atomic="true">{visible ? label : ''}</span>
      <div className="ui-state-message" aria-busy="true">
        {visible && <Progress value={null} className="ui-state-progress">
          <ProgressLabel>{label}</ProgressLabel>
          <ProgressTrack className="ui-state-track"><ProgressIndicator className="ui-state-indicator" /></ProgressTrack>
        </Progress>}
      </div>
    </>
  )
}

export function LoadingState({ delayMs = 300, label = 'Loading…', className = '' }: LoadingStateProps) {
  return <div className={`ui-state ui-loading-state ${className}`}><LoadingIndicator key={delayMs} delayMs={delayMs} label={label} /></div>
}

type ErrorStateProps = {
  title?: string
  description?: string
  onRetry?: () => void
  retryLabel?: string
  className?: string
}

export function SectionError({ title = 'This section couldn’t load', description = 'The rest of the page is still available.', onRetry, retryLabel = 'Try again', className = '' }: ErrorStateProps) {
  return (
    <div className={`ui-state ui-section-error ${className}`}>
      <div className="ui-state-message">
        <div role="alert" aria-atomic="true">
          <h2 className="ui-state-title">{title}</h2>
          <p className="ui-state-description">{description}</p>
        </div>
        {onRetry && <Button className="ui-state-retry" onClick={onRetry}>{retryLabel}</Button>}
      </div>
    </div>
  )
}

export function PageFailure({ title = 'This page couldn’t load', description = 'Navigation is still available. You can try loading this page again.', ...props }: ErrorStateProps) {
  return <SectionError {...props} title={title} description={description} className={`ui-page-failure ${props.className ?? ''}`} />
}

type StateRegionProps = LoadingStateProps & {
  state: 'ready' | 'loading' | 'error'
  children?: ReactNode
  error?: ReactNode
}

export function StateRegion({ state, children, error, delayMs, label, className = '' }: StateRegionProps) {
  const root = useRef<HTMLDivElement>(null)
  const content = useRef<HTMLDivElement>(null)
  const layer = useRef<HTMLDivElement>(null)
  const [recovery] = useState(() => createFocusRecovery({ fallback: () => root.current }))
  const inactive = state !== 'ready'

  useLayoutEffect(() => {
    const element = state === 'ready' ? content.current : layer.current
    if (element) return recovery.bind(element, { key: state, props: {} })
  }, [recovery, state])

  return (
    <div ref={root} className={`ui-state-region ${className}`} data-state={state} aria-label={label} tabIndex={-1}>
      <div ref={content} className="ui-state-content" aria-busy={state === 'loading'} inert={inactive} aria-hidden={inactive || undefined} data-agent-hidden={inactive ? '' : undefined}>
        {children}
      </div>
      {inactive && <div ref={layer} className="ui-state-layer">
        {state === 'loading' ? <LoadingState delayMs={delayMs} label={label} /> : error ?? <SectionError />}
      </div>}
    </div>
  )
}
