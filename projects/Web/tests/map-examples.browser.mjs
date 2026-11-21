import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
const url = `${origin}/kitchen-sink`
const diagnosticsUrl = `${url}?mapDiagnostics`
const provider = 'https://tiles.openfreemap.org'
const providerAttribution = '<a href="https://openfreemap.org/">OpenFreeMap</a> © <a href="https://openmaptiles.org/">OpenMapTiles</a> Data from <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
const testTile = Buffer.from('GiB4AgoFd2F0ZXIogCASEhgDIg4JQEAagCAAAIAU/x8ADw==', 'base64') // One MVT water polygon.
const root = '9b904ae6-8341-487e-9c6d-86a4a90f4070'
const vilijampole = 'f99f399f-167e-4ac3-ba2a-c950bcf92a89'
const panerys = 'fd24c30e-606e-453c-96af-082301039c12'
const dainava = 'c321b85c-5075-41c5-8cfd-8df26014d884'

const pathCases = [
  ['Single strong', 'strong', [root, vilijampole], 1, 0],
  ['Single weak', 'weak', [root, vilijampole], 0, 1],
  ['Multiple weak', 'weak-alternatives', [root, vilijampole, panerys, dainava], 0, 4],
  ['Multiple strong', 'strong-alternatives', [root, vilijampole, panerys, dainava], 0, 4],
  ['Strong + weak', 'mixed', [root, vilijampole, panerys, dainava], 2, 0],
  ['Missing middle hop', 'missing', [root, vilijampole], 0, 2, ['gap-2']],
  ['Consecutive missing hops', 'consecutive-missing', [root, vilijampole], 0, 3, ['gap-2', 'gap-3']],
  ['Repeated node', 'repeat', [root, vilijampole], 2, 0],
  ['Candidate without location', 'unlocated', [root, vilijampole], 0, 2, ['gap-2']],
  ['Empty raw path', 'empty', [], 0, 0],
  ['Only unresolved prefix', 'unresolved', [], 0, 0],
  ['Missing outer hops', 'exterior-missing', [root], 0, 0],
]
const nodeCases = ['5,000 regional nodes', 'Distant dots', 'Role glyphs', 'Labels & collision', 'Raised selection', 'Clusters & counts',
  'Every role & fallback', 'Overlapping markers', 'Near-overlapping markers', 'Dense overlapping markers', 'Long node label']
const flowCases = [
  ['Moving packet & trail', 'route', 1, ['arrival', 'trail']],
  ['Single-node arrival', 'arrival', 1, ['arrival']],
  ['Shared prefix & branches', 'branch', 2, ['arrival', 'trail']],
  ['Concurrent arrivals', 'concurrent', 2, ['arrival', 'trail']],
  ['Revisit & repeated arrival', 'revisit', 1, ['arrival', 'trail']],
  ['Schematic unresolved stop', 'schematic', 1, ['arrival', 'schematic', 'trail']],
]
let browser

function mockProvider(route, request) {
  const headers = { 'access-control-allow-origin': '*' }
  if (['/styles/positron', '/styles/dark'].includes(request.pathname)) {
    const dark = request.pathname.endsWith('/dark')
    return route.fulfill({ headers, json: { version: 8, sources: {}, layers: [
      { id: 'background', type: 'background', paint: { 'background-color': dark ? '#172028' : '#faf4e8' } },
      { id: 'water', type: 'fill', source: 'openmaptiles', 'source-layer': 'water', paint: { 'fill-color': dark ? '#234754' : '#a6d5e1' } },
    ] } })
  }
  if (request.pathname === '/planet') {
    return route.fulfill({ headers, json: { tilejson: '3.0.0', tiles: [`${provider}/planet/test/{z}/{x}/{y}.pbf`], minzoom: 0, maxzoom: 14, attribution: 'OpenFreeMap · © OpenStreetMap' } })
  }
  if (request.pathname.startsWith('/planet/test/')) return route.fulfill({ headers, body: testTile, contentType: 'application/x-protobuf' })
  throw new Error(`Unexpected provider request: ${request.pathname}`)
}

before(async () => {
  browser = await chromium.launch({
    executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium',
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  })
})

after(async () => { await browser?.close() })

async function withMap(verify, { diagnostics = true, ...options } = {}, prepare, providerResponse = mockProvider) {
  const page = await browser.newPage({ viewport: { width: 1100, height: 750 }, reducedMotion: 'reduce', ...options })
  page.setDefaultTimeout(8000)
  const external = []
  const errors = []
  const workers = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => {
    if (message.type() === 'error' && message.text().includes('Encountered two children with the same key')) errors.push(message.text())
  })
  page.on('worker', worker => workers.push(worker.url()))
  try {
    await page.context().route('**/*', (route) => {
      const request = new URL(route.request().url())
      if (request.origin === provider) return providerResponse(route, request)
      if (request.origin !== origin || /^\/(graphql|api)(\/|$)/.test(request.pathname)) {
        external.push(request.href)
        return route.abort()
      }
      return route.continue()
    })
    await page.addInitScript(() => {
      document.addEventListener('map-preview-rendered', event => { window.renderedMap = event.detail })
      document.addEventListener('map-preview-flow-rendered', event => { window.renderedFlow = event.detail })
    })
    if (await prepare?.(page) === false) return
    assert.equal((await page.goto(diagnostics ? diagnosticsUrl : url, { waitUntil: 'domcontentloaded', timeout: 12000 })).status(), 200)
    const showcase = page.getByRole('region', { name: 'Map examples', exact: true })
    await showcase.scrollIntoViewIfNeeded({ timeout: 10000 })
    await verify(page, showcase, workers)
    assert.deepEqual(errors, [])
    assert.deepEqual(external, [])
  } finally {
    await page.close()
  }
}

async function frameMatching(page, predicate, argument) {
  const result = await page.waitForFunction(predicate, argument, { timeout: 8000 })
  return await result.jsonValue()
}

async function exposeOverlapMap(page) {
  await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
    const response = await route.fetch()
    await route.fulfill({ response, body: (await response.text())
      .replace('map = new MapLibre(', 'map = window.overlapMap = new MapLibre(')
      .replace('screenOverlaps(points)', '(window.overlapComputations = (window.overlapComputations ?? 0) + 1, screenOverlaps(points))') })
  })
}

for (const [device, options] of [
  ['desktop', {}],
  ['mobile', { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true }],
]) test(`Spiderfy is opt-in and reversible without changing selection or camera on ${device}`, async () => {
  await withMap(async (page, showcase) => {
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong')
    const toggle = showcase.getByRole('button', { name: 'Spiderfy', exact: true })
    assert.equal(await toggle.getAttribute('aria-pressed'), 'false')
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    if (device === 'mobile') {
      await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).click()
      await page.getByRole('option', { name: 'Overlapping markers', exact: true }).click()
    } else await showcase.getByRole('button', { name: 'Overlapping markers', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'overlap' && window.renderedMap.markerIds.length === 8)
    assert.equal(await showcase.locator('.map-overlap-stack').count(), 0)
    assert.equal(await page.evaluate(() => window.overlapComputations ?? 0), 0)
    const canvas = showcase.getByLabel('Overlapping markers fixture map', { exact: true })
    for (const method of ['click', 'press']) {
      if (method === 'press') await canvas.press('Enter')
      else await canvas.click()
      await showcase.getByText('8 markers overlap. Choose an anchor from the list.', { exact: true }).waitFor()
      assert.deepEqual(await page.evaluate(() => window.renderedMap.raisedIds), [])
    }
    const camera = () => page.evaluate(() => ({ center: window.overlapMap.getCenter().toArray(), zoom: window.overlapMap.getZoom() }))
    const before = await camera()
    await toggle.press('Space')
    assert.equal(await toggle.getAttribute('aria-pressed'), 'true')
    const stack = showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true })
    await stack.click()
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    await fan.waitFor()
    assert.deepEqual(await camera(), before)
    await fan.getByRole('button', { name: /Specimen 2 · Repeater/ }).click()
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds.includes('specimen-1'))
    await page.waitForFunction(() => !window.overlapMap.isMoving())
    await showcase.getByRole('radio', { name: 'Dark', exact: true }).click()
    await stack.click()
    await fan.waitFor()
    const selectedCamera = await camera()
    await toggle.press('Enter')
    assert.equal(await toggle.getAttribute('aria-pressed'), 'false')
    assert.equal(await showcase.locator('.map-overlap-stack, .map-overlap-content, .map-overlap-tooltip').count(), 0)
    await frameMatching(page, () => window.renderedMap?.raisedIds.includes('specimen-1') && window.renderedMap.roleIds.length === 8)
    assert.deepEqual(await camera(), selectedCamera)
    await showcase.getByText('Selected on map: Specimen 2 · Repeater', { exact: true }).waitFor()
    const computations = await page.evaluate(() => window.overlapComputations)
    await page.evaluate(() => { window.overlapMap.triggerRepaint(); window.overlapMap.fire('sourcedata'); window.overlapMap.fire('move') })
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    assert.equal(await page.evaluate(() => window.overlapComputations), computations)
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview').screenshot({ path: `../../.tmp/spiderfy-toggle-off-${device}.png` })
    await toggle.click()
    await stack.click()
    await fan.waitFor()
    assert.deepEqual(await camera(), selectedCamera)
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview').screenshot({ path: `../../.tmp/spiderfy-toggle-on-${device}.png` })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await showcase.getByRole('button', { name: 'Path evidence', exact: true }).click()
    assert.equal(await toggle.getAttribute('aria-pressed'), 'true')
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong')
    await page.evaluate(() => window.overlapMap.jumpTo({ zoom: 7 }))
    await showcase.getByRole('button', { name: 'Choose 2 overlapping nodes', exact: true }).waitFor()
  }, options, exposeOverlapMap)
})

test('Spiderfy survives map retry and does not interrupt playback', async () => {
  await withMap(async (page, showcase) => {
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong')
    const toggle = showcase.getByRole('button', { name: 'Spiderfy', exact: true })
    await toggle.click()
    await page.evaluate(() => window.overlapMap.getCanvas().dispatchEvent(new Event('webglcontextlost', { cancelable: true })))
    const retry = showcase.getByRole('button', { name: 'Retry map', exact: true })
    await retry.waitFor()
    assert.equal(await toggle.isDisabled(), true)
    await retry.click()
    await page.waitForFunction(() => document.querySelector('.map-preview[data-map-state="ready"]'))
    assert.equal(await toggle.getAttribute('aria-pressed'), 'true')
    assert.equal(await toggle.isEnabled(), true)
    await page.evaluate(() => window.overlapMap.jumpTo({ zoom: 7 }))
    await showcase.getByRole('button', { name: 'Choose 2 overlapping nodes', exact: true }).click()
    await showcase.getByRole('dialog', { name: '2 overlapping nodes', exact: true }).waitFor()
    await showcase.getByRole('button', { name: 'Packet flow', exact: true }).click()
    await showcase.getByRole('button', { name: 'Loop preview', exact: true }).click()
    await showcase.getByRole('button', { name: 'Pause & clear', exact: true }).waitFor()
    await toggle.click()
    await showcase.getByRole('button', { name: 'Pause & clear', exact: true }).waitFor()
    await toggle.click()
    await showcase.getByRole('button', { name: 'Pause & clear', exact: true }).waitFor()
    assert.equal(await toggle.getAttribute('aria-pressed'), 'true')
  }, {}, exposeOverlapMap)
})

