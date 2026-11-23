import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
let browser

before(async () => {
  browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium',
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] })
})
after(async () => { await browser?.close() })

async function withMap(options, verify, prepare) {
  const context = await browser.newContext({ reducedMotion: 'reduce', serviceWorkers: 'block', ...options })
  const errors = []
  const unexpected = []
  await context.routeWebSocket('**/*', socket => socket.close())
  await context.route('**/*', route => {
    const target = new URL(route.request().url())
    if (target.origin === 'https://tiles.openfreemap.org') return route.abort()
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)) {
      unexpected.push(target.href)
      return route.abort()
    }
    return route.continue()
  })
  const page = await context.newPage()
  page.setDefaultTimeout(10000)
  page.on('pageerror', error => errors.push(error.message))
  await page.addInitScript(() => {
    document.addEventListener('map-preview-rendered', event => { window.renderedMap = event.detail })
  })
  try {
    await prepare?.(page)
    assert.equal((await page.goto(`${origin}/map?mapDiagnostics`)).status(), 200)
    await page.getByRole('heading', { name: 'Map', exact: true, level: 1 }).waitFor()
    await page.getByRole('region', { name: 'Map workspace', exact: true }).waitFor()
    await page.waitForFunction(() => window.renderedMap?.zoom !== undefined)
    await verify(page)
    assert.deepEqual(errors, [])
    assert.deepEqual(unexpected, [])
  } finally { await context.close() }
}

test('map route uses the full viewport and a real map in both application themes', async () => {
  for (const colorScheme of ['light', 'dark']) {
    await withMap({ viewport: { width: 1440, height: 1000 }, colorScheme }, async page => {
      assert.equal(await page.getByRole('main').count(), 1)
      assert.match(await page.getByRole('main').innerText(), /fixture/i)
      const canvas = await page.locator('.maplibregl-canvas').boundingBox()
      assert.ok(canvas.width >= 1000 && canvas.height >= 450, 'map fills the working area rather than a specimen card')
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
      assert.equal(await page.evaluate(() => document.documentElement.scrollHeight > innerHeight + 1), false)
      assert.equal(await page.locator('.map-preview').getAttribute('data-map-state'), 'ready')
      assert.equal(await page.locator('.map-preview').getAttribute('data-map-theme'), colorScheme)
      assert.doesNotMatch(await page.getByRole('main').innerText(), /Live packets/)
      await page.getByRole('button', { name: 'Zoom in', exact: true }).click()
      await page.getByRole('button', { name: 'Fit all nodes', exact: true }).click()
      assert.equal(await page.getByRole('region', { name: 'Map settings', exact: true }).count(), 0, 'fit is available without opening settings')
    })
  }
})

test('node search selects the existing regional fixture on the actual map', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    await page.getByRole('button', { name: 'Find node', exact: true }).click()
    const finder = page.getByRole('region', { name: "What's active", exact: true })
    await finder.getByRole('searchbox', { name: 'Name or node ID', exact: true }).fill('regional-0001')
    await finder.getByRole('option', { name: /Klaipėda 0001/ }).click()
    await page.waitForFunction(() => window.renderedMap?.selectedNameplateIds?.includes('regional-0001'))
    await finder.getByRole('region', { name: 'Node overview', exact: true }).waitFor()
    await finder.getByRole('heading', { name: 'Klaipėda 0001', exact: true, level: 2 }).waitFor()
    await finder.getByRole('button', { name: 'Clear selection', exact: true }).click()
    await page.waitForFunction(() => !window.renderedMap?.selectedNameplateIds?.includes('regional-0001'))
  })
})

test('workspace has no document scrolling while packet and node panels change state', async () => {
  for (const colorScheme of ['light', 'dark']) {
    for (const width of [1440, 320]) {
      await withMap({ viewport: { width, height: 900 }, colorScheme, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
        const assertNoScroll = async () => {
          const geometry = await page.evaluate(() => {
            window.scrollTo(100, 100)
            return { x: scrollX, y: scrollY, width: document.documentElement.scrollWidth,
              height: document.documentElement.scrollHeight, viewportWidth: innerWidth, viewportHeight: innerHeight }
          })
          assert.deepEqual([geometry.x, geometry.y], [0, 0], JSON.stringify(geometry))
          assert.equal(geometry.height, geometry.viewportHeight)
          assert.equal(geometry.width, geometry.viewportWidth)
        }
        await assertNoScroll()
        await page.getByRole('button', { name: 'Hide packets', exact: true }).click()
        await assertNoScroll()
        await page.getByRole('button', { name: 'Show packets', exact: true }).click()
        const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
        await handle.focus()
        await page.keyboard.press('ArrowUp')
        await assertNoScroll()
        const showNodes = page.getByRole('button', { name: 'Show nodes', exact: true })
        if (await showNodes.isVisible()) await showNodes.click()
        await page.getByRole('listbox', { name: 'Nodes in view', exact: true }).waitFor()
        await assertNoScroll()
      })
    }
  }
})

test('active-panel actions are accessible ghost icon buttons on desktop and touch screens', async () => {
  for (const width of [1440, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const show = page.getByRole('button', { name: 'Show nodes', exact: true })
      if (await show.isVisible()) await show.click()
      const panel = page.getByRole('region', { name: "What's active", exact: true })
      const headings = () => panel.getByRole('heading').evaluateAll(nodes => nodes.filter(node => !node.classList.contains('ks-visually-hidden')).map(node => [...node.childNodes].filter(child => child.nodeType === Node.TEXT_NODE).map(child => child.textContent).join('')))
      assert.deepEqual(await headings(), ['Visible nodes'])
      for (const name of ['Find node', 'Clear selection']) {
        const button = panel.getByRole('button', { name, exact: true })
        assert.equal(await button.innerText(), '')
        assert.equal(await button.getAttribute('title'), name)
        assert.ok(await button.locator('svg[aria-hidden="true"]').isVisible())
        await page.mouse.move(width - 1, 899)
        await button.evaluate(node => node.blur())
        const style = await button.evaluate(node => {
          const computed = getComputedStyle(node)
          return { background: computed.backgroundColor, border: computed.borderTopColor }
        })
        assert.equal(style.background, 'rgba(0, 0, 0, 0)')
        assert.equal(style.border, 'rgba(0, 0, 0, 0)')
        const bounds = await button.boundingBox()
        const toggle = await page.getByRole('button', { name: 'Hide nodes', exact: true }).boundingBox()
        assert.equal(bounds.width, toggle.width, `${name} matches the panel toggle width`)
        const header = await panel.locator('.map-active-header').boundingBox()
        assert.ok(Math.abs(toggle.y - header.y) < 1, 'edge tab starts at the titlebar top')
        assert.ok(Math.abs(toggle.height - header.height) < 1, 'edge tab fills the titlebar height')
        const icon = await button.locator('svg').boundingBox()
        const toggleIcon = await page.getByRole('button', { name: 'Hide nodes', exact: true }).locator('svg').boundingBox()
        assert.equal(icon.width, toggleIcon.width)
        assert.equal(icon.height, toggleIcon.height)
        assert.deepEqual(await headings(), [name === 'Find node' ? 'Visible nodes' : 'Find node'])
        if (width < 500) {
          assert.ok(bounds.width >= 44 && bounds.height >= 44)
        }
        await button.click()
        if (name === 'Find node') assert.ok(await panel.getByRole('searchbox', { name: 'Name or node ID' }).evaluate(node => node === document.activeElement))
      }
      await panel.getByRole('listbox', { name: 'Nodes in view', exact: true }).waitFor()
      assert.equal(await panel.getByRole('searchbox').count(), 0)
      await panel.getByRole('option').first().click()
      await panel.getByRole('region', { name: 'Node overview', exact: true }).waitFor()
      const title = panel.getByRole('heading', { level: 2 })
      assert.equal(await title.evaluate(node => getComputedStyle(node).outlineStyle), 'none', 'pointer selection does not outline the title')
      await panel.getByRole('button', { name: 'Clear selection', exact: true }).click()
      const list = panel.getByRole('listbox', { name: 'Nodes in view', exact: true })
      await list.focus()
      await page.keyboard.press('Home')
      await page.keyboard.press('Enter')
      await panel.getByRole('region', { name: 'Node overview', exact: true }).waitFor()
      assert.ok(await title.evaluate(node => node === document.activeElement && getComputedStyle(node).outlineStyle !== 'none'), 'keyboard selection keeps visible title focus')
      for (const fontSize of [16, 24]) {
        await page.evaluate(size => { document.documentElement.style.fontSize = `${size}px` }, fontSize)
        await page.waitForFunction(() => {
          const header = document.querySelector('.map-active-header').getBoundingClientRect()
          const toggle = document.querySelector('.map-workspace-node-toggle').getBoundingClientRect()
          return Math.abs(toggle.y - header.y) < 1 && Math.abs(toggle.height - header.height) < 1
        })
      }
    })
  }
})

