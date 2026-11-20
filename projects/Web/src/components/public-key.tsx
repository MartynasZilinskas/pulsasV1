import './ui/theme.css'

export function PublicKey({ value }: { value: string }) {
  const compact = value.length > 12
  return <code className="ks-public-key" title={compact ? value : undefined}>{compact ? `${value.slice(0, 6)}..${value.slice(-6)}` : value}</code>
}
