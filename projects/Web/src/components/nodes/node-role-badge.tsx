import { NodeModifierIcon, NodeRoleIcon, nodeRoles, type NodeIdentity } from './node-role'
import '../ui/badge.css'

export function NodeRoleBadge({ role, observer, warning }: NodeIdentity) {
  const { label, tone } = nodeRoles[role]
  return (
    <span className="ks-badge ks-role-badge" data-tone={tone}>
      <NodeRoleIcon role={role} />
      <span>{label}</span>
      {(observer || warning) && (
        <span className="ks-badge-modifiers">
          <span className="ks-visually-hidden">{observer && ' · Observer'}{warning && ' · Warning'}</span>
          {observer && <span data-tone="violet"><NodeModifierIcon modifier="observer" /></span>}
          {warning && <span data-tone="amber"><NodeModifierIcon modifier="warning" /></span>}
        </span>
      )}
    </span>
  )
}