test('workspace starts with individual nodes rather than clusters', async () => {
  for (const colorScheme of ['light', 'dark']) {
    await withMap({ viewport: { width: 1440, height: 900 }, colorScheme }, async page => {
      const state = await page.evaluate(() => ({
        clustered: window.workspaceMap.getSource('markers').getClusterOptions().cluster,
        clusters: window.workspaceMap.queryRenderedFeatures({ layers: ['preview-cluster'] }).length,
        nodes: window.workspaceMap.queryRenderedFeatures({ layers: ['preview-marker'] }).length,
      }))
      assert.equal(state.clustered, false)
      assert.equal(state.clusters, 0)
      assert.ok(state.nodes > 0)
    }, async page => {
      await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
        const response = await route.fetch()
        const source = await response.text()
        assert.ok(source.includes('map = new MapLibre('))
        await route.fulfill({ response, body: source.replace('map = new MapLibre(', 'map = window.workspaceMap = new MapLibre(') })
      })
    })
  }
})

test('dark node dots use subdued role colors and dark outlines, and theme switching restores light markers', async () => {
  await withMap({ viewport: { width: 1440, height: 900 }, colorScheme: 'dark' }, async page => {
    const paint = () => page.evaluate(() => ({
      fill: window.workspaceMap.getPaintProperty('preview-marker', 'circle-color'),
      edge: window.workspaceMap.getPaintProperty('preview-marker', 'circle-stroke-color'),
      width: window.workspaceMap.getPaintProperty('preview-marker', 'circle-stroke-width'),
    }))
    const dark = await paint()
    assert.equal(dark.edge[2], '#1f1f1f')
    assert.ok(JSON.stringify(dark.fill).includes('interpolate'), 'dark dots blend ink with their darker role fill')
    assert.ok(JSON.stringify(dark.fill).includes('roleDarkFill'))
    await page.getByRole('button', { name: 'Map settings', exact: true }).click()
    await page.getByRole('radio', { name: 'Light', exact: true }).click()
    await page.waitForFunction(() => document.querySelector('.map-preview').dataset.mapTheme === 'light')
    const light = await paint()
    assert.equal(light.edge[2], '#fff')
    assert.equal(JSON.stringify(light.fill).includes('interpolate'), false)
    assert.deepEqual(light.width, dark.width, 'theme does not change marker geometry')
    await page.getByRole('radio', { name: 'Dark', exact: true }).click()
    await page.waitForFunction(() => document.querySelector('.map-preview').dataset.mapTheme === 'dark')
    assert.deepEqual(await paint(), dark)
  }, async page => {
    await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
      const response = await route.fetch()
      const source = await response.text()
      assert.ok(source.includes('map = new MapLibre('))
      await route.fulfill({ response, body: source.replace('map = new MapLibre(', 'map = window.workspaceMap = new MapLibre(') })
    })
  })
})

test('map framing and labels use shared neutrals while interactive clusters use the brand accent', async () => {
  for (const colorScheme of ['light', 'dark']) {
    await withMap({ viewport: { width: 1440, height: 900 }, colorScheme }, async page => {
      const colors = await page.evaluate(() => {
        const map = window.workspaceMap
        const preview = document.querySelector('.map-preview')
        const probe = document.createElement('span')
        preview.append(probe)
        const resolve = value => {
          probe.style.color = value
          return getComputedStyle(probe).color
        }
        const result = {
          ground: getComputedStyle(document.querySelector('.map-preview-stage')).backgroundColor,
          sharedGround: resolve('var(--ks-ground)'),
          label: resolve(map.getPaintProperty('preview-name', 'text-color')),
          sharedInk: resolve('var(--ks-ink)'),
          halo: resolve(map.getPaintProperty('preview-name', 'text-halo-color')),
          sharedPaper: resolve('var(--ks-paper)'),
          cluster: resolve(map.getPaintProperty('preview-cluster', 'circle-color')),
          accent: resolve('var(--ks-accent)'),
          grid: resolve('var(--map-grid)'),
          brand: getComputedStyle(document.querySelector('.app-brand .ks-mesh-mark')).color,
          selectedNavigation: getComputedStyle(document.querySelector('.app-nav-link[aria-current="page"]')).color,
        }
        probe.remove()
        return result
      })
      assert.equal(colors.ground, colors.sharedGround, 'fallback ground belongs to the shared surface palette')
      assert.equal(colors.label, colors.sharedInk, 'map names use ordinary text, not a green status color')
      assert.equal(colors.halo, colors.sharedPaper)
      for (const color of [colors.cluster, colors.brand, colors.selectedNavigation]) assert.equal(color, colors.accent)
      for (const color of [colors.ground, colors.label, colors.grid]) {
        const [r, g, b] = color.match(/[\d.]+/g).slice(0, 3).map(Number)
        assert.ok(Math.max(r, g, b) - Math.min(r, g, b) <= 13, `${colorScheme}: neutral framing must not become a chromatic surface`)
        assert.ok(b >= g && g >= r, `${colorScheme}: neutral framing must not have a green cast`)
      }
    }, async page => {
      await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
        const response = await route.fetch()
        const source = await response.text()
        assert.ok(source.includes('map = new MapLibre('))
        await route.fulfill({ response, body: source.replace('map = new MapLibre(', 'map = window.workspaceMap = new MapLibre(') })
      })
    })
  }
})

