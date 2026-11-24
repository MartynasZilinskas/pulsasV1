import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
const url = `${origin}/kitchen-sink`
let browser

before(async () => { browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' }) })
after(async () => { await browser?.close() })

async function withStates(options, verify) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce', ...options })
  const errors = []
  const blocked = []
  page.on('pageerror', error => errors.push(error.message))
  await page.routeWebSocket('**/*', socket => socket.close())
  await page.route('**/*', route => {
    const target = new URL(route.request().url())
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)) {
      blocked.push(target.href)
      return route.abort()
    }
    return route.continue()
  })
  await page.route(url, async route => {
    const response = await route.fetch()
    const html = await response.text()
    const body = html.replace(/src="\/src\/main\.tsx(?:\?[^"]*)?"/, 'src="/tests/kitchen-sink-agent.mjs"')
    assert.notEqual(body, html)
    await route.fulfill({ response, body })
  })
  await page.route('**/src/routes/kitchen-sink/map-specimens.tsx*', route => route.fulfill({ contentType: 'text/javascript', body: 'export default function MapSpecimens() { return null }' }))
  try {
    await page.goto(url)
    const section = page.getByRole('region', { name: 'Loading & failures', exact: true })
    await section.waitFor()
    await section.scrollIntoViewIfNeeded()
    await page.clock.install()
    await page.clock.pauseAt(new Date(Date.now() + 100))
    await verify(page, section)
    assert.deepEqual(errors, [])
    assert.deepEqual(blocked, [])
  } finally { await page.close() }
}

async function geometry(section) {
  return section.evaluate(node => Object.fromEntries([
    ['workspace', node.querySelector('.ks-state-workspace')],
    ['page', node.querySelector('.ks-state-page-region')],
    ...[...node.querySelectorAll('[data-state-neighbour]')].map(element => [element.dataset.stateNeighbour, element]),
  ].map(([name, element]) => {
    const box = element.getBoundingClientRect()
    return [name, { x: box.x, y: box.y + scrollY, width: box.width, height: box.height }]
  })))
}

const press = (section, name) => section.getByRole('button', { name, exact: true }).evaluate(node => node.click())

test('default delay, recovery and both failure scopes retain content-derived geometry', async () => {
  await withStates({}, async (page, section) => {
    const before = await geometry(section)
    await press(section, 'Load · 300ms delay')
    assert.equal(await section.getByRole('progressbar').count(), 0)
    assert.equal(await section.getByRole('button', { name: 'Refresh example summary' }).count(), 0)
    assert.equal(await section.locator('.ks-state-summary').evaluate(node => node.parentElement.inert), true)
    assert.equal(await section.getByRole('status').innerText(), '')
    assert.deepEqual(await geometry(section), before)
    await page.clock.runFor(299)
    assert.equal(await section.getByRole('progressbar').count(), 0)
    await page.clock.runFor(1)
    assert.equal(await section.getByRole('progressbar', { name: 'Loading example summary' }).count(), 1)
    assert.equal(await section.getByRole('status').innerText(), 'Loading example summary')
    assert.deepEqual(await geometry(section), before)
    await press(section, 'Show section error')
    assert.equal(await section.getByRole('alert').count(), 1)
    assert.ok(await section.getByText('Adjacent context stays available during a section failure.').isVisible())
    assert.deepEqual(await geometry(section), before)
    await section.getByRole('button', { name: 'Retry example section' }).focus()
    await page.keyboard.press('Enter')
    assert.ok(await section.locator('.ks-state-page-content > .ui-state-region').evaluate(node => node === document.activeElement))
    await page.clock.runFor(300)
    assert.equal(await section.getByRole('progressbar').count(), 1)
    await page.clock.runFor(600)
    assert.equal(await section.getByRole('progressbar').count(), 0)
    assert.ok(await section.getByRole('button', { name: 'Refresh example summary' }).isVisible())
    assert.deepEqual(await geometry(section), before)
    await press(section, 'Show page failure')
    assert.ok(await section.getByRole('heading', { name: 'Example page couldn’t load' }).isVisible())
    assert.equal(await section.getByText('Adjacent context stays available during a section failure.').count(), 1)
    assert.equal(await section.getByText('Adjacent context stays available during a section failure.').isVisible(), false)
    assert.deepEqual(await geometry(section), before)
    await section.getByRole('button', { name: 'Retry example page' }).press('Space')
    assert.deepEqual(await geometry(section), before)
    assert.ok(await section.locator('.ks-state-page-region').evaluate(node => node === document.activeElement))
  })
})