test('empty map clicks clear selection and fan while drags and controls preserve it', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Role glyphs', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'roles')
    const canvas = showcase.getByLabel('Role glyphs fixture map', { exact: true })
    await canvas.click()
    await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).waitFor()
    await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id), root)
    await showcase.getByRole('button', { name: 'Zoom in', exact: true }).click()
    assert.ok(await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).isVisible())
    const bounds = await canvas.boundingBox()
    await page.mouse.move(bounds.x + 30, bounds.y + 100)
    await page.mouse.down()
    await page.mouse.move(bounds.x + 95, bounds.y + 115, { steps: 6 })
    await page.mouse.up()
    assert.deepEqual(await page.evaluate(() => window.renderedMap.raisedIds), [root])
    const credits = showcase.locator('.maplibregl-ctrl-attrib summary')
    await credits.click()
    assert.ok(await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).isVisible())
    await canvas.click({ position: { x: 10, y: 80 } })
    await frameMatching(page, () => window.renderedMap?.raisedIds.length === 0 && window.renderedMap.selectedNameplateIds.length === 0)
    await showcase.getByRole('button', { name: 'Overlapping markers', exact: true }).click()
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    const stack = showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true })
    await stack.click()
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    await fan.getByRole('button', { name: /Specimen 2 · Repeater/ }).click()
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds.includes('specimen-1'))
    await stack.click()
    await fan.waitFor()
    await showcase.getByLabel('Overlapping markers fixture map', { exact: true }).click({ position: { x: 10, y: 80 } })
    await fan.waitFor({ state: 'hidden' })
    await frameMatching(page, () => window.renderedMap?.raisedIds.length === 0 && window.renderedMap.selectedNameplateIds.length === 0)
  }, {}, exposeOverlapMap)
})

test('hover and keyboard focus reveal full native and fan names without picking nodes', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Role glyphs', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'roles')
    const canvas = showcase.getByLabel('Role glyphs fixture map', { exact: true })
    assert.deepEqual(await page.evaluate(() => window.renderedMap.labelIds), [])
    await canvas.hover()
    const native = showcase.locator('.map-preview-node-hint')
    await native.getByText('LT-KM ROOT ED', { exact: true }).waitFor()
    assert.deepEqual(await page.evaluate(() => window.renderedMap.raisedIds), [])
    if (process.env.PULSAS_CAPTURE_HOVER) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/node-hover-native.png' })
    await showcase.getByRole('button', { name: 'Fit example', exact: true }).hover()
    await native.waitFor({ state: 'hidden' })
    await canvas.focus()
    await native.getByText('LT-KM ROOT ED', { exact: true }).waitFor()
    await canvas.blur()
    await native.waitFor({ state: 'hidden' })
    await canvas.click()
    await frameMatching(page, id => window.renderedMap?.selectedNameplateIds.includes(id), root)
    await showcase.getByRole('radio', { name: 'Dark', exact: true }).click()
    await canvas.hover()
    await native.getByText('LT-KM ROOT ED', { exact: true }).waitFor()
    assert.equal(await native.locator('.maplibregl-popup-content').evaluate(el => getComputedStyle(el).color), 'rgb(237, 244, 238)')
    await page.evaluate(() => {
      const map = window.overlapMap, point = map.project([23.89257, 54.91074])
      map.panBy([point.x - 8, 0], { duration: 0 })
    })
    await frameMatching(page, id => !window.renderedMap.selectedNameplateIds.includes(id), root)
    const edgeBounds = await canvas.boundingBox()
    await page.mouse.move(edgeBounds.x + 8, edgeBounds.y + edgeBounds.height / 2)
    await native.getByText('LT-KM ROOT ED', { exact: true }).waitFor()
    const hintBounds = await native.boundingBox()
    assert.ok(hintBounds.x >= edgeBounds.x && hintBounds.x + hintBounds.width <= edgeBounds.x + edgeBounds.width)
    await showcase.getByRole('button', { name: 'Overlapping markers', exact: true }).click()
    await native.waitFor({ state: 'hidden' })
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    await showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true }).hover()
    assert.equal(await showcase.getByRole('tooltip').count(), 0)
    await showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true }).click()
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    const choice = fan.locator('[data-node-id="specimen-1"]')
    await fan.locator('[data-node-id="specimen-0"]').focus()
    await choice.hover()
    const hint = fan.locator('[data-slot="tooltip-content"]').filter({ hasText: /^Specimen 2 · Repeater$/ })
    await hint.waitFor()
    if (process.env.PULSAS_CAPTURE_HOVER) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/node-hover-fan.png' })
    await fan.getByRole('button', { name: 'Collapse overlapping nodes', exact: true }).hover()
    await hint.waitFor({ state: 'hidden' })
    await fan.locator('[data-node-id="specimen-0"]').focus()
    await page.keyboard.press('Tab')
    assert.equal(await choice.evaluate(el => document.activeElement === el), true)
    await hint.waitFor()
    await showcase.getByRole('button', { name: 'Long node label', exact: true }).click()
    await hint.waitFor({ state: 'hidden' })
    await native.waitFor({ state: 'hidden' })
    await showcase.getByRole('button', { name: 'Role glyphs', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'roles')
    await showcase.getByLabel('Role glyphs fixture map', { exact: true }).hover()
    await native.waitFor()
    await showcase.getByLabel('Role glyphs fixture map', { exact: true }).evaluate(canvas => canvas.dispatchEvent(new Event('webglcontextlost', { cancelable: true })))
    await showcase.getByRole('button', { name: 'Retry map', exact: true }).waitFor()
    await native.waitFor({ state: 'hidden' })
  }, {}, exposeOverlapMap)
})

for (const [device, options] of [
  ['desktop', { viewport: { width: 1100, height: 750 }, reducedMotion: 'no-preference' }],
  ['mobile', { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true, reducedMotion: 'reduce' }],
]) test(`eight colocated nodes spread without a visible banner on ${device}`, async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    if (device === 'mobile') {
      await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).click()
      await page.getByRole('option', { name: 'Overlapping markers', exact: true }).click()
    } else await showcase.getByRole('button', { name: 'Overlapping markers', exact: true }).click()
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    const stack = showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true })
    await stack.waitFor()
    const source = await page.evaluate(async () => window.overlapMap.getSource('markers').getData())
    assert.equal(source.features.length, 8)
    assert.equal(new Set(source.features.map(feature => JSON.stringify(feature.geometry.coordinates))).size, 1)
    await page.evaluate(() => {
      window.fanFrames = []
      window.stopFanFrames = false
      const sample = () => {
        const positions = [...document.querySelectorAll('.map-overlap-position')]
        const lines = [...document.querySelectorAll('.map-overlap-connectors line')]
        if (positions.length) window.fanFrames.push(positions.map((position, i) => ({
          x: parseFloat(position.style.left), y: parseFloat(position.style.top),
          lineX: Number(lines[i].getAttribute('x2')), lineY: Number(lines[i].getAttribute('y2')),
        })))
        if (!window.stopFanFrames) requestAnimationFrame(sample)
      }
      requestAnimationFrame(sample)
    })
    await stack.press('Enter')
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    await fan.waitFor()
    await page.waitForFunction(() => document.querySelector('.map-overlap-content[data-overlap-layout="fan"]:not([data-spreading])') && window.fanFrames.length >= 3)
    assert.equal(await fan.getAttribute('data-overlap-layout'), 'fan')
    assert.equal(await fan.locator('[data-node-id]').count(), 8)
    assert.equal(await fan.locator('.map-overlap-caption').count(), 0)
    for (const text of ['8 overlapping nodes', 'Display offsets only · lines lead to advertised positions']) {
      const hidden = fan.getByText(text, { exact: true })
      assert.ok(await hidden.evaluate(el => getComputedStyle(el).clipPath === 'inset(50%)' && el.getBoundingClientRect().width === 1))
    }
    const frames = await page.evaluate(() => { window.stopFanFrames = true; return window.fanFrames })
    const last = frames.at(-1)
    assert.equal(new Set(last.map(point => `${point.x}:${point.y}`)).size, 8)
    for (const frame of frames) for (const point of frame) {
      assert.ok(Math.abs(point.x - point.lineX) < .01 && Math.abs(point.y - point.lineY) < .01, 'connectors follow animated glyph positions')
    }
    if (options.reducedMotion === 'reduce') assert.ok(frames.every(frame => JSON.stringify(frame) === JSON.stringify(last)), 'reduced motion has final positions only')
    else assert.ok(frames.some(frame => {
      const point = frame[0], target = last[0]
      return Math.hypot(point.x - target.x, point.y - target.y) > 2 && Math.hypot(point.x - target.x, point.y - target.y) < 80
    }), 'normal motion includes intermediate spread positions')
    const bounds = await showcase.locator('.map-preview-stage').boundingBox()
    const boxes = await fan.locator('[data-node-id]').evaluateAll(nodes => nodes.map(node => {
      const { x, y, width, height } = node.getBoundingClientRect(); return { x, y, width, height }
    }))
    for (const a of boxes) {
      assert.ok(a.width === 44 && a.height === 44)
      assert.ok(a.x >= bounds.x && a.x + a.width <= bounds.x + bounds.width && a.y >= bounds.y && a.y + a.height <= bounds.y + bounds.height)
      for (const b of boxes) if (a !== b) assert.ok(a.x + a.width <= b.x || b.x + b.width <= a.x || a.y + a.height <= b.y || b.y + b.height <= a.y)
    }
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview-stage').screenshot({ path: `../../.tmp/spiderfy-eight-${device}.png` })
  }, options, exposeOverlapMap)
})

test('overlap fans support keyboard dismissal, selection through camera motion, and theme changes without moving advertised anchors', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Overlapping markers', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'overlap')
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    const stack = showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true })
    await stack.waitFor()
    const before = await page.evaluate(async () => ({ markers: await window.overlapMap.getSource('markers').getData(), zoom: window.overlapMap.getZoom() }))
    const canvas = showcase.getByLabel('Overlapping markers fixture map', { exact: true })
    await canvas.press('Enter')
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    await fan.waitFor()
    assert.equal(await fan.locator('[data-node-id]').count(), 8)
    assert.equal(await fan.locator('.map-overlap-connectors line').count(), 8)
    assert.equal(await fan.getAttribute('data-overlap-layout'), 'fan')
    await page.waitForFunction(() => !document.querySelector('.map-overlap-content')?.getAttribute('data-spreading'))
    assert.equal(await page.evaluate(() => window.overlapMap.getZoom()), before.zoom)
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/spiderfy-light.png' })
    await page.keyboard.press('Escape')
    await fan.waitFor({ state: 'hidden' })
    assert.equal(await canvas.evaluate(el => document.activeElement === el), true)
    await showcase.getByRole('radio', { name: 'Dark', exact: true }).click()
    await stack.press('Enter')
    await fan.waitFor()
    assert.ok(Math.abs(await fan.locator('[data-role="ROOM_SERVER"] svg').first().evaluate(el => el.getBoundingClientRect().width) - 14.4) < .02)
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/spiderfy-dark.png' })
    await page.evaluate(() => { window.overlapCanvas = window.overlapMap.getCanvas(); window.overlapPicks = []; document.addEventListener('map-preview-rendered', e => window.overlapPicks.push(e.detail.raisedIds)) })
    await fan.getByRole('button', { name: /Specimen 2 · Repeater/ }).click()
    await fan.waitFor({ state: 'hidden' })
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds.includes('specimen-1') && window.renderedMap.zoom >= 12.5)
    assert.ok(await showcase.getByText('Selected on map: Specimen 2 · Repeater', { exact: true }).isVisible())
    const after = await page.evaluate(async () => ({ markers: await window.overlapMap.getSource('markers').getData(), sameCanvas: window.overlapCanvas === window.overlapMap.getCanvas(), picks: window.overlapPicks }))
    assert.deepEqual(after.markers, before.markers)
    assert.equal(after.sameCanvas, true)
    assert.ok(after.picks.every(ids => ids.length === 0 || ids.every(id => id === 'specimen-1')))
    await canvas.press('Escape')
    await frameMatching(page, () => window.renderedMap?.raisedIds.length === 0)
    await stack.click()
    await fan.waitFor()
    await canvas.click({ position: { x: 12, y: 100 } })
    await fan.waitFor({ state: 'hidden' })
    await stack.click()
    await fan.waitFor()
    await page.evaluate(() => window.overlapMap.panBy([20, 0], { duration: 0 }))
    await fan.waitFor({ state: 'hidden' })
    await stack.click()
    await fan.waitFor()
    await showcase.getByRole('button', { name: 'Long node label', exact: true }).click()
    await fan.waitFor({ state: 'hidden' })
  }, { reducedMotion: 'no-preference' }, exposeOverlapMap)
})

