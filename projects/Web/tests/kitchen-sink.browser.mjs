import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
const url = `${origin}/kitchen-sink`
let browser

before(async () => {
  browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' })
})

after(async () => {
  await browser?.close()
})

async function withWorkbench(options, verify, prepare) {
  const page = await browser.newPage({ ...options, reducedMotion: 'reduce' })
  const errors = []
  const disallowedRequests = []
  page.on('pageerror', (error) => errors.push(error.message))
  await page.routeWebSocket('**/*', socket => socket.close())
  await page.route('**/*', (route) => {
    const target = new URL(route.request().url())
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)) {
      disallowedRequests.push(target.href)
      return route.abort()
    }
    return route.continue()
  })
  try {
    await prepare?.(page)
    assert.equal((await page.goto(url)).status(), 200)
    await page.getByRole('heading', { name: 'Kitchen Sink', level: 1 }).waitFor()
    await verify(page)
    assert.deepEqual(errors, [])
    assert.deepEqual(disallowedRequests, [])
  } finally {
    await page.close()
  }
}

async function loadWithAgent(page) {
  await page.route(url, async (route) => {
    const response = await route.fetch()
    const html = await response.text()
    // Install the observer before the real app mounts its SDK controls.
    const body = html.replace(/src="\/src\/main\.tsx(?:\?[^"]*)?"/, 'src="/tests/kitchen-sink-agent.mjs"')
    assert.notEqual(body, html, 'Expected the Vite HTML to load /src/main.tsx')
    await route.fulfill({ response, body })
  })
}

async function assertMoreReceptions(page, table) {
  await page.waitForFunction(node => node.getAttribute('aria-rowcount') === '68', await table.elementHandle(), { timeout: 5000 })
}

function arrivalFields(traffic) {
  return { from: traffic.getByLabel('From', { exact: true }), to: traffic.getByLabel('To', { exact: true }) }
}

async function fillArrivalRange(traffic, start, end) {
  const { from, to } = arrivalFields(traffic)
  await from.fill(start)
  await to.fill(end)
}

const arrivalDayFormat = new Intl.DateTimeFormat('lt-LT', { dateStyle: 'full', timeZone: 'UTC' })

function arrivalDay(editor, date) {
  return editor.getByRole('gridcell', { name: arrivalDayFormat.format(Date.parse(date)), exact: true })
}

test('keyboard selects a role, dismisses the picker, and changes both modifiers', async () => {
  await withWorkbench({}, async (page) => {
    const role = page.getByRole('combobox', { name: 'Advertised role' })
    const observer = page.getByRole('checkbox', { name: 'Observer', exact: true })
    const warning = page.getByRole('checkbox', { name: 'Warning', exact: true })
    const status = page.getByRole('status', { name: 'Node identity preview' })
    const listbox = page.getByRole('listbox')

    assert.match(await status.innerText(), /Room server · Observer/)
    await page.keyboard.press('Tab')
    assert.ok(await page.getByRole('link', { name: 'Skip to content', exact: true }).evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Enter')
    assert.ok(await page.getByRole('main').evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Tab')
    const index = page.getByRole('navigation', { name: 'Specimen index', exact: true })
    for (const link of await index.getByRole('link').all()) {
      assert.ok(await link.evaluate(node => node === document.activeElement))
      await page.keyboard.press('Tab')
    }
    assert.ok(await page.getByRole('button', { name: 'Pause badge updates', exact: true }).evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Tab')
    assert.ok(await role.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('ArrowDown')
    await listbox.waitFor()
    await page.waitForFunction(() => document.activeElement?.getAttribute('role') === 'option')
    await page.keyboard.press('Home')
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('Enter')
    await listbox.waitFor({ state: 'hidden' })
    assert.match(await status.innerText(), /Repeater · Observer/)
    assert.ok(await role.evaluate((node) => node === document.activeElement))

    await page.keyboard.press('Enter')
    await listbox.waitFor()
    await page.waitForFunction(() => document.activeElement?.getAttribute('role') === 'option')
    await page.keyboard.press('End')
    await page.keyboard.press('Escape')
    await listbox.waitFor({ state: 'hidden' })
    assert.ok(await role.evaluate((node) => node === document.activeElement))
    assert.match(await status.innerText(), /Repeater · Observer/)

    await page.keyboard.press('Tab')
    assert.ok(await observer.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Space')
    assert.equal(await observer.getAttribute('aria-checked'), 'false')
    await page.keyboard.press('Tab')
    assert.ok(await warning.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Space')
    assert.equal(await warning.getAttribute('aria-checked'), 'true')
    assert.match(await status.innerText(), /Repeater · Warning: example node needs attention/)
  })
})

test('the Client role uses the legacy phone-and-message icon in avatars, badges, and the picker', async () => {
  await withWorkbench({}, async (page) => {
    const legacyPaths = [
      'M11 3h10v8h-3l-4 2v-2h-3l0 -8',
      'M15 16v4a1 1 0 0 1 -1 1h-8a1 1 0 0 1 -1 -1v-14a1 1 0 0 1 1 -1h2',
      'M10 18v.01',
    ]
    const client = page.getByRole('region', { name: 'Role specimens' }).getByRole('listitem').filter({ hasText: 'Client' })
    const icons = await client.locator('svg').evaluateAll((nodes) => nodes.map((svg) => [...svg.querySelectorAll('path')].map((path) => path.getAttribute('d'))))
    assert.deepEqual(icons, [legacyPaths, legacyPaths])
    await page.getByRole('combobox', { name: 'Advertised role' }).press('Enter')
    const option = page.getByRole('option', { name: 'Client', exact: true })
    await option.waitFor()
    assert.deepEqual(await option.locator('svg').first().locator('path').evaluateAll((paths) => paths.map((path) => path.getAttribute('d'))), legacyPaths)
  })
})

test('the live status badge counts elapsed stale time in the largest whole unit', async () => {
  const start = new Date('2026-11-21T12:00:00Z')
  await withWorkbench({}, async (page) => {
    const specimen = page.getByRole('region', { name: 'Live status badge', exact: true })
    const surfaces = await specimen.getByRole('figure').all()
    assert.equal(surfaces.length, 2)
    for (const surface of surfaces) {
      assert.ok(await surface.getByText('Live', { exact: true }).isVisible())
      assert.match(await surface.ariaSnapshot(), /Stale/)
    }
    let elapsed = 0
    for (const [seconds, label] of [
      [0, '0s'], [1, '1s'], [59, '59s'], [60, '1m'], [119, '1m'],
      [3599, '59m'], [3600, '1h'], [86399, '23h'], [86400, '1d'], [172800, '2d'],
    ]) {
      if (seconds > elapsed) await page.clock.fastForward((seconds - elapsed) * 1000)
      elapsed = seconds
      for (const surface of surfaces) {
        const timer = surface.getByRole('timer').filter({ hasText: new RegExp(`^${label}$`) })
        await timer.waitFor({ timeout: 3000 })
        assert.equal(await timer.getAttribute('aria-live'), 'off')
      }
    }

    const pause = specimen.getByRole('button', { name: 'Pause badge updates', exact: true })
    const resume = specimen.getByRole('button', { name: 'Resume badge updates', exact: true })
    await pause.press('Space')
    await resume.waitFor({ timeout: 3000 })
    await page.clock.fastForward(86_400_000)
    assert.deepEqual(await specimen.getByRole('timer').allTextContents(), ['2d', '2d'])
    await resume.press('Enter')
    await specimen.getByRole('timer', { name: 'for 3 days' }).last().waitFor({ timeout: 3000 })
    assert.deepEqual(await specimen.getByRole('timer').allTextContents(), ['3d', '3d'])
  }, async (page) => {
    await page.clock.install({ time: new Date(start.getTime() - 60_000) })
    await page.clock.pauseAt(start)
  })
})

test('only the live status badge pulses, and it respects reduced motion', async () => {
  await withWorkbench({ viewport: { width: 320, height: 780 } }, async (page) => {
    const specimen = page.getByRole('region', { name: 'Live status badge', exact: true })
    const live = specimen.locator('.ks-live-status-badge[data-state="live"]')
    const stale = specimen.locator('.ks-live-status-badge[data-state="stale"]')
    const animations = (badges) => badges.evaluateAll((nodes) =>
      nodes.flatMap((node) => node.getAnimations({ subtree: true }).map((animation) => animation.effect?.getTiming().iterations)))
    assert.deepEqual(await animations(live), [])

    await page.emulateMedia({ reducedMotion: 'no-preference' })
    assert.deepEqual(await animations(live), [Infinity, Infinity])
    assert.deepEqual(await animations(stale), [])
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref: 'Pause badge updates', action: 'press' }] })
    })
    assert.equal(result.ok, true, result.error?.message)
    await specimen.getByRole('button', { name: 'Resume badge updates', exact: true }).waitFor({ timeout: 3000 })
    assert.deepEqual(await live.evaluateAll((badges) => badges.flatMap((badge) =>
      badge.getAnimations({ subtree: true }).map((animation) => animation.playState))), ['paused', 'paused'])
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  }, loadWithAgent)
})

test('320px workbench keeps the previews and new specimens within the viewport', async () => {
  await withWorkbench({ viewport: { width: 320, height: 780 }, isMobile: true, hasTouch: true }, async (page) => {
    await page.getByRole('combobox', { name: 'Advertised role' }).tap()
    await page.getByRole('option', { name: 'Sensor' }).tap()
    await page.getByRole('checkbox', { name: 'Warning', exact: true }).tap()
    assert.match(await page.getByRole('status', { name: 'Node identity preview' }).innerText(), /Sensor · Observer · Warning/)

    const surfaces = page.getByRole('region', { name: 'Node identity', exact: true }).getByRole('figure')
    assert.equal(await surfaces.count(), 2)
    const [light, dark] = await Promise.all([surfaces.nth(0).boundingBox(), surfaces.nth(1).boundingBox()])
    assert.ok(light && dark && light.x + light.width <= dark.x && dark.x + dark.width <= 320)
    for (const surface of await surfaces.all()) {
      assert.match(await surface.innerText(), /Sensor/)
      assert.equal(await surface.locator('.ks-avatar-mark').count(), 2)
    }
    const context = page.getByRole('region', { name: /in context/i })
    assert.match(await context.innerText(), /Sensor/)
    const markers = await context.locator('.ks-avatar[data-size="marker"]').all()
    assert.equal(markers.length, 2)
    for (const marker of markers) {
      assert.equal(await marker.locator('.ks-avatar-mark').count(), 2)
    }
    assert.equal(await page.getByRole('region', { name: 'Role specimens' }).getByRole('listitem').count(), 5)
    const scopedRoutes = page.getByRole('region', { name: 'Packet badges', exact: true })
    assert.ok(await scopedRoutes.getByText('#baltics', { exact: true }).isVisible())
    await page.getByRole('region', { name: 'Icon + text', exact: true }).getByRole('button', { name: 'Anon req', exact: true }).tap()
    const tip = page.getByRole('tooltip')
    await tip.waitFor({ timeout: 3000 })
    assert.match(await tip.innerText(), /Anonymous request \(0x07\)/)
    for (const name of [/channel.*icons/i, /packet path/i, /mesh (logo|icon)/i]) {
      assert.ok(await page.getByRole('heading', { level: 2, name }).isVisible())
    }
    const hop = page.getByRole('region', { name: /packet path/i }).getByRole('button', { name: /Hop 2\b.*B4/i })
    await hop.tap()
    const popup = page.getByRole('dialog')
    await popup.waitFor()
    const bounds = await popup.boundingBox()
    assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 320)
    const close = popup.getByRole('button', { name: 'Close', exact: true })
    const closeBounds = await close.boundingBox()
    assert.ok(closeBounds && closeBounds.width >= 24 && closeBounds.height >= 24)
    await close.tap()
    await popup.waitFor({ state: 'hidden' })
    await hop.tap()
    await popup.getByRole('button', { name: 'Select North relay' }).tap()
    await popup.waitFor({ state: 'hidden' })
    assert.equal(await page.getByRole('status', { name: 'Packet path selection' }).innerText(), 'Selected North relay for hop 2 (B4).')
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  })
})

