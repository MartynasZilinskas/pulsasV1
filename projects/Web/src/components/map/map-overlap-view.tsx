import { Button } from '@ui-sdk/react/button'
import { createPopoverController, Popover, PopoverClose, PopoverContent, PopoverDescription, PopoverTitle, PopoverTrigger } from '@ui-sdk/react/popover'
import { Tooltip, TooltipContent, TooltipTrigger } from '@ui-sdk/react/tooltip'
import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'
import type { Map as MapLibre } from 'maplibre-gl'
import { NodeRoleIcon, nodeRoles } from '../nodes/node-role'
import type { MapPreviewScene } from './map-preview'
import { overlapFan, type ScreenOverlap } from './map-overlap'

type Props = {
  map: MapLibre; groups: readonly ScreenOverlap[]; markers: MapPreviewScene['markers']; selectedId: string | null
  expanded: string | null; onExpandedChange: (id: string | null) => void
  openRef: RefObject<((id: string) => void) | null>; theme: 'light' | 'dark'; onPick: (id: string) => void
}

export function MapOverlap({ map, groups, markers, selectedId, expanded, onExpandedChange, openRef, theme, onPick }: Props) {
  const [controller] = useState(() => createPopoverController<string>())
  const content = useRef<HTMLDivElement>(null)
  const [spread, setSpread] = useState(1)
  const group = groups.find(group => group.id === expanded)
  const { clientWidth: width, clientHeight: height } = map.getCanvas()
  const fan = group ? overlapFan(group, width, height) : null
  useLayoutEffect(() => {
    if (!expanded) return
    const motion = matchMedia('(prefers-reduced-motion: reduce)')
    let frame = 0
    const finish = () => { cancelAnimationFrame(frame); setSpread(1) }
    if (motion.matches) { finish(); return }
    setSpread(0)
    let start: number | undefined
    const animate = (time: number) => {
      start ??= time
      const progress = Math.min(1, (time - start) / 240)
      setSpread(1 - (1 - progress) ** 3)
      if (progress < 1) frame = requestAnimationFrame(animate)
    }
    frame = requestAnimationFrame(animate)
    motion.addEventListener('change', finish)
    return () => { cancelAnimationFrame(frame); motion.removeEventListener('change', finish) }
  }, [expanded, width, height])
  const displayedFan = fan?.map(node => ({ ...node,
    x: node.anchorX + (node.x - node.anchorX) * spread,
    y: node.anchorY + (node.y - node.anchorY) * spread,
  }))
  useEffect(() => {
    openRef.current = id => { onExpandedChange(id); void controller.open({ payload: id }) }
    return () => { openRef.current = null }
  }, [controller, onExpandedChange, openRef])
  useLayoutEffect(() => {
    if (!content.current || !fan) return
    const fitNames = () => {
      const bounds = content.current!.getBoundingClientRect()
      const faces = [...content.current!.querySelectorAll('.map-overlap-face')].map(el => el.getBoundingClientRect())
      const accepted: DOMRect[] = []
      for (const label of content.current!.querySelectorAll<HTMLElement>('.map-overlap-name')) {
        const box = label.getBoundingClientRect()
        const fits = box.left >= bounds.left + 2 && box.right <= bounds.right - 2 && box.top >= bounds.top && box.bottom <= bounds.bottom - 2
        const collides = [...faces, ...accepted].some(other => box.left < other.right + 3 && box.right > other.left - 3 && box.top < other.bottom + 3 && box.bottom > other.top - 3)
        label.style.visibility = spread === 1 && fits && !collides ? 'visible' : 'hidden'
        if (fits && !collides) accepted.push(box)
      }
    }
    fitNames()
    const observer = new ResizeObserver(fitNames)
    observer.observe(content.current)
    for (const label of content.current.querySelectorAll('.map-overlap-name')) observer.observe(label)
    return () => observer.disconnect()
  }, [fan, markers, spread])
  const choose = (id: string) => { controller.close(); onPick(id) }
  const choice = (id: string, compact: boolean) => {
    const appearances = markers.filter(marker => marker.id === id)
    const marker = appearances[0]
    if (!marker) return null
    const role = marker.role ?? 'NONE'
    const evidence = appearances.filter(marker => marker.confidence)
    const description = evidence.map(marker => `Hop ${marker.label} · ${marker.confidence}${marker.ambiguous ? ' · multiple candidates' : ''}`).join('; ')
    const button = <Button key={id} className={compact ? 'map-overlap-node' : 'map-overlap-choice'} data-node-id={id}
      data-role={role} data-tone={nodeRoles[role].tone} data-evidence={evidence.length ? 'true' : undefined}
      data-confidence={evidence.some(marker => marker.confidence === 'weak') ? 'weak' : 'strong'}
      aria-label={`${marker.name ?? marker.label} · ${nodeRoles[role].label}${description ? ` · ${description}` : ''}`}
      aria-pressed={id === selectedId} onClick={() => choose(id)}>
      <span className="map-overlap-face" aria-hidden="true"><NodeRoleIcon role={role} /></span>
      {compact ? <>
        {evidence.length > 0 && <span className="map-overlap-hop" aria-hidden="true">{[...new Set(evidence.map(marker => marker.label))].join(',')}</span>}
        {appearances.some(marker => marker.ambiguous) && <span className="map-overlap-ambiguity" aria-hidden="true">?</span>}
        <span className="map-overlap-name">{marker.name ?? marker.label}</span>
      </> : <span><strong>{marker.name ?? marker.label}</strong><small>{nodeRoles[role].label}{description && ` · ${description}`}</small></span>}
    </Button>
    return compact ? <Tooltip key={id} delay={150}>
      <TooltipTrigger render={button} />
      <TooltipContent className="map-overlap-tooltip" side="bottom" sideOffset={4} collisionPadding={8}>{marker.name ?? marker.label}</TooltipContent>
    </Tooltip> : button
  }
  return <Popover controller={controller} modal={false} open={Boolean(group)} onOpenChange={(open) => { if (!open) onExpandedChange(null) }}>
    <div className="map-overlap-stacks">
      {groups.map(group => {
        const selected = group.nodes.find(node => node.id === selectedId)
        return <PopoverTrigger key={group.id} payload={group.id} className="map-overlap-stack"
        style={{ left: selected ? Math.min(width - 22, Math.max(22, selected.x + 28)) : Math.min(width - 22, Math.max(22, group.x)), top: selected ? Math.max(22, selected.y - 24) : Math.max(22, Math.min(height - 22, group.y)), visibility: expanded === group.id ? 'hidden' : undefined }} aria-label={`Choose ${group.nodes.length} overlapping nodes`}
        onClick={() => onExpandedChange(group.id)}>
        <span aria-hidden="true">{selected ? `+${group.nodes.length - 1}` : group.nodes.length}</span>
      </PopoverTrigger>})}
    </div>
    <PopoverContent ref={content} inline className="map-overlap-content" data-map-theme={theme} data-overlap-layout={fan ? 'fan' : 'choices'} data-spreading={fan && spread < 1 ? 'true' : undefined}
      initialFocus="[data-node-id]" finalFocus={() => map.getCanvas()}
      onPointerDown={event => { if (event.target === event.currentTarget) controller.close() }}>
      <PopoverTitle className="map-overlap-sr-only">{group?.nodes.length} overlapping nodes</PopoverTitle>
      <PopoverDescription className="map-overlap-sr-only">{fan ? 'Display offsets only · lines lead to advertised positions' : 'Choose a node · advertised positions are unchanged'}</PopoverDescription>
      <PopoverClose className="map-overlap-close" aria-label="Collapse overlapping nodes">×</PopoverClose>
      {displayedFan ? <>
        <svg className="map-overlap-connectors" aria-hidden="true" width={width} height={height}>
          {displayedFan.map(node => <g key={node.id}><line x1={node.anchorX} y1={node.anchorY} x2={node.x} y2={node.y} /><circle cx={node.anchorX} cy={node.anchorY} r="2" /></g>)}
        </svg>
        {displayedFan.map(node => <div key={node.id} className="map-overlap-position" style={{ left: node.x, top: node.y }}>
          {choice(node.id, true)}
        </div>)}
      </> : <div className="map-overlap-choices">{group?.nodes.map(node => choice(node.id, false))}</div>}
    </PopoverContent>
  </Popover>
}