test('near-screen fan and dense chooser stay usable on a 320px touch map', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    // At mobile widths the select is the supported scenario navigation.
    const selectScenario = async (name) => {
      await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).click()
      await page.getByRole('option', { name, exact: true }).click()
    }
    await selectScenario('Near-overlapping markers')
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    const stack = showcase.getByRole('button', { name: 'Choose 3 overlapping nodes', exact: true })
    await stack.tap()
    const fan = showcase.getByRole('dialog', { name: '3 overlapping nodes', exact: true })
    await fan.waitFor()
    const bounds = await showcase.locator('.map-preview-stage').boundingBox()
    for (const button of await fan.locator('[data-node-id]').all()) {
      const box = await button.boundingBox()
      assert.ok(box.x >= bounds.x && box.x + box.width <= bounds.x + bounds.width)
      assert.ok(box.y >= bounds.y && box.y + box.height <= bounds.y + bounds.height)
    }
    const anchors = await fan.locator('.map-overlap-connectors line').evaluateAll(lines => lines.map(line => line.getAttribute('x1')))
    assert.equal(new Set(anchors).size, 3)
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/spiderfy-mobile-near.png' })
    await fan.getByRole('button', { name: /Specimen 3 · Client/ }).tap()
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds.includes('specimen-2'))
    for (const edge of ['left', 'right', 'bottom']) {
      await page.evaluate(edge => {
        const map = window.overlapMap
        const point = map.project([23.9036, 54.8985])
        const target = edge === 'left' ? [5, 150] : edge === 'right' ? [map.getCanvas().clientWidth - 5, 150] : [map.getCanvas().clientWidth / 2, map.getCanvas().clientHeight - 5]
        map.panBy([point.x - target[0], point.y - target[1]], { duration: 0 })
      }, edge)
      await stack.tap()
      await fan.waitFor()
      const edgeBounds = await showcase.locator('.map-preview-stage').boundingBox()
      for (const button of await fan.locator('[data-node-id]').all()) {
        const box = await button.boundingBox()
        assert.ok(box.x >= edgeBounds.x && box.x + box.width <= edgeBounds.x + edgeBounds.width)
        assert.ok(box.y >= edgeBounds.y && box.y + box.height <= edgeBounds.y + edgeBounds.height, JSON.stringify({ edge, box, edgeBounds }))
      }
      await fan.getByRole('button', { name: 'Collapse overlapping nodes', exact: true }).tap()
    }
    await selectScenario('Dense overlapping markers')
    await showcase.getByRole('button', { name: 'Choose 16 overlapping nodes', exact: true }).tap()
    const chooser = showcase.getByRole('dialog', { name: '16 overlapping nodes', exact: true })
    await chooser.waitFor()
    assert.equal(await chooser.locator('[data-node-id]').count(), 16)
    assert.equal(await chooser.locator('.map-overlap-connectors line').count(), 0)
    assert.ok(await chooser.locator('.map-overlap-choices').evaluate(el => el.scrollHeight > el.clientHeight))
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/spiderfy-mobile-dense.png' })
    await chooser.locator('[data-node-id="specimen-15"]').tap()
    await frameMatching(page, () => window.renderedMap?.raisedIds.includes('specimen-15'))
    assert.equal(await chooser.isVisible(), false)
  }, { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true }, exposeOverlapMap)
})

test('spiderfy leaves ambiguous path evidence intact and repeated appearances count once', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Multiple weak', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'weak-alternatives')
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    // Presentation-only camera zoom makes the existing distinct candidates overlap.
    await page.evaluate(() => window.overlapMap.jumpTo({ zoom: 8 }))
    const stack = showcase.getByRole('button', { name: /Choose \d+ overlapping nodes/ }).first()
    await stack.waitFor()
    const evidence = () => page.evaluate(async () => ({ markers: await window.overlapMap.getSource('markers').getData(), paths: await window.overlapMap.getSource('paths').getData() }))
    const before = await evidence()
    await stack.press('Enter')
    const fan = showcase.getByRole('dialog', { name: /overlapping nodes/ })
    await fan.waitFor()
    assert.ok(await fan.locator('[data-evidence="true"]').count() >= 2)
    assert.ok(await fan.locator('[data-confidence="weak"]').count() > 0)
    assert.ok(await fan.locator('.map-overlap-ambiguity').count() > 0)
    assert.deepEqual(await evidence(), before)
    await fan.locator('[data-node-id]').first().press('Enter')
    await fan.waitFor({ state: 'hidden' })
    assert.deepEqual(await evidence(), before)
    await showcase.getByRole('button', { name: 'Repeated node', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'repeat')
    await page.evaluate(() => window.overlapMap.jumpTo({ zoom: 7 }))
    await showcase.getByRole('button', { name: 'Choose 2 overlapping nodes', exact: true }).press('Enter')
    const repeated = showcase.getByRole('dialog', { name: '2 overlapping nodes', exact: true })
    assert.equal(await repeated.locator('[data-node-id]').count(), 2)
    const ids = await repeated.locator('[data-node-id]').evaluateAll(nodes => nodes.map(node => node.dataset.nodeId))
    assert.equal(new Set(ids).size, 2)
    if (process.env.PULSAS_CAPTURE_SPIDERFY) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/spiderfy-evidence.png' })
  }, {}, exposeOverlapMap)
})

test('the map renders exact Beacon anchors and a path through a same-origin MapLibre worker', async () => {
  await withMap(async (page, showcase, workers) => {
    const frame = await frameMatching(page, () => window.renderedMap?.pathIds.includes('single-strong') && window.renderedMap)
    assert.ok(workers.some(url => /maplibre-gl-worker/.test(url) && new URL(url).origin === origin), workers.join(', '))
    assert.deepEqual(frame.markerIds.sort(), [root, vilijampole])
    assert.deepEqual(frame.pathIds, ['single-strong'])
    assert.ok(await showcase.getByText('LT-KM ROOT ED', { exact: true }).first().isVisible())
    assert.ok(await showcase.getByText('LT-KM Vilijampolė FF', { exact: true }).first().isVisible())
  })
})

test('zoom buttons render intermediate zoom levels when motion is allowed', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Distant dots', exact: true }).click()
    const start = await frameMatching(page, () => window.renderedMap?.sceneId === 'dots' && window.renderedMap)
    await page.evaluate(() => {
      window.zoomFrames = []
      document.addEventListener('map-preview-rendered', ({ detail }) => window.zoomFrames.push(detail.zoom))
    })
    for (const [name, target] of [['Zoom in', start.zoom + 1], ['Zoom out', start.zoom]]) {
      await page.evaluate(() => { window.zoomFrames.length = 0 })
      await showcase.getByRole('button', { name, exact: true }).click()
      await frameMatching(page, target => Math.abs(window.renderedMap?.zoom - target) < 0.01, target)
      const frames = await page.evaluate(() => window.zoomFrames)
      assert.ok(frames.some(zoom => zoom > start.zoom + 0.05 && zoom < start.zoom + 0.95), `${name}: expected an intermediate zoom, got ${frames}`)
    }
    const plus = showcase.getByRole('button', { name: 'Zoom in', exact: true })
    await plus.press('Enter')
    await plus.press('Enter')
    await frameMatching(page, target => Math.abs(window.renderedMap?.zoom - target) < 0.01, start.zoom + 2)
  }, { reducedMotion: 'no-preference' })
})

test('clicking a node glyph selects it and zooms in', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Role glyphs', exact: true }).click()
    const start = await frameMatching(page, () => window.renderedMap?.sceneId === 'roles' && window.renderedMap)
    const canvas = showcase.getByLabel('Role glyphs fixture map', { exact: true })
    await canvas.click()
    const selected = await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id) && window.renderedMap, root)
    assert.ok(selected.zoom >= 12 && selected.zoom > start.zoom, `Expected a close-up, got ${selected.zoom}`)
    await showcase.getByRole('button', { name: 'Zoom in', exact: true }).click()
    const closer = await frameMatching(page, zoom => window.renderedMap?.zoom > zoom && window.renderedMap, selected.zoom)
    await page.evaluate(() => {
      window.reselectZooms = []
      document.addEventListener('map-preview-rendered', ({ detail }) => window.reselectZooms.push(detail.zoom))
    })
    await canvas.click()
    assert.ok(await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).isVisible())
    assert.ok(await page.evaluate(zoom => window.renderedMap.zoom === zoom && window.reselectZooms.every(value => value === zoom), closer.zoom))
  })
})

test('evidence names return below their marker after selection motion when there is room', async () => {
  await withMap(async (page, showcase) => {
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong')
    const initialZoom = await page.evaluate(() => window.renderedMap.zoom)
    await showcase.getByRole('button', { name: 'Zoom out', exact: true }).click()
    await frameMatching(page, zoom => window.renderedMap?.zoom === Math.round((zoom - 1) * 100) / 100, initialZoom)
    await page.evaluate(() => window.placementMap.jumpTo({ zoom: 13.75 }))
    await showcase.getByRole('button', { name: /LT-KM Vilijampolė FF.*Repeater/ }).click()
    await frameMatching(page, id => window.renderedMap?.selectedNameplateIds?.includes(id), vilijampole)
    await page.waitForFunction(() => window.placementMap.loaded() && !window.placementMap.isMoving())
    await page.evaluate(async () => {
      const map = window.placementMap
      for (const x of [-200, -100, -50, 50, 100, 200]) {
        map.panBy([x, 0], { duration: 0 })
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
        map.panBy([-x, 0], { duration: 0 })
      }
      map.panBy([100, 0], { duration: 0 })
    })
    await page.waitForFunction(() => window.placementMap.loaded() && !window.placementMap.isMoving())
    const placement = await page.evaluate(() => {
      const map = window.placementMap
      return { labels: window.renderedMap.evidenceLabelIds, offsets: map.style.placement.variableOffsets }
    })
    assert.deepEqual(placement.labels, [root])
    if (process.env.PULSAS_CAPTURE_LABEL_PLACEMENT) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/evidence-label-baseline.png' })
    const layout = await page.evaluate(() => window.placementMap.getLayoutProperty('preview-evidence-name', 'text-anchor'))
    assert.equal(layout, 'top', 'ROOT name should remain below, not use a retained side anchor')
    assert.deepEqual(placement.offsets, {}, 'fixed below-only labels must not retain variable side placements')
    await page.evaluate(id => {
      const map = window.placementMap
      const marker = map.querySourceFeatures('markers').find(feature => feature.properties.previewId === id)
      const point = map.project(marker.geometry.coordinates)
      map.panBy([0, point.y - (map.getCanvas().clientHeight - 25)], { duration: 0 })
    }, root)
    await page.waitForFunction(id => !window.renderedMap.evidenceLabelIds.includes(id), root)
    await page.evaluate(id => {
      const map = window.placementMap
      const marker = map.querySourceFeatures('markers').find(feature => feature.properties.previewId === id)
      map.jumpTo({ center: marker.geometry.coordinates })
    }, root)
    await frameMatching(page, id => window.renderedMap.evidenceLabelIds.includes(id), root)
  }, { viewport: { width: 1440, height: 850 }, reducedMotion: 'no-preference' }, async page => {
    await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
      const response = await route.fetch()
      const source = await response.text()
      assert.ok(source.includes('map = new MapLibre('))
      await route.fulfill({ response, body: source.replace('map = new MapLibre(', 'map = window.placementMap = new MapLibre(') })
    })
  })
})

