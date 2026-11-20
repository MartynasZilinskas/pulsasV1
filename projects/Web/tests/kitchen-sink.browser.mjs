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
  await page.route('**/*', (route) => {
    const target = new URL(route.request().url())
    if (target.origin !== origin || target.pathname.startsWith('/graphql')) {
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
    const body = html.replace('src="/src/main.tsx"', 'src="/tests/kitchen-sink-agent.mjs"')
    assert.notEqual(body, html, 'Expected the Vite HTML to load /src/main.tsx')
    await route.fulfill({ response, body })
  })
}

async function assertMoreReceptions(page, table) {
  await page.waitForFunction(node => node.getAttribute('aria-rowcount') === '68', await table.elementHandle(), { timeout: 5000 })
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

    await traffic.getByRole('button', { name: 'Choose time range' }).press('Enter')
    await traffic.getByRole('button', { name: 'Stage range' }).press('Enter')
    await traffic.getByRole('button', { name: 'Zoom', exact: true }).press('Enter')
    assert.equal(await scroller.evaluate(node => node.scrollTop), 0)
    await traffic.getByRole('button', { name: 'Clear time range' }).press('Enter')
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
    const code = (row) => row.getByRole('button', { name: /^View reception/ })
    await code(before).press('ArrowDown')
    assert.ok(await code(after).evaluate(node => node === document.activeElement))
    await page.keyboard.press('ArrowUp')
    assert.ok(await code(before).evaluate(node => node === document.activeElement))
    await before.getByRole('button').last().focus()
    await page.keyboard.press('Tab')
    assert.ok(await code(after).evaluate(node => node === document.activeElement))
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
    assert.equal(await page.getByRole('dialog', { name: /reception/i }).count(), 0)
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
    const details = page.getByRole('dialog', { name: /reception/i })
    await row.getByText('Path trace · signal samples reported').dblclick()
    assert.ok(await page.evaluate(() => getSelection()?.toString()), 'Summary text should remain selectable')
    assert.equal(await details.count(), 0)

    const opener = row.getByRole('button', { name: /^View reception A46F29 · KUN/i })
    const region = row.getByRole('cell', { name: 'KUN', exact: true })
    await region.click({ modifiers: ['Control'] })
    assert.equal(await details.count(), 0)
    await region.click()
    await details.waitFor()
    assert.match(await details.innerText(), /A46F29/)
    assert.match(await details.innerText(), /Path trace · signal samples reported/)
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
    assert.match(await details.locator('time').innerText(), /15:59:49/)
    assert.ok(await details.evaluate(node => node.contains(document.activeElement)))
    await details.getByRole('button', { name: /close/i }).click()
    await details.waitFor({ state: 'hidden' })
    assert.ok(await opener.evaluate(node => node === document.activeElement))
  })
})

test('a passive cell opens the same drawer on touch', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const row = page.locator('tr[data-reception-id="reception-95-82"]')
    await row.getByRole('cell', { name: 'VNO', exact: true }).tap()
    const details = page.getByRole('dialog', { name: /reception/i })
    await details.waitFor()
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:38.571Z')
    const bounds = await details.boundingBox()
    assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 321, 'Drawer fits the 320px viewport')
  })
})

test('closing reception details focuses the table if the originating row has scrolled away', async () => {
  await withWorkbench({}, async (page) => {
    const scroller = page.getByRole('region', { name: 'Reception list scroll area' })
    const row = scroller.locator('tr[data-reception-id="reception-95-83"]')
    await row.getByRole('button', { name: /^View reception/ }).click()
    const details = page.getByRole('dialog', { name: /reception/i })
    await details.waitFor()
    const scrollArea = page.locator('[data-packet-table-scroll]')
    await scrollArea.evaluate(node => { node.scrollTop = node.scrollHeight })
    await row.waitFor({ state: 'detached' })
    await page.keyboard.press('Escape')
    await details.waitFor({ state: 'hidden' })
    assert.ok(await scroller.evaluate(node => node === document.activeElement))
  })
})