test('overlapping nodes use a readable finder and return to global search with keyboard focus', async () => {
  for (const width of [1440, 390, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const hide = page.getByRole('button', { name: 'Hide nodes', exact: true })
      if (await hide.isVisible()) await hide.click()
      await page.evaluate(() => new Promise(resolve => {
        const map = window.workspaceMap
        map.once('idle', resolve)
        map.jumpTo({ center: [21.14, 55.71], zoom: 8 })
      }))
      const canvas = page.locator('.maplibregl-canvas')
      await canvas.focus()
      await page.keyboard.press('Enter')
      const finder = page.getByRole('region', { name: "What's active", exact: true })
      const search = finder.getByRole('searchbox')
      await search.waitFor()
      assert.ok(await search.evaluate(node => node === document.activeElement))
      const all = finder.getByRole('button', { name: 'Clear selection', exact: true })
      await all.waitFor()
      await search.press('Escape')
      assert.ok(await canvas.evaluate(node => node === document.activeElement))
      await page.keyboard.press('Enter')
      await all.waitFor()
      const results = finder.getByRole('listbox', { name: 'Nodes in cluster', exact: true })
      assert.ok(await results.getByRole('option').count() > 0)
      assert.equal(Number(await results.getByRole('option').first().getAttribute('aria-setsize')), 36)
      const resultBounds = await results.boundingBox()
      const rows = await results.getByRole('option').evaluateAll(nodes => nodes.map(node => node.getBoundingClientRect().toJSON()))
      assert.ok(rows.filter(row => row.y >= resultBounds.y && row.y + row.height <= resultBounds.y + resultBounds.height).length >= 3,
        'at least three complete candidates fit without scrolling')
      const searchBounds = await search.boundingBox()
      await results.evaluate(node => { node.scrollTop = node.scrollHeight })
      assert.deepEqual(await search.boundingBox(), searchBounds, 'results scroll without moving the search field')
      const panel = await page.locator('.map-workspace-node-panel').boundingBox()
      for (const control of [search, all]) {
        const bounds = await control.boundingBox()
        assert.ok(bounds.x >= panel.x && bounds.x + bounds.width <= panel.x + panel.width + 1)
      }
      const renderedRows = await results.getByRole('option').evaluateAll(nodes => nodes.map(node => node.getBoundingClientRect().toJSON()))
      assert.ok(renderedRows.every(bounds => bounds.x >= panel.x && bounds.x + bounds.width <= panel.x + panel.width + 1),
        JSON.stringify({ width, panel, renderedRows }))
      assert.equal(await finder.evaluate(node => node.scrollWidth > node.clientWidth), false)
      await all.click()
      assert.equal(await finder.getByRole('searchbox').count(), 0)
      await finder.getByRole('listbox', { name: 'Nodes in view', exact: true }).getByRole('option', { name: /Klaipėda 0001/ }).click()
      await page.waitForFunction(() => window.renderedMap?.selectedNameplateIds?.includes('regional-0001'))
    }, async page => {
      await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
        const response = await route.fetch()
        await route.fulfill({ response, body: (await response.text()).replace('map = new MapLibre(', 'map = window.workspaceMap = new MapLibre(') })
      })
    })
  }
})

test('active panel switches from visible nodes to node overview and back on desktop and touch screens', async () => {
  for (const width of [1440, 390, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const showNodes = page.getByRole('button', { name: 'Show nodes', exact: true })
      if (await showNodes.isVisible()) await showNodes.click()
      const panel = page.getByRole('region', { name: "What's active", exact: true })
      const list = panel.getByRole('listbox', { name: 'Nodes in view', exact: true })
      await list.waitFor()
      const name = await list.getByRole('option').first().innerText()
      await list.focus()
      await page.keyboard.press('Home')
      await page.keyboard.press('Enter')
      const overview = panel.getByRole('region', { name: 'Node overview', exact: true })
      await overview.waitFor()
      assert.equal(await panel.getByRole('heading', { level: 2 }).innerText(), name.split('\n')[0])
      assert.equal(await panel.getByRole('listbox').count(), 0)
      await panel.getByRole('button', { name: 'Clear selection', exact: true }).click()
      await list.waitFor()
      assert.equal(await overview.count(), 0)
      assert.equal(await panel.evaluate(node => node.scrollWidth > node.clientWidth), false)
    })
  }
})

test('actual clustered map clicks list the complete cluster and individual map clicks show node overview', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    await page.getByRole('button', { name: 'Map settings', exact: true }).click()
    await page.getByRole('radiogroup', { name: 'Clustering', exact: true }).getByRole('radio', { name: 'On', exact: true }).click()
    await page.keyboard.press('Escape')
    await page.evaluate(() => new Promise(resolve => {
      const map = window.workspaceMap
      map.once('idle', resolve)
      map.jumpTo({ center: [21.14, 55.71], zoom: 8 })
    }))
    const canvas = page.locator('.maplibregl-canvas')
    await canvas.focus()
    await page.keyboard.press('Enter')
    const list = page.getByRole('listbox', { name: 'Nodes in cluster', exact: true })
    await list.waitFor()
    assert.equal(Number(await list.getByRole('option').first().getAttribute('aria-setsize')), 36)
    await list.focus()
    await page.keyboard.press('End')
    await page.keyboard.press('Enter')
    const node = page.getByRole('region', { name: 'Node overview', exact: true })
    await node.waitFor()
    assert.ok(await page.getByRole('region', { name: "What's active", exact: true }).getByRole('heading', { level: 2 }).evaluate(element => element === document.activeElement), 'selection moves focus to the context heading rather than a detached list')
    await page.getByRole('button', { name: 'Clear selection', exact: true }).click()
    await page.getByRole('button', { name: 'Map settings', exact: true }).click()
    await page.getByRole('radiogroup', { name: 'Clustering', exact: true }).getByRole('radio', { name: 'Off', exact: true }).click()
    await page.keyboard.press('Escape')
    await page.evaluate(() => new Promise(resolve => {
      const map = window.workspaceMap
      map.once('idle', resolve)
      const node = map.getSource('markers').serialize().data.features.find(feature => feature.properties.name.startsWith('Kaunas'))
      map.jumpTo({ center: node.geometry.coordinates, zoom: 18 })
    }))
    const point = await page.evaluate(() => {
      const map = window.workspaceMap
      const feature = map.queryRenderedFeatures({ layers: ['preview-marker'] })[0]
      const p = map.project(feature.geometry.coordinates)
      return { x: p.x, y: p.y }
    })
    const bounds = await canvas.boundingBox()
    await page.mouse.click(bounds.x + point.x, bounds.y + point.y)
    await node.waitFor()
    await page.getByRole('table', { name: 'Packet receptions' }).locator('tr[data-reception-id]').first().locator('td').first().click()
    await page.getByRole('region', { name: 'Packet overview', exact: true }).waitFor()
    assert.equal(await node.count(), 0, 'packet replaces node context')
    assert.equal(await page.getByRole('listbox', { name: 'Nodes in cluster' }).count(), 0)
  }, async page => {
    await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
      const response = await route.fetch()
      await route.fulfill({ response, body: (await response.text()).replace('map = new MapLibre(', 'map = window.workspaceMap = new MapLibre(') })
    })
  })
})