test('packet badges use the legacy compact labels with and without decorative icons', async () => {
  await withWorkbench({}, async (page) => {
    const labels = ['Request', 'Response', 'Text', 'Ack', 'Advert', 'Text', 'Data', 'Anon req', 'Path', 'Trace',
      'Multipart', 'Control', 'Rsv 0xC', 'Rsv 0xD', 'Rsv 0xE', 'Custom', '???']
    for (const [name, glyphCount] of [['Icon + text', 17], ['Text only', 0]]) {
      const gallery = page.getByRole('region', { name, exact: true })
      assert.deepEqual(await gallery.getByRole('listitem').allInnerTexts(), labels)
      assert.equal(await gallery.locator('svg[aria-hidden="true"]').count(), glyphCount)
    }
    const gallery = page.getByRole('region', { name: 'Icon + text', exact: true })
    const groupText = gallery.getByRole('listitem').nth(5).getByRole('button')
    await gallery.getByRole('listitem').nth(4).getByRole('button').focus()
    await page.keyboard.press('Tab')
    assert.ok(await groupText.evaluate((node) => node === document.activeElement))
    const tooltip = page.getByRole('tooltip')
    await tooltip.waitFor({ timeout: 3000 })
    assert.match(await tooltip.innerText(), /GRP_TXT.*Group text message \(0x05\)/)
    await page.keyboard.press('Escape')
    await tooltip.waitFor({ state: 'hidden', timeout: 3000 })
    assert.ok(await groupText.evaluate((node) => node === document.activeElement))
    const rows = ['Advert', 'Text', 'Anon req', 'Rsv 0xC']
    for (const name of ['Icon + text · sample packets', 'Text only · sample packets']) {
      assert.deepEqual(await page.getByRole('table', { name, exact: true }).getByRole('button').allInnerTexts(), rows)
    }
  })
})

test('route badges retain the legacy glyphs, including scoped variants', async () => {
  await withWorkbench({}, async (page) => {
    const route = page.getByRole('region', { name: 'Route', exact: true })
    assert.deepEqual(await route.getByRole('list').first().getByRole('listitem').allInnerTexts(), ['Direct', 'Flood', 'T·Direct', 'T·Flood', '???'])
    const glyphs = await route.locator('svg').evaluateAll((nodes) => nodes.map((svg) => [
      svg.getAttribute('aria-hidden'),
      [...svg.children].map((child) => child.localName).join(','),
    ]))
    assert.deepEqual(glyphs, [
      ['true', 'circle,path,circle'],
      ['true', 'rect,rect,rect,path,path'],
      ['true', 'circle,path,circle'],
      ['true', 'rect,rect,rect,path,path'],
      ['true', 'circle,path,path'],
      ['true', 'circle,path,circle'],
      ['true', 'rect,rect,rect,path,path'],
    ])
  })
})

test('channel icons and the mesh logo show their specimen sizes on light and dark surfaces', async () => {
  await withWorkbench({}, async (page) => {
    const channels = page.getByRole('region', { name: /channel.*icons/i })
    for (const name of [/^public\b/i, /^hashtag\b/i, /^(shared.key|private)\b/i]) {
      assert.ok(await channels.getByRole('heading', { name }).isVisible())
    }
    assert.equal(await channels.getByText(/light surface/i).count(), 3)
    assert.equal(await channels.getByText(/dark surface/i).count(), 3)
    for (const size of [16, 20, 24]) {
      assert.equal(await channels.getByText(new RegExp(`^${size}\\s?px\\b`)).count(), 6)
    }

    const logo = page.getByRole('region', { name: /mesh (logo|icon)/i })
    assert.ok(await logo.getByText('Light surface', { exact: true }).isVisible())
    assert.ok(await logo.getByText('Dark surface', { exact: true }).isVisible())
    for (const size of [16, 24, 48]) {
      assert.ok(await logo.getByText(new RegExp(`^${size}\\s?px\\b`)).isVisible())
    }
  })
})

test('packet path variants compare the same hops and the picker updates the density examples', async () => {
  await withWorkbench({}, async (page) => {
    const path = page.getByRole('region', { name: /packet path/i })
    const variants = ['squares', 'dots', 'corner', 'underline', 'ambiguity', 'count', 'count-horizontal']
    const comparison = path.getByRole('region', { name: 'Compare indicators' })
    for (const variant of variants) {
      const row = comparison.locator(`[data-path-comparison="${variant}"]`)
      assert.deepEqual(await row.locator('.ks-hop-prefix').allTextContents(), ['AA', 'AB', 'AC', 'AD', 'AE', 'AF', 'B0', 'B1'])
      assert.equal(await row.locator(`button[data-indicator="${variant}"]`).count(), 8)
      const many = row.getByRole('button', { name: /Hop 5\b.*AE/i })
      if (variant.startsWith('count')) assert.equal(await many.locator('.ks-hop-count').textContent(), '+6')
      else if (variant === 'ambiguity') assert.equal(await many.locator('.ks-hop-marks').textContent(), '≋')
      else {
        assert.equal(await many.locator('[data-confidence="weak"]').count(), 3)
        const mixed = row.getByRole('button', { name: /Hop 8\b.*B1/i })
        assert.deepEqual(await mixed.locator('[data-confidence]').evaluateAll((nodes) => nodes.map((node) => node.dataset.confidence)), ['weak', 'strong', 'strong'])
      }
    }
    const examples = path.locator('[data-path-examples]')
    for (const [bytes, hops] of [[1, 64], [2, 32], [3, 16]]) {
      assert.equal(await examples.getByRole('list', { name: `${bytes}-byte prefixes, ${hops} hops`, exact: true }).getByRole('button').count(), hops)
    }
    assert.ok(await examples.getByText('No hops supplied', { exact: true }).isVisible())
    const picker = path.getByRole('combobox', { name: 'Indicator variant', exact: true })
    assert.equal(await picker.innerText(), 'Extra count · horizontal (default)')
    assert.equal(await examples.locator('button[data-indicator="count-horizontal"]').count(), 115)
    for (const [index, label] of ['Squares', 'Edge dots', 'Corner pips', 'Segmented underline', 'Ambiguity mark',
      'Extra count · vertical', 'Extra count · horizontal (default)'].entries()) {
      await picker.press('Enter')
      await page.getByRole('option', { name: label, exact: true }).click()
      assert.equal(await examples.locator(`button[data-indicator="${variants[index]}"]`).count(), 115)
    }
    const comparisonHop = comparison.locator('[data-path-comparison="dots"]').getByRole('button', { name: /Hop 5\b.*AE/i })
    await comparisonHop.press('Enter')
    const inspector = page.getByRole('dialog', { name: 'Hop 5 · AE', exact: true })
    await inspector.waitFor()
    assert.ok(await inspector.getByText('1 strong · 6 weak', { exact: true }).isVisible())
    assert.equal(await inspector.getByRole('listitem').count(), 7)
    await inspector.getByRole('button', { name: 'Close', exact: true }).click()
    await inspector.waitFor({ state: 'hidden' })
    assert.ok(await comparisonHop.evaluate((node) => node === document.activeElement))
  })
})

