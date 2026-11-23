import { Button } from '@ui-sdk/react/button'
import { Collapsible, CollapsiblePanel, CollapsibleTrigger } from '@ui-sdk/react/collapsible'
import { Radio, RadioGroup } from '@ui-sdk/react/radio'
import { useLayoutEffect, useRef } from 'react'
import type { MapPreviewTheme } from './map-preview-basemap-style'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  theme: MapPreviewTheme
  onThemeChange: (theme: MapPreviewTheme) => void
  clustered: boolean
  onClusterChange?: (clustered: boolean) => void
  ready: boolean
  canZoomIn: boolean
  canZoomOut: boolean
  onZoom: (direction: -1 | 1) => void
  onFit: () => void
}

export function MapWorkspaceControls({ open, onOpenChange, theme, onThemeChange, clustered, onClusterChange, ready, canZoomIn, canZoomOut, onZoom, onFit }: Props) {
  const trigger = useRef<HTMLButtonElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)

  useLayoutEffect(() => {
    if (open) heading.current?.focus({ preventScroll: true })
  }, [open])

  function close() {
    onOpenChange(false)
    trigger.current?.focus({ preventScroll: true })
  }

  return <Collapsible className="map-preview-workspace-controls" open={open} onOpenChange={onOpenChange}>
    <div className="map-preview-workspace-rail">
      <CollapsibleTrigger ref={trigger} className="map-preview-button map-preview-settings-toggle" aria-label="Map settings" title="Map settings">
        <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <path d="M4 6h4m4 0h8M4 12h10m4 0h2M4 18h4m4 0h8M8 3v6m6 0v6m-6 0v6" />
        </svg>
      </CollapsibleTrigger>
      <div className="map-preview-workspace-zoom">
        <Button className="map-preview-button map-preview-zoom-button" aria-label="Zoom in" disabled={!ready || !canZoomIn} onClick={() => onZoom(1)}>
          <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><path d="M5 12h14M12 5v14" /></svg>
        </Button>
        <Button className="map-preview-button map-preview-zoom-button" aria-label="Zoom out" disabled={!ready || !canZoomOut} onClick={() => onZoom(-1)}>
          <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><path d="M5 12h14" /></svg>
        </Button>
      </div>
      <Button className="map-preview-button" aria-label="Fit all nodes" title="Fit all nodes" disabled={!ready} onClick={onFit}>
        <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <path d="M8 4H4v4m12-4h4v4M4 16v4h4m12-4v4h-4" />
          <rect x="8" y="8" width="8" height="8" rx="1" />
        </svg>
      </Button>
    </div>
    <CollapsiblePanel className="map-preview-settings" render={<section aria-label="Map settings" />} onKeyDown={event => {
      if (event.key !== 'Escape' || event.defaultPrevented || event.nativeEvent.isComposing) return
      event.preventDefault()
      event.stopPropagation()
      close()
    }}>
      <header className="map-preview-settings-header">
        <h2 ref={heading} tabIndex={-1}>Map settings</h2>
        <Button className="map-preview-button map-preview-settings-close" aria-label="Close map settings" onClick={close}>
          <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><path d="m6 6 12 12M6 18 18 6" /></svg>
        </Button>
      </header>
      <div className="map-preview-settings-body">
        <fieldset className="map-preview-settings-group">
          <legend>Map theme</legend>
          <RadioGroup className="map-preview-settings-options" aria-label="Map theme" value={theme}
            onValueChange={value => { if (value === 'light' || value === 'dark') onThemeChange(value) }}>
            <Radio className="map-preview-button" value="light">Light</Radio>
            <Radio className="map-preview-button" value="dark">Dark</Radio>
          </RadioGroup>
        </fieldset>
        {onClusterChange && <fieldset className="map-preview-settings-group">
          <legend>Clustering</legend>
          <RadioGroup className="map-preview-settings-options" aria-label="Clustering" value={clustered ? 'on' : 'off'} disabled={!ready}
            onValueChange={value => { if (value === 'on' || value === 'off') onClusterChange(value === 'on') }}>
            <Radio className="map-preview-button" value="on">On</Radio>
            <Radio className="map-preview-button" value="off">Off</Radio>
          </RadioGroup>
        </fieldset>}
      </div>
    </CollapsiblePanel>
  </Collapsible>
}