test('native names and selected plates omit at mobile edges and return when they fit', async () => {
  await withMap(async (page, showcase) => {
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong')
    for (const selected of [false, true]) {
      if (selected) await showcase.getByRole('button', { name: /LT-KM Vilijampolė FF.*Repeater/ }).click()
      const id = selected ? vilijampole : root
      for (const edge of ['left', 'right', 'bottom']) {
        await page.evaluate(({ id, edge }) => {
          const map = window.placementMap
          const marker = map.querySourceFeatures('markers').find(feature => feature.properties.previewId === id)
          map.jumpTo({ center: marker.geometry.coordinates, zoom: 12.5 })
          const point = map.project(marker.geometry.coordinates)
          const target = edge === 'left' ? [5, point.y] : edge === 'right' ? [map.getCanvas().clientWidth - 5, point.y] : [point.x, map.getCanvas().clientHeight - 25]
          map.panBy([point.x - target[0], point.y - target[1]], { duration: 0 })
        }, { id, edge })
        await page.waitForFunction(({ id, selected }) => !(selected ? window.renderedMap.selectedNameplateIds : window.renderedMap.evidenceLabelIds).includes(id), { id, selected })
        await page.waitForFunction(({ id, selected }) => window.placementMap.getFeatureState({ source: selected ? 'preview-chosen' : 'markers', id }).nameOutsideViewport === true, { id, selected })
        await page.evaluate(id => {
          const map = window.placementMap
          map.jumpTo({ center: map.querySourceFeatures('markers').find(feature => feature.properties.previewId === id).geometry.coordinates })
        }, id)
        await page.waitForFunction(({ id, selected }) => (selected ? window.renderedMap.selectedNameplateIds : window.renderedMap.evidenceLabelIds).includes(id), { id, selected })
      }
    }
  }, { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true }, async page => {
    await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
      const response = await route.fetch()
      const source = await response.text()
      await route.fulfill({ response, body: source.replace('map = new MapLibre(', 'map = window.placementMap = new MapLibre(') })
    })
  })
})

test('cluster expansion animates through intermediate zoom levels', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Clusters & counts', exact: true }).click()
    const start = await frameMatching(page, () => window.renderedMap?.sceneId === 'clusters' && window.renderedMap.clusterCounts.includes(6) && window.renderedMap)
    await page.evaluate(() => {
      window.clusterZoomFrames = []
      document.addEventListener('map-preview-rendered', ({ detail }) => window.clusterZoomFrames.push(detail.zoom))
    })
    await showcase.getByRole('button', { name: /Expand cluster.*6 nodes/ }).press('Enter')
    await frameMatching(page, () => window.renderedMap?.clusterCounts.includes(6) === false)
    const frames = await page.evaluate(() => window.clusterZoomFrames)
    const final = Math.max(...frames)
    assert.ok(frames.some(zoom => zoom > start.zoom + 0.05 && zoom < final - 0.05), `Expected smooth cluster expansion: ${frames}`)
    assert.ok(frames.every(zoom => zoom >= start.zoom), 'Cluster expansion must not zoom out')
  }, { reducedMotion: 'no-preference' })
})

test('cluster expansion skips intermediate zoom frames with reduced motion', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Clusters & counts', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'clusters' && window.renderedMap.clusterCounts.includes(6))
    await page.evaluate(() => {
      window.clusterZoomFrames = []
      document.addEventListener('map-preview-rendered', ({ detail }) => window.clusterZoomFrames.push(detail.zoom))
    })
    await showcase.getByRole('button', { name: /Expand cluster.*6 nodes/ }).press('Enter')
    await frameMatching(page, () => window.renderedMap?.clusterCounts.includes(6) === false)
    const frames = await page.evaluate(() => window.clusterZoomFrames)
    assert.equal(new Set(frames).size, 1, `Reduced motion must not animate: ${frames}`)
  })
})

test('compact node-state names and selected plates remain below their icons', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Raised selection', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'selected' && window.renderedMap.labelIds.includes('f99f399f-167e-4ac3-ba2a-c950bcf92a89'))
    await showcase.getByRole('button', { name: /LT-KM ROOT ED.*Repeater/ }).click()
    await frameMatching(page, id => window.renderedMap?.selectedNameplateIds.includes(id), root)
    if (process.env.PULSAS_CAPTURE_LABEL_PLACEMENT) await showcase.locator('.map-preview-stage').screenshot({ path: '../../.tmp/node-state-compact-name-gaps.png' })
    assert.ok(await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).isVisible())
  }, { viewport: { width: 1200, height: 850 } })
})

test('selected nameplates follow map, list, and keyboard selection without changing path evidence', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Multiple strong', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong-alternatives')
    await showcase.getByRole('button', { name: /LT-KM Panerys C14A.*Repeater/ }).click()
    const selected = await frameMatching(page, id => window.renderedMap?.selectedNameplateIds?.includes(id) && window.renderedMap, panerys)
    assert.deepEqual(selected.selectedNameplateIds, [panerys])
    assert.ok(selected.ambiguousIds.includes(panerys))
    assert.equal(selected.hopLabels[panerys], '2')
    assert.equal(selected.weakPathIds.length, 4)
    const canvas = showcase.getByLabel('Multiple strong fixture map', { exact: true })
    await canvas.click()
    assert.deepEqual(await page.evaluate(() => window.renderedMap.selectedNameplateIds), [panerys])
    for (const theme of ['Dark', 'Light']) {
      await showcase.getByRole('radio', { name: theme, exact: true }).click()
      await showcase.locator(`[data-basemap-state="ready"][data-map-theme="${theme.toLowerCase()}"]`).waitFor()
      const themed = await frameMatching(page, id => window.renderedMap?.selectedNameplateIds?.includes(id) && window.renderedMap, panerys)
      assert.equal(themed.zoom, selected.zoom)
      assert.ok(themed.ambiguousIds.includes(panerys))
      assert.equal(themed.weakPathIds.length, 4)
    }
    await canvas.press('Escape')
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds?.length === 0 && window.renderedMap.raisedIds.length === 0)
    await canvas.press('Enter')
    await frameMatching(page, id => window.renderedMap?.selectedNameplateIds?.includes(id), panerys)
    await showcase.getByRole('button', { name: 'Clear selection', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds?.length === 0)
    await showcase.getByRole('button', { name: 'Single weak', exact: true }).click()
    const changed = await frameMatching(page, () => window.renderedMap?.sceneId === 'weak' && window.renderedMap)
    assert.deepEqual(changed.selectedNameplateIds, [])
  })
})

test('selected nameplates handle long names and overlapping nodes on a 320px map', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    const choose = async (name) => {
      await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).click()
      await page.getByRole('option', { name, exact: true }).click()
    }
    await choose('Long node label')
    await frameMatching(page, () => window.renderedMap?.sceneId === 'long-label')
    const canvas = showcase.getByLabel('Long node label fixture map', { exact: true })
    await canvas.press('Enter')
    await frameMatching(page, () => window.renderedMap?.selectedNameplateIds?.includes('specimen-0'))
    assert.ok(await showcase.getByText(/Selected on map: Specimen · A deliberately long node label/).isVisible())
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await choose('Overlapping markers')
    await frameMatching(page, () => window.renderedMap?.sceneId === 'overlap' && window.renderedMap.selectedNameplateIds.length === 0)
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    await showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true }).waitFor()
    await showcase.getByLabel('Overlapping markers fixture map', { exact: true }).press('Enter')
    await showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true }).getByRole('button', { name: /Specimen 2 · Repeater/ }).press('Enter')
    const overlap = await frameMatching(page, () => window.renderedMap?.selectedNameplateIds?.includes('specimen-1') && window.renderedMap)
    assert.deepEqual(overlap.selectedNameplateIds, ['specimen-1'])
    assert.ok(overlap.markerIds.includes('specimen-0'))
  }, { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true })
})

test('Light and Dark use OpenFreeMap tiles without replacing the selected map', async () => {
  const requests = []
  await withMap(async (page, showcase) => {
    await showcase.locator('[data-basemap-state="ready"]').waitFor()
    await frameMatching(page, () => window.renderedMap?.pathIds.includes('single-strong'))
    await showcase.getByRole('button', { name: /LT-KM ROOT ED/ }).click()
    await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id), root)
    const canvas = showcase.getByLabel('Single strong fixture map', { exact: true })
    const original = await canvas.elementHandle()
    const zoom = await showcase.getByText(/^Zoom \d/).innerText()
    const light = showcase.getByRole('radio', { name: 'Light', exact: true })
    const dark = showcase.getByRole('radio', { name: 'Dark', exact: true })
    await Promise.all([
      page.waitForResponse(response => response.url() === `${provider}/styles/dark` && response.ok()),
      light.press('ArrowRight'),
    ])
    assert.equal(await dark.getAttribute('aria-checked'), 'true')
    await showcase.locator('[data-basemap-state="ready"][data-map-theme="dark"]').waitFor()
    await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id), root)
    assert.equal(await canvas.evaluate((node, original) => node === original, original), true)
    assert.equal(await showcase.getByText(/^Zoom \d/).innerText(), zoom)
    await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).waitFor()
    assert.ok(requests.includes('/styles/positron'))
    assert.ok(requests.includes('/styles/dark'))
    assert.ok(requests.some(path => path.startsWith('/planet/test/')))

    await showcase.getByRole('button', { name: 'Packet flow', exact: true }).click()
    await showcase.getByRole('button', { name: 'Loop preview', exact: true }).click()
    await frameMatching(page, () => window.renderedFlow?.sceneId === 'route' && window.renderedFlow.routes.length > 0)
    await page.evaluate(() => {
      window.flowCleared = false
      document.addEventListener('map-preview-flow-rendered', ({ detail }) => { if (!detail.routes.length) window.flowCleared = true })
    })
    await light.click()
    await showcase.locator('[data-basemap-state="ready"][data-map-theme="light"]').waitFor()
    assert.equal(await page.evaluate(() => window.flowCleared), false)
    assert.ok(await showcase.getByRole('button', { name: 'Pause & clear', exact: true }).isVisible())
  }, {}, undefined, (route, request) => {
    requests.push(request.pathname)
    return mockProvider(route, request)
  })
})

test('basemap failure keeps evidence visible and retries without replacing the map', async () => {
  let failed = false
  await withMap(async (page, showcase) => {
    const frame = await frameMatching(page, () => window.renderedMap?.pathIds.includes('single-strong') && window.renderedMap)
    assert.ok(frame.labelIds.includes(root))
    const retry = showcase.getByRole('button', { name: 'Retry basemap', exact: true })
    await retry.waitFor()
    const canvas = showcase.getByLabel('Single strong fixture map', { exact: true })
    const original = await canvas.elementHandle()
    await retry.press('Enter')
    await retry.waitFor({ state: 'hidden' })
    assert.equal(await canvas.evaluate((node, original) => node === original, original), true)
    assert.ok(await showcase.getByRole('heading', { name: 'Raw hop chain' }).isVisible())
  }, {}, undefined, (route, request) => {
    if (!failed && request.pathname === '/planet') {
      failed = true
      return route.fulfill({ status: 503, headers: { 'access-control-allow-origin': '*' } })
    }
    return mockProvider(route, request)
  })
})

