import assert from 'node:assert/strict'
import test from 'node:test'
import { formatArrivalDateTime, formatArrivalTime, parseArrivalTime, withArrivalDate } from '../src/routes/kitchen-sink/arrival-time-range.ts'

const timeZone = 'Europe/Vilnius'

test('arrival fields round-trip Vilnius wall time across seasons and midnight', () => {
  for (const [instant, local] of [
    ['2026-11-19T14:15:00.000Z', '2026-11-19T16:15:00'],
    ['2026-11-19T22:30:42.000Z', '2026-11-20T00:30:42'],
    ['2026-07-19T13:15:41.000Z', '2026-07-19T16:15:41'],
  ]) {
    assert.equal(formatArrivalTime(instant, timeZone), local)
    assert.deepEqual(parseArrivalTime(local, timeZone), { instant })
  }
  assert.deepEqual(parseArrivalTime('2026-11-19T16:15', timeZone), { instant: '2026-11-19T14:15:00.000Z' })
})

test('arrival fields reject invalid and ambiguous wall times rather than shifting the range', () => {
  assert.deepEqual(parseArrivalTime('2026-02-30T16:15', timeZone), { error: 'invalid' })
  assert.deepEqual(parseArrivalTime('2026-03-29T03:30', timeZone), { error: 'nonexistent' })
  assert.deepEqual(parseArrivalTime('2026-10-25T03:30', timeZone), { error: 'ambiguous' })
})

test('the compact editor uses year-first 24-hour values and shows seconds only when needed', () => {
  assert.equal(formatArrivalDateTime('2026-11-19T19:45:00.000Z', timeZone), '2026-11-19 21:45')
  assert.equal(formatArrivalDateTime('2026-11-19T22:30:42.000Z', timeZone), '2026-11-20 00:30:42')
  assert.deepEqual(parseArrivalTime(' 2026-11-20 00:30:42 ', timeZone), { instant: '2026-11-19T22:30:42.000Z' })
  assert.deepEqual(parseArrivalTime('11/19/2026 9:45 PM', timeZone), { error: 'invalid' })
})

test('picking a calendar day keeps the existing time, including seconds and partial input', () => {
  for (const [draft, expected] of [
    ['2026-11-19 16:14:43', '2026-11-20 16:14:43'],
    ['2026-11-19T00:15', '2026-11-20 00:15'],
    ['2026-11-19 16:', '2026-11-20 16:'],
    ['16:4', '2026-11-20 16:4'],
  ]) assert.equal(withArrivalDate(draft, '2026-11-20', '00:00'), expected)
})

test('picking a calendar day fills a missing time with the supplied bound default', () => {
  assert.equal(withArrivalDate('', '2026-11-20', '00:00'), '2026-11-20 00:00')
  assert.equal(withArrivalDate('2026-11-19', '2026-11-20', '23:59:59'), '2026-11-20 23:59:59')
})
