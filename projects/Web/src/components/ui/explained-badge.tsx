import { Tooltip, TooltipContent, TooltipTrigger } from '@ui-sdk/react/tooltip'
import type { ComponentProps } from 'react'
import './badge.css'

export function ExplainedBadge({ explanation, className, ...props }: ComponentProps<typeof TooltipTrigger> & { explanation: string; className?: string }) {
  return (
    <Tooltip type="description" toggleable>
      <TooltipTrigger className={`ks-badge ks-explained-badge${className ? ` ${className}` : ''}`} {...props} />
      <TooltipContent className="ks-badge-tooltip" collisionPadding={8}>{explanation}</TooltipContent>
    </Tooltip>
  )
}