async function assertCompactAttribution(showcase) {
  const bounds = await showcase.locator('.maplibregl-ctrl-attrib').evaluate(node => {
    const rect = element => {
      const { x, y, width, height, right, bottom } = element.getBoundingClientRect()
      return { x, y, width, height, right, bottom }
    }
    const text = document.createRange()
    text.selectNodeContents(node.querySelector('.maplibregl-ctrl-attrib-inner'))
    return {
      strip: rect(node), map: rect(node.closest('.map-preview-canvas')), summary: rect(node.querySelector('summary')),
      text: rect(text), radius: getComputedStyle(node).borderRadius, margin: getComputedStyle(node).margin,
    }
  })
  assert.ok(Math.abs(bounds.strip.right - bounds.map.right) < 1, 'attribution is flush right')
  assert.ok(Math.abs(bounds.strip.bottom - bounds.map.bottom) < 1, 'attribution is flush bottom')
  assert.equal(bounds.radius, '0px')
  assert.equal(bounds.margin, '0px')
  assert.ok(bounds.summary.width >= 24 && bounds.summary.height >= 24)
  const center = box => box.y + box.height / 2
  assert.ok(Math.abs(center(bounds.summary) - center(bounds.strip)) < 1, 'summary is vertically centered')
  assert.ok(Math.abs(center(bounds.text) - center(bounds.strip)) <= 2, 'credit text is vertically centered')
}

test('map copyrights collapse to an info control and reopen for hover and keyboard', async () => {
  await withMap(async (page, showcase) => {
    await showcase.locator('[data-basemap-state="ready"]').waitFor()
    const attribution = showcase.locator('.maplibregl-ctrl-attrib')
    const toggle = attribution.locator('summary')
    const credits = attribution.locator('.maplibregl-ctrl-attrib-inner')
    assert.ok(await credits.isVisible())
    await assertCompactAttribution(showcase)
    for (const name of ['OpenFreeMap', 'OpenMapTiles', 'OpenStreetMap']) assert.ok((await credits.innerText()).includes(name))
    await toggle.click()
    await showcase.getByRole('button', { name: 'Fit example', exact: true }).hover()
    await credits.waitFor({ state: 'hidden' })
    const target = await toggle.boundingBox()
    assert.ok(target.width >= 24 && target.height >= 24)
    await toggle.hover()
    await credits.waitFor({ state: 'visible' })
    await credits.getByRole('link', { name: 'OpenStreetMap', exact: true }).hover()
    assert.ok(await credits.isVisible())
    await showcase.getByRole('button', { name: 'Fit example', exact: true }).hover()
    await credits.waitFor({ state: 'hidden' })
    await toggle.focus()
    await toggle.press('Enter')
    await credits.waitFor({ state: 'visible' })
    await credits.getByRole('link', { name: 'OpenStreetMap', exact: true }).focus()
    await page.keyboard.press('Escape')
    await credits.waitFor({ state: 'hidden' })
    assert.ok(await toggle.evaluate(node => node === document.activeElement))
    await toggle.press('Space')
    await credits.waitFor({ state: 'visible' })
    await showcase.getByRole('radio', { name: 'Dark', exact: true }).click()
    await showcase.locator('[data-basemap-state="ready"][data-map-theme="dark"]').waitFor()
    assert.ok((await credits.innerText()).includes('OpenStreetMap'))
    assert.equal(await credits.getByRole('link', { name: 'OpenStreetMap', exact: true }).getAttribute('href'), 'https://www.openstreetmap.org/copyright')
  }, {}, undefined, (route, request) => {
    if (request.pathname !== '/planet') return mockProvider(route, request)
    return route.fulfill({ headers: { 'access-control-allow-origin': '*' }, json: {
      tilejson: '3.0.0', tiles: [`${provider}/planet/test/{z}/{x}/{y}.pbf`], minzoom: 0, maxzoom: 14,
      attribution: providerAttribution,
    } })
  })
})

test('touch can toggle complete map credits without overflowing a 320px map', async () => {
  await withMap(async (page, showcase) => {
    await showcase.locator('[data-basemap-state="ready"]').waitFor()
    const attribution = showcase.locator('.maplibregl-ctrl-attrib')
    const toggle = attribution.locator('summary')
    const credits = attribution.locator('.maplibregl-ctrl-attrib-inner')
    await credits.waitFor({ state: 'visible' })
    await toggle.tap()
    await credits.waitFor({ state: 'hidden' })
    await toggle.tap()
    await credits.waitFor({ state: 'visible' })
    const outer = await showcase.locator('.map-preview-stage').boundingBox()
    await assertCompactAttribution(showcase)
    const inner = await attribution.boundingBox()
    assert.ok(inner.x >= outer.x && inner.x + inner.width <= outer.x + outer.width + 1)
    assert.ok((await credits.innerText()).includes('OpenStreetMap'))
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  }, { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true }, undefined, (route, request) => {
    if (request.pathname !== '/planet') return mockProvider(route, request)
    return route.fulfill({ headers: { 'access-control-allow-origin': '*' }, json: {
      tilejson: '3.0.0', tiles: [`${provider}/planet/test/{z}/{x}/{y}.pbf`], minzoom: 0, maxzoom: 14,
      attribution: providerAttribution,
    } })
  })
})

test('path evidence keeps its names at a zoom that hides ordinary node names', async () => {
  await withMap(async (page, showcase) => {
    await frameMatching(page, () => window.renderedMap?.pathIds.includes('single-strong'))
    const zoomOut = showcase.getByRole('button', { name: 'Zoom out', exact: true })
    const zoom = showcase.getByText(/^Zoom \d/)
    const level = async () => Number((await zoom.innerText()).match(/\d+(?:\.\d+)?/)[0])
    let frame
    for (let count = 0; count < 4 && await level() >= 11.5; count++) {
      const before = await zoom.innerText()
      await page.evaluate(() => { delete window.renderedMap })
      await zoomOut.click()
      await page.waitForFunction(({ node, text }) => node.innerText !== text, { node: await zoom.elementHandle(), text: before })
      frame = await frameMatching(page, () => window.renderedMap?.sceneId === 'strong' && window.renderedMap)
    }
    assert.ok(await level() < 11.5)
    assert.ok(frame.labelIds.includes(root))
    assert.equal(frame.hopLabels[root], '1')
  })
})

test('every path example renders only its supported nodes, links, and schematic gaps', async () => {
  await withMap(async (page, showcase) => {
    const catalogue = showcase.getByRole('complementary', { name: 'Scenario catalogue' })
    await catalogue.getByRole('button', { name: 'Single strong', exact: true }).waitFor()
    assert.equal(await catalogue.getByRole('button').count(), pathCases.length)
    for (const [name, sceneId, markers, strong, weak, gaps = []] of pathCases) {
      await catalogue.getByRole('button', { name, exact: true }).click()
      const frame = await frameMatching(page, id => window.renderedMap?.sceneId === id && window.renderedMap, sceneId)
      assert.deepEqual(frame.markerIds.sort(), markers.toSorted(), name)
      assert.deepEqual(frame.gapIds.sort(), gaps, name)
      assert.equal(frame.strongPathIds.length, strong, name)
      assert.equal(frame.weakPathIds.length, weak, name)
      assert.deepEqual(frame.weakPathHaloIds.sort(), frame.weakPathIds.sort(), name)
      if (sceneId === 'consecutive-missing') {
        assert.deepEqual(frame.gapPrefixes, { 'gap-2': 'e1e1e1', 'gap-3': 'e2e2e2' })
        assert.deepEqual(frame.gapHopLabels, { 'gap-2': '2', 'gap-3': '3' })
      }
      if (sceneId === 'repeat') {
        assert.equal(frame.hopLabels[root], '1, 3')
        assert.ok(await showcase.getByText(/^Hop 1, 3\b/).isVisible())
      }
      if (sceneId === 'unlocated') {
        assert.ok(await showcase.getByText('myhost wio', { exact: true }).isVisible())
        assert.ok(await showcase.getByText(/No map location/).isVisible())
      }
      if (sceneId === 'empty') assert.ok(await showcase.getByText('No hops recorded.').isVisible())
    }
  })
})

test('hiding an alternative keeps the raw evidence ambiguous and selecting it reveals it', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Multiple strong', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'strong-alternatives' && window.renderedMap)
    await showcase.getByText('Show candidates on map', { exact: true }).click()
    const candidate = showcase.getByRole('checkbox', { name: /LT-KM Panerys C14A/ })
    await candidate.press('Space')
    const filtered = await frameMatching(page, id => !window.renderedMap.markerIds.includes(id) && window.renderedMap, panerys)
    assert.equal(filtered.strongPathIds.length, 0)
    assert.equal(filtered.weakPathIds.length, 2)
    assert.deepEqual(filtered.ambiguousIds, [vilijampole])
    const remaining = showcase.getByRole('checkbox', { name: /LT-KM Vilijampolė FF/ })
    assert.ok(await remaining.isDisabled())
    assert.equal(await remaining.getAttribute('aria-checked'), 'true')
    assert.ok(await showcase.getByText(/keep at least one located candidate/i).isVisible())
    const rawHop = showcase.getByRole('button', { name: /Hop 2.*7d8e31/i })
    await rawHop.press('Enter')
    await page.getByRole('dialog', { name: /Hop 2.*7d8e31/i }).getByRole('button', { name: /LT-KM Panerys C14A/ }).click()
    const raised = await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id) && window.renderedMap, panerys)
    assert.ok(raised.ambiguousIds.includes(panerys))
    assert.equal(raised.hopLabels[panerys], '2')
    assert.equal(await candidate.getAttribute('aria-checked'), 'true')
    assert.ok(await remaining.isEnabled())
    assert.ok(await showcase.getByText(/Selected on map: LT-KM Panerys/).isVisible())
  })
})

test('node comparisons change real zoom layers, expand clusters, and resolve overlaps', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    const catalogue = showcase.getByRole('complementary', { name: 'Scenario catalogue' })
    assert.equal(await catalogue.getByRole('button').count(), nodeCases.length)
    for (const name of nodeCases) await catalogue.getByRole('button', { name, exact: true }).waitFor()
    async function choose(name, id) {
      await catalogue.getByRole('button', { name, exact: true }).click()
      return frameMatching(page, value => window.renderedMap?.sceneId === value && window.renderedMap, id)
    }
    const dots = await choose('Distant dots', 'dots')
    assert.ok(dots.markerIds.includes(root))
    assert.deepEqual(dots.roleIds, [])
    const roles = await choose('Role glyphs', 'roles')
    assert.ok(roles.roleIds.includes(root))
    assert.deepEqual(roles.labelIds, [])
    const labels = await choose('Labels & collision', 'labels')
    assert.ok(labels.labelIds.includes(root))
    await choose('Raised selection', 'selected')
    await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id), root)
    await choose('Clusters & counts', 'clusters')
    await frameMatching(page, () => window.renderedMap?.clusterCounts.includes(6))
    const zoom = showcase.getByText(/^Zoom \d/)
    const before = await zoom.innerText()
    await showcase.getByRole('button', { name: /Expand cluster.*6 nodes/ }).press('Enter')
    await frameMatching(page, () => window.renderedMap?.clusterCounts.some(count => count === 6) === false)
    assert.notEqual(await zoom.innerText(), before)
    const specimens = await choose('Every role & fallback', 'role-specimens')
    assert.equal(specimens.roleIds.length, 7)
    await choose('Overlapping markers', 'overlap')
    await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).click()
    await showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true }).waitFor()
    await showcase.getByLabel('Overlapping markers fixture map', { exact: true }).press('Enter')
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    await fan.waitFor()
    await fan.getByRole('button', { name: /Specimen 2 · Repeater/ }).press('Enter')
    await frameMatching(page, () => window.renderedMap?.raisedIds.includes('specimen-1'))
    const long = await choose('Long node label', 'long-label')
    assert.ok(long.labelIds.includes('specimen-0'))
  })
})