test('packet hop inspection exposes candidates and returns keyboard focus after selection and dismissal', async () => {
  await withWorkbench({}, async (page) => {
    const path = page.getByRole('region', { name: /packet path/i })
    const ambiguous = path.getByRole('button', { name: /Hop 2\b.*B4/i })
    const missing = path.getByRole('button', { name: /Hop 3\b.*E7/i })
    const popup = page.getByRole('dialog', { name: 'Hop 2 · B4' })

    await ambiguous.focus()
    await page.keyboard.press('Enter')
    await popup.waitFor()
    assert.match(await popup.innerText(), /strong/i)
    assert.match(await popup.innerText(), /weak/i)

    await page.keyboard.press('Escape')
    await popup.waitFor({ state: 'hidden' })
    assert.ok(await ambiguous.evaluate((node) => node === document.activeElement))

    await page.keyboard.press('Space')
    await popup.waitFor()
    const candidate = popup.getByRole('button', { name: 'Select Riverside relay' })
    assert.match(await candidate.getAttribute('aria-description'), /weak confidence/i)
    assert.equal(await candidate.locator('code').innerText(), 'B40000..000002')
    assert.match(await candidate.getAttribute('aria-description'), /Public key: B4[0-9A-F]{60}02$/)
    assert.ok(await candidate.evaluate((node) => node !== document.activeElement))
    await page.keyboard.press('Tab')
    assert.ok(await candidate.evaluate((node) => node === document.activeElement))
    await candidate.press('Enter')
    await popup.waitFor({ state: 'hidden' })
    assert.ok(await ambiguous.evaluate((node) => node === document.activeElement))
    const status = path.getByRole('status', { name: 'Packet path selection' })
    assert.equal(await status.innerText(), 'Selected Riverside relay for hop 2 (B4).')

    await missing.press('Enter')
    const missingPopup = page.getByRole('dialog', { name: 'Hop 3 · E7' })
    await missingPopup.waitFor()
    assert.match(await missingPopup.innerText(), /no candidates/i)
    assert.equal(await missingPopup.getByRole('button').count(), 1)
    const close = missingPopup.getByRole('button', { name: 'Close', exact: true })
    assert.ok(await close.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('Enter')
    await missingPopup.waitFor({ state: 'hidden' })
    assert.ok(await missing.evaluate((node) => node === document.activeElement))
    assert.equal(await status.innerText(), 'Selected Riverside relay for hop 2 (B4).')
  })
})

test('a long candidate list scrolls without moving the hop heading or corner close button', async () => {
  await withWorkbench({ viewport: { width: 320, height: 400 } }, async (page) => {
    const row = page.locator('[data-path-comparison="dots"]')
    const hop = row.getByRole('button', { name: /Hop 5\b.*AE/i })
    await hop.press('Enter')
    const popup = page.getByRole('dialog', { name: 'Hop 5 · AE', exact: true })
    const heading = popup.getByRole('heading')
    const close = popup.getByRole('button', { name: 'Close', exact: true })
    const list = popup.getByRole('list')
    await list.waitFor()
    const [titleBefore, closeBefore, listBefore] = await Promise.all([heading.boundingBox(), close.boundingBox(), list.boundingBox()])
    assert.ok(titleBefore && closeBefore && listBefore)
    assert.ok(closeBefore.x >= titleBefore.x + titleBefore.width - 1 && closeBefore.y < listBefore.y)
    assert.ok(closeBefore.width >= 44 && closeBefore.height >= 44)
    assert.ok(await list.evaluate((node) => node.scrollHeight > node.clientHeight))
    const pageScroll = await page.evaluate(() => window.scrollY)
    await page.mouse.move(listBefore.x + listBefore.width / 2, listBefore.y + listBefore.height / 2)
    await page.mouse.wheel(0, 160)
    await page.waitForFunction((node) => node.scrollTop > 0, await list.elementHandle(), { timeout: 3000 })
    assert.equal(await page.evaluate(() => window.scrollY), pageScroll)

    const last = list.getByRole('button', { name: 'Select Candidate 7', exact: true })
    await last.focus()
    assert.ok(await list.evaluate((node) => node.scrollTop > 0))
    assert.equal(await popup.evaluate((node) => node.scrollTop), 0)
    assert.deepEqual(await heading.boundingBox(), titleBefore)
    assert.deepEqual(await close.boundingBox(), closeBefore)

    await close.click()
    await popup.waitFor({ state: 'hidden' })
    assert.ok(await hop.evaluate((node) => node === document.activeElement))
  })
})

test('packet traffic loads older fixture receptions inside its own scrolling table', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    assert.deepEqual(await table.getByRole('columnheader').allTextContents(), ['Code', 'Type', 'Transport', 'Summary', 'Region', 'Age'])
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    const heading = table.getByRole('columnheader', { name: 'Code' })
    await heading.scrollIntoViewIfNeeded()
    const pageScroll = await page.evaluate(() => scrollY)
    const scroller = traffic.getByRole('region', { name: 'Reception list scroll area' })
    assert.ok(await scroller.evaluate(node => getComputedStyle(node).overflowY === 'auto' && node.scrollHeight > node.clientHeight))
    await scroller.evaluate((element) => { element.scrollTop = element.scrollHeight })
    await assertMoreReceptions(page, table)
    assert.ok(await table.locator('time').count() < 32, 'Older pages should not keep all loaded rows mounted')
    assert.equal(await page.evaluate(() => scrollY), pageScroll)
    const [headBounds, scrollBounds] = await Promise.all([heading.boundingBox(), scroller.boundingBox()])
    assert.ok(headBounds && scrollBounds && Math.abs(headBounds.y - scrollBounds.y) <= 2)

    await traffic.getByRole('button', { name: 'Edit arrival time' }).press('Enter')
    await fillArrivalRange(traffic, '2026-11-19 16:00', '2026-11-19 16:15')
    await traffic.getByRole('button', { name: 'Apply range' }).press('Enter')
    assert.equal(await scroller.evaluate(node => node.scrollTop), 0)
    await traffic.getByRole('button', { name: 'Clear arrival time' }).press('Enter')
    assert.equal(await table.getAttribute('aria-rowcount'), '36')
    await scroller.evaluate(element => { element.scrollTop = element.scrollHeight })
    await assertMoreReceptions(page, table)
  })
})

test('feed markers span every column, stay in timeline order, and are skipped by packet navigation', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const markers = table.locator('tr[data-feed-status]')
    assert.deepEqual(await markers.evaluateAll(nodes => nodes.map(node => {
      const marker = getComputedStyle(node.cells[0], '::before')
      return {
        status: node.dataset.feedStatus,
        spans: Array.from(node.cells, cell => cell.colSpan),
        height: node.getBoundingClientRect().height,
        leftAccent: marker.insetInlineStart === '0px' && marker.borderInlineStartWidth === '3px',
        accentMatchesStatus: marker.borderInlineStartColor === getComputedStyle(node.querySelector('strong')).color,
      }
    })), [
      { status: 'live', spans: [6], height: 40, leftAccent: true, accentMatchesStatus: true },
      { status: 'resumed', spans: [6], height: 40, leftAccent: true, accentMatchesStatus: true },
      { status: 'lost-connection', spans: [6], height: 40, leftAccent: true, accentMatchesStatus: true },
    ])
    assert.match(await markers.nth(0).innerText(), /Live packets/i)
    assert.match(await markers.nth(1).innerText(), /Resumed/i)
    assert.match(await markers.nth(2).innerText(), /Lost connection/i)
    assert.equal(await markers.getByRole('button').count(), 0)
    assert.match(await traffic.innerText(), /32 loaded/)
    await markers.nth(1).getByText(/Resumed/i).first().click()
    assert.equal(await page.getByRole('dialog', { name: /reception/i }).count(), 0)

    const before = table.locator('tr[data-reception-id="reception-95-79"]')
    const after = table.locator('tr[data-reception-id="reception-95-78"]')
    assert.equal(await before.evaluate(node => node.nextElementSibling.dataset.feedStatus), 'resumed')
    await before.press('ArrowDown')
    assert.ok(await after.evaluate(node => node === document.activeElement))
    await page.keyboard.press('ArrowUp')
    assert.ok(await before.evaluate(node => node === document.activeElement))
    await before.getByRole('button').last().focus()
    await page.keyboard.press('Tab')
    assert.ok(await after.evaluate(node => node === document.activeElement))
    await page.keyboard.press('Shift+Tab')
    assert.ok(await before.getByRole('button').last().evaluate(node => node === document.activeElement))
  })
})

test('scoped route badges show the full explanation and scope in the packet table', async () => {
  await withWorkbench({}, async (page) => {
    const table = page.getByRole('table', { name: 'Packet receptions', exact: true })
    const routeName = { name: 'T·Direct #baltics', exact: true }
    const row = table.getByRole('row').filter({ has: page.getByRole('button', routeName) }).first()
    const badge = row.getByRole('button', routeName)
    const scope = badge.getByText('#baltics', { exact: true })
    assert.ok(await scope.evaluate(node => node.scrollWidth <= node.clientWidth + 1), 'Short scopes should fit in the badge')
    const surface = node => {
      const { backgroundColor, borderTopColor, borderTopWidth } = getComputedStyle(node)
      return { backgroundColor, visibleBorder: parseFloat(borderTopWidth) > 0 && borderTopColor !== 'rgba(0, 0, 0, 0)' }
    }
    const type = row.getByRole('button', { name: 'Text', exact: true })
    assert.deepEqual(await badge.evaluate(surface), await type.evaluate(surface))
    assert.deepEqual(await badge.evaluate(surface), { backgroundColor: 'rgba(0, 0, 0, 0)', visibleBorder: false })
    await badge.hover()
    const tooltip = page.getByRole('tooltip')
    await tooltip.waitFor({ timeout: 3000 })
    assert.equal(await page.getByRole('region', { name: 'Reception details' }).count(), 0)
    assert.match(await tooltip.innerText(), /Transport direct.*Scope: #baltics/is)
    assert.doesNotMatch(await tooltip.innerText(), /with transport codes/i)
    assert.ok(await tooltip.evaluate(tip => tip.scrollWidth <= tip.clientWidth + 1), 'Tooltip text should wrap inside the box')
    await page.keyboard.press('Escape')
    await tooltip.waitFor({ state: 'hidden' })

    await page.mouse.move(0, 0)
    await type.focus()
    await page.keyboard.press('Tab')
    assert.ok(await badge.evaluate(node => node === document.activeElement))
    await tooltip.waitFor({ timeout: 3000 })
    assert.match(await tooltip.innerText(), /#baltics/)
  })
})

test('the route tooltip stays readable when tapped in the horizontally scrolled table', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const table = page.getByRole('table', { name: 'Packet receptions', exact: true })
    const transport = page.getByRole('button', { name: 'T·Direct #baltics', exact: true })
    const row = table.getByRole('row').filter({ has: transport }).first()
    await row.getByRole('button', { name: 'T·Direct #baltics', exact: true }).tap()
    const tooltip = page.getByRole('tooltip')
    await tooltip.waitFor({ timeout: 3000 })
    const drawer = page.getByRole('dialog', { name: /reception/i })
    assert.equal(await drawer.count(), 0)
    assert.match(await tooltip.innerText(), /Transport direct.*Scope: #baltics/is)
    const bounds = await tooltip.boundingBox()
    assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 320)
    assert.ok(await tooltip.evaluate(tip => tip.scrollWidth <= tip.clientWidth + 1))
    await page.keyboard.press('Escape')
    await tooltip.waitFor({ state: 'hidden' })
    await row.getByRole('button', { name: 'Text', exact: true }).tap()
    await tooltip.waitFor({ timeout: 3000 })
    assert.equal(await drawer.count(), 0)
  })
})

test('clicking a passive cell opens its reception without stealing summary selection', async () => {
  await withWorkbench({}, async (page) => {
    const row = page.locator('tr[data-reception-id="reception-95-83"]')
    const details = page.getByRole('region', { name: 'Reception details' })
    await row.getByText('Path trace · signal samples reported').dblclick()
    assert.ok(await page.evaluate(() => getSelection()?.toString()), 'Summary text should remain selectable')
    assert.equal(await details.count(), 0)

    const region = row.getByRole('cell', { name: 'KUN', exact: true })
    await region.click({ modifiers: ['Control'] })
    assert.equal(await details.count(), 0)
    await region.click()
    await details.waitFor()
    assert.match(await details.innerText(), /A46F29/)
    assert.match(await details.innerText(), /Path trace · signal samples reported/)
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    assert.match(await details.locator('time').innerText(), /15:59:49/)
    assert.ok(await details.evaluate(node => !node.contains(document.activeElement)))
    await details.getByRole('button', { name: /close/i }).click()
    await details.waitFor({ state: 'hidden' })
    assert.ok(await row.evaluate(node => node === document.activeElement))
  })
})

