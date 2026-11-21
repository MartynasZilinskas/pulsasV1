import { Button } from '@ui-sdk/react/button'
import { Checkbox, CheckboxIndicator } from '@ui-sdk/react/checkbox'
import { Input } from '@ui-sdk/react/input'
import { useRef, useState, type ReactNode } from 'react'
import { LucideGlyph } from './lucide-glyph'
import './searchable-multi-select.css'

export type SearchableMultiSelectOption<T extends string = string> = { value: T; label: string }

export function SearchableMultiSelect<T extends string>({ label, searchLabel, options, value, onValueChange, renderOptionLabel }: {
  label: string
  searchLabel: string
  options: readonly SearchableMultiSelectOption<T>[]
  value: readonly T[]
  onValueChange: (value: readonly T[]) => void
  // Render non-interactive content; the option's label still names its checkbox.
  renderOptionLabel?: (option: SearchableMultiSelectOption<T>) => ReactNode
}) {
  const [search, setSearch] = useState('')
  const list = useRef<HTMLDivElement>(null)
  const query = search.trim().toLowerCase()
  const visibleOptions = options.filter(option => option.label.toLowerCase().includes(query))

  return <fieldset className="ks-searchable-multi-select">
    <legend className="ks-searchable-multi-select-legend">{label}</legend>
    <Input type="search" className="ks-searchable-multi-select-input" placeholder="Search…" aria-label={searchLabel}
      autoComplete="off" spellCheck={false} value={search} onValueChange={next => {
        setSearch(next)
        if (list.current) list.current.scrollTop = 0
      }} />
    <div className="ks-searchable-multi-select-scroll" ref={list}>
      {visibleOptions.map(option => {
        const checked = value.includes(option.value)
        return <div className="ks-searchable-multi-select-row" key={option.value}>
          <label className="ks-searchable-multi-select-choice">
            <Checkbox className="ks-searchable-multi-select-check" aria-label={option.label} checked={checked} onCheckedChange={next => {
              if ((next === true) === checked) return
              onValueChange(next === true ? [...value, option.value] : value.filter(item => item !== option.value))
            }}>
              <CheckboxIndicator aria-hidden="true"><LucideGlyph name="check" /></CheckboxIndicator>
            </Checkbox>
            <span className="ks-searchable-multi-select-option" aria-hidden="true">{renderOptionLabel ? renderOptionLabel(option) : option.label}</span>
          </label>
          <Button type="button" className="ks-searchable-multi-select-only" aria-label={`Only ${option.label}`} onClick={() => {
            if (!checked || value.length !== 1) onValueChange([option.value])
          }}>Only</Button>
        </div>
      })}
      {visibleOptions.length === 0 && <p className="ks-searchable-multi-select-empty" role="status">
        {options.length ? 'No matching options.' : 'No options available.'}
      </p>}
    </div>
  </fieldset>
}