test('workspace settings preserve map controls and shell theme sync', async () => {
  await withMap({ viewport: { width: 1024, height: 900 }, colorScheme: 'light' }, async page => {
    const settings = page.getByRole('button', { name: 'Map settings', exact: true })
    await settings.press('Enter')
    const clusters = page.getByRole('radiogroup', { name: 'Clustering', exact: true })
    assert.equal(await clusters.getByRole('radio', { name: 'Off', exact: true }).getAttribute('aria-checked'), 'true')
    await clusters.getByRole('radio', { name: 'On', exact: true }).press('Space')
    assert.equal(await clusters.getByRole('radio', { name: 'On', exact: true }).getAttribute('aria-checked'), 'true')
    await clusters.getByRole('radio', { name: 'Off', exact: true }).press('Space')
    const themes = page.getByRole('radiogroup', { name: 'Map theme', exact: true })
    await themes.getByRole('radio', { name: 'Dark', exact: true }).press('Space')
    await page.waitForFunction(() => document.querySelector('.map-preview')?.getAttribute('data-map-theme') === 'dark')
    await themes.getByRole('radio', { name: 'Light', exact: true }).press('Space')
    await page.waitForFunction(() => document.querySelector('.map-preview')?.getAttribute('data-map-theme') === 'light')
    const initialZoom = await page.evaluate(() => window.renderedMap.zoom)
    await page.getByRole('button', { name: 'Zoom in', exact: true }).click()
    await page.waitForFunction(zoom => window.renderedMap.zoom >= zoom + 0.9, initialZoom)
    await page.getByRole('button', { name: 'Zoom out', exact: true }).click()
    await page.waitForFunction(zoom => Math.abs(window.renderedMap.zoom - zoom) < 0.05, initialZoom)
    await page.getByRole('button', { name: 'Zoom in', exact: true }).click()
    await page.waitForFunction(zoom => window.renderedMap.zoom >= zoom + 0.9, initialZoom)
    const beforeFit = await page.evaluate(() => window.renderedMap.zoom)
    await page.getByRole('button', { name: 'Fit all nodes', exact: true }).click()
    await page.waitForFunction(zoom => window.renderedMap.zoom < zoom - 0.5, beforeFit)
    assert.ok(await page.evaluate(() => {
      const map = window.workspaceMap
      const bounds = map.getBounds()
      return map.getSource('markers').serialize().data.features.every(feature => bounds.contains(feature.geometry.coordinates))
    }), 'Fit all nodes frames the complete regional fixture')
    await themes.getByRole('radio', { name: 'Light', exact: true }).focus()
    await page.keyboard.press('Escape')
    assert.ok(await settings.evaluate(node => node === document.activeElement))
    await page.getByRole('combobox', { name: 'Appearance', exact: true }).click()
    await page.getByRole('option', { name: 'Dark', exact: true }).click()
    await page.waitForFunction(() => document.querySelector('.map-preview')?.getAttribute('data-map-theme') === 'dark')
  }, async page => {
    await page.route(/\/src\/components\/map\/map-preview\.tsx(?:\?.*)?$/, async route => {
      const response = await route.fetch()
      await route.fulfill({ response, body: (await response.text()).replace('map = new MapLibre(', 'map = window.workspaceMap = new MapLibre(') })
    })
  })
})

test('settings use an icon rail and concise nonmodal panel that follows the packet dock', async () => {
  for (const width of [1440, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const settings = page.getByRole('button', { name: 'Map settings', exact: true })
      const layers = page.getByRole('button', { name: 'Map layers', exact: true })
      assert.ok(await layers.isEnabled(), 'layers opens a panel even while its source is unavailable')
      assert.equal(await layers.innerText(), '', 'layers uses an accessible icon-only button')
      const layersBounds = await layers.boundingBox()
      const settingsBounds = await settings.boundingBox()
      assert.ok(layersBounds.y + layersBounds.height <= settingsBounds.y, 'layers sits above settings')
      assert.ok(Math.abs(layersBounds.x - settingsBounds.x) < 1)
      const zoomIn = page.getByRole('button', { name: 'Zoom in', exact: true })
      const zoomOut = page.getByRole('button', { name: 'Zoom out', exact: true })
      const fit = page.getByRole('button', { name: 'Fit all nodes', exact: true })
      const rail = await Promise.all([settings, zoomIn, zoomOut, fit].map(control => control.boundingBox()))
      assert.ok(rail[0].y + rail[0].height <= rail[1].y && rail[1].y + rail[1].height <= rail[2].y + 1)
      assert.ok(rail[2].y + rail[2].height < rail[3].y, 'fit is a separate action below the zoom pair')
      assert.ok(rail.every(box => Math.abs(box.x - rail[0].x) < 1), 'settings, zoom and fit share a vertical axis')
      assert.equal(await settings.innerText(), '', 'settings trigger is icon-only')
      assert.equal(await fit.innerText(), '', 'fit uses an icon with an accessible name')
      await fit.click()
      assert.ok(await zoomIn.isEnabled())
      assert.ok(await zoomOut.isEnabled())
      if (width < 500) assert.ok(rail.every(box => box.width >= 44 && box.height >= 44))
      await settings.click()
      const panel = page.getByRole('region', { name: 'Map settings', exact: true })
      await panel.waitFor()
      assert.equal(await settings.getAttribute('aria-expanded'), 'true')
      const panelControls = await panel.getByRole('radio').evaluateAll(nodes => nodes.map(node => {
        const bounds = node.getBoundingClientRect()
        const group = node.closest('[role="radiogroup"]').getBoundingClientRect()
        return { name: node.textContent, width: bounds.width, groupWidth: group.width, clipped: node.scrollWidth > node.clientWidth }
      }))
      assert.ok(panelControls.every(control => control.width >= control.groupWidth * 0.4 && !control.clipped),
        `panel choices must fill their segments without clipped labels: ${JSON.stringify(panelControls)}`)
      assert.equal(await page.getByRole('dialog').count(), 0)
      assert.equal((await panel.innerText()).toLowerCase().replace(/\s+/g, ' '), 'map settings map theme light dark clustering on off')
      const labelTypography = await panel.locator('legend').evaluateAll(nodes => nodes.map(node => {
        const style = getComputedStyle(node)
        return { family: style.fontFamily, size: style.fontSize, weight: style.fontWeight,
          transform: style.textTransform, spacing: style.letterSpacing }
      }))
      const expectedLabel = await page.evaluate(() => ({ family: getComputedStyle(document.documentElement).getPropertyValue('--ks-sans').trim() }))
      assert.ok(labelTypography.every(style => style.transform === 'none' && style.spacing === 'normal' && style.weight === '400'
        && style.size === '12px' && style.family === expectedLabel.family), JSON.stringify(labelTypography))
      assert.equal(await panel.getByRole('button', { name: 'Fit all nodes', exact: true }).count(), 0)
      const nodes = page.getByRole('region', { name: "What's active", exact: true })
      assert.equal(await nodes.isVisible(), width >= 768, 'desktop keeps both panels; mobile shows one at a time')
      const assertPanelBounds = async () => {
        const bounds = await panel.boundingBox()
        const view = await page.locator('.map-preview-stage').boundingBox()
        assert.ok(Math.abs(bounds.y - view.y - 8) < 2)
        assert.ok(Math.abs(bounds.y + bounds.height - (view.y + view.height - 8)) < 2, 'settings fills available map height')
        assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width)
        assert.equal(await page.evaluate(() => document.documentElement.scrollHeight), 900)
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), width)
      }
      await assertPanelBounds()
      const initialHeight = (await panel.boundingBox()).height
      const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
      await handle.focus()
      await page.keyboard.press('Home')
      assert.ok((await panel.boundingBox()).height < initialHeight)
      await assertPanelBounds()
      await page.evaluate(() => { document.documentElement.style.fontSize = '24px' })
      const readableChoices = await panel.getByRole('radio').evaluateAll(nodes => nodes.map(node => {
        const range = document.createRange()
        range.selectNodeContents(node)
        const text = range.getBoundingClientRect()
        const bounds = node.getBoundingClientRect()
        return text.width <= bounds.width && text.height <= parseFloat(getComputedStyle(node).lineHeight) + 1
      }))
      assert.ok(readableChoices.every(Boolean), 'enlarged mobile labels remain single-line within their controls')
      const body = panel.locator('.map-preview-settings-body')
      assert.ok(await body.evaluate(node => node.scrollHeight > node.clientHeight), 'short settings body scrolls internally at enlarged text')
      await panel.getByRole('radio', { name: 'Off', exact: true }).focus()
      await page.keyboard.press('Escape')
      await panel.waitFor({ state: 'hidden' })
      assert.ok(await settings.evaluate(node => node === document.activeElement))
      await page.evaluate(() => { document.documentElement.style.fontSize = '' })
      await settings.press('Enter')
      await panel.getByRole('button', { name: 'Close map settings', exact: true }).click()
      assert.ok(await settings.evaluate(node => node === document.activeElement))
      await settings.click()
      const canvas = page.locator('.maplibregl-canvas')
      await canvas.click({ position: { x: 3, y: (await canvas.boundingBox()).height / 2 } })
      assert.ok(await panel.isVisible(), 'nonmodal settings does not dismiss on map interaction')
      if (width < 500) {
        await page.getByRole('button', { name: 'Show nodes', exact: true }).click()
        assert.equal(await panel.isVisible(), false)
        assert.ok(await nodes.isVisible())
        await settings.click()
        await page.getByRole('table', { name: 'Packet receptions' }).locator('tr[data-reception-id]').first().locator('td').first().click()
        assert.equal(await panel.isVisible(), false)
        await nodes.getByRole('region', { name: 'Packet overview', exact: true }).waitFor()
      } else {
        await page.setViewportSize({ width: 600, height: 900 })
        await nodes.waitFor({ state: 'hidden' })
        assert.equal(await nodes.isVisible(), false, 'resizing narrow resolves simultaneous panels')
        assert.ok(await panel.isVisible())
      }
    })
  }
})