test('keyboard opens the exact reception and continues through virtualized rows after closing', async () => {
  await withWorkbench({}, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const code = /^View reception A46F29 · /i
    const duplicateCode = table.getByRole('button', { name: code })
    const newest = duplicateCode.first()
    const details = page.getByRole('dialog', { name: /reception/i })
    const selectedRow = traffic.locator('tr[data-selected]')
    await newest.press('Enter')
    await details.waitFor()
    assert.equal(await selectedRow.getAttribute('data-reception-id'), 'reception-95-83')
    const selectedStyles = await selectedRow.locator('td').evaluateAll(cells => {
      const marker = getComputedStyle(cells[0], '::before')
      return {
        startMarker: parseFloat(marker.borderInlineStartWidth) > 0 && marker.insetInlineStart === '0px',
        tintedCells: cells.every(cell => getComputedStyle(cell).backgroundColor !== 'rgba(0, 0, 0, 0)'),
        typeShadow: getComputedStyle(cells[1]).boxShadow,
      }
    })
    assert.deepEqual(selectedStyles, { startMarker: true, tintedCells: true, typeShadow: 'none' })
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
    assert.ok(await table.getByRole('button', { name: /^View reception/ }).count() < 32)
  })
})

test('Zoom narrows and rebins the histogram and table by the same exact instants', async () => {
  await withWorkbench({ timezoneId: 'America/New_York' }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const newest = () => table.locator('time').first().getAttribute('datetime')
    const initialNewest = await newest()
    const from = '2026-11-19T14:15:00.000Z'
    const to = '2026-11-19T14:45:00.000Z'
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
    const staged = traffic.getByRole('dialog', { name: 'Staged time range (Vilnius)', exact: true })
    await staged.waitFor()
    assert.deepEqual(await staged.locator('time').evaluateAll(nodes => nodes.map(node => node.dateTime)), [from, to])
    assert.match(await staged.locator('time').first().innerText(), /16:15/)
    assert.match(await staged.locator('time').last().innerText(), /16:45/)
    assert.match(await staged.innerText(), /182 receptions/i)
    assert.equal(await newest(), initialNewest)
    assert.equal(await bins.count(), 96)
    await staged.getByRole('button', { name: 'Zoom', exact: true }).click()
    await staged.waitFor({ state: 'hidden' })
    assert.equal(await newest(), '2026-11-19T14:44:47.142Z')
    assert.match(await traffic.getByRole('status', { name: 'Active time range' }).innerText(), /16:15.*16:45.*Vilnius/is)
    assert.ok(await table.locator('time').evaluateAll((nodes, [lower, upper]) => nodes.every(({ dateTime }) => dateTime >= lower && dateTime < upper), [from, to]))
    assert.equal(await bins.count(), 60)
    assert.equal(await table.locator('[data-feed-status]').count(), 0)
    assert.equal(await bins.first().getAttribute('data-histogram-bucket'), from)
    assert.equal(await bins.last().getAttribute('data-histogram-bucket'), '2026-11-19T14:44:30.000Z')
    assert.equal(await histogram.locator('.time-histogram-brush').count(), 0)
    const axis = histogram.locator('.time-histogram-axis')
    assert.deepEqual(await axis.locator('span').allTextContents(), ['16:15:00', '16:22:30', '16:30:00', '16:37:30', '16:45:00'])
    await bins.nth(1).click()
    await staged.waitFor()
    assert.match(await staged.innerText(), /4 receptions/i)
    await staged.getByRole('button', { name: 'Zoom', exact: true }).click()
    await staged.waitFor({ state: 'hidden' })
    assert.equal(await bins.count(), 30)
    assert.equal(await bins.first().getAttribute('data-histogram-bucket'), '2026-11-19T14:15:30.000Z')
    assert.equal(await newest(), '2026-11-19T14:15:56.250Z')
    assert.match(await traffic.getByRole('status', { name: 'Active time range' }).innerText(), /16:15:30.*16:16:00.*Vilnius/is)
    await traffic.getByRole('button', { name: /clear.*range/i }).click()
    assert.equal(await newest(), initialNewest)
    assert.equal(await bins.count(), 96)
    assert.equal(await table.locator('[data-feed-status]').count(), 3)

    const emptyBucket = bins.nth(3)
    await emptyBucket.click()
    await staged.waitFor()
    assert.match(await staged.innerText(), /0 receptions/i)
    await staged.getByRole('button', { name: 'Zoom', exact: true }).click()
    await staged.waitFor({ state: 'hidden' })
    assert.equal(await table.locator('time').count(), 0)
    assert.match(await table.innerText(), /no receptions/i)
    assert.equal(await bins.first().getAttribute('data-histogram-bucket'), '2026-11-19T14:45:00.000Z')
    assert.equal(await bins.locator('span').count(), 0)
  })
})

