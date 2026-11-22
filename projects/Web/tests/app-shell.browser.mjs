import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
const primarySections = ['Packets', 'Channels', 'Map', 'Nodes']
const overflowSections = ['Observers', 'Analytics']
let browser

before(async () => {
  browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' })
})

after(async () => {
  await browser?.close()
})

async function withShell(options, verify, useAgent = false) {
  const page = await browser.newPage({ ...options, reducedMotion: 'reduce' })
  const errors = []
  const disallowedRequests = []
  page.on('pageerror', (error) => errors.push(error.message))
  await page.routeWebSocket('**/*', (socket) => socket.close())
  await page.route('**/*', async (route) => {
    const request = route.request()
    const target = new URL(request.url())
    if (target.origin === 'https://tiles.openfreemap.org') return route.abort()
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)
      || (['fetch', 'xhr'].includes(request.resourceType()) && !target.pathname.startsWith('/src/'))) {
      disallowedRequests.push(target.href)
      return route.abort()
    }
    if (useAgent && request.isNavigationRequest()) {
      const response = await route.fetch()
      const html = await response.text()
      const body = html.replace(/src="\/src\/main\.tsx(?:\?[^"]*)?"/, 'src="/tests/kitchen-sink-agent.mjs"')
      assert.notEqual(body, html, 'Expected the Vite HTML to load /src/main.tsx')
      return route.fulfill({ response, body })
    }
    return route.continue()
  })
  try {
    assert.equal((await page.goto(`${origin}/packets`)).status(), 200)
    await page.getByRole('heading', { name: 'Packets', level: 1 }).waitFor()
    await verify(page)
    assert.deepEqual(errors, [])
    assert.deepEqual(disallowedRequests, [])
  } finally {
    await page.close()
  }
}

async function assertSection(page, name) {
  await page.getByRole('heading', { name, level: 1, exact: true }).waitFor()
  assert.equal(new URL(page.url()).pathname, `/${name.toLowerCase()}`)
  if (name === 'Map') {
    await page.getByRole('region', { name: 'Map workspace', exact: true }).waitFor()
    assert.match(await page.getByRole('main').innerText(), /fixture/i)
    return
  }
  assert.match(await page.getByRole('main').innerText(), /not built yet/)
  assert.match(await page.getByRole('main').innerText(), /no live data connection/)
}

async function assertNoOverflow(page) {
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'The shell must not overflow horizontally')
}

test('desktop header reaches every section and keeps an understated footer at the bottom', async () => {
  await withShell({ viewport: { width: 1440, height: 900 } }, async (page) => {
    const nav = page.getByRole('navigation', { name: 'Main navigation' })
    for (const name of [...primarySections, ...overflowSections]) {
      const link = nav.getByRole('link', { name, exact: true })
      await link.click()
      await assertSection(page, name)
      assert.equal(await link.getAttribute('aria-current'), 'page')
      assert.equal(await nav.locator('[aria-current="page"]').count(), 1)
    }
    const footer = page.getByRole('contentinfo')
    assert.ok(await footer.isVisible())
    assert.match(await footer.innerText(), /Pulsas\s*·\s*Atviras Tinklas/)
    const bounds = await footer.boundingBox()
    assert.equal(Math.round(bounds.y + bounds.height), 900)
    const creditStart = await footer.evaluate((node) => {
      const range = document.createRange()
      range.selectNodeContents(node.firstChild)
      return range.getBoundingClientRect().x
    })
    const paddingStart = await footer.evaluate((node) => parseFloat(getComputedStyle(node).paddingInlineStart))
    assert.ok(Math.abs(creditStart - bounds.x - paddingStart) < 0.1, 'Bottom-bar credit must be left aligned')
    await assertNoOverflow(page)
    await nav.getByRole('button', { name: 'More', exact: true }).click()
    const menu = page.getByRole('menu')
    assert.deepEqual(await menu.getByRole('menuitem').allTextContents(), ['Kitchen Sink'])
    await menu.getByRole('menuitem', { name: 'Kitchen Sink', exact: true }).click()
    await page.getByRole('heading', { name: 'Kitchen Sink', level: 1 }).waitFor()
    assert.equal(await page.locator('.app-shell').count(), 1)
    assert.equal(await page.getByRole('main').count(), 1)
    assert.equal(await nav.getByRole('button', { name: 'More', exact: true }).getAttribute('data-overflow-selected'), 'kitchen-sink')
    await assertNoOverflow(page)
    await nav.getByRole('button', { name: 'More', exact: true }).click()
    assert.equal(await page.getByRole('menuitem', { name: 'Kitchen Sink', exact: true }).getAttribute('aria-current'), 'page')
    await page.keyboard.press('Escape')
    await nav.getByRole('link', { name: 'Packets', exact: true }).click()
    await assertSection(page, 'Packets')
  })
})