test('layers opens a panel with a disabled MeshMapper control and coordinates with other panels', async () => {
  for (const width of [1440, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const layers = page.getByRole('button', { name: 'Map layers', exact: true })
      const settings = page.getByRole('button', { name: 'Map settings', exact: true })
      const panel = page.getByRole('region', { name: 'Map layers', exact: true })
      const settingsPanel = page.getByRole('region', { name: 'Map settings', exact: true })
      const nodes = page.getByRole('region', { name: "What's active", exact: true })
      await layers.press('Enter')
      await panel.waitFor()
      assert.equal(await layers.getAttribute('aria-expanded'), 'true')
      assert.equal((await panel.innerText()).replace(/\s+/g, ' '), 'Map layers MeshMapper')
      const meshMapper = panel.getByRole('switch', { name: 'MeshMapper', exact: true })
      assert.ok(await meshMapper.isDisabled())
      assert.equal(await meshMapper.getAttribute('aria-checked'), 'false')
      await meshMapper.dispatchEvent('click')
      assert.equal(await meshMapper.getAttribute('aria-checked'), 'false', 'disabled control cannot enable a layer')
      assert.equal(await nodes.isVisible(), width >= 768)
      const bounds = await panel.boundingBox()
      const view = await page.locator('.map-preview-stage').boundingBox()
      assert.ok(Math.abs(bounds.height - view.height + 16) < 2)
      assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width)
      await page.keyboard.press('Escape')
      await panel.waitFor({ state: 'hidden' })
      assert.ok(await layers.evaluate(node => node === document.activeElement))
      await layers.click()
      await panel.getByRole('button', { name: 'Close map layers', exact: true }).click()
      assert.ok(await layers.evaluate(node => node === document.activeElement))
      await settings.click()
      await layers.click()
      assert.equal(await settingsPanel.isVisible(), false)
      assert.ok(await panel.isVisible())
      await settings.click()
      assert.equal(await panel.isVisible(), false)
      assert.ok(await settingsPanel.isVisible())
      await layers.click()
      if (width < 500) {
        await page.getByRole('button', { name: 'Show nodes', exact: true }).click()
        assert.equal(await panel.isVisible(), false)
        assert.ok(await nodes.isVisible())
      } else {
        await page.setViewportSize({ width: 600, height: 900 })
        await nodes.waitFor({ state: 'hidden' })
        assert.ok(await panel.isVisible())
      }
      assert.equal(await page.evaluate(() => document.documentElement.scrollHeight), 900)
    })
  }
})

test('rail actions remain fully reachable with enlarged touch text and maximum packet height', async () => {
  await withMap({ viewport: { width: 320, height: 900 }, isMobile: true, hasTouch: true }, async page => {
    await page.evaluate(() => { document.documentElement.style.fontSize = '24px' })
    const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
    for (const state of ['closed', 'settings', 'layers', 'nodes']) {
      if (state === 'settings') await page.getByRole('button', { name: 'Map settings', exact: true }).click()
      if (state === 'layers') await page.getByRole('button', { name: 'Map layers', exact: true }).click()
      if (state === 'nodes') await page.getByRole('button', { name: 'Show nodes', exact: true }).click()
      await handle.focus()
      await page.keyboard.press('Home')
      for (const name of ['Map layers', 'Map settings', 'Zoom in', 'Zoom out', 'Fit all nodes']) {
        const control = page.getByRole('button', { name, exact: true })
        await control.focus()
        const visible = await control.evaluate(node => {
          const bounds = node.getBoundingClientRect()
          const rail = node.closest('.map-preview-workspace-rail').getBoundingClientRect()
          return bounds.top >= rail.top - 1 && bounds.bottom <= rail.bottom + 1 && bounds.height >= 44
        })
        assert.ok(visible, `${name} is fully exposed in ${state} state`)
      }
      assert.equal(await page.evaluate(() => document.documentElement.scrollHeight), 900)
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 320)
    }
  })
})

test('incoming packet drawer opens directly to the table without search or filtering chrome', async () => {
  for (const width of [1440, 1024, 390, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const body = page.locator('.map-workspace-packet-body')
      const table = body.getByRole('table', { name: 'Packet receptions', exact: true })
      const assertTableOnly = async () => {
        await table.waitFor()
        assert.equal(await body.getByRole('searchbox').count(), 0)
        assert.equal(await body.locator('.ks-traffic-filters, .ks-traffic-overview, .time-histogram').count(), 0)
        assert.equal(await body.getByRole('button', { name: /filters/i }).count(), 0)
        const bounds = await table.boundingBox()
        const header = await page.locator('.map-workspace-packet-header').boundingBox()
        assert.ok(Math.abs(bounds.y - header.y - header.height) < 3, 'table starts directly below dock header')
        const scroller = await body.locator('[data-packet-table-scroll]').boundingBox()
        const content = await body.boundingBox()
        assert.ok(Math.abs(scroller.height - content.height) < 3, 'table fills available dock height')
      }
      await assertTableOnly()
      await page.getByRole('button', { name: 'Hide packets', exact: true }).click()
      await page.getByRole('button', { name: 'Show packets', exact: true }).click()
      await assertTableOnly()
      if (width < 500) {
        const scroller = body.locator('[data-packet-table-scroll]')
        await scroller.evaluate(node => { node.scrollTop = node.scrollHeight })
        await page.waitForFunction(node => Number(node.getAttribute('aria-rowcount')) > 32, await table.elementHandle())
      }
    })
  }
})

