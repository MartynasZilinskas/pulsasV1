import { useId, useState } from 'react'
import { PacketTypeBadge, type PacketTypeCode } from '../../components/packets/packet-type-badge'
import { SearchableMultiSelect } from '../../components/ui/searchable-multi-select'
import './searchable-multi-select-specimen.css'

const packetTypes: readonly { value: PacketTypeCode; label: string }[] = [
  { value: '0x04', label: 'Advert' },
  { value: '0x02', label: 'Text' },
  { value: '0x05', label: 'Group text' },
  { value: '0x03', label: 'Ack' },
  { value: '0x09', label: 'Trace' },
  { value: '0x00', label: 'Request' },
  { value: '0x01', label: 'Response' },
  { value: '0x08', label: 'Path' },
  { value: '0x0B', label: 'Control' },
]

export function SearchableMultiSelectSpecimen() {
  const heading = useId()
  const [value, setValue] = useState<readonly PacketTypeCode[]>([])
  const selectedLabels = packetTypes.filter(option => value.includes(option.value)).map(option => option.label)

  return <section className="ks-section" aria-labelledby={heading}>
    <div className="ks-section-heading">
      <h2 id={heading}>Searchable multi-select</h2>
      <p>Choose example packet types. Use Only to select just one.</p>
    </div>
    <div className="ks-searchable-multi-select-example">
      <SearchableMultiSelect label="Example packet types" searchLabel="Search example packet types" options={packetTypes} value={value} onValueChange={setValue}
        renderOptionLabel={option => <PacketTypeBadge code={option.value} passive />} />
      <p className="ks-searchable-multi-select-example-status" role="status" aria-label="Example packet type selection" aria-atomic="true">
        {selectedLabels.length ? `Selected: ${selectedLabels.join(', ')}` : 'No packet types selected.'}
      </p>
    </div>
  </section>
}