test('keyboard can stage, cancel, and apply a histogram bucket without dragging', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 } }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    const choose = traffic.getByRole('button', { name: 'Choose time range', exact: true })
    const picker = traffic.getByRole('dialog', { name: /Time range.*Vilnius/i })
    const staged = traffic.getByRole('dialog', { name: 'Staged time range (Vilnius)', exact: true })
    const stage = async () => {
      await choose.press('Enter')
      await picker.waitFor()
      assert.ok(await picker.getByRole('combobox', { name: 'First bucket' }).isVisible())
      assert.ok(await picker.getByRole('combobox', { name: 'Last bucket' }).isVisible())
      await picker.getByRole('button', { name: 'Stage range', exact: true }).press('Enter')
      await staged.waitFor()
    }
    const firstTime = () => table.locator('time').first().getAttribute('datetime')
    const initialNewest = await firstTime()
    await stage()
    await page.keyboard.press('Escape')
    await staged.waitFor({ state: 'hidden' })
    assert.equal(await firstTime(), initialNewest)
    assert.ok(await choose.evaluate(node => document.activeElement === node))
    await stage()
    await staged.getByRole('button', { name: 'Zoom', exact: true }).press('Enter')
    await staged.waitFor({ state: 'hidden' })
    assert.equal(await firstTime(), '2026-11-19T14:14:43.928Z')
    const scroller = traffic.getByRole('region', { name: 'Reception list scroll area' })
    await scroller.evaluate(node => { node.scrollTop = node.scrollHeight })
    await scroller.getByText('End of available history.', { exact: true }).waitFor({ timeout: 5000 })
    assert.equal(await table.getAttribute('aria-rowcount'), '57')
  })
})

test('touch can choose and apply histogram bounds without brushing', async () => {
  await withWorkbench({ viewport: { width: 320, height: 700 }, isMobile: true, hasTouch: true }, async (page) => {
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    await traffic.getByRole('button', { name: 'Choose time range', exact: true }).tap()
    const picker = traffic.getByRole('dialog', { name: /Time range.*Vilnius/i })
    const last = picker.getByRole('combobox', { name: 'Last bucket' })
    await last.tap()
    await page.getByRole('option', { name: /Nov 19.*16:15.*16:30/ }).tap()
    await picker.getByRole('button', { name: 'Stage range', exact: true }).tap()
    const staged = traffic.getByRole('dialog', { name: 'Staged time range (Vilnius)', exact: true })
    await staged.waitFor()
    assert.match(await staged.innerText(), /168 receptions/i)
    await staged.getByRole('button', { name: 'Zoom', exact: true }).tap()
    await staged.waitFor({ state: 'hidden' })
    const table = traffic.getByRole('table', { name: 'Packet receptions', exact: true })
    assert.equal(await table.locator('time').first().getAttribute('datetime'), '2026-11-19T14:29:51.964Z')
    assert.equal(await table.getAttribute('aria-rowcount'), '33')
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

test('the UI SDK agent can open a reception from the Code button', async () => {
  await withWorkbench({}, async (page) => {
    const opener = page.getByRole('button', { name: /^View reception A46F29 · KUN/i })
    await opener.scrollIntoViewIfNeeded()
    const label = await opener.getAttribute('aria-label')
    const result = await page.evaluate(async (ref) => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.act({ steps: [{ ref, action: 'press' }] })
    }, label)
    assert.equal(result.ok, true, result.error?.message)
    const details = page.getByRole('dialog', { name: /reception/i })
    await details.waitFor()
    assert.equal(await details.locator('time').getAttribute('datetime'), '2026-11-20T13:59:49.285Z')
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