test('the desktop inspector switches rows in place and stays open when the filter excludes a reception', async () => {
  await withWorkbench({ viewport: { width: 1440, height: 800 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const scroller = traffic.getByRole('region', { name: 'Reception list scroll area' })
    const inspector = traffic.getByRole('region', { name: 'Reception details' })
    const selected = scroller.locator('tr[data-selected]')
    const first = scroller.locator('tr[data-reception-id="reception-95-83"]')
    const second = scroller.locator('tr[data-reception-id="reception-95-82"]')
    const staticCode = first.getByRole('cell', { name: 'A46F29', exact: true }).locator('code')

    assert.equal(await staticCode.count(), 1, 'The Code cell contains static text')
    assert.equal(await staticCode.textContent(), 'A46F29')
    assert.ok(await staticCode.evaluate((node) => {
      const { color, textDecorationLine } = getComputedStyle(node)
      const accent = getComputedStyle(node).getPropertyValue('--ks-accent').trim()
      const sample = document.createElement('span')
      sample.style.color = accent
      node.after(sample)
      const accentColor = getComputedStyle(sample).color
      sample.remove()
      return !node.closest('a, button, [role="link"], [role="button"]') && !node.hasAttribute('tabindex') &&
        textDecorationLine === 'none' && color === accentColor
    }))
    await staticCode.click()
    await inspector.waitFor({ timeout: 5000 })
    const original = await inspector.elementHandle()
    const [filtersBar, detailsBar] = await Promise.all([
      traffic.getByRole('heading', { name: 'Filters', exact: true }).boundingBox(), inspector.locator('header').boundingBox(),
    ])
    assert.ok(filtersBar && detailsBar && Math.abs(filtersBar.y - detailsBar.y) < 1 && Math.abs(filtersBar.height - detailsBar.height) < 1)
    const close = inspector.getByRole('button', { name: 'Close reception details' })
    assert.ok(await close.evaluate(node => {
      const style = getComputedStyle(node)
      return style.backgroundColor === 'rgba(0, 0, 0, 0)' && style.borderTopWidth === '0px'
    }))
    const [listBounds, panelBounds] = await Promise.all([scroller.boundingBox(), inspector.boundingBox()])
    assert.ok(listBounds && panelBounds && listBounds.x + listBounds.width <= panelBounds.x + 1,
      'The inspector sits beside the table rather than covering it')
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    assert.ok(await first.evaluate((node) => node === document.activeElement))
    assert.ok(await staticCode.evaluate((node) => !node.matches(':focus')))

    await second.getByRole('cell', { name: 'VNO', exact: true }).click()
    assert.ok(await inspector.evaluate((node, initial) => node === initial, original))
    assert.ok(await second.evaluate((node) => node === document.activeElement))
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-82')
    assert.equal(await second.getAttribute('aria-current'), 'true')
    assert.equal(await second.getAttribute('aria-haspopup'), null)
    assert.equal(await inspector.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    await scroller.getByRole('columnheader', { name: 'Code' }).click()
    assert.ok(await inspector.isVisible(), 'Clicking the table header keeps details open')
    await second.getByRole('cell', { name: 'VNO', exact: true }).click()

    const hasUniformBorder = (row, width) => {
      const cells = Array.from(row.cells, (cell) => getComputedStyle(cell, '::before'))
      const color = cells[0].borderBlockStartColor
      const edge = (style, side) => style[`border${side}Width`] === width &&
        style[`border${side}Style`] === 'solid' && style[`border${side}Color`] === color
      return cells.every((cell) => edge(cell, 'BlockStart') && edge(cell, 'BlockEnd')) &&
        edge(cells[0], 'InlineStart') && edge(cells.at(-1), 'InlineEnd')
    }
    assert.ok(await second.evaluate(hasUniformBorder, '1px'), 'Pointer selection has an even border across all cells')

    await page.keyboard.press('ArrowDown')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-81')
    assert.ok(await selected.evaluate((node) => node === document.activeElement && node.matches(':focus-visible')))
    assert.ok(await selected.evaluate(hasUniformBorder, '2px'), 'Keyboard focus has a continuous, thicker row border')
    assert.equal(await inspector.locator('time').getAttribute('datetime'), '2026-11-20T13:59:27.857Z')
    await page.keyboard.press('ArrowUp')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-82')
    assert.equal(await inspector.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')

    const edit = traffic.getByRole('button', { name: 'Edit arrival time', exact: true })
    await edit.click()
    await fillArrivalRange(traffic, '2026-11-19 16:00', '2026-11-19 16:15')
    const apply = traffic.getByRole('button', { name: 'Apply range' })
    await apply.click()
    assert.ok(await inspector.evaluate((node, initial) => node === initial, original))
    assert.match(await inspector.innerText(), /Outside the current filter/i)
    assert.equal(await inspector.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    assert.equal(await selected.count(), 0)
    await edit.focus()
    await page.keyboard.press('Escape')
    assert.ok(await inspector.isVisible(), 'Escape on a filter control does not close the inspector')

    await traffic.getByRole('button', { name: 'Clear arrival time' }).click()
    assert.doesNotMatch(await inspector.innerText(), /Outside the current filter/i)
    assert.equal(await inspector.locator('header').innerText(), 'Reception details')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-82')
    assert.equal(await traffic.getByRole('button', { name: /^Go to (reception )?details$/ }).count(), 0)
    await inspector.getByRole('button', { name: 'Close reception details' }).click()
    await inspector.waitFor({ state: 'hidden' })
    assert.ok(await second.evaluate((node) => node === document.activeElement))
  })
})

test('reception details separate Transport and Scope on desktop and in the narrow drawer', async () => {
  for (const width of [1440, 320]) {
    const mobile = width === 320
    await withWorkbench({ viewport: { width, height: 800 }, isMobile: mobile, hasTouch: mobile }, async (page) => {
      const row = page.locator('tr[data-reception-id="reception-95-79"]')
      const badgeScope = row.getByRole('button', { name: 'T·Direct #baltics', exact: true }).getByText('#baltics', { exact: true })
      assert.ok(await badgeScope.isVisible())
      const badgeScopeColor = await badgeScope.evaluate(node => getComputedStyle(node).color)
      const code = row.getByRole('cell').first()
      if (mobile) await code.tap()
      else await code.click()

      const details = page.getByRole(mobile ? 'dialog' : 'region', { name: 'Reception details', exact: true })
      await details.waitFor()
      const fact = (name) => details.locator('dl > div').filter({ has: page.getByText(name, { exact: true }) })
      const transport = fact('Transport')
      const scope = fact('Scope')
      const arrival = fact('Arrival (Vilnius)')
      assert.equal(await transport.locator('dd').innerText(), 'T·Direct')
      const scopeValue = scope.locator('dd code')
      assert.equal(await scopeValue.innerText(), '#baltics')
      assert.equal(await scopeValue.evaluate(node => getComputedStyle(node).color), badgeScopeColor)
      assert.equal(await scopeValue.evaluate(node => getComputedStyle(node).fontWeight), '700')
      const [routeBox, scopeBox, arrivalBox] = await Promise.all([
        transport.boundingBox(), scope.boundingBox(), arrival.boundingBox(),
      ])
      assert.ok(routeBox && scopeBox && arrivalBox && scopeBox.width > routeBox.width && scopeBox.width === arrivalBox.width)

      if (mobile) await details.getByRole('button', { name: 'Previous', exact: true }).tap()
      else await page.keyboard.press('ArrowUp')
      const noScope = scope.getByText('No scope', { exact: true })
      assert.ok(await noScope.isVisible())
      assert.notEqual(await noScope.evaluate(node => getComputedStyle(node).color), badgeScopeColor)
      assert.equal(await arrival.locator('time').getAttribute('datetime'), '2026-11-20T13:59:17.142Z')
    })
  }
})

test('a passive cell opens the narrow drawer and touch controls switch receptions', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const row = page.locator('tr[data-reception-id="reception-95-82"]')
    const reopen = page.getByRole('button', { name: 'Go to details', exact: true })
    assert.equal(await reopen.count(), 0)
    await row.getByRole('cell', { name: 'VNO', exact: true }).tap()
    const details = page.getByRole('dialog', { name: /reception/i })
    await details.waitFor()
    const header = details.locator('header')
    assert.equal(await header.innerText(), 'Reception details')
    const [headerBox, closeBox] = await Promise.all([
      header.boundingBox(), details.getByRole('button', { name: 'Close reception details' }).boundingBox(),
    ])
    assert.ok(headerBox && closeBox && closeBox.width >= 44 && closeBox.height >= 44 && headerBox.height <= closeBox.height + 16)
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    const bounds = await details.boundingBox()
    assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 321, 'Drawer fits the 320px viewport')
    assert.equal(await details.getAttribute('aria-modal'), 'true')
    const previous = details.getByRole('button', { name: /previous/i })
    const next = details.getByRole('button', { name: /next/i })
    await previous.tap()
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    assert.ok(await previous.isDisabled())
    await next.tap()
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    await next.tap()
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:27.857Z')
    await details.getByRole('button', { name: 'Close reception details' }).tap()
    await details.waitFor({ state: 'hidden' })
    await reopen.tap()
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:27.857Z')
    await details.getByRole('button', { name: 'Close reception details' }).press('Enter')
    await details.waitFor({ state: 'hidden' })
    assert.ok(await reopen.evaluate(node => node === document.activeElement))
  })
})

test('closing reception details focuses the table if the originating row has scrolled away', async () => {
  await withWorkbench({}, async (page) => {
    const scroller = page.getByRole('region', { name: 'Reception list scroll area' })
    const table = scroller.getByRole('table', { name: 'Packet receptions', exact: true })
    const row = scroller.locator('tr[data-reception-id="reception-95-83"]')
    await row.getByRole('cell', { name: 'A46F29', exact: true }).click()
    const details = page.getByRole('region', { name: 'Reception details' })
    await details.waitFor()
    await details.getByRole('button', { name: 'Close reception details' }).focus()
    const scrollArea = page.locator('[data-packet-table-scroll]')
    await scrollArea.evaluate(node => { node.scrollTop = node.scrollHeight })
    await row.waitFor({ state: 'detached' })
    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    assert.ok(await scroller.evaluate(node => node === document.activeElement))
    await assertMoreReceptions(page, table)
  })
})