for (const [device, options] of [
  ['desktop', { reducedMotion: 'no-preference' }],
  ['320px mobile', { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true }],
]) test(`5,000 regional nodes fit, cluster, and remain keyboard-accessible on ${device}`, async (t) => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    if (options.isMobile) {
      await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).press('Enter')
      await page.getByRole('option', { name: '5,000 regional nodes', exact: true }).tap()
    } else {
      await showcase.getByRole('button', { name: '5,000 regional nodes', exact: true }).click()
    }

    const fit = showcase.getByRole('button', { name: 'Fit all nodes', exact: true })
    async function allFit() {
      return page.evaluate(async () => {
        const map = window.overlapMap
        const features = (await map.getSource('markers').getData()).features
        const { clientWidth, clientHeight } = map.getCanvas()
        return features.length === 5000 && features.every(({ geometry }) => {
          const { x, y } = map.project(geometry.coordinates)
          return x >= 0 && x <= clientWidth && y >= 0 && y <= clientHeight
        })
      })
    }
    await page.waitForFunction(async () => {
      const source = window.overlapMap?.getSource('markers')
      return source?.getClusterOptions().cluster && (await source.getData()).features.length === 5000
        && window.overlapMap.isSourceLoaded('markers')
    })
    assert.ok(await allFit(), 'The initial viewport contains every fixture node')
    assert.equal(await showcase.getByRole('button', { name: /Spiderfy|Expand cluster/ }).count(), 0)
    const visibleNodes = showcase.getByRole('region', { name: 'Visible nodes', exact: true })
    await visibleNodes.getByText(/5,000 nodes in view/).waitFor()

    const canvas = showcase.getByLabel('5,000 regional nodes fixture map', { exact: true })
    const cluster = await frameMatching(page, () => {
      const map = window.overlapMap
      const { clientWidth, clientHeight } = map.getCanvas()
      const target = map.queryRenderedFeatures({ layers: ['preview-cluster'] }).map(feature => ({
        ...map.project(feature.geometry.coordinates), count: feature.properties.point_count,
      })).find(({ x, y }) => x > 20 && y > 20 && x < clientWidth - 20 && y < clientHeight - 40)
      if (!target) return false
      window.regionalClusterStart = { zoom: map.getZoom(), time: performance.now() }
      if (!matchMedia('(prefers-reduced-motion: reduce)').matches) {
        window.regionalZoomFrames = new Set()
        const track = () => window.regionalZoomFrames.add(map.getZoom())
        map.on('zoom', track)
        map.once('zoomend', () => map.off('zoom', track))
      }
      return target
    })
    assert.ok(cluster.count > 1)
    const position = { x: cluster.x, y: cluster.y }
    if (options.isMobile) await canvas.tap({ position })
    else await canvas.click({ position })
    const clusterMs = await frameMatching(page, () => {
      const map = window.overlapMap
      return !map.isMoving() && map.getZoom() > window.regionalClusterStart.zoom && performance.now() - window.regionalClusterStart.time
    })
    t.diagnostic(`Cluster expansion observed after ${Math.round(clusterMs)} ms (including input and browser wait)`)
    if (!options.isMobile) assert.ok(await page.evaluate(() => window.regionalZoomFrames.size > 2), 'Animated expansion renders intermediate zoom levels')

    const finder = showcase.getByRole('region', { name: 'Map node finder' })
    const search = finder.getByRole('searchbox', { name: 'Find node' })
    const results = finder.getByRole('list', { name: 'Node search results' })
    const selection = finder.getByRole('region', { name: 'Selected node' })
    await search.fill('regional-')
    assert.equal(await results.getByRole('button').count(), 8)
    await search.fill('REGIONAL-0036')
    await search.press('Tab')
    const target = results.getByRole('button', { name: /Klaipėda 0036/ })
    assert.ok(await target.evaluate(node => node === document.activeElement))
    await page.keyboard.press('Enter')
    await selection.getByText('Klaipėda 0036', { exact: true }).waitFor()
    await page.waitForFunction(() => {
      const map = window.overlapMap
      return !map.isMoving() && map.getZoom() >= 13 && Math.abs(map.getCenter().lng - 21.14) < 0.00001 && map.isSourceLoaded('markers')
    })
    await visibleNodes.getByText(/36 nodes in view/).waitFor()

    await canvas.press('Enter')
    assert.ok(await search.evaluate(node => node === document.activeElement))
    await finder.getByText(/Search these 36 overlapping nodes/).waitFor()
    assert.equal(await results.getByRole('button').count(), 8)
    await search.press('Escape')
    assert.ok(await canvas.evaluate(node => node === document.activeElement))
    await canvas.press('Enter')
    const initiallyDisplayed = await results.getByRole('button').allTextContents()
    const available = ['0035', '0034', '0033', '0032', '0031', '0030', '0029', '0028', '0027']
      .find(number => !initiallyDisplayed.some(text => text.includes(`regional-${number}`)))
    assert.ok(available)
    await search.fill(`regional-${available}`)
    await search.press('Tab')
    await page.keyboard.press('Enter')
    await selection.getByText(`Klaipėda ${available}`, { exact: true }).waitFor()

    const camera = await page.evaluate(() => window.overlapMap.getCenter().lng)
    await canvas.press('ArrowRight')
    await page.waitForFunction(lng => !window.overlapMap.isMoving() && window.overlapMap.getCenter().lng !== lng, camera)
    await fit.click()
    assert.ok(await allFit(), 'Fit all nodes restores the regional extent')
    await visibleNodes.getByText(/5,000 nodes in view/).waitFor()

    const clustering = showcase.getByRole('button', { name: 'Clustering', exact: true })
    const view = () => page.evaluate(() => {
      const map = window.overlapMap
      return { center: map.getCenter().toArray(), zoom: map.getZoom() }
    })
    const fittedView = await view()
    assert.equal(await clustering.getAttribute('aria-pressed'), 'true')
    if (options.isMobile) await clustering.tap()
    else await clustering.press('Space')
    assert.equal(await clustering.getAttribute('aria-pressed'), 'false')
    await page.waitForFunction(() => {
      const map = window.overlapMap
      if (!map.isSourceLoaded('markers') || map.queryRenderedFeatures({ layers: ['preview-cluster'] }).length) return false
      const nodes = map.queryRenderedFeatures({ layers: ['preview-marker'] })
      return new Set(nodes.map(node => node.properties.previewId)).size === 5000
    })
    assert.deepEqual(await view(), fittedView)
    assert.ok(await selection.getByText(`Klaipėda ${available}`, { exact: true }).isVisible())
    await visibleNodes.getByText(/5,000 nodes in view/).waitFor()

    if (options.isMobile) await clustering.tap()
    else await clustering.press('Space')
    assert.equal(await clustering.getAttribute('aria-pressed'), 'true')
    await page.waitForFunction(() => window.overlapMap.queryRenderedFeatures({ layers: ['preview-cluster'] }).length > 0)
    assert.deepEqual(await view(), fittedView)

    assert.ok(await showcase.evaluate(node => node.scrollWidth <= node.clientWidth))
    assert.equal(await page.evaluate(() => window.renderedMap), undefined, 'Frame reports are off by default')
  }, { ...options, diagnostics: false }, exposeOverlapMap)
})

for (const [device, options] of [
  ['desktop', {}],
  ['320px mobile', { viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true }],
]) test(`visible nodes scroll independently and search only the map viewport on ${device}`, async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    if (options.isMobile) {
      await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).press('Enter')
      await page.getByRole('option', { name: '5,000 regional nodes', exact: true }).tap()
    } else {
      await showcase.getByRole('button', { name: '5,000 regional nodes', exact: true }).click()
    }
    const panel = showcase.getByRole('region', { name: 'Visible nodes', exact: true })
    const search = panel.getByRole('searchbox', { name: 'Search visible nodes', exact: true })
    await panel.getByText(/5,000 nodes in view/).waitFor()
    const list = panel.getByRole('listbox', { name: 'Nodes in view' })
    const heading = panel.getByRole('heading', { name: 'Visible nodes', exact: true })
    assert.equal(await panel.getByText(/Includes nodes with advertised coordinates/).count(), 0)
    const [panelBox, headingBox, searchBox, listBox] = await Promise.all(
      [panel, heading, search, list].map(locator => locator.boundingBox()))
    const countBox = await panel.getByText('5,000', { exact: true }).boundingBox()
    assert.ok(countBox.x > headingBox.x + headingBox.width && countBox.y < headingBox.y + headingBox.height,
      'The count sits beside the title without another status line')
    assert.ok(panelBox.width <= 400, 'The content stays drawer-width on a wide page')
    assert.ok(searchBox.height >= 24 && searchBox.height <= 36, 'The search field is compact and still has a usable target')
    assert.ok(searchBox.y >= headingBox.y + headingBox.height && searchBox.y + searchBox.height <= listBox.y,
      'The search field sits between the title and scrolling rows')
    const rows = list.getByRole('option')
    await rows.first().waitFor()
    assert.ok((await rows.first().boundingBox()).height <= 50, 'Rows have the reference drawer density')
    const [nameBox, idBox, roleBox] = await Promise.all(
      ['strong', 'code', '.ks-map-visible-role'].map(selector => rows.first().locator(selector).boundingBox()))
    assert.ok(idBox.y > nameBox.y && Math.abs(nameBox.x - idBox.x) < 1 && roleBox.x >= nameBox.x + nameBox.width,
      'The ID sits below the name and the role has its own column on the right')
    const initial = await rows.allTextContents()
    assert.ok(initial.length > 0 && initial.length < 60, 'The initial view does not mount all 5,000 rows')

    const scroller = list
    await scroller.scrollIntoViewIfNeeded()
    if (!options.isMobile) await scroller.hover()
    const searchPosition = await search.boundingBox()
    if (options.isMobile) await scroller.evaluate(root => { root.scrollTop = 1800 })
    else await page.mouse.wheel(0, 1800)
    await page.waitForFunction(root => root.scrollTop > 0, await scroller.elementHandle())
    await page.waitForFunction(({ root, first }) => root.querySelector('[role="option"]')?.textContent !== first,
      { root: await list.elementHandle(), first: initial[0] })
    assert.deepEqual(await search.boundingBox(), searchPosition, 'The search field does not scroll with the rows')
    assert.ok(await rows.count() < 60, 'Scrolling does not accumulate mounted rows')
    assert.ok(await scroller.evaluate(root => root.scrollWidth === root.clientWidth))

    await scroller.evaluate(root => { root.scrollTop = root.scrollHeight })
    const finalNode = list.getByRole('option', { name: /Zakopane 4971/ })
    await finalNode.waitFor()
    assert.equal(await finalNode.getAttribute('aria-posinset'), '5000')
    assert.ok(await rows.count() < 60)

    await list.press('Home')
    await page.waitForFunction(root => root.scrollTop === 0, await list.elementHandle())
    await list.press('End')
    await finalNode.waitFor()
    assert.equal(await list.getAttribute('aria-activedescendant'), await finalNode.getAttribute('id'))
    await search.fill('REGIONAL-0036')
    const inView = list.getByRole('option', { name: /Klaipėda 0036/ })
    await inView.waitFor()
    assert.equal(await rows.count(), 1)
    if (options.isMobile) await inView.tap()
    else {
      await search.press('Tab')
      assert.ok(await list.evaluate(root => root === document.activeElement))
      await list.press('Enter')
    }
    await showcase.getByRole('region', { name: 'Selected node' }).getByText('Klaipėda 0036', { exact: true }).waitFor()
    await page.waitForFunction(() => {
      const map = window.overlapMap
      return !map.isMoving() && map.getZoom() >= 13 && Math.abs(map.getCenter().lng - 21.14) < 0.00001
    })

    await search.fill('regional-0037')
    await panel.getByText(/no .*match/i).waitFor()
    assert.equal(await rows.count(), 0)
    await search.fill('')
    await panel.getByText(/36 nodes in view/).waitFor()
    await list.focus()
    await page.evaluate(() => window.overlapMap.jumpTo({ center: [-35, 35], zoom: 10 }))
    await panel.getByText('No nodes in view.', { exact: true }).waitFor()
    assert.ok(await panel.evaluate(root => root.contains(document.activeElement)), 'Focus stays in the panel when results leave the view')
    assert.ok(await showcase.evaluate(root => root.scrollWidth <= root.clientWidth))
  }, { ...options, diagnostics: false }, exposeOverlapMap)
})