test('quick completion cancels loading, new cycles reset, override changes restart, and zero is immediate', async () => {
  await withStates({}, async (page, section) => {
    const before = await geometry(section)
    await press(section, 'Quick load · 100ms')
    await page.clock.runFor(99)
    assert.equal(await section.getByRole('progressbar').count(), 0)
    await page.clock.runFor(1)
    assert.equal(await section.getByRole('status').count(), 0)
    await page.clock.runFor(400)
    assert.equal(await section.getByRole('progressbar').count(), 0)
    await press(section, 'Load · 300ms delay')
    await page.clock.runFor(300)
    assert.equal(await section.getByRole('progressbar').count(), 1)
    await press(section, 'Restore content')
    await press(section, 'Load · 300ms delay')
    await page.clock.runFor(299)
    assert.equal(await section.getByRole('progressbar').count(), 0)
    await press(section, 'Load · 700ms delay')
    await page.clock.runFor(699)
    assert.equal(await section.getByRole('progressbar').count(), 0)
    await page.clock.runFor(1)
    assert.equal(await section.getByRole('progressbar').count(), 1)
    await press(section, 'Load · 300ms delay')
    assert.equal(await section.getByRole('progressbar').count(), 0)
    await press(section, 'Load immediately')
    assert.equal(await section.getByRole('progressbar').count(), 1)
    assert.deepEqual(await geometry(section), before)
    await press(section, 'Restore content')
    await page.clock.runFor(1000)
    assert.equal(await section.getByRole('status').count(), 0)
  })
})

test('SDK agents discover fixture controls and retry while stale controls stay unavailable', async () => {
  await withStates({}, async (page, section) => {
    await page.clock.resume()
    async function act(ref) {
      const result = await page.evaluate(async ref => {
        const { agent } = await import('/tests/kitchen-sink-agent.mjs')
        return agent.act({ steps: [{ ref, action: 'press' }] })
      }, ref)
      assert.equal(result.ok, true, result.error?.message)
    }
    const names = ['Load · 300ms delay', 'Load · 700ms delay', 'Load immediately', 'Quick load · 100ms', 'Show section error', 'Show page failure', 'Restore content', 'Refresh example summary']
    const snapshot = await page.evaluate(async () => (await import('/tests/kitchen-sink-agent.mjs')).agent.snapshot())
    for (const name of names) assert.ok(snapshot.some(entry => entry.label === name), `Missing ${name}`)
    await act('Refresh example summary')
    await section.getByRole('button', { name: 'Refresh example summary' }).waitFor()
    for (const name of names.slice(0, 4)) { await act(name); await act('Restore content') }
    await act('Show section error')
    await act('Retry example section')
    await section.getByRole('button', { name: 'Refresh example summary' }).waitFor()
    await act('Show page failure')
    const unavailable = await page.evaluate(async () => (await import('/tests/kitchen-sink-agent.mjs')).agent.act({ steps: [{ ref: 'Refresh example summary', action: 'press' }] }))
    assert.equal(unavailable.ok, false)
    await act('Retry example page')
    assert.ok(await section.getByRole('button', { name: 'Refresh example summary' }).isVisible())
  })
})

test('320px enlarged text and both themes keep states readable, scrollable and motion-safe', async () => {
  for (const theme of ['light', 'dark']) await withStates({ viewport: { width: 320, height: 780 } }, async (page, section) => {
    await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.style.colorScheme = theme; document.documentElement.style.fontSize = '200%' }, theme)
    const before = await geometry(section)
    for (const name of ['Load immediately', 'Show section error', 'Show page failure', 'Restore content']) {
      await press(section, name)
      assert.deepEqual(await geometry(section), before)
      assert.ok(await section.evaluate(node => node.getBoundingClientRect().right <= innerWidth && node.scrollWidth <= node.clientWidth + 1), 'State specimens stay within the narrow viewport')
      assert.ok(await section.locator('.ui-state').evaluateAll(nodes => nodes.every(node => node.scrollWidth <= node.clientWidth + 1)))
    }
    await press(section, 'Load immediately')
    assert.deepEqual(await section.locator('.ui-state-indicator').evaluateAll(nodes => nodes.flatMap(node => node.getAnimations())), [])
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    assert.equal(await section.locator('.ui-state-indicator').evaluate(node => node.getAnimations().length), 1)
    await press(section, 'Show page failure')
    const retry = section.getByRole('button', { name: 'Retry example page' })
    const contrast = await retry.evaluate(node => {
      const rgb = color => color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => { const c = value / 255; return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4 })
      const luminance = color => { const c = rgb(color); return c[0] * 0.2126 + c[1] * 0.7152 + c[2] * 0.0722 }
      const style = getComputedStyle(node)
      const background = luminance(style.backgroundColor)
      const ratio = color => { const other = luminance(color); return (Math.max(background, other) + 0.05) / (Math.min(background, other) + 0.05) }
      return { text: ratio(style.color), border: ratio(style.borderTopColor), scheme: style.colorScheme }
    })
    assert.equal(contrast.scheme, theme)
    assert.ok(contrast.text >= 4.5 && contrast.border >= 3, JSON.stringify(contrast))
    await retry.focus()
    assert.ok(await retry.evaluate(node => node.matches(':focus-visible')))
    await page.keyboard.press('Enter')
    assert.deepEqual(await geometry(section), before)
  })
})