test('keyboard opens the exact reception and continues through virtualized rows after closing', async () => {
  await withWorkbench({}, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const duplicateCode = table.getByRole('row').filter({ has: page.getByRole('cell', { name: 'A46F29', exact: true }) })
    const newest = duplicateCode.first()
    const details = page.getByRole('region', { name: 'Reception details' })
    const selectedRow = traffic.locator('tr[data-selected]')
    await traffic.getByRole('region', { name: 'Reception list scroll area' }).focus()
    await page.keyboard.press('Tab')
    assert.ok(await newest.evaluate(node => node === document.activeElement))
    await page.keyboard.press('Tab')
    assert.ok(await newest.getByRole('button').first().evaluate(node => node === document.activeElement))
    await page.keyboard.press('Shift+Tab')
    assert.ok(await newest.evaluate(node => node === document.activeElement))
    await page.keyboard.press('Enter')
    await details.waitFor()
    assert.equal(await selectedRow.getAttribute('data-reception-id'), 'reception-95-83')
    assert.ok(await selectedRow.locator('td').evaluateAll(cells =>
      cells.every(cell => getComputedStyle(cell).backgroundColor !== 'rgba(0, 0, 0, 0)')))
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    assert.equal(await selectedRow.count(), 0)
    assert.ok(await newest.evaluate(node => node === document.activeElement))
    await page.keyboard.press('ArrowDown')
    assert.ok(await duplicateCode.nth(1).evaluate(node => node === document.activeElement))
    await page.keyboard.press('Space')
    await details.waitFor()
    assert.equal(await selectedRow.getAttribute('data-reception-id'), 'reception-95-82')
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    assert.ok(await duplicateCode.nth(1).evaluate(node => node === document.activeElement))
    for (let index = 1; index < 24; index += 1) await page.keyboard.press('ArrowDown')
    assert.equal(await table.locator('tr:focus-within').getAttribute('aria-rowindex'), '29')
    assert.ok(await table.locator('tr[data-reception-id]').count() < 32)
  })
})

test('arrow keys change the selected reception without leaving its details drawer', async () => {
  await withWorkbench({ viewport: { width: 640, height: 800 } }, async (page) => {
    const table = page.getByRole('table', { name: 'Packet receptions', exact: true })
    const newest = table.locator('tr[data-reception-id="reception-95-83"]')
    const details = page.getByRole('dialog', { name: 'Reception details' })
    const selected = page.locator('tr[data-selected]')
    const arrival = details.locator('time')

    await newest.press('Enter')
    await details.waitFor()
    const drawer = await details.elementHandle()
    const initialFocus = await page.evaluateHandle(() => document.activeElement)
    assert.match(await details.ariaSnapshot(), /Up and Down arrow keys/)
    const status = details.getByRole('status').filter({ hasText: /^Reception \d+ of \d+:/ })
    await page.keyboard.press('ArrowUp')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-83')
    await page.keyboard.press('ArrowDown')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-82')
    assert.equal(await arrival.getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    assert.match(await status.innerText(), /A46F29.*VNO/)
    assert.ok(await details.evaluate((node, original) => node === original, drawer))
    assert.ok(await initialFocus.evaluate((node) => node === document.activeElement))

    await page.keyboard.press('Control+ArrowDown')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-82')
    await page.keyboard.press('ArrowUp')
    assert.equal(await arrival.getAttribute('datetime'), '2026-11-20T13:59:49.285Z')

    for (let index = 0; index < 5; index += 1) await page.keyboard.press('ArrowDown')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-78')
    const close = details.getByRole('button', { name: 'Close reception details' })
    await close.focus()
    assert.ok(await close.evaluate((node) => node === document.activeElement))
    await page.keyboard.press('ArrowUp')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-79')
    assert.ok(await close.evaluate((node) => node === document.activeElement))

    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    const current = table.locator('tr[data-reception-id="reception-95-79"]')
    assert.ok(await current.evaluate((node) => node === document.activeElement))
  })
})

test('inspector navigation preloads older receptions before reaching the last loaded row', async () => {
  await withWorkbench({}, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const scroller = traffic.getByRole('region', { name: 'Reception list scroll area' })
    const newest = table.locator('tr[data-reception-id="reception-95-83"]')
    const oldest = table.locator('tr[data-reception-id="reception-95-52"]')
    const selected = table.locator('tr[data-selected]')
    const scrollPosition = () => scroller.evaluate((node) => ({ top: node.scrollTop, left: node.scrollLeft, pageTop: window.scrollY }))
    const selectedRowIsVisible = () => selected.evaluate((row) => {
      const frame = row.closest('[data-packet-table-scroll]')
      const bounds = row.getBoundingClientRect()
      return bounds.top >= row.closest('table').tHead.getBoundingClientRect().bottom - 1 &&
        bounds.bottom <= frame.getBoundingClientRect().top + frame.clientHeight + 1
    })
    await scroller.scrollIntoViewIfNeeded()
    assert.equal(await oldest.count(), 0)

    await newest.press('Enter')
    const details = page.getByRole('region', { name: 'Reception details' })
    await details.waitFor()
    await details.getByRole('button', { name: 'Close reception details' }).focus()
    const start = await scrollPosition()
    assert.equal(await table.getAttribute('aria-rowcount'), '36')
    await page.keyboard.press('ArrowDown')
    assert.deepEqual(await scrollPosition(), start, 'Do not scroll while the next row is already visible')

    for (let index = 0; index < 17; index += 1) await page.keyboard.press('ArrowDown')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-65')
    await assertMoreReceptions(page, table)
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-65', 'Loading does not change the selection')

    for (let index = 0; index < 14; index += 1) await page.keyboard.press('ArrowDown')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-51')
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:54:06.428Z')
    assert.ok(await details.evaluate((node) => node.contains(document.activeElement)))
    const bottom = await scrollPosition()
    assert.ok(bottom.top > start.top, 'Selecting an offscreen reception should scroll the table')
    assert.equal(bottom.left, start.left)
    assert.equal(bottom.pageTop, start.pageTop)
    assert.ok(await selectedRowIsVisible())

    for (let index = 0; index < 32; index += 1) await page.keyboard.press('ArrowUp')
    assert.equal(await selected.getAttribute('data-reception-id'), 'reception-95-83')
    assert.ok((await scrollPosition()).top < bottom.top)
    assert.ok(await selectedRowIsVisible())

    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    assert.ok(await newest.evaluate((node) => node === document.activeElement))
  })
})

test('histogram Zoom applies the same arrival filter as the sidebar', async () => {
  await withWorkbench({ timezoneId: 'America/New_York', viewport: { width: 1440, height: 800 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const newest = () => table.locator('time').first().getAttribute('datetime')
    const initialNewest = await newest()
    const from = '2026-11-19T14:15:00.000Z'
    const to = '2026-11-19T14:44:30.000Z'
    const fields = arrivalFields(traffic)
    const apply = traffic.getByRole('button', { name: 'Apply range' })
    const active = traffic.getByRole('status', { name: 'Active time range' })
    const confirmation = traffic.getByRole('dialog', { name: 'Selected time range', exact: true })
    const edit = traffic.getByRole('button', { name: 'Edit arrival time', exact: true })
    const histogram = traffic.getByRole('figure', { name: /receptions over time.*Vilnius/i })
    const bins = traffic.locator('[data-histogram-bucket]')
    assert.equal(await bins.count(), 96)
    assert.ok(await histogram.getByText(/Nov 19.*16:00/).isVisible())
    assert.ok(await histogram.getByText(/Nov 20.*16:00/).isVisible())
    assert.match(await traffic.innerText(), /10,778/)
    await bins.nth(1).scrollIntoViewIfNeeded()
    const [earlier, later] = await Promise.all([bins.nth(1).boundingBox(), bins.nth(2).boundingBox()])
    assert.ok(earlier && later)
    await bins.nth(1).hover()
    assert.match(await traffic.locator('.time-histogram-inspect').innerText(), /112 receptions/i)
    await page.mouse.move(later.x + later.width / 2, later.y + later.height / 2)
    await page.mouse.down()
    await page.mouse.move(earlier.x + earlier.width / 2, earlier.y + earlier.height / 2, { steps: 4 })
    await page.mouse.up()
    await confirmation.waitFor()
    assert.match(await confirmation.innerText(), /2026-11-19.*16:15.*16:45/s)
    assert.equal(await confirmation.getByRole('combobox').count(), 0, 'The chart confirms the range without another picker')
    assert.equal(await fields.from.isVisible(), false)
    assert.equal(await newest(), initialNewest)
    assert.ok(await histogram.locator('.time-histogram-brush').isVisible())
    await confirmation.getByRole('button', { name: 'Zoom', exact: true }).click()
    await confirmation.waitFor({ state: 'hidden' })
    assert.equal(await newest(), '2026-11-19T14:44:47.142Z')
    assert.match(await active.innerText(), /2026-11-19.*16:15.*16:45/s)

    await bins.nth(3).click()
    await confirmation.waitFor()
    await page.keyboard.press('Escape')
    await confirmation.waitFor({ state: 'hidden' })
    assert.equal(await newest(), '2026-11-19T14:44:47.142Z')

    await page.mouse.move(later.x + later.width / 2, later.y + later.height / 2)
    await page.mouse.down()
    await page.mouse.move(earlier.x - earlier.width / 2, earlier.y + earlier.height / 2, { steps: 4 })
    await page.keyboard.press('Escape')
    await page.mouse.up()
    assert.equal(await confirmation.isVisible(), false, 'Canceling an in-progress drag does not open the confirmation')
    assert.equal(await newest(), '2026-11-19T14:44:47.142Z')

    await edit.click()
    assert.equal(await fields.from.inputValue(), '2026-11-19 16:15')
    assert.equal(await fields.to.inputValue(), '2026-11-19 16:45')
    await fields.to.fill('2026-11-19 16:40')
    await page.keyboard.press('Escape')
    assert.equal(await fields.to.isVisible(), false)
    assert.ok(await edit.evaluate(node => node === document.activeElement))
    await edit.click()
    assert.equal(await fields.to.inputValue(), '2026-11-19 16:45', 'Cancel discards unsubmitted manual changes')
    await fields.to.fill('2026-11-19 16:44:30')
    assert.equal(await newest(), '2026-11-19T14:44:47.142Z')
    const draftWidth = parseFloat(await histogram.locator('.time-histogram-brush').evaluate(node => node.style.width))
    assert.ok(Math.abs(draftWidth - (29.5 / (24 * 60) * 100)) < 0.001, 'Typed seconds are not snapped to a bucket')
    await apply.click()
    const summary = await active.innerText()
    assert.match(summary, /2026-11-19.*16:15.*16:44:30/s)
    assert.equal((summary.match(/2026-11-19/g) ?? []).length, 1, 'Same-day ranges print the date once')
    assert.notEqual(await newest(), initialNewest)
    assert.ok(await table.locator('time').evaluateAll((nodes, [lower, upper]) => nodes.every(({ dateTime }) => dateTime >= lower && dateTime < upper), [from, to]))
    assert.equal(await table.locator('[data-feed-status]').count(), 0)
    assert.equal(await bins.count(), 96)
    assert.equal(await bins.first().getAttribute('data-histogram-bucket'), '2026-11-19T14:00:00.000Z')
    assert.ok(await histogram.locator('.time-histogram-brush[data-applied]').isVisible())

    const appliedNewest = await newest()
    await bins.nth(3).click()
    await confirmation.waitFor()
    assert.equal(await active.innerText(), summary, 'A new chart selection does not hide the active range')
    assert.equal(await newest(), appliedNewest)
    await confirmation.getByRole('button', { name: 'Cancel', exact: true }).click()
    await confirmation.waitFor({ state: 'hidden' })
    assert.equal(await newest(), appliedNewest)
    await bins.nth(3).click()
    await confirmation.getByRole('button', { name: 'Zoom', exact: true }).click()
    assert.match(await table.innerText(), /no receptions/i)
    assert.equal(await bins.count(), 96)
    await traffic.getByRole('button', { name: 'Clear arrival time' }).click()
    assert.equal(await newest(), initialNewest)
    assert.equal(await table.locator('[data-feed-status]').count(), 3)
    assert.equal(await histogram.locator('.time-histogram-brush').count(), 0)
    await edit.click()
    assert.equal(await fields.from.inputValue(), '')
    assert.equal(await fields.to.inputValue(), '')
  })
})

test('keyboard users can open the single editor, correct bounds, and apply a range', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, timezoneId: 'America/New_York' }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const initialNewest = await table.locator('time').first().getAttribute('datetime')
    const { from, to } = arrivalFields(traffic)
    const apply = traffic.getByRole('button', { name: 'Apply range' })
    await traffic.getByRole('button', { name: 'Edit arrival time' }).press('Enter')
    assert.equal(await from.count(), 1)
    assert.ok(await from.evaluate(node => node === document.activeElement))
    assert.ok(await traffic.getByText(/Europe\/Vilnius/).first().isVisible())
    await apply.press('Enter')
    assert.equal(await from.getAttribute('aria-invalid'), 'true')
    assert.ok(await from.evaluate(node => node === document.activeElement))
    await fillArrivalRange(traffic, '2026-11-19 16:15', '2026-11-19 16:00')
    await apply.press('Enter')
    assert.equal(await table.locator('time').first().getAttribute('datetime'), initialNewest)
    assert.equal(await to.getAttribute('aria-invalid'), 'true')
    assert.ok(await to.evaluate(node => node === document.activeElement))

    await fillArrivalRange(traffic, '2026-03-29 03:30', '2026-03-29 04:30')
    await apply.press('Enter')
    assert.ok(await traffic.getByText(/this time is skipped/i).isVisible())
    assert.equal(await table.locator('time').first().getAttribute('datetime'), initialNewest)
    await fillArrivalRange(traffic, '2026-10-25 03:30', '2026-10-25 04:30')
    await apply.press('Enter')
    assert.ok(await traffic.getByText(/this time occurs twice/i).isVisible())
    assert.equal(await table.locator('time').first().getAttribute('datetime'), initialNewest)

    await fillArrivalRange(traffic, '2026-11-19 16:00', '2026-11-19 16:15')
    await to.press('Enter')
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-19T14:14:43.928Z')
    const scroller = traffic.getByRole('region', { name: 'Reception list scroll area' })
    await scroller.evaluate(node => { node.scrollTop = node.scrollHeight })
    await scroller.getByText('End of available history.', { exact: true }).waitFor({ timeout: 5000 })
    assert.equal(await table.getAttribute('aria-rowcount'), '57')
  })
})