test('regional simulated packets move concurrently without moving the camera or selection', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: '5,000 regional nodes', exact: true }).click()
    const traffic = showcase.getByRole('button', { name: 'Simulated traffic', exact: true })
    const selection = showcase.getByRole('region', { name: 'Selected node' })
    const camera = () => page.evaluate(() => [window.overlapMap.getCenter().toArray(), window.overlapMap.getZoom()])
    await page.waitForFunction(() => window.overlapMap.getSource('markers').getClusterOptions().cluster && window.overlapMap.isSourceLoaded('markers'))
    const view = await camera()
    assert.equal(await traffic.getAttribute('aria-pressed'), 'false')
    await traffic.press('Space')
    assert.equal(await traffic.getAttribute('aria-pressed'), 'true')
    const concurrent = await frameMatching(page, () => {
      const heads = window.overlapMap.queryRenderedFeatures({ layers: ['preview-flow-head'] })
      const routes = [...new Set(heads.map(head => head.properties.route))]
      return routes.length >= 3 && routes
    })
    await page.waitForFunction(() => window.overlapMap.queryRenderedFeatures({ layers: ['preview-flow-arrival'] }).length > 0)
    await page.waitForFunction(previous => {
      const heads = window.overlapMap.queryRenderedFeatures({ layers: ['preview-flow-head'] })
      const routes = heads.map(head => head.properties.route)
      return routes.some(id => previous.includes(id)) && routes.some(id => !previous.includes(id))
    }, concurrent)
    assert.deepEqual(await camera(), view)
    assert.ok(await selection.getByText('No node selected.', { exact: true }).isVisible())
    await traffic.press('Space')
    assert.equal(await traffic.getAttribute('aria-pressed'), 'false')
    await page.waitForFunction(async () => (await window.overlapMap.getSource('preview-flow').getData()).features.length === 0)
  }, { diagnostics: false, reducedMotion: 'no-preference' }, exposeOverlapMap)
})

test('regional simulated traffic refreshes static routes with reduced motion and clears on scenario change', async (t) => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: '5,000 regional nodes', exact: true }).click()
    await showcase.getByText('Routes shown without animation.', { exact: true }).waitFor()
    const trafficButton = showcase.getByRole('button', { name: 'Simulated traffic', exact: true })
    await page.waitForFunction(button => !button.disabled, await trafficButton.elementHandle())
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const entries = await agent.query({ text: 'Simulated traffic' })
      const traffic = entries.find(entry => entry.label === 'Simulated traffic')
      if (!traffic) return null
      if (!traffic.actions.includes('setPressed')) return { ok: false, error: { message: traffic.actions.join(', ') } }
      return agent.act({ steps: [{ ref: traffic.ref, action: 'setPressed', args: [true] }] })
    })
    assert.equal(result?.ok, true, result?.error?.message ?? 'Simulated traffic toggle not discovered')
    assert.equal(await trafficButton.getAttribute('aria-pressed'), 'true')
    const first = await frameMatching(page, async () => {
      const map = window.overlapMap
      const { features } = await map.getSource('preview-flow').getData()
      if (!features.length) return false
      const { features: nodes } = await map.getSource('markers').getData()
      const positions = new Set(nodes.map(node => String(node.geometry.coordinates)))
      const routes = Object.groupBy(features.filter(feature => feature.properties.kind === 'trail'), feature => feature.properties.route)
      const stops = Object.values(routes).map(trails =>
        [trails[0].geometry.coordinates[0], ...trails.map(trail => trail.geometry.coordinates[1])].map(String))
      return { stops, ids: Object.keys(routes), atFixtureNodes: stops.flat().every(stop => positions.has(stop)), kinds: [...new Set(features.map(feature => feature.properties.kind))].sort() }
    })
    assert.deepEqual(first.kinds, ['arrival', 'trail'])
    assert.ok(first.atFixtureNodes)
    assert.equal(first.stops.length, 2)
    assert.ok(first.stops.every(route => route.length >= 2 && new Set(route).size === route.length))
    await page.waitForFunction(async (previous) => {
      const { features } = await window.overlapMap.getSource('preview-flow').getData()
      const routes = [...new Set(features.filter(feature => feature.properties.kind === 'trail').map(trail => trail.properties.route))]
      return routes.length === 2 && routes.filter(id => previous.includes(id)).length === 1
        && !features.some(feature => feature.properties.kind === 'head')
    }, first.ids)
    await showcase.getByRole('button', { name: 'Distant dots', exact: true }).click()
    await page.waitForFunction(async () => (await window.overlapMap.getSource('preview-flow').getData()).features.length === 0)
    await showcase.getByRole('button', { name: '5,000 regional nodes', exact: true }).click()
    assert.equal(await showcase.getByRole('button', { name: 'Simulated traffic' }).getAttribute('aria-pressed'), 'false')
  }, {}, async page => {
    if (await prepareMapAgent(page, t) === false) return false
    await exposeOverlapMap(page)
  })
})

test('the UI SDK agent can find and use the compact visible-node search', async (t) => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: '5,000 regional nodes', exact: true }).click()
    const panel = showcase.getByRole('region', { name: 'Visible nodes', exact: true })
    await panel.getByRole('listbox', { name: 'Nodes in view' }).waitFor()
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const entries = await agent.query({ text: 'Search visible nodes' })
      const search = entries.find(entry => entry.label === 'Search visible nodes')
      return search && agent.act({ steps: [{ ref: search.ref, action: 'setValue', args: ['regional-0036'] }] })
    })
    assert.equal(result?.ok, true, result?.error?.message ?? 'Visible-node search not discovered')
    assert.equal(await panel.getByRole('searchbox', { name: 'Search visible nodes' }).inputValue(), 'regional-0036')
    await panel.getByRole('option', { name: /Klaipėda 0036/ }).waitFor()
    assert.equal(await panel.getByRole('option').count(), 1)
  }, {}, page => prepareMapAgent(page, t))
})

test('overlapping map badges hide the glyphs and outlines underneath them in both themes', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Node states', exact: true }).click()
    await showcase.getByRole('button', { name: 'Every role & fallback', exact: true }).click()
    await frameMatching(page, () => window.renderedMap?.sceneId === 'role-specimens' && window.renderedMap.roleIds.length === 7)

    for (const theme of ['Light', 'Dark']) {
      await showcase.getByRole('radio', { name: theme, exact: true }).click()
      await showcase.locator(`.map-preview[data-map-theme="${theme.toLowerCase()}"][data-basemap-state="ready"]`).waitFor()
      const matches = await page.evaluate(async () => {
        const map = window.overlapMap
        const source = map.getSource('markers')
        const original = await source.getData()
        const [first, second] = original.features
        const canvas = map.getCanvas()
        const center = { x: Math.floor(canvas.clientWidth / 2), y: Math.floor(canvas.clientHeight / 2) }
        map.jumpTo({ zoom: 10.5 })
        const at = (feature, x) => ({ ...feature, geometry: { type: 'Point', coordinates: map.unproject([center.x + x, center.y]).toArray() } })
        const left = at(first, 0), right = at(second, 8), coincident = at(second, 0)
        const image = document.createElement('canvas')
        image.width = 40
        image.height = 24
        const ctx = image.getContext('2d', { willReadFrequently: true })
        async function draw(...features) {
          await source.setData({ type: 'FeatureCollection', features })
          await map.once('idle')
          return new Promise(resolve => {
            map.once('render', () => {
              const scale = canvas.width / canvas.clientWidth
              ctx.clearRect(0, 0, 40, 24)
              ctx.drawImage(canvas, (center.x - 12) * scale, (center.y - 12) * scale, 40 * scale, 24 * scale, 0, 0, 40, 24)
              resolve(ctx.getImageData(0, 0, 40, 24).data)
            })
            map.triggerRepaint()
          })
        }
        function badgeInteriorMatches(actual, expected, x) {
          for (let dy = -7; dy <= 7; dy++) for (let dx = -7; dx <= 7; dx++) {
            if (dx * dx + dy * dy > 49) continue
            const pixel = 4 * ((12 + dy) * 40 + 12 + x + dx)
            for (let channel = 0; channel < 4; channel++) if (actual[pixel + channel] !== expected[pixel + channel]) return false
          }
          return true
        }

        const leftAlone = await draw(left)
        const coincidentAlone = await draw(coincident)
        const rightAlone = await draw(right)
        const exact = await draw(left, coincident)
        const partial = await draw(left, right)
        const bothDrawn = new Set(map.queryRenderedFeatures({ layers: ['preview-role'] }).map(({ properties }) => properties.previewId)).size === 2
        await source.setData(original)
        return {
          distinctArtwork: !badgeInteriorMatches(leftAlone, coincidentAlone, 0),
          exact: badgeInteriorMatches(exact, leftAlone, 0) || badgeInteriorMatches(exact, coincidentAlone, 0),
          partial: badgeInteriorMatches(partial, leftAlone, 0) || badgeInteriorMatches(partial, rightAlone, 8),
          bothDrawn,
        }
      })
      assert.deepEqual(matches, { distinctArtwork: true, exact: true, partial: true, bothDrawn: true }, theme)
    }
  }, {}, exposeOverlapMap)
})

test('a failed WebGL initialization leaves evidence available and map retry restores it', async () => {
  await withMap(async (page, showcase) => {
    const retry = showcase.getByRole('button', { name: 'Retry map', exact: true })
    await retry.waitFor()
    assert.ok(await showcase.getByRole('heading', { name: 'Raw hop chain' }).isVisible())
    assert.ok(await showcase.getByText('LT-KM ROOT ED', { exact: true }).first().isVisible())
    await page.evaluate(() => { window.failWebGL = false })
    await retry.press('Enter')
    await frameMatching(page, () => window.renderedMap?.pathIds.includes('single-strong'))
    assert.equal(await retry.count(), 0)
  }, {}, page => page.addInitScript(() => {
    window.failWebGL = true
    const getContext = HTMLCanvasElement.prototype.getContext
    HTMLCanvasElement.prototype.getContext = function (context, ...options) {
      return window.failWebGL && /webgl/.test(context) ? null : getContext.call(this, context, ...options)
    }
  }))
})

test('losing WebGL during playback keeps the inspector usable and retry restores the map', async () => {
  await withMap(async (page, showcase) => {
    const button = name => showcase.getByRole('button', { name, exact: true })
    await button('Packet flow').click()
    await button('Loop preview').click()
    await frameMatching(page, () => window.renderedFlow?.routes[0]?.kinds.includes('trail'))
    await showcase.getByLabel('Moving packet & trail fixture map', { exact: true }).evaluate(async canvas => {
      const extension = canvas.getContext('webgl2').getExtension('WEBGL_lose_context')
      if (!extension) throw new Error('WebGL context-loss extension unavailable')
      const lost = new Promise(resolve => canvas.addEventListener('webglcontextlost', resolve, { once: true }))
      extension.loseContext()
      await lost
    })

    await button('Retry map').waitFor()
    assert.ok(await button('Loop preview').isDisabled())
    assert.ok(await showcase.getByText('No preview running.', { exact: true }).isVisible())
    await button('Path evidence').click()
    assert.ok(await showcase.getByRole('heading', { name: 'Raw hop chain' }).isVisible())
    await showcase.getByRole('button', { name: /LT-KM ROOT ED/ }).press('Enter')
    await showcase.getByText('Selected on map: LT-KM ROOT ED', { exact: true }).waitFor()
    await page.evaluate(() => { delete window.renderedMap })
    await button('Retry map').press('Enter')
    await frameMatching(page, () => window.renderedMap?.pathIds.includes('single-strong'))
    assert.equal(await button('Retry map').count(), 0)
    await button('Packet flow').click()
    await frameMatching(page, () => window.renderedFlow?.sceneId === 'route' && window.renderedFlow.routes.length === 0)
    assert.ok(await button('Loop preview').isEnabled())
  })
})

