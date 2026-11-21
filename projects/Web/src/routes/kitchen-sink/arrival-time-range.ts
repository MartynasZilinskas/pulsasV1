const localInput = /^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}(?::\d{2})?$/
const day = 86_400_000

function wallClock(timeZone: string) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone, calendar: 'iso8601', numberingSystem: 'latn', hourCycle: 'h23',
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  })
}

function inputValue(instant: number, clock: Intl.DateTimeFormat) {
  const parts = Object.fromEntries(clock.formatToParts(instant).map(({ type, value }) => [type, value]))
  return `${parts.year!.padStart(4, '0')}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}`
}

export function formatArrivalTime(instant: string, timeZone: string): string {
  return inputValue(Date.parse(instant), wallClock(timeZone))
}

export function formatArrivalDateTime(instant: string, timeZone: string): string {
  const local = formatArrivalTime(instant, timeZone).replace('T', ' ')
  return local.endsWith(':00') ? local.slice(0, -3) : local
}

export function withArrivalDate(value: string, date: string, fallbackTime: string): string {
  const draft = value.trim()
  const time = /^\d{4}-\d{1,2}-\d{1,2}[ T]+(.*)$/.exec(draft)?.[1] ?? (/^\d{0,2}:/.test(draft) ? draft : '')
  return `${date} ${time || fallbackTime}`
}

export function parseArrivalTime(value: string, timeZone: string): { instant: string } | { error: 'invalid' | 'nonexistent' | 'ambiguous' } {
  const local = value.trim()
  if (!localInput.test(local)) return { error: 'invalid' }
  const iso = local.replace(' ', 'T')
  const normalized = iso.length === 16 ? `${iso}:00` : iso
  const wall = Date.parse(`${normalized}Z`)
  if (!Number.isFinite(wall) || new Date(wall).toISOString().slice(0, 19) !== normalized) return { error: 'invalid' }

  const clock = wallClock(timeZone)
  // Try offsets on both sides of a clock change; only exact round trips are valid.
  const offsets = new Set([wall - day, wall, wall + day].map((sample) => Date.parse(`${inputValue(sample, clock)}Z`) - sample))
  const matches = [...offsets].map((offset) => wall - offset).filter((instant) => inputValue(instant, clock) === normalized)
  if (matches.length === 0) return { error: 'nonexistent' }
  if (matches.length > 1) return { error: 'ambiguous' }
  return { instant: new Date(matches[0]!).toISOString() }
}