test('ordinary mouse-wheel scrolling zooms the workspace map without a modifier', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    const initial = await page.evaluate(() => window.renderedMap.zoom)
    const canvas = await page.locator('.maplibregl-canvas').boundingBox()
    await page.mouse.move(canvas.x + canvas.width * 0.65, canvas.y + canvas.height * 0.5)
    await page.mouse.wheel(0, -450)
    await page.waitForFunction(zoom => window.renderedMap.zoom > zoom + 0.1, initial)
    assert.equal(await page.evaluate(() => scrollY), 0)
    assert.equal(await page.locator('.maplibregl-cooperative-gesture-screen').isVisible(), false)
  })
})

test('the visible-node list scrolls independently of the map', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    const list = page.getByRole('listbox', { name: 'Nodes in view', exact: true })
    await list.waitFor()
    const initialZoom = await page.evaluate(() => window.renderedMap.zoom)
    const header = page.locator('.map-active-header')
    const headerBounds = await header.boundingBox()
    assert.equal(await page.getByRole('region', { name: "What's active", exact: true }).getByRole('searchbox').count(), 0)
    const count = header.locator('.map-active-count')
    assert.equal(Number((await count.innerText()).replaceAll(',', '')), Number(await list.getByRole('option').first().getAttribute('aria-setsize')))
    await list.hover()
    await page.mouse.wheel(0, 800)
    await page.waitForFunction(node => node.scrollTop > 0, await list.elementHandle())
    assert.deepEqual(await header.boundingBox(), headerBounds)
    assert.equal(await page.evaluate(() => window.renderedMap.zoom), initialZoom)
    assert.equal(await page.evaluate(() => scrollY), 0)
  })
})

test('active panel fills the map height with spacing and follows packet dock resizing', async () => {
  for (const width of [1440, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const show = page.getByRole('button', { name: 'Show nodes', exact: true })
      if (await show.isVisible()) await show.click()
      const geometry = async () => {
        const stage = await page.locator('.map-preview-stage').boundingBox()
        const panel = await page.locator('.map-workspace-node-panel').boundingBox()
        assert.ok(Math.abs(panel.y - stage.y - 8) < 1, 'small top inset')
        assert.ok(Math.abs(stage.y + stage.height - panel.y - panel.height - 8) < 1, 'small bottom inset')
        return panel
      }
      const initial = await geometry()
      const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
      await handle.focus()
      await page.keyboard.press('ArrowUp')
      assert.ok((await geometry()).height < initial.height)
      await page.getByRole('button', { name: 'Hide packets', exact: true }).click()
      assert.ok((await geometry()).height > initial.height)
      await page.getByRole('button', { name: 'Show packets', exact: true }).click()
      await geometry()
    })
  }
})

test('incoming fixture receptions activate packet overview in the side panel and clear stale node context', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    const table = page.getByRole('table', { name: 'Packet receptions', exact: true })
    await table.waitFor()
    const first = table.locator('tr[data-reception-id]').first()
    const id = await first.getAttribute('data-reception-id')
    await first.locator('td').first().click()
    const panel = page.getByRole('region', { name: "What's active", exact: true })
    const details = panel.getByRole('region', { name: 'Packet overview', exact: true })
    await details.waitFor()
    await panel.getByRole('heading', { name: 'Packet overview', level: 2, exact: true }).waitFor()
    assert.equal(await details.getByRole('heading').count(), 0, 'packet overview has no duplicate body title')
    assert.equal(await table.locator('tr[data-selected]').getAttribute('data-reception-id'), id)
    const code = await first.locator('td').first().innerText()
    assert.ok((await details.innerText()).includes(code))
    assert.equal(await page.getByRole('region', { name: 'Reception details', exact: true }).count(), 0)
    await first.focus()
    await page.keyboard.press('ArrowDown')
    const second = table.locator('tr[data-selected]')
    assert.notEqual(await second.getAttribute('data-reception-id'), id)
    assert.ok(await second.evaluate(node => node === document.activeElement), 'packet browsing keeps focus in the table')
    assert.ok((await details.innerText()).includes(await second.locator('td').first().innerText()))
    await panel.getByRole('button', { name: 'Clear selection', exact: true }).click()
    await details.waitFor({ state: 'hidden' })
    const scroller = page.locator('[data-packet-table-scroll]')
    await scroller.evaluate(node => { node.scrollTop = node.scrollHeight })
    await page.waitForFunction(node => Number(node.getAttribute('aria-rowcount')) > 32, await table.elementHandle())
    await page.getByRole('button', { name: 'Hide packets', exact: true }).click()
    await table.waitFor({ state: 'hidden' })
    await page.getByRole('button', { name: 'Show packets', exact: true }).click()
    await table.waitFor()
    assert.match(await page.getByRole('main').innerText(), /not filtered by map position/i)
  })
})

test('mobile map and dock fit above the bottom navigation in both themes', async () => {
  for (const width of [320, 390]) for (const colorScheme of ['light', 'dark']) {
    await withMap({ viewport: { width, height: 844 }, colorScheme, isMobile: true, hasTouch: true }, async page => {
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
      assert.equal(await page.evaluate(() => document.documentElement.scrollHeight > innerHeight + 1), false)
      const canvas = await page.locator('.maplibregl-canvas').boundingBox()
      assert.ok(canvas.width >= width - 4 && canvas.height >= 250)
      assert.ok(canvas.y + canvas.height <= 844, 'map fits the viewport')
      const nav = page.getByRole('navigation', { name: 'Main navigation', exact: true })
      assert.ok(await nav.isVisible())
      const navBounds = await nav.boundingBox()
      assert.ok(Math.abs(navBounds.y + navBounds.height - 844) < 1)
      assert.equal(await nav.getByRole('link', { name: 'Map', exact: true }).getAttribute('aria-current'), 'page')
      const dockBounds = await page.locator('.map-workspace-packets').boundingBox()
      assert.ok(dockBounds.y + dockBounds.height <= navBounds.y + 1, 'navigation never covers packet rows')
      for (const label of ['Packets', 'Channels', 'Map', 'Nodes', 'More']) {
        const item = nav.getByRole(label === 'More' ? 'button' : 'link', { name: label, exact: true })
        assert.ok(await item.locator('span').isVisible(), `${label} keeps its visible label`)
        assert.ok((await item.boundingBox()).height >= 44)
      }
      const zoom = page.getByRole('button', { name: 'Zoom in', exact: true })
      assert.ok(await zoom.isVisible())
      await zoom.tap()
      assert.equal(await page.locator('.map-workspace-fixture').count(), 0)
      assert.ok(await page.getByRole('button', { name: 'Retry basemap', exact: true }).isVisible(), 'failed basemap retains recovery')
      const headers = await page.getByRole('table', { name: 'Packet receptions', exact: true }).getByRole('columnheader')
        .evaluateAll(nodes => nodes.filter(node => node.checkVisibility()).map(node => node.textContent))
      assert.ok(headers.some(header => header.includes('Summary')), 'mobile table includes a readable summary')
      assert.ok(headers.length <= 4, 'mobile receptions use compact columns')
    })
  }
})

