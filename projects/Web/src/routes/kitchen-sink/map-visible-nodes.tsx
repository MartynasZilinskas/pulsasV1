import { useMemo, useState, type ReactNode } from 'react'
import type { EvidenceAnchor } from '../../components/map/map-path-evidence'
import { MapNodeList } from './map-node-list'

const formatCount = new Intl.NumberFormat('en')
const byName = new Intl.Collator('en', { numeric: true })

type Props = {
  anchors: readonly EvidenceAnchor[]
  ids: readonly string[] | null
  selectedId: string | null
  onSelect: (id: string) => void
  hideTitle?: boolean
  searchable?: boolean
  renderHeader?: (count: string | null) => ReactNode
}

export function MapVisibleNodes({ anchors, ids, selectedId, onSelect, hideTitle = false, searchable = true, renderHeader }: Props) {
  const [query, setQuery] = useState('')
  const ordered = useMemo(() => [...anchors].sort((a, b) => byName.compare(a.name, b.name)
    || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)), [anchors])
  const visible = useMemo(() => {
    if (!ids) return []
    const inView = new Set(ids)
    return ordered.filter(({ id }) => inView.has(id))
  }, [ids, ordered])
  const term = searchable ? query.trim().toLowerCase() : ''
  const matches = useMemo(() => term ? visible.filter(({ name, id }) => name.toLowerCase().includes(term)
    || id.toLowerCase().includes(term)) : visible, [term, visible])
  const count = formatCount.format(matches.length)
  const feedback = ids === null ? 'Waiting for the map view.' : visible.length === 0 ? 'No nodes in view.'
    : matches.length === 0 ? 'No matching nodes.' : null
  const status = feedback ?? (term
    ? `${count} ${matches.length === 1 ? 'match' : 'matches'} out of ${formatCount.format(visible.length)} nodes in view.`
    : `${count} ${matches.length === 1 ? 'node' : 'nodes'} in view.`)

  return <MapNodeList matches={matches} title="Visible nodes" hideTitle={hideTitle} searchLabel="Search visible nodes" listLabel="Nodes in view"
    searchable={searchable} header={renderHeader?.(ids === null ? null : formatCount.format(visible.length))}
    query={query} onQueryChange={setQuery} count={ids === null ? null : count} status={status} feedback={feedback}
    selectedId={selectedId} onSelect={onSelect} />
}