test('navigation controls, icons, and labels stay stable between standard section routes', async () => {
  for (const width of [1440, 390]) {
    await withShell({ viewport: { width, height: 900 } }, async (page) => {
      const nav = page.getByRole('navigation', { name: 'Main navigation' })
      const geometry = () => nav.evaluate((node) => [...node.querySelectorAll(':scope > a, :scope > button')]
        .filter((control) => control.checkVisibility())
        .map((control) => ({
          label: control.textContent,
          rects: [control, control.querySelector('svg'), control.querySelector('span')].map((part) => {
            const { x, y, width, height } = part.getBoundingClientRect()
            return { x, y, width, height }
          }),
        })))
      const initial = await geometry()
      for (const name of primarySections.filter(name => name !== 'Map')) {
        await nav.getByRole('link', { name, exact: true }).click()
        await assertSection(page, name)
        assert.deepEqual(await geometry(), initial, `${name} selection must not move navigation`)
      }
      await nav.getByRole('button', { name: 'More', exact: true }).click()
      await page.getByRole('menuitem', { name: width === 390 ? 'Observers' : 'Kitchen Sink', exact: true }).waitFor()
      assert.deepEqual(await geometry(), initial, 'Opening More must not move navigation')
      if (width === 390) {
        await page.getByRole('menuitem', { name: 'Observers', exact: true }).click()
        await assertSection(page, 'Observers')
        assert.deepEqual(await geometry(), initial, 'Selecting an overflow destination must not move More')
      }
    })
  }
})

test('Packets and Map share header and navigation geometry before and after lazy map loading', async () => {
  for (const options of [
    { viewport: { width: 1440, height: 900 } },
    { viewport: { width: 390, height: 900 }, isMobile: true, hasTouch: true },
  ]) {
    await withShell(options, async page => {
      const geometry = () => page.evaluate(() => Object.fromEntries(['.app-header', '.app-brand', '.app-navigation', '.app-appearance-trigger'].map(selector => {
        const { x, y, width, height } = document.querySelector(selector).getBoundingClientRect()
        return [selector, { x, y, width, height }]
      })))
      const initial = await geometry()
      const nav = page.getByRole('navigation', { name: 'Main navigation' })
      await nav.getByRole('link', { name: 'Map', exact: true }).click()
      await assertSection(page, 'Map')
      assert.deepEqual(await geometry(), initial, 'Map must not apply route-specific shell sizing')
      await nav.getByRole('link', { name: 'Packets', exact: true }).click()
      await assertSection(page, 'Packets')
      assert.deepEqual(await geometry(), initial, 'Loading map CSS must not change other routes')
    })
  }
})

test('mobile touch navigation moves overflow destinations into More and marks the current section', async () => {
  await withShell({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }, async (page) => {
    const nav = page.getByRole('navigation', { name: 'Main navigation' })
    const more = nav.getByRole('button', { name: /^More/ })
    assert.equal(await page.getByRole('contentinfo').isVisible(), false)
    assert.deepEqual(await nav.getByRole('link').allTextContents(), primarySections)
    for (const name of primarySections) {
      const link = nav.getByRole('link', { name, exact: true })
      await link.tap()
      await assertSection(page, name)
       assert.ok(await nav.isVisible())
      assert.equal(await link.getAttribute('aria-current'), 'page')
      const bounds = await link.boundingBox()
      assert.ok(bounds.height >= 44 && bounds.width >= 44)
    }
    for (const name of overflowSections) {
      await more.tap()
      const menu = page.getByRole('menu')
      assert.deepEqual(await menu.getByRole('menuitem').allTextContents(), [...overflowSections, 'Kitchen Sink'])
      const bounds = await menu.boundingBox()
      assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390)
      assert.ok(bounds.y >= 0 && bounds.y + bounds.height <= 844)
      await menu.getByRole('menuitem', { name, exact: true }).tap()
      await assertSection(page, name)
      await menu.waitFor({ state: 'hidden' })
      assert.notEqual(await more.getAttribute('data-overflow-selected'), null)
      await more.tap()
      assert.equal(await page.getByRole('menuitem', { name, exact: true }).getAttribute('aria-current'), 'page')
      await page.keyboard.press('Escape')
      await menu.waitFor({ state: 'hidden' })
    }
    await assertNoOverflow(page)
    await more.tap()
    await page.getByRole('menuitem', { name: 'Kitchen Sink', exact: true }).tap()
    await page.getByRole('heading', { name: 'Kitchen Sink', level: 1 }).waitFor()
    assert.equal(await page.locator('.app-shell').count(), 1)
    assert.equal(await page.getByRole('main').count(), 1)
    assert.equal(await more.getAttribute('data-overflow-selected'), 'kitchen-sink')
    await assertNoOverflow(page)
    await nav.getByRole('link', { name: 'Packets', exact: true }).tap()
    await assertSection(page, 'Packets')
  })
})