test('touch can apply calendar defaults and edit an overnight range without horizontal overflow', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const edit = traffic.getByRole('button', { name: 'Edit arrival time' })
    await edit.tap()
    const editor = traffic.getByRole('dialog', { name: 'Arrival time', exact: true })
    const fromDay = arrivalDay(editor, '2026-11-19')
    const toDay = arrivalDay(editor, '2026-11-20')
    const apply = editor.getByRole('button', { name: 'Apply range' })
    const sheet = await editor.elementHandle()
    // Measure before tapping or scrolling any controls; wait for the sheet's opening motion.
    await page.waitForFunction(node => {
      const panel = node.getBoundingClientRect()
      return ['[data-value="2026-11-19"]', '[data-value="2026-11-20"]', 'button[type="submit"]'].every(selector => {
        const box = node.querySelector(selector)?.getBoundingClientRect()
        return box && box.height > 0 && box.top >= Math.max(0, panel.top) && box.bottom <= Math.min(innerHeight, panel.bottom) + 1
      })
    }, sheet, { timeout: 3000 })
    await fromDay.tap()
    await toDay.tap()
    assert.equal(await fromDay.getAttribute('aria-selected'), 'true')
    assert.equal(await toDay.getAttribute('aria-selected'), 'true')
    const fields = arrivalFields(traffic)
    assert.equal(await fields.from.inputValue(), '2026-11-19 00:00')
    assert.equal(await fields.to.inputValue(), '2026-11-20 23:59:59')
    const active = traffic.getByRole('status', { name: 'Active time range', includeHidden: true })
    assert.equal(await active.textContent(), 'All times')
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    await apply.tap()
    await editor.waitFor({ state: 'hidden' })
    assert.deepEqual(await active.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), [
      '2026-11-18T22:00:00.000Z', '2026-11-20T21:59:59.000Z',
    ])
    await edit.tap()
    await fillArrivalRange(traffic, '2026-11-19 23:45', '2026-11-20 00:15')
    await apply.tap()
    await editor.waitFor({ state: 'hidden' })
    assert.match(await active.innerText(), /2026-11-19.*23:45.*2026-11-20.*00:15/s)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const rows = await table.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime))
    assert.ok(rows.length > 0 && rows.every(time => time >= '2026-11-19T21:45:00.000Z' && time < '2026-11-19T22:15:00.000Z'))
    await traffic.getByRole('button', { name: 'Hide filters' }).tap()
    assert.equal(await traffic.getByRole('button', { name: 'Edit arrival time' }).isVisible(), false)
    await traffic.getByRole('button', { name: 'Show filters' }).tap()
    assert.match(await active.textContent(), /23:45.*00:15/s)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  })
})

test('the UI SDK agent can apply an arrival range, toggle filters, and clear the range', async () => {
  await withWorkbench({}, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const act = async (steps) => {
      const result = await page.evaluate(async (steps) => {
        const { agent } = await import('/tests/kitchen-sink-agent.mjs')
        return agent.act({ steps })
      }, steps)
      assert.equal(result.ok, true, result.error?.message)
    }
    await act([
      { ref: 'Edit arrival time', action: 'press' },
      { ref: 'Arrival dates', action: 'setValue', args: [['2026-11-19', '2026-11-19']] },
    ])
    const { from, to } = arrivalFields(traffic)
    assert.equal(await from.inputValue(), '2026-11-19 00:00')
    assert.equal(await to.inputValue(), '2026-11-19 23:59:59')
    await act([
      { ref: 'From', action: 'setValue', args: ['2026-11-19 16:14:43'] },
      { ref: 'To', action: 'setValue', args: ['2026-11-19 16:15'] },
    ])
    assert.equal(await from.inputValue(), '2026-11-19 16:14:43')
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    await act([{ ref: 'Apply range', action: 'press' }])
    assert.deepEqual(await table.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), ['2026-11-19T14:14:43.928Z'])
    await act([{ ref: 'Hide filters', action: 'toggle' }])
    assert.equal(await traffic.getByRole('button', { name: 'Edit arrival time' }).isVisible(), false)
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-19T14:14:43.928Z')
    await act([{ ref: 'Show filters', action: 'toggle' }])
    await act([{ ref: 'Clear arrival time', action: 'press' }])
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
  }, loadWithAgent)
})

test('selecting the same day twice fills an immediately applicable range', async () => {
  await withWorkbench({ timezoneId: 'America/New_York' }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const editor = traffic.getByRole('dialog', { name: 'Arrival time', exact: true })
    const edit = traffic.getByRole('button', { name: 'Edit arrival time' })
    const { from, to } = arrivalFields(traffic)
    const active = traffic.getByRole('status', { name: 'Active time range' })
    const day = arrivalDay(editor, '2026-11-19')
    await edit.press('Enter')
    assert.equal(await from.inputValue(), '')
    assert.equal(await to.inputValue(), '')
    await day.press('Enter')
    await day.press('Enter')
    assert.equal(await from.inputValue(), '2026-11-19 00:00')
    assert.equal(await to.inputValue(), '2026-11-19 23:59:59')
    assert.equal(await active.textContent(), 'All times')
    await editor.getByRole('button', { name: 'Apply range' }).press('Enter')
    await editor.waitFor({ state: 'hidden' })
    assert.deepEqual(await active.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), [
      '2026-11-18T22:00:00.000Z', '2026-11-19T21:59:59.000Z',
    ])
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const times = await table.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime))
    assert.ok(times.length && times.every(time => time >= '2026-11-18T22:00:00.000Z' && time < '2026-11-19T21:59:59.000Z'))
  })
})

test('selecting days with the calendar preserves exact times and waits for Apply', async () => {
  await withWorkbench({ timezoneId: 'America/New_York' }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const newest = () => table.locator('time').first().getAttribute('datetime')
    const initialNewest = await newest()
    const active = traffic.getByRole('status', { name: 'Active time range' })
    const editor = traffic.getByRole('dialog', { name: 'Arrival time', exact: true })
    const day = (date) => arrivalDay(editor, date)
    await traffic.getByRole('button', { name: 'Edit arrival time' }).click()
    assert.equal(await editor.getByRole('grid').count(), 1)
    await editor.getByRole('button', { name: 'Next month' }).click()
    assert.ok(await day('2026-12-19').isVisible())
    await editor.getByRole('button', { name: 'Previous month' }).click()
    await fillArrivalRange(traffic, '2026-11-19 16:14:43', '2026-11-20 16:15')
    assert.equal(await day('2026-11-20').getAttribute('aria-selected'), 'true', 'Manual dates are reflected in the calendar')

    // Pick a same-day range using the calendar; the From and To times are distinct.
    await day('2026-11-19').press('Enter')
    assert.equal(await newest(), initialNewest)
    await day('2026-11-19').press('Enter')
    assert.equal(await active.innerText(), 'All times')
    assert.equal(await newest(), initialNewest)

    await editor.getByRole('button', { name: 'Apply range' }).click()
    await editor.waitFor({ state: 'hidden' })
    assert.deepEqual(await active.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), [
      '2026-11-19T14:14:43.000Z', '2026-11-19T14:15:00.000Z',
    ])
    assert.deepEqual(await table.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), ['2026-11-19T14:14:43.928Z'])

    await traffic.getByRole('button', { name: 'Edit arrival time' }).click()
    await day('2026-11-20').click()
    await day('2026-11-20').click()
    await editor.getByRole('button', { name: 'Cancel', exact: true }).click()
    await editor.waitFor({ state: 'hidden' })
    assert.equal(await newest(), '2026-11-19T14:14:43.928Z')
    await traffic.getByRole('button', { name: 'Edit arrival time' }).click()
    assert.equal(await day('2026-11-19').getAttribute('aria-selected'), 'true')
    assert.equal(await day('2026-11-20').getAttribute('aria-selected'), 'false')
  })
})