test('incoming packets resize by pointer and keyboard and retain size when hidden', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
    const pane = page.locator('.map-workspace-packets')
    const initial = await pane.boundingBox()
    const grip = await handle.boundingBox()
    await page.mouse.move(grip.x + grip.width / 2, grip.y + grip.height / 2)
    await page.mouse.down()
    await page.mouse.move(grip.x + grip.width / 2, grip.y - 100, { steps: 8 })
    await page.mouse.up()
    const dragged = await pane.boundingBox()
    assert.ok(dragged.height > initial.height + 75)
    await handle.focus()
    const value = Number(await handle.getAttribute('aria-valuenow'))
    await page.keyboard.press('ArrowUp')
    assert.ok(Number(await handle.getAttribute('aria-valuenow')) < value)
    assert.ok((await pane.boundingBox()).height > dragged.height)
    const expanded = await pane.boundingBox()
    await page.getByRole('button', { name: 'Hide packets', exact: true }).click()
    assert.ok((await pane.boundingBox()).height < 65)
    await page.getByRole('button', { name: 'Show packets', exact: true }).click()
    assert.ok(Math.abs((await pane.boundingBox()).height - expanded.height) < 2)
    await handle.focus()
    await page.keyboard.press('Home')
    const maximized = await pane.boundingBox()
    assert.ok(maximized.height <= 750 && maximized.height > expanded.height)
    await page.keyboard.press('End')
    assert.ok((await pane.boundingBox()).height < maximized.height)
    assert.equal(await page.evaluate(() => document.documentElement.scrollHeight > innerHeight + 1), false)
  })
})

test('packet toggle is a centered ghost chevron when open and closed with keyboard activation', async () => {
  for (const width of [1440, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      const toggle = page.locator('.map-workspace-packet-toggle')
      const initialPane = await page.locator('.map-workspace-packets').boundingBox()
      const paths = []
      for (const expanded of [true, false]) {
        assert.equal(await toggle.getAttribute('aria-expanded'), String(expanded))
        assert.equal(await toggle.getAttribute('aria-label'), expanded ? 'Hide packets' : 'Show packets')
        assert.equal(await toggle.innerText(), '')
        await page.mouse.move(width - 1, 0)
        const style = await toggle.evaluate(node => {
          const style = getComputedStyle(node)
          return { background: style.backgroundColor, border: style.borderTopColor }
        })
        assert.deepEqual(style, { background: 'rgba(0, 0, 0, 0)', border: 'rgba(0, 0, 0, 0)' })
        const button = await toggle.boundingBox()
        const icon = await toggle.locator('svg').boundingBox()
        assert.equal(icon.width, 20)
        assert.equal(icon.height, 20)
        assert.ok(Math.abs(button.x + button.width / 2 - icon.x - icon.width / 2) < 0.5)
        assert.ok(Math.abs(button.y + button.height / 2 - icon.y - icon.height / 2) < 0.5)
        if (width < 500) assert.ok(button.width >= 44 && button.height >= 44)
        paths.push(await toggle.locator('path').getAttribute('d'))
        await toggle.focus()
        await page.keyboard.press(expanded ? 'Enter' : 'Space')
        assert.ok(await toggle.evaluate(node => node === document.activeElement))
        assert.equal(await toggle.getAttribute('aria-expanded'), String(!expanded))
        assert.equal(await page.locator('#incoming-packets-body').isVisible(), !expanded)
      }
      assert.notEqual(paths[0], paths[1])
      assert.ok(Math.abs((await page.locator('.map-workspace-packets').boundingBox()).height - initialPane.height) < 2)
    })
  }
})

test('packet dock shrinks to one reception row before snapping closed and restores its size', async () => {
  for (const width of [1440, 320]) {
    for (const fontSize of [16, 24]) {
      await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
        await page.evaluate(size => { document.documentElement.style.fontSize = `${size}px` }, fontSize)
        const pane = page.locator('.map-workspace-packets')
        const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
        const expected = await pane.evaluate(node => {
          const scroller = node.querySelector('[data-packet-table-scroll]')
          const row = scroller.querySelector('[data-reception-id]')
          return { collapsed: node.querySelector('.map-workspace-packet-header').getBoundingClientRect().height,
            minimum: node.querySelector('.map-workspace-packet-header').getBoundingClientRect().height
              + scroller.querySelector('thead').getBoundingClientRect().height
              + row.getBoundingClientRect().height + scroller.offsetHeight - scroller.clientHeight }
        })
        const resizeTo = async height => {
          const grip = await handle.boundingBox()
          const current = await pane.boundingBox()
          const x = grip.x + grip.width / 2
          const y = grip.y + grip.height / 2
          const target = y + current.height - height
          if (width < 500) {
            const session = await page.context().newCDPSession(page)
            await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] })
            await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: target }] })
            await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
            await session.detach()
          } else {
            await page.mouse.move(x, y)
            await page.mouse.down()
            await page.mouse.move(x, target, { steps: 6 })
            await page.mouse.up()
          }
        }
        await resizeTo(expected.minimum - 2)
        assert.equal(await pane.getAttribute('data-collapsed'), null)
        const minimum = await pane.boundingBox()
        assert.ok(Math.abs(minimum.height - expected.minimum) < 2, JSON.stringify({ width, fontSize, expected, minimum }))
        const scroller = pane.locator('[data-packet-table-scroll]')
        const capacity = await scroller.evaluate(node => {
          const row = node.querySelector('[data-reception-id]')
          const header = node.querySelector('thead').getBoundingClientRect()
          const rowBounds = row.getBoundingClientRect()
          const visibleBody = node.clientHeight - header.height
          node.scrollTop += rowBounds.y - header.bottom
          return { visibleBody, rowHeight: rowBounds.height }
        })
        assert.ok(Math.abs(capacity.visibleBody - capacity.rowHeight) < 2, 'table body has space for exactly one row, not a status row plus a reception')
        const row = await pane.locator('[data-reception-id]').first().boundingBox()
        assert.ok(row.y + row.height <= minimum.y + minimum.height + 1, 'one reception fits after scrolling past the feed-status marker')
        await resizeTo((expected.minimum + expected.collapsed) / 2 - 5)
        assert.notEqual(await pane.getAttribute('data-collapsed'), null)
        assert.ok(Math.abs((await pane.boundingBox()).height - expected.collapsed) < 2)
        await page.getByRole('button', { name: 'Show packets', exact: true }).click()
        assert.ok(Math.abs((await pane.boundingBox()).height - minimum.height) < 2, 'reopening restores the one-row size')
        await handle.focus()
        await page.keyboard.press('ArrowUp')
        const enlarged = await pane.boundingBox()
        assert.ok(enlarged.height > minimum.height)
        await page.keyboard.press('Enter')
        assert.notEqual(await pane.getAttribute('data-collapsed'), null)
        await page.keyboard.press('Enter')
        assert.ok(Math.abs((await pane.boundingBox()).height - enlarged.height) < 2)
        assert.equal(await page.evaluate(() => document.documentElement.scrollHeight), 900)
      })
    }
  }
})

