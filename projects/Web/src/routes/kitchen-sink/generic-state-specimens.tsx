import { Button } from '@ui-sdk/react/button'
import { useEffect, useRef, useState } from 'react'
import { PageFailure, SectionError, StateRegion } from '../../components/ui/generic-states'
import './generic-state-specimens.css'

export function GenericStateSpecimens() {
  const [state, setState] = useState<'ready' | 'loading' | 'error'>('ready')
  const [failure, setFailure] = useState<'section' | 'page'>('section')
  const [delayMs, setDelayMs] = useState(300)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  function transition(next: typeof state, delay = delayMs, duration?: number) {
    window.clearTimeout(timer.current)
    setDelayMs(delay)
    setState(next)
    if (duration !== undefined) timer.current = window.setTimeout(() => setState('ready'), duration)
  }

  return (
    <section className="ks-section ks-state-specimens" aria-labelledby="ks-states-heading">
      <header className="ks-section-heading">
        <h2 id="ks-states-heading">Loading &amp; failures</h2>
        <p>Fixture controls only. Replace a section or the whole page content without moving its neighbours. Retry restores this local example; it makes no network request.</p>
      </header>
      <div className="ks-state-controls" aria-label="State fixture controls">
        <Button className="ks-state-control" onClick={() => transition('loading', 300)}>Load · 300ms delay</Button>
        <Button className="ks-state-control" onClick={() => transition('loading', 700)}>Load · 700ms delay</Button>
        <Button className="ks-state-control" onClick={() => transition('loading', 0)}>Load immediately</Button>
        <Button className="ks-state-control" onClick={() => transition('loading', 300, 100)}>Quick load · 100ms</Button>
        <Button className="ks-state-control" onClick={() => { setFailure('section'); transition('error') }}>Show section error</Button>
        <Button className="ks-state-control" onClick={() => { setFailure('page'); transition('error') }}>Show page failure</Button>
        <Button className="ks-state-control" onClick={() => transition('ready')}>Restore content</Button>
      </div>
      <div className="ks-state-readout"><span aria-hidden="true">State: section failure · Indicator delay: 700ms</span><p>State: <strong>{state === 'error' ? `${failure} failure` : state}</strong> · Indicator delay: {delayMs}ms</p></div>
      <div className="ks-state-workspace">
        <div className="ks-state-shell-bar" data-state-neighbour="above">Example page header · stays in place</div>
        <StateRegion state={failure === 'page' && state === 'error' ? 'error' : 'ready'} label="Example page content" className="ks-state-page-region" error={
          <PageFailure title="Example page couldn’t load" description="Only this page content is replaced. The example header and footer stay available." onRetry={() => transition('ready')} retryLabel="Retry example page" />
        }>
          <div className="ks-state-page-content">
            <header><h3>Packet summary</h3><p>Local fixture · no live reception</p></header>
            <StateRegion state={failure === 'page' && state === 'error' ? 'ready' : state} delayMs={delayMs} label="Loading example summary" error={
              <SectionError title="Summary couldn’t load" description="The page header, context, and footer are still available." onRetry={() => transition('loading', 300, 900)} retryLabel="Retry example section" />
            }>
              <div className="ks-state-summary">
                <h4>Example reception</h4>
                <p>The section’s real content sets its height. This longer description wraps naturally at narrow widths and with enlarged text.</p>
                <dl><div><dt>Region</dt><dd>Kaunas</dd></div><div><dt>Payload</dt><dd>Advert</dd></div></dl>
                <Button className="ks-state-control" onClick={() => transition('loading', 300, 900)}>Refresh example summary</Button>
              </div>
            </StateRegion>
            <p className="ks-state-context" data-state-neighbour="context">Adjacent context stays available during a section failure.</p>
          </div>
        </StateRegion>
        <div className="ks-state-shell-bar" data-state-neighbour="below">Example page footer · stays in place</div>
      </div>
      <details className="ks-state-usage">
        <summary>Usage &amp; size contract</summary>
        <p><code>StateRegion</code> keeps its supplied children mounted, hidden and inert while loading or failed. Keep those children supplied across transitions to retain their responsive footprint. Don’t put portals or independently active background work in retained content.</p>
        <p>Before content exists, reserve the region in the host layout. Standalone <code>LoadingState</code> and <code>PageFailure</code> fill a definite-height host; use a grid or flex content cell shared with the eventual page. Smaller content regions should reserve their own expected footprint. Error copy scrolls within that space if necessary.</p>
        <pre><code>{`<StateRegion state={state} label="Loading summary"\n  error={<SectionError onRetry={reloadSummary} />}>
  <Summary data={lastResult} />
</StateRegion>

<Suspense fallback={<LoadingState label="Loading page" />}>
  <Page />
</Suspense>`}</code></pre>
        <p><code>delayMs</code> defaults to 300; set 0 for immediate feedback. Unmount loading between requests to start a fresh delay. Retry appears only when you supply a real callback; no component assumes recovery.</p>
      </details>
    </section>
  )
}
