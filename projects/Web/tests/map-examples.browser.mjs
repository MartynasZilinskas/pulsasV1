import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
const url = `${origin}/kitchen-sink`
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
const nodeCases = ['Distant dots', 'Role glyphs', 'Labels & collision', 'Raised selection', 'Clusters & counts',
  'Every role & fallback', 'Overlapping markers', 'Long node label']
const flowCases = [
  ['Moving packet & trail', 'route', 1, ['arrival', 'trail']],
  ['Single-node arrival', 'arrival', 1, ['arrival']],
  ['Shared prefix & branches', 'branch', 2, ['arrival', 'trail']],
  ['Concurrent arrivals', 'concurrent', 2, ['arrival', 'trail']],
  ['Revisit & repeated arrival', 'revisit', 1, ['arrival', 'trail']],
  ['Schematic unresolved stop', 'schematic', 1, ['arrival', 'schematic', 'trail']],
]
let browser

before(async () => {
  browser = await chromium.launch({
    executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium',
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  })
})

after(async () => { await browser?.close() })

async function withMap(verify, options = {}, prepare) {
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
    assert.equal((await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 12000 })).status(), 200)
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
    const rawHop = showcase.getByRole('button', { name: /Hop 2.*7d8e31/i })
    await rawHop.press('Enter')
    await page.getByRole('dialog', { name: /Hop 2.*7d8e31/i }).getByRole('button', { name: /LT-KM Panerys C14A/ }).click()
    await frameMatching(page, id => window.renderedMap?.raisedIds.includes(id), panerys)
    assert.equal(await candidate.getAttribute('aria-checked'), 'true')
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
    await showcase.getByLabel('Overlapping markers fixture map', { exact: true }).click()
    await showcase.getByText('2 markers overlap.', { exact: false }).waitFor()
    await showcase.getByRole('button', { name: /Specimen 2 · Repeater/ }).press('Enter')
    await frameMatching(page, () => window.renderedMap?.raisedIds.includes('specimen-1'))
    const long = await choose('Long node label', 'long-label')
    assert.ok(long.labelIds.includes('specimen-0'))
  })
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
  }, { viewport: { width: 390, height: 780 }, reducedMotion: 'no-preference' }, async page => {
    const source = await page.request.get(`${origin}/src/main.tsx`)
    if (!source.headers()['content-type']?.includes('javascript')) {
      t.skip('The agent harness is a Vite development module')
      return false
    }
    await page.route(url, async route => {
      const response = await route.fetch()
      const html = await response.text()
      const body = html.replace('src="/src/main.tsx"', 'src="/tests/kitchen-sink-agent.mjs"')
      assert.notEqual(body, html)
      await route.fulfill({ response, body })
    })
  })
})