test('ready workspace omits metadata chrome and collapsed packets fit their header', async () => {
  for (const width of [1440, 320]) {
    await withMap({ viewport: { width, height: 900 }, ...(width < 500 ? { isMobile: true, hasTouch: true } : {}) }, async page => {
      await page.locator('.map-preview[data-basemap-state="ready"]').waitFor()
      assert.equal(await page.locator('.map-preview-map-meta').count(), 0)
      assert.equal(await page.locator('.map-workspace-fixture').count(), 0)
      await page.getByRole('button', { name: 'Hide packets', exact: true }).click()
      const header = await page.locator('.map-workspace-packet-header').boundingBox()
      const pane = await page.locator('.map-workspace-packets').boundingBox()
      assert.ok(Math.abs(header.height - pane.height) < 2, 'collapsed dock has no empty residual strip')
      const nav = page.getByRole('navigation', { name: 'Main navigation' })
      assert.ok(Math.abs(pane.y + pane.height - (width < 500 ? (await nav.boundingBox()).y : 900)) < 2)
    }, async page => {
      await page.context().route('https://tiles.openfreemap.org/**', route => {
        const url = new URL(route.request().url())
        if (url.pathname.startsWith('/styles/')) return route.fulfill({ headers: { 'access-control-allow-origin': '*' }, json: { version: 8, sources: {},
          layers: [{ id: 'background', type: 'background', paint: { 'background-color': '#edf3ef' } }] } })
        if (url.pathname === '/planet') return route.fulfill({ headers: { 'access-control-allow-origin': '*' },
          json: { tilejson: '3.0.0', tiles: [], minzoom: 0, maxzoom: 14 } })
        return route.abort()
      })
    })
  }
})

test('small node edge toggle hides the panel without moving or reframing the map', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    const canvas = page.locator('.maplibregl-canvas')
    const initial = await canvas.boundingBox()
    const zoom = await page.evaluate(() => window.renderedMap.zoom)
    const toggle = page.getByRole('button', { name: 'Hide nodes', exact: true })
    const bounds = await toggle.boundingBox()
    const header = await page.locator('.map-active-header').boundingBox()
    assert.ok(bounds.width <= 32, 'desktop edge tab stays narrow')
    assert.ok(Math.abs(bounds.height - header.height) < 1, 'open tab matches the titlebar height')
    await toggle.click()
    await page.getByRole('listbox', { name: 'Nodes in view', exact: true }).waitFor({ state: 'hidden' })
    const show = page.getByRole('button', { name: 'Show nodes', exact: true })
    assert.ok((await show.boundingBox()).x < 20)
    assert.ok((await show.boundingBox()).height <= 32, 'collapsed tab retains compact control height')
    assert.deepEqual(await canvas.boundingBox(), initial)
    assert.equal(await page.evaluate(() => window.renderedMap.zoom), zoom)
    await show.press('Enter')
    await page.getByRole('listbox', { name: 'Nodes in view', exact: true }).waitFor()
    assert.deepEqual(await canvas.boundingBox(), initial)
  })
})

test('mobile incoming packets support touch resizing and activate the shared overview without a modal', async () => {
  await withMap({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }, async page => {
    const handle = page.getByRole('separator', { name: 'Resize incoming packets', exact: true })
    const pane = page.locator('.map-workspace-packets')
    const initial = await pane.boundingBox()
    const grip = await handle.boundingBox()
    const session = await page.context().newCDPSession(page)
    const x = Math.round(grip.x + grip.width / 2), y = Math.round(grip.y + grip.height / 2)
    await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] })
    await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: y - 100 }] })
    await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    assert.ok((await pane.boundingBox()).height > initial.height + 75)
    const table = page.getByRole('table', { name: 'Packet receptions', exact: true })
    await table.locator('tr[data-reception-id]').first().locator('td').first().tap()
    const panel = page.getByRole('region', { name: "What's active", exact: true })
    const overview = panel.getByRole('region', { name: 'Packet overview', exact: true })
    await overview.waitFor()
    const bounds = await panel.boundingBox()
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390)
    assert.equal(await page.getByRole('dialog', { name: 'Reception details', exact: true }).count(), 0)
    await panel.getByRole('button', { name: 'Clear selection', exact: true }).tap()
    await overview.waitFor({ state: 'hidden' })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  })
})

test('SDK agents can discover and operate the map workspace controls', async () => {
  await withMap({ viewport: { width: 1440, height: 1000 } }, async page => {
    await page.getByRole('button', { name: 'Find node', exact: true }).click()
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = agent.snapshot().filter(entry => !entry.offPage && entry.component !== 'more')
      const unnamed = controls.filter(entry => !entry.label).map(entry => entry.component)
      const find = label => {
        const entry = controls.find(entry => entry.label === label)
        if (!entry) throw new Error(`Missing SDK control: ${label}`)
        return entry.ref
      }
      const changed = await agent.act({ steps: [{ ref: find('Name or node ID'), action: 'setValue', args: ['regional-0001'] }] })
      return { unnamed, ok: changed.ok, error: changed.error?.message,
        resize: controls.find(entry => entry.label === 'Resize incoming packets')?.component }
    })
    assert.deepEqual(result.unnamed, [])
    assert.equal(result.ok, true, result.error)
    assert.ok(result.resize, 'resize handle is exposed to the SDK agent')
    await page.getByRole('option', { name: /Klaipėda 0001/ }).waitFor()
    await page.getByRole('button', { name: 'Map settings', exact: true }).click()
    const settingsResult = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = agent.snapshot().filter(entry => !entry.offPage && entry.component !== 'more')
      const required = ['Map settings', 'Close map settings', 'Map theme', 'Clustering', 'Fit all nodes', 'Zoom in', 'Zoom out']
      const missing = required.filter(label => !controls.some(entry => entry.label === label))
      const steps = [
        { label: 'Map theme', action: 'setValue', args: ['dark'] },
        { label: 'Clustering', action: 'setValue', args: ['on'] },
        { label: 'Zoom in', action: 'press' },
        { label: 'Zoom out', action: 'press' },
        { label: 'Fit all nodes', action: 'press' },
      ].map(({ label, ...step }) => ({ ref: controls.find(entry => entry.label === label)?.ref, ...step }))
      const changed = missing.length ? null : await agent.act({ steps })
      return { missing, unnamed: controls.filter(entry => !entry.label).map(entry => entry.component),
        ok: changed?.ok, error: changed?.error?.message }
    })
    assert.deepEqual(settingsResult.missing, [])
    assert.deepEqual(settingsResult.unnamed, [])
    assert.equal(settingsResult.ok, true, settingsResult.error)
    await page.waitForFunction(() => document.querySelector('.map-preview')?.getAttribute('data-map-theme') === 'dark')
    assert.equal(await page.getByRole('radiogroup', { name: 'Clustering', exact: true }).getByRole('radio', { name: 'On', exact: true }).getAttribute('aria-checked'), 'true')
    await page.getByRole('button', { name: 'Map layers', exact: true }).click()
    const layerControls = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const controls = agent.snapshot().filter(entry => !entry.offPage && entry.component !== 'more')
      return { unnamed: controls.filter(entry => !entry.label).map(entry => entry.component),
        meshMapper: controls.find(entry => entry.label === 'MeshMapper')?.component }
    })
    assert.deepEqual(layerControls.unnamed, [])
    assert.equal(layerControls.meshMapper, 'switch')
  }, async page => {
    await page.route(`${origin}/map?mapDiagnostics`, async route => {
      const response = await route.fetch()
      const html = await response.text()
      const body = html.replace(/src="\/src\/main\.tsx(?:\?[^"]*)?"/, 'src="/tests/kitchen-sink-agent.mjs"')
      assert.notEqual(body, html)
      await route.fulfill({ response, body })
    })
  })
})