test('on a 320px touch viewport the keyboard can choose a scenario without horizontal overflow', async () => {
  await withMap(async (page, showcase) => {
    const scenario = showcase.getByRole('combobox', { name: 'Scenario', exact: true })
    await scenario.press('Enter')
    await page.getByRole('option', { name: 'Single weak', exact: true }).waitFor()
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('Enter')
    const frame = await frameMatching(page, () => window.renderedMap?.sceneId === 'weak' && window.renderedMap)
    assert.equal(frame.weakPathIds.length, 1)
    assert.deepEqual(frame.markerIds.sort(), [root, vilijampole])
    assert.ok(await scenario.evaluate(node => document.activeElement === node))
    assert.ok(await showcase.evaluate(node => node.scrollWidth <= node.clientWidth))
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth === innerWidth))
    await showcase.getByRole('button', { name: /LT-KM ROOT ED/ }).tap()
    await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id), root)
  }, { viewport: { width: 320, height: 720 }, hasTouch: true, isMobile: true })
})

test('all six packet-flow scenes render statically under OS reduced motion and clear on pause', async () => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('button', { name: 'Packet flow', exact: true }).click()
    const catalogue = showcase.getByRole('complementary', { name: 'Scenario catalogue' })
    assert.equal(await catalogue.getByRole('button').count(), flowCases.length)
    assert.ok(await showcase.getByRole('checkbox', { name: 'Reduced-motion preview' }).isDisabled())
    assert.ok(await showcase.getByText(/Your system requests reduced motion/).isVisible())
    assert.ok(await showcase.getByRole('button', { name: 'Clear', exact: true }).isDisabled())
    for (const [name, sceneId, routes, kinds] of flowCases) {
      await catalogue.getByRole('button', { name, exact: true }).click()
      await showcase.getByRole('button', { name: 'Loop preview', exact: true }).click()
      const rendered = await frameMatching(page, ({ sceneId, routes }) => window.renderedFlow?.sceneId === sceneId &&
        window.renderedFlow.routes.length === routes && window.renderedFlow, { sceneId, routes })
      assert.deepEqual(rendered.routes.map(({ kinds: variants }) => variants), Array.from({ length: routes }, () => kinds), name)
      await showcase.getByRole('button', { name: 'Pause & clear', exact: true }).press('Enter')
      await frameMatching(page, id => window.renderedFlow?.sceneId === id && window.renderedFlow.routes.length === 0, sceneId)
    }
    assert.ok(await showcase.getByText(/not a node location/).isVisible())
    await showcase.getByRole('button', { name: 'Play once', exact: true }).click()
    await frameMatching(page, () => window.renderedFlow?.routes[0]?.kinds.includes('schematic'))
    await showcase.getByText('No preview running.', { exact: true }).waitFor()
    await frameMatching(page, () => window.renderedFlow?.routes.length === 0)
  })
})

test('packet playback moves, clears when timing or scene changes, and play-once expires', async () => {
  await withMap(async (page, showcase) => {
    const button = name => showcase.getByRole('button', { name, exact: true })
    const head = () => window.renderedFlow?.sceneId === 'route' && window.renderedFlow.routes.some(({ kinds }) => kinds.includes('head'))
    const cleared = () => window.renderedFlow?.routes.length === 0
    await button('Packet flow').click()
    await button('Loop preview').click()
    await frameMatching(page, head)
    await showcase.getByRole('slider', { name: 'Time per hop' }).press('Home')
    await frameMatching(page, cleared)
    assert.ok(await button('Clear').isDisabled())
    await button('Play once').click()
    await frameMatching(page, head)
    await showcase.getByText('No preview running.', { exact: true }).waitFor()
    await frameMatching(page, cleared)
    await button('Loop preview').click()
    await frameMatching(page, head)
    await button('Single-node arrival').click()
    await frameMatching(page, () => window.renderedFlow?.sceneId === 'arrival' && window.renderedFlow.routes.length === 0)
    await button('Play once').click()
    await frameMatching(page, () => window.renderedFlow?.routes[0]?.kinds.includes('arrival'))
    await button('Clear').click()
    await frameMatching(page, cleared)
  }, { reducedMotion: 'no-preference' })
})

test('the UI SDK agent can discover map controls, choose a scenario, and change playback', async (t) => {
  await withMap(async (page, showcase) => {
    const scenario = showcase.getByRole('combobox', { name: 'Scenario', exact: true })
    await scenario.waitFor()
    const category = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const mode = await agent.query({ text: 'Packet flow' })
      const packetFlow = Array.isArray(mode) && mode.find(({ label }) => label === 'Packet flow')
      return packetFlow && await agent.act({ steps: [{ ref: packetFlow.ref, action: 'press' }] })
    })
    assert.equal(category?.ok, true, category?.error?.message ?? 'Packet flow control not found')
    await showcase.getByRole('heading', { name: 'Moving packet & trail', exact: true }).waitFor()
    const outcome = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const selectors = await agent.query({ within: 'Scenario' })
      const selector = Array.isArray(selectors) && selectors.find(({ label }) => label === 'Scenario')
      const arrival = selector?.items?.find(({ label }) => label === 'Single-node arrival')
      const selected = arrival && await agent.act({ steps: [{ ref: selector.ref, action: 'setValue', args: [arrival.value] }] })
      return { selected, selectors }
    })
    assert.equal(outcome.selected?.ok, true, outcome.selected?.error?.message ?? `Scenario choice not found: ${JSON.stringify(outcome.selectors)}`)
    await frameMatching(page, () => window.renderedMap?.sceneId === 'arrival')
    const actions = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = []
      for (const text of ['Time per hop', 'Reduced-motion preview', 'Zoom in', 'Fit example', 'Play once', 'Loop preview', 'Clear']) {
        const matches = await agent.query({ text })
        const control = Array.isArray(matches) && matches.find(({ label }) => label === text)
        if (!control) return { missing: text }
        controls.push(control)
      }
      const [timing, motion, zoom, fit, , loop, clear] = controls
      const setup = await agent.act({ steps: [
        { ref: timing.ref, action: 'setValue', args: [500] },
        { ref: motion.ref, action: 'setChecked', args: [true] },
        { ref: zoom.ref, action: 'press' },
        { ref: fit.ref, action: 'press' },
        { ref: loop.ref, action: 'press' },
      ] })
      return { setup, clear: clear.ref }
    })
    assert.equal(actions.setup?.ok, true, actions.setup?.error?.message ?? `Missing control: ${actions.missing}`)
    assert.equal(await showcase.getByRole('slider', { name: 'Time per hop' }).inputValue(), '500')
    assert.equal(await showcase.getByRole('checkbox', { name: 'Reduced-motion preview' }).getAttribute('aria-checked'), 'true')
    await frameMatching(page, () => window.renderedFlow?.sceneId === 'arrival' && window.renderedFlow.routes[0]?.kinds.includes('arrival'))
    const clear = await page.evaluate(async ref => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref, action: 'press' }] })
    }, actions.clear)
    assert.equal(clear.ok, true, clear.error?.message)
    await frameMatching(page, () => window.renderedFlow?.routes.length === 0)
  }, { viewport: { width: 390, height: 780 }, reducedMotion: 'no-preference' }, page => prepareMapAgent(page, t))
})

test('the UI SDK agent discovers an overlap count and selects a node through the fan', async (t) => {
  await withMap(async (page, showcase) => {
    await showcase.getByRole('combobox', { name: 'Scenario', exact: true }).waitFor()
    const overlapSetup = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const press = async text => {
        const controls = await agent.query({ text })
        const control = controls.find(control => control.label === text)
        return control && agent.act({ steps: [{ ref: control.ref, action: 'press' }] })
      }
      const mode = await press('Node states')
      if (!mode?.ok) return mode
      const selectors = await agent.query({ within: 'Scenario' })
      const selector = selectors.find(control => control.label === 'Scenario')
      const overlap = selector?.items?.find(item => item.label === 'Overlapping markers')
      return overlap && agent.act({ steps: [{ ref: selector.ref, action: 'setValue', args: [overlap.value] }] })
    })
    assert.equal(overlapSetup?.ok, true, overlapSetup?.error?.message ?? 'Overlap scenario not discovered')
    await frameMatching(page, () => window.renderedMap?.sceneId === 'overlap' && window.renderedMap.raisedIds.length === 0)
    assert.equal(await showcase.getByRole('button', { name: 'Spiderfy', exact: true }).getAttribute('aria-pressed'), 'false')
    const enabled = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = await agent.query({ text: 'Spiderfy' })
      const toggle = controls.find(control => control.label === 'Spiderfy')
      return toggle && agent.act({ steps: [{ ref: toggle.ref, action: 'press' }] })
    })
    assert.equal(enabled?.ok, true, enabled?.error?.message ?? 'Spiderfy toggle not discovered')
    await showcase.getByRole('button', { name: 'Choose 8 overlapping nodes', exact: true }).waitFor()
    const opened = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = await agent.query({ text: 'Choose 8 overlapping nodes' })
      const trigger = controls.find(control => control.label === 'Choose 8 overlapping nodes')
      return trigger && agent.act({ steps: [{ ref: trigger.ref, action: 'press' }] })
    })
    assert.equal(opened?.ok, true, opened?.error?.message ?? 'Overlap count not discovered')
    const fan = showcase.getByRole('dialog', { name: '8 overlapping nodes', exact: true })
    await fan.waitFor()
    const picked = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = await agent.query({ text: 'Specimen 2 · Repeater · Repeater' })
      const choice = controls.find(control => control.label === 'Specimen 2 · Repeater · Repeater')
      return choice && agent.act({ steps: [{ ref: choice.ref, action: 'press' }] })
    })
    assert.equal(picked?.ok, true, picked?.error?.message ?? 'Fan node not discovered')
    const selected = await frameMatching(page, () => window.renderedMap?.selectedNameplateIds.includes('specimen-1') && window.renderedMap)
    assert.deepEqual(selected.raisedIds, ['specimen-1'])
    assert.deepEqual(selected.selectedNameplateIds, ['specimen-1'])
    await fan.waitFor({ state: 'hidden' })
    assert.ok(await showcase.getByText('Selected on map: Specimen 2 · Repeater', { exact: true }).isVisible())
  }, { viewport: { width: 390, height: 780 }, reducedMotion: 'no-preference' }, page => prepareMapAgent(page, t))
})

async function prepareMapAgent(page, t) {
    const source = await page.request.get(`${origin}/src/main.tsx`)
    if (!source.headers()['content-type']?.includes('javascript')) {
      t.skip('The agent harness is a Vite development module')
      return false
    }
  await page.route(diagnosticsUrl, async route => {
      const response = await route.fetch()
      const html = await response.text()
      const body = html.replace(/src="\/src\/main\.tsx(?:\?[^"]*)?"/, 'src="/tests/kitchen-sink-agent.mjs"')
      assert.notEqual(body, html)
      await route.fulfill({ response, body })
    })
}
