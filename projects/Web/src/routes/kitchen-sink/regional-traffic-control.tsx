import { Toggle } from '@ui-sdk/react/toggle'
import { useId } from 'react'

type Props = {
  ready: boolean
  running: boolean
  reduced: boolean
  onToggle: () => void
}

export function RegionalTrafficControl({ ready, running, reduced, onToggle }: Props) {
  const motionHint = useId()

  return (
    <div className="ks-map-traffic-control">
      <Toggle className="map-preview-button" pressed={running} disabled={!ready} onPressedChange={onToggle}
        aria-describedby={reduced ? motionHint : undefined}>
        Simulated traffic
      </Toggle>
      {reduced && <span id={motionHint}>Routes shown without animation.</span>}
    </div>
  )
}