test('hiding filters reclaims the rail without clearing the range or inspector', async () => {
  await withWorkbench({ viewport: { width: 1440, height: 800 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const list = traffic.getByRole('region', { name: 'Reception list scroll area' })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const edit = traffic.getByRole('button', { name: 'Edit arrival time', exact: true })
    const hide = traffic.getByRole('button', { name: 'Hide filters', exact: true })
    const show = traffic.getByRole('button', { name: 'Show filters', exact: true })
    const loaded = traffic.getByText(/^\d+ loaded$/)
    const active = traffic.getByRole('status', { name: 'Active time range' })
    const inspector = traffic.getByRole('region', { name: 'Reception details' })
    await table.locator('tr[data-reception-id] td').first().click()
    await edit.click()
    await fillArrivalRange(traffic, '2026-11-19 16:00', '2026-11-19 16:15')
    await traffic.getByRole('button', { name: 'Apply range' }).click()
    const withFilters = await list.boundingBox()
    const hideBox = await hide.boundingBox()
    const loadedWithFilters = await loaded.boundingBox()
    assert.ok(hideBox && loadedWithFilters && hideBox.x + hideBox.width <= loadedWithFilters.x)
    assert.ok(await hide.locator('svg[aria-hidden="true"]').isVisible())
    assert.equal(await hide.getAttribute('aria-expanded'), 'true')
    const controlled = await hide.getAttribute('aria-controls')
    assert.ok(controlled)
    await hide.press('Enter')
    assert.equal(await show.getAttribute('aria-expanded'), 'false')
    assert.equal(await show.getAttribute('aria-controls'), controlled)
    assert.ok(await show.evaluate(node => node === document.activeElement))
    assert.equal(await edit.isVisible(), false)
    assert.ok(await traffic.getByText('1 active filter', { exact: true }).isVisible())
    const withoutFilters = await list.boundingBox()
    assert.ok(withFilters && withoutFilters && withoutFilters.width > withFilters.width)
    const showBox = await show.boundingBox()
    const loadedWithoutFilters = await loaded.boundingBox()
    assert.ok(showBox && loadedWithoutFilters && showBox.x + showBox.width <= loadedWithoutFilters.x)
    assert.ok(await inspector.isVisible())
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-19T14:14:43.928Z')

    await page.getByRole('heading', { name: 'Packet traffic', exact: true }).click()
    assert.ok(await page.getByRole('main').evaluate(node => node === document.activeElement))
    await show.focus()
    await traffic.locator('[data-histogram-bucket]').nth(1).click()
    await traffic.getByRole('dialog', { name: 'Selected time range' }).getByRole('button', { name: 'Zoom' }).click()
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-19T14:29:51.964Z')
    assert.ok(await show.evaluate(node => node === document.activeElement))
    assert.ok(await inspector.isVisible())
    await show.press('Enter')
    assert.ok(await edit.isVisible())
    assert.ok(await hide.evaluate(node => node === document.activeElement))
    assert.deepEqual(await active.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), [
      '2026-11-19T14:15:00.000Z', '2026-11-19T14:30:00.000Z',
    ])
  })
})

test('the standalone searchable multi-select keeps hidden choices until Only replaces them', async () => {
  await withWorkbench({}, async (page) => {
    const specimen = page.getByRole('region', { name: 'Searchable multi-select', exact: true })
    const search = specimen.getByLabel('Search example packet types', { exact: true })
    await search.waitFor({ timeout: 3000 })
    assert.equal(await search.getAttribute('placeholder'), 'Search…')
    assert.equal(await specimen.getByText('Search example packet types', { exact: true }).isVisible(), false)
    const choice = name => specimen.getByRole('checkbox', { name, exact: true })
    const advert = choice('Advert')
    const text = choice('Text')
    const groupText = choice('Group text')
    assert.ok(await advert.evaluate(node => {
      for (let parent = node.parentElement; parent; parent = parent.parentElement) {
        if (/^(auto|scroll)$/.test(getComputedStyle(parent).overflowY) && parent.scrollHeight > parent.clientHeight) return true
      }
      return false
    }), 'The options have their own scroll container')
    assert.ok(await specimen.locator('[data-packet-type="0x04"]').isVisible())
    await advert.press('Space')
    await search.fill(' TeXT ')
    assert.equal(await advert.isVisible(), false)
    assert.ok(await groupText.isVisible())
    await text.press('Space')
    await search.fill('')
    assert.ok(await advert.isChecked())
    assert.ok(await text.isChecked())

    await search.fill('group text')
    await search.press('Tab')
    assert.ok(await groupText.evaluate(node => document.activeElement === node))
    await page.keyboard.press('Tab')
    const only = specimen.getByRole('button', { name: /^only group text$/i })
    assert.ok(await only.evaluate(node => document.activeElement === node && getComputedStyle(node).opacity === '1'))
    await page.keyboard.press('Enter')
    assert.ok(await groupText.isChecked())
    assert.equal(await search.inputValue(), 'group text')
    await search.fill('no-matching-packet-type')
    assert.equal(await specimen.getByRole('checkbox').count(), 0)
    assert.match(await specimen.innerText(), /no .*match/i)
    await search.fill('')
    assert.ok(await groupText.isChecked())
    assert.equal(await advert.isChecked(), false)
    assert.equal(await text.isChecked(), false)
  })
})

test('the standalone searchable multi-select supports the SDK and exposes Only on touch', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const specimen = page.getByRole('region', { name: 'Searchable multi-select', exact: true })
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [
        { ref: 'Search example packet types', action: 'setValue', args: ['Advert'] },
        { ref: 'Only Advert', action: 'press' },
      ] })
    })
    assert.equal(result.ok, true, result.error?.message)
    assert.ok(await specimen.getByRole('checkbox', { name: 'Advert', exact: true }).isChecked())
    const search = specimen.getByLabel('Search example packet types', { exact: true })
    await search.fill('trace')
    const only = specimen.getByRole('button', { name: /^only trace$/i })
    await only.scrollIntoViewIfNeeded()
    assert.ok(await only.evaluate(node => getComputedStyle(node).opacity === '1'))
    await only.tap()
    await search.fill('')
    assert.ok(await specimen.getByRole('checkbox', { name: 'Trace', exact: true }).isChecked())
    assert.equal(await specimen.getByRole('checkbox', { name: 'Advert', exact: true }).isChecked(), false)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  }, loadWithAgent)
})

test('hash search finds older receptions, combines with filters, and stays above the histogram', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const search = traffic.getByRole('searchbox', { name: 'Search by hash', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const rows = table.locator('tr[data-reception-id]')
    const buckets = traffic.locator('[data-histogram-bucket]')
    const bucketStarts = () => buckets.evaluateAll(nodes => nodes.map(node => node.getAttribute('data-histogram-bucket')))
    const initialBuckets = await bucketStarts()
    assert.equal(await search.getAttribute('placeholder'), 'Search by hash...')
    assert.equal(await traffic.getByText('Hash', { exact: true }).isVisible(), false)
    const inputBox = await search.boundingBox()
    const chartBox = await traffic.locator('[data-histogram-plot]').boundingBox()
    assert.ok(inputBox && chartBox && inputBox.y + inputBox.height <= chartBox.y)
    assert.ok(Math.abs(inputBox.x - chartBox.x) < 1 && Math.abs(inputBox.width - chartBox.width) < 1)
    assert.equal(await table.getByText('A41000', { exact: true }).count(), 0)
    await search.fill(' a41000 ')
    assert.deepEqual(await rows.locator('td:first-child').allTextContents(), ['A41000', 'A41000'])
    await search.fill('41000')
    assert.equal(await rows.count(), 2)
    await traffic.getByRole('button', { name: 'Region', exact: true }).tap()
    await traffic.getByRole('checkbox', { name: 'KUN', exact: true }).tap()
    assert.deepEqual(await rows.locator('td:nth-child(5)').allTextContents(), ['KUN'])
    await traffic.getByRole('button', { name: 'Hide filters', exact: true }).tap()
    assert.equal(await search.inputValue(), '41000')
    await search.fill('no-such-hash')
    assert.ok(await table.getByText('No receptions match the current filters.').isVisible())
    await search.fill('')
    const regions = await rows.locator('td:nth-child(5)').allTextContents()
    assert.ok(regions.length > 1)
    assert.deepEqual([...new Set(regions)], ['KUN'])
    assert.deepEqual(await bucketStarts(), initialBuckets)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  })
})

test('the UI SDK agent can search by hash', async () => {
  await withWorkbench({ viewport: { width: 1440, height: 800 } }, async (page) => {
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref: 'Search by hash', action: 'setValue', args: ['a41000'] }] })
    })
    assert.equal(result.ok, true, result.error?.message)
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const inputBox = await traffic.getByRole('searchbox', { name: 'Search by hash', exact: true }).boundingBox()
    const chartBox = await traffic.locator('[data-histogram-plot]').boundingBox()
    assert.ok(inputBox && chartBox && Math.abs(inputBox.x - chartBox.x) < 1 && Math.abs(inputBox.width - chartBox.width) < 1)
    assert.deepEqual(await traffic.locator('tr[data-reception-id] td:first-child').allTextContents(), ['A41000', 'A41000'])
  }, loadWithAgent)
})