test('skip link and keyboard More navigation preserve focus and dismissal', async () => {
  await withShell({ viewport: { width: 390, height: 844 } }, async (page) => {
    await page.keyboard.press('Tab')
    const skip = page.getByRole('link', { name: 'Skip to content', exact: true })
    assert.ok(await skip.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Enter')
    assert.ok(await page.getByRole('main').evaluate((node) => node === document.activeElement))
    const more = page.getByRole('button', { name: /^More/ })
    await more.focus()
    await page.keyboard.press('Enter')
    const menu = page.getByRole('menu')
    await menu.waitFor()
    await page.getByRole('menuitem', { name: 'Observers', exact: true }).waitFor()
    await page.waitForFunction(() => document.activeElement?.getAttribute('role') === 'menuitem')
    await page.keyboard.press('End')
    assert.ok(await page.getByRole('menuitem', { name: 'Kitchen Sink', exact: true }).evaluate((node) => node === document.activeElement))
    await page.keyboard.press('ArrowUp')
    assert.ok(await page.getByRole('menuitem', { name: 'Analytics', exact: true }).evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Escape')
    await menu.waitFor({ state: 'hidden' })
    assert.ok(await more.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Space')
    await menu.waitFor()
    await page.keyboard.press('Home')
    await page.keyboard.press('Enter')
    await assertSection(page, 'Observers')
    await menu.waitFor({ state: 'hidden' })
    assert.ok(await more.evaluate((node) => node === document.activeElement))
    await more.press('Enter')
    await menu.waitFor()
    await page.getByRole('heading', { name: 'Observers', exact: true }).click()
    await menu.waitFor({ state: 'hidden' })
  })
})

test('small and enlarged-text layouts scroll content clear of the bottom navigation', async () => {
  for (const width of [320, 768, 1024]) {
    await withShell({ viewport: { width, height: 600 } }, async (page) => {
      await assertNoOverflow(page)
      await page.evaluate(() => {
        document.documentElement.style.fontSize = '20px'
        const content = document.querySelector('.app-section-message')
        const paragraph = document.createElement('p')
        paragraph.textContent = 'Long content for shell scrolling verification. '.repeat(120)
        content.prepend(paragraph)
      })
      await page.getByRole('link', { name: 'Explore the Kitchen Sink', exact: true }).scrollIntoViewIfNeeded()
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
      await assertNoOverflow(page)
      const link = await page.getByRole('link', { name: 'Explore the Kitchen Sink', exact: true }).boundingBox()
      const nav = await page.getByRole('navigation', { name: 'Main navigation' }).boundingBox()
      const fixedNavigation = await page.getByRole('navigation', { name: 'Main navigation' }).evaluate((node) => getComputedStyle(node).position === 'fixed')
      if (fixedNavigation) {
        assert.ok(link.y + link.height <= nav.y, 'The last content action must remain above mobile navigation')
        for (const control of await page.getByRole('navigation').locator(':scope > a, :scope > button').all()) {
          if (!await control.isVisible()) continue
          const bounds = await control.boundingBox()
          const label = await control.locator('span').first().boundingBox()
          if (label) assert.ok(label.x >= bounds.x && label.x + label.width <= bounds.x + bounds.width, 'Large-text navigation labels must stay within their targets')
          assert.ok(bounds.y + bounds.height <= 600, 'Large-text navigation targets must stay in the viewport')
        }
      } else {
        const header = await page.getByRole('banner').boundingBox()
        assert.ok(link.y >= header.y + header.height, 'The last content action must remain below the desktop header')
        assert.ok(link.y + link.height <= 600)
        assert.ok(await page.getByRole('contentinfo').isVisible())
      }
      assert.ok(link.y >= 0)
    })
  }
})

test('compact bottom navigation keeps visible labels, accessible names, and SDK actions', async () => {
  for (const { width, fontSize } of [
    { width: 320, fontSize: 16 },
    { width: 320, fontSize: 20 },
    { width: 390, fontSize: 16 },
    { width: 390, fontSize: 20 },
  ]) {
    await withShell({ viewport: { width, height: 844 } }, async (page) => {
      await page.evaluate((size) => { document.documentElement.style.fontSize = `${size}px` }, fontSize)
      const nav = page.getByRole('navigation', { name: 'Main navigation' })
      for (const name of [...primarySections, 'More']) {
        const control = nav.getByRole(name === 'More' ? 'button' : 'link', { name, exact: true })
        assert.ok(await control.isVisible(), `${name} must remain accessible by name`)
        assert.ok(await control.locator('span').isVisible(), `${name} label at ${width}px / ${fontSize}px text`)
        assert.equal(await control.evaluate(node => node.scrollWidth > node.clientWidth), false)
        assert.ok(await control.locator('svg').isVisible())
        const bounds = await control.boundingBox()
        assert.ok(bounds.width >= 44 && bounds.height >= 44)
      }
      const geometry = () => nav.locator(':scope > a').evaluateAll((nodes) => nodes.filter((node) => node.offsetWidth).map((node) => {
        const rect = node.getBoundingClientRect()
        const icon = node.querySelector('svg').getBoundingClientRect()
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height, iconX: icon.x, iconY: icon.y }
      }))
      const before = await geometry()
      await nav.getByRole('link', { name: 'Channels', exact: true }).click()
      await assertSection(page, 'Channels')
      assert.equal(await nav.getByRole('link', { name: 'Channels', exact: true }).getAttribute('aria-current'), 'page')
      assert.deepEqual(await geometry(), before)
      const result = await page.evaluate(async () => {
        const { agent } = await import('/tests/kitchen-sink-agent.mjs')
        const labels = agent.snapshot().filter((entry) => !entry.offPage).map((entry) => entry.label)
        const action = await agent.act({ steps: [{ ref: 'More', action: 'press', args: ['Observers'] }] })
        return { labels, action }
      })
      for (const name of [...primarySections, 'More']) assert.ok(result.labels.includes(name), `${name} is missing from the SDK snapshot`)
      assert.equal(result.action.ok, true, result.action.error?.message)
      await assertSection(page, 'Observers')
      assert.notEqual(await nav.getByRole('button', { name: 'More', exact: true }).getAttribute('data-overflow-selected'), null)
      await assertNoOverflow(page)
    }, true)
  }
})

test('SDK agents discover and operate shell navigation at desktop and mobile widths', async () => {
  for (const width of [1440, 390]) {
    await withShell({ viewport: { width, height: 900 } }, async (page) => {
      const snapshot = await page.evaluate(async () => {
        const { agent } = await import('/tests/kitchen-sink-agent.mjs')
        return agent.snapshot().filter((entry) => !entry.offPage && entry.component !== 'more')
      })
      console.log(JSON.stringify({ width, snapshot }, null, 2))
      for (const entry of snapshot) assert.ok(entry.label, `${entry.component} ${entry.ref} has no name`)
      const labels = snapshot.map((entry) => entry.label)
      for (const label of ['Pulsas home', 'Skip to content', ...primarySections, 'More', 'Explore the Kitchen Sink']) {
        assert.ok(labels.includes(label), `${label} is missing from the SDK snapshot`)
      }
      if (width === 1440) {
        for (const label of overflowSections) assert.ok(labels.includes(label), `${label} is missing from the SDK snapshot`)
      }
      const act = async (steps) => {
        const result = await page.evaluate(async (steps) => {
          const { agent } = await import('/tests/kitchen-sink-agent.mjs')
          const controls = agent.snapshot().filter(entry => !entry.offPage)
          return agent.act({ steps: steps.map(step => ({ ...step,
            ref: controls.find(entry => entry.label === step.ref)?.ref ?? step.ref })) })
        }, steps)
        assert.equal(result.ok, true, result.error?.message)
      }
      for (const name of width === 1440 ? [...primarySections, ...overflowSections] : primarySections) {
        await act([{ ref: name, action: 'press' }])
        await assertSection(page, name)
      }
      if (width === 390) {
        for (const name of overflowSections) {
          await act([{ ref: 'More', action: 'press', args: [name] }])
          await assertSection(page, name)
        }
      }
      await act([{ ref: 'Pulsas home', action: 'press' }])
      await assertSection(page, 'Packets')
      await act([{ ref: 'Skip to content', action: 'press' }])
      assert.ok(await page.getByRole('main').evaluate((node) => node === document.activeElement))
      await act([{ ref: 'More', action: 'press', args: ['Kitchen Sink'] }])
      await page.getByRole('heading', { name: 'Kitchen Sink', level: 1 }).waitFor()
      await page.goto(`${origin}/packets`)
      await page.getByRole('heading', { name: 'Packets', level: 1 }).waitFor()
      await act([{ ref: 'Explore the Kitchen Sink', action: 'press' }])
      await page.getByRole('heading', { name: 'Kitchen Sink', level: 1 }).waitFor()
    }, true)
  }
})
