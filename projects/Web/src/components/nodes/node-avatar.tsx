import { Avatar, AvatarFallback } from '@ui-sdk/react/avatar'
import { NodeModifierIcon, NodeRoleIcon, nodeRoles, type NodeIdentity } from './node-role'

/** Decorative identity mark; pair with a visible or accessible text label. */
export function NodeAvatar({ role, observer, warning, size = 'contact' }: NodeIdentity & { size?: 'marker' | 'contact' | 'display' }) {
  return (
    <Avatar className="ks-avatar" data-tone={nodeRoles[role].tone} data-size={size} aria-hidden="true">
      <AvatarFallback className="ks-avatar-face"><NodeRoleIcon role={role} /></AvatarFallback>
      {observer && <span className="ks-avatar-mark ks-avatar-observer" data-tone="violet"><NodeModifierIcon modifier="observer" /></span>}
      {warning && <span className="ks-avatar-mark ks-avatar-warning" data-tone="amber"><NodeModifierIcon modifier="warning" /></span>}
    </Avatar>
  )
}