test('packet filter sections combine, stay applied when hidden, and share the arrival range', async () => {
  await withWorkbench({ viewport: { width: 1440, height: 800 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const rows = table.locator('tr[data-reception-id]')
    const hist = traffic.locator('[data-histogram-bucket]')
    const bucketStarts = () => hist.evaluateAll(nodes => nodes.map(node => node.getAttribute('data-histogram-bucket')))
    const initialBuckets = await bucketStarts()
    const section = (name) => traffic.getByRole('button', { name: new RegExp(`^${name}\\b`) })
    const choice = (name) => traffic.getByRole('checkbox', { name, exact: true })
    const types = () => rows.locator('[data-packet-type]').evaluateAll(nodes => [...new Set(nodes.map(node => node.getAttribute('data-packet-type')))].sort())
    await rows.first().press('Enter')
    const inspector = traffic.getByRole('region', { name: 'Reception details' })
    const selectedTime = await inspector.locator('time').getAttribute('datetime')
    for (const name of ['Packet type', 'Scope', 'Observer', 'Region']) {
      assert.equal(await section(name).getAttribute('aria-expanded'), 'false')
    }

    await section('Packet type').press('Enter')
    const search = traffic.getByLabel('Search packet type', { exact: true })
    const initialReception = await rows.first().getAttribute('data-reception-id')
    await search.fill('advert')
    const advertBadge = traffic.getByRole('group', { name: 'Packet type options' }).locator('[data-packet-type="0x04"]')
    assert.ok(await advertBadge.isVisible())
    assert.equal(await choice('Text').isVisible(), false)
    assert.equal(await rows.first().getAttribute('data-reception-id'), initialReception)
    await traffic.getByRole('button', { name: /^only advert$/i }).press('Enter')
    assert.deepEqual(await types(), ['0x04'])
    await search.fill('')
    await choice('Text').press('Space')
    assert.deepEqual(await types(), ['0x02', '0x04'])
    await section('Packet type').press('Enter')
    assert.equal(await choice('Advert').isVisible(), false)
    assert.deepEqual(await types(), ['0x02', '0x04'])

    await section('Scope').click()
    await choice('#baltics').click()
    await section('Observer').click()
    await choice('Kaunas 1').click()
    assert.equal(await rows.first().getAttribute('data-reception-id'), 'reception-94-114')
    await section('Region').click()
    await choice('VNO').click()
    await choice('KUN').click()
    assert.equal(await rows.first().getAttribute('data-reception-id'), 'reception-94-114')
    await traffic.getByRole('button', { name: /^only vno$/i }).press('Enter')
    assert.ok(await table.getByText('No receptions match the current filters.').isVisible())
    assert.ok(await inspector.getByText('Outside the current filter').isVisible())
    assert.equal(await inspector.locator('time').getAttribute('datetime'), selectedTime)
    await traffic.getByRole('button', { name: /^only kun$/i }).press('Enter')
    assert.equal(await rows.first().getAttribute('data-reception-id'), 'reception-94-114')

    await traffic.getByRole('button', { name: 'Hide filters', exact: true }).click()
    assert.equal(await section('Region').isVisible(), false)
    assert.ok(await traffic.getByText('4 active filters', { exact: true }).isVisible())
    assert.equal(await rows.first().getAttribute('data-reception-id'), 'reception-94-114')
    assert.deepEqual(await bucketStarts(), initialBuckets)
    await hist.nth(4).click()
    await traffic.getByRole('dialog', { name: 'Selected time range' }).getByRole('button', { name: 'Zoom' }).click()
    assert.ok(await traffic.getByText('5 active filters', { exact: true }).isVisible())
    assert.deepEqual(await rows.evaluateAll(nodes => nodes.map(node => node.getAttribute('data-reception-id'))), [
      'reception-4-54', 'reception-4-30', 'reception-4-6',
    ])
    assert.deepEqual(await bucketStarts(), initialBuckets)

    await traffic.getByRole('button', { name: 'Show filters', exact: true }).press('Enter')
    if (await section('Region').getAttribute('aria-expanded') === 'false') await section('Region').press('Enter')
    assert.equal(await choice('KUN').getAttribute('aria-checked'), 'true')
    await traffic.getByRole('button', { name: 'Clear arrival time' }).click()
    assert.equal(await rows.first().getAttribute('data-reception-id'), 'reception-94-114')
  })
})

test('the packet filter sidebar scrolls independently of the reception list', async () => {
  for (const width of [320, 1440]) {
    const touch = width === 320
    await withWorkbench({ viewport: { width, height: 700 }, isMobile: touch, hasTouch: touch }, async (page) => {
      const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
      const sidebar = traffic.getByRole('region', { name: 'Packet traffic filters', exact: true })
      const list = traffic.getByRole('region', { name: 'Reception list scroll area', exact: true })
      for (const name of ['Packet type', 'Scope', 'Observer', 'Region']) {
        const section = sidebar.getByRole('button', { name, exact: true })
        if (touch) await section.tap()
        else await section.press('Enter')
      }
      assert.ok(await sidebar.evaluate(node => node.scrollHeight > node.clientHeight && /^(auto|scroll)$/.test(getComputedStyle(node).overflowY)))
      if (!touch) {
        await sidebar.focus()
        await sidebar.press('End')
        await page.waitForFunction(node => node.scrollTop === node.scrollHeight - node.clientHeight, await sidebar.elementHandle(), { timeout: 3000 })
        assert.equal(await list.evaluate(node => node.scrollTop), 0)
      }
      const region = sidebar.getByRole('checkbox', { name: 'VNO', exact: true })
      if (touch) await region.tap()
      else await region.press('Space')
      assert.ok(await region.isChecked())
      assert.ok(await sidebar.evaluate(node => node.scrollTop > 0))
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    })
  }
})

test('the UI SDK agent can expand a packet filter and update its selection', async () => {
  await withWorkbench({}, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [
        { ref: 'Region', action: 'setOpen', args: [true] },
        { ref: 'KUN', action: 'setChecked', args: [true] },
      ] })
    })
    assert.equal(result.ok, true, result.error?.message)
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const regions = await table.locator('tr[data-reception-id] td:nth-child(5)').allTextContents()
    assert.ok(regions.length > 0)
    assert.deepEqual([...new Set(regions)], ['KUN'])
  }, loadWithAgent)
})

test('medium-width packet traffic keeps the filters and inline inspector usable together', async () => {
  await withWorkbench({ viewport: { width: 1024, height: 800 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const from = arrivalFields(traffic).from
    const edit = traffic.getByRole('button', { name: 'Edit arrival time' })
    assert.equal(await from.isVisible(), false)
    await traffic.locator('tr[data-reception-id] td').first().click()
    const inspector = traffic.getByRole('region', { name: 'Reception details' })
    const list = traffic.getByRole('region', { name: 'Reception list scroll area' })
    assert.equal(await traffic.getByRole('button', { name: /^Go to (reception )?details$/ }).count(), 0)
    await edit.click()
    assert.ok(await from.isVisible())
    const [trigger, panel, scroller] = await Promise.all([edit.boundingBox(), inspector.boundingBox(), list.boundingBox()])
    assert.ok(trigger && panel && scroller && trigger.y + trigger.height < scroller.y && scroller.x + scroller.width <= panel.x + 1)
    await page.keyboard.press('Escape')
    assert.ok(await inspector.isVisible(), 'Escape dismisses the editor, not the inspector')
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth))
  })
})

test('the packet table can also load older receptions through the UI SDK agent', async () => {
  await withWorkbench({}, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    await traffic.getByRole('heading', { name: 'Packet traffic', exact: true }).scrollIntoViewIfNeeded()
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref: 'Load older receptions', action: 'press' }] })
    })
    assert.equal(result.ok, true, result.error?.message)
    await assertMoreReceptions(page, table)
  }, loadWithAgent)
})

test('the UI SDK agent can reopen reception details on mobile', async () => {
  await withWorkbench({ viewport: { width: 390, height: 750 }, isMobile: true, hasTouch: true }, async (page) => {
    const row = page.locator('tr[data-reception-id="reception-95-83"]')
    await row.getByRole('cell', { name: 'A46F29', exact: true }).click()
    const details = page.getByRole('dialog', { name: 'Reception details' })
    await details.getByRole('button', { name: 'Close reception details' }).click()
    await details.waitFor({ state: 'hidden' })
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref: 'Go to details', action: 'press' }] })
    })
    assert.equal(result.ok, true, result.error?.message)
    await details.waitFor()
    assert.ok(await details.evaluate(node => node === document.activeElement))
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    assert.ok(await page.getByRole('button', { name: 'Go to details', exact: true }).evaluate(node => node === document.activeElement))
  }, loadWithAgent)
})

test('UI SDK agent can change identity and inspect packet hops without registering browser tools', async () => {
  await withWorkbench({}, async (page) => {
    const outcome = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const components = agent.snapshot().filter((entry) => !entry.offPage && entry.component !== 'more')
      const role = components.find((entry) => entry.label === 'Advertised role')
      const unknown = role?.items?.find((item) => item.label === 'Unknown')
      const result = unknown && await agent.act({ steps: [
        { ref: 'Advertised role', action: 'setValue', args: [unknown.value] },
        { ref: 'Observer', action: 'setChecked', args: [false] },
        { ref: 'Warning', action: 'setChecked', args: [true] },
      ] })
      const variants = await agent.query({ within: 'Indicator variant' })
      const variant = Array.isArray(variants) && variants[0]?.items?.find((item) => item.label === 'Corner pips')
      const changedVariant = variant && await agent.act({ steps: [
        { ref: 'Indicator variant', action: 'setValue', args: [variant.value] },
      ] })
      const hops = await agent.query({ text: 'B4', kind: 'popover' })
      const hop = Array.isArray(hops) && hops.find((entry) => /Hop 2\b.*B4/i.test(entry.label ?? ''))
      const opened = hop && await agent.act({ steps: [{ ref: hop.ref, action: 'setOpen', args: [true] }] })
      return { components: components.map(({ component, label }) => ({ component, label })), result, changedVariant, opened, hopRef: hop?.ref }
    })
    const snapshot = JSON.stringify(outcome.components)
    assert.ok(outcome.components.every(({ label }) => label), `Unnamed SDK entry: ${snapshot}`)
    for (const label of ['Advertised role', 'Observer', 'Warning']) {
      assert.ok(outcome.components.some((entry) => entry.label === label), `Missing ${label}: ${snapshot}`)
    }
    assert.equal(outcome.result?.ok, true, outcome.result?.error?.message)
    assert.match(await page.getByRole('status', { name: 'Node identity preview' }).innerText(), /Unknown · Warning: example node needs attention/)
    assert.equal(outcome.changedVariant?.ok, true, outcome.changedVariant?.error?.message)
    assert.match(await page.getByRole('combobox', { name: 'Indicator variant' }).innerText(), /Corner pips/)
    assert.equal(outcome.opened?.ok, true, outcome.opened?.error?.message ?? `Missing ambiguous hop: ${snapshot}`)
    const popup = page.getByRole('dialog', { name: 'Hop 2 · B4' })
    await popup.waitFor()
    const selection = await page.evaluate(async (ref) => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref, action: 'press', args: ['Riverside relay'] }] })
    }, outcome.hopRef)
    assert.equal(selection.ok, true, selection.error?.message)
    await popup.waitFor({ state: 'hidden' })
    assert.equal(await page.getByRole('status', { name: 'Packet path selection' }).innerText(), 'Selected Riverside relay for hop 2 (B4).')
  }, loadWithAgent)
})
