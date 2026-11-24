import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
let browser
before(async () => { browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' }) })
after(async () => { await browser?.close() })

async function withPackets(options, verify, agent = false) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce', ...options })
  const errors = []
  const blocked = []
  page.on('pageerror', error => errors.push(error.message))
  await page.routeWebSocket('**/*', socket => socket.close())
  await page.route('**/*', async route => {
    const request = route.request()
    const target = new URL(request.url())
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)
      || (['fetch', 'xhr'].includes(request.resourceType()) && !target.pathname.startsWith('/src/'))) {
      blocked.push(target.href)
      return route.abort()
    }
    if (agent && request.isNavigationRequest()) {
      const response = await route.fetch()
      const html = await response.text()
      const body = html.replace(/src="\/src\/main\.tsx(?:\?[^"]*)?"/, 'src="/tests/kitchen-sink-agent.mjs"')
      assert.notEqual(body, html)
      return route.fulfill({ response, body })
    }
    return route.continue()
  })
  try {
    assert.equal((await page.goto(`${origin}/packets`)).status(), 200)
    await page.getByRole('heading', { name: 'Packets', level: 1 }).waitFor()
    await page.getByRole('table', { name: 'Packet receptions' }).waitFor()
    await verify(page)
    assert.deepEqual(errors, [])
    assert.deepEqual(blocked, [])
  } finally { await page.close() }
}

const rows = page => page.locator('[data-reception-id]')
const filters = page => page.getByRole('region', { name: 'Packet traffic filters', exact: true })
const scroller = page => page.getByRole('region', { name: 'Reception list scroll area' })
async function verifyFilterControls(page) {
  const search = page.getByRole('searchbox', { name: 'Search by hash', exact: true })
  const context = page.locator('.ks-traffic-context')
  const toggle = context.getByRole('button', { name: /^(Hide|Show) filters$/ })
  const clear = context.getByRole('button', { name: 'Clear filters', exact: true })
  assert.equal(await page.getByText('Search by hash', { exact: true }).count(), 0)
  assert.equal(await search.getAttribute('placeholder'), 'Search by hash...')
  assert.equal(await toggle.count(), 1)
  for (const control of [search, toggle, clear]) {
    assert.ok(await control.isVisible())
    await control.scrollIntoViewIfNeeded()
    assert.ok(await control.evaluate(node => {
      const box = node.getBoundingClientRect()
      return node.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2))
    }), 'Search and paired filter controls must be reachable within their scroll region')
  }
  const searchBox = await search.boundingBox()
  const toggleBox = await toggle.boundingBox()
  const clearBox = await clear.boundingBox()
  assert.ok(Math.abs(clearBox.y - toggleBox.y) < 1 || (clearBox.y >= toggleBox.y + toggleBox.height && clearBox.y <= toggleBox.y + toggleBox.height + 10), 'Paired filter controls may wrap at enlarged text sizes')
  const histogram = await page.locator('.time-histogram').boundingBox()
  assert.ok(histogram.y >= searchBox.y + searchBox.height, 'Search precedes histogram')
  assert.ok(toggleBox.y >= histogram.y + histogram.height - 1, 'Paired filter controls follow histogram')
  assert.equal(await page.locator('.ks-traffic-hash-control .ks-traffic-filter-toggle').count(), 0)
  return { search, toggle }
}
async function noDocumentOverflow(page) {
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.documentElement.scrollHeight <= innerHeight), 'Only workspace regions should scroll')
  const table = await scroller(page).boundingBox()
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const fixed = await nav.evaluate(node => getComputedStyle(node).position === 'fixed')
  const bottom = fixed ? (await nav.boundingBox()).y : (await page.viewportSize()).height
  const footer = page.getByRole('contentinfo')
  assert.ok(await footer.isVisible(), 'Packets retains its bottom bar')
  const footerBox = await footer.boundingBox()
  assert.ok(table.height > 40 && table.y + table.height <= footerBox.y + 1, 'Receptions must remain usable above the bottom bar')
  assert.ok(footerBox.y + footerBox.height <= bottom + 1, 'Bottom bar must remain above navigation')
  assert.ok(Math.abs(footerBox.y + footerBox.height - bottom) < 1, 'Bottom bar sits at the workspace bottom')
}

test('credit footer stays compact without clipping on desktop, mobile, and enlarged text', async () => {
  for (const width of [1440, 390, 320]) {
    for (const fontSize of [16, 20]) {
      await withPackets({ viewport: { width, height: 900 }, isMobile: width < 1024, hasTouch: width < 1024 }, async page => {
        await page.evaluate(size => { document.documentElement.style.fontSize = `${size}px` }, fontSize)
        await noDocumentOverflow(page)
        const footer = page.getByRole('contentinfo')
        assert.match(await footer.innerText(), /Pulsas\s*·\s*Atviras Tinklas/)
        const bounds = await footer.boundingBox()
        const scale = fontSize / 16
        assert.ok(Math.abs(bounds.height - 30 * scale) < 1, `Credit footer must be 30px at standard text size and scale with text; at ${width}px / ${fontSize}px text got ${bounds.height}px`)
        assert.ok(await footer.evaluate((node, size) => {
          const style = getComputedStyle(node)
          const box = node.getBoundingClientRect()
          const range = document.createRange()
          range.selectNodeContents(node)
          return parseFloat(style.fontSize) === size * 0.6875
            && [...range.getClientRects()].every(rect => rect.x >= box.x && rect.right <= box.right && rect.y >= box.y && rect.bottom <= box.bottom)
        }, fontSize), 'Footer typography must remain readable and fully contained')
      })
    }
  }
})

test('desktop packets fills the viewport with independently scrolling left filters and history', async () => {
  await withPackets({}, async page => {
    await noDocumentOverflow(page)
    assert.match(await page.getByRole('contentinfo').innerText(), /Pulsas\s*·\s*Atviras Tinklas/)
    assert.equal(await page.locator('.packets-page > header').count(), 0)
    assert.doesNotMatch(await page.getByRole('main').innerText(), /Fixture data.*No live connection/)
    assert.ok(await page.getByText('Sample receptions', { exact: true }).isVisible())
    const rail = await filters(page).boundingBox()
    const list = await scroller(page).boundingBox()
    assert.ok(rail.x < list.x && rail.y < list.y && Math.abs(rail.y + rail.height - list.y - list.height) < 1)
    for (const label of ['Packet type', 'Scope', 'Observer', 'Region']) {
      assert.equal(await filters(page).getByRole('button', { name: label, exact: true }).getAttribute('aria-expanded'), 'true')
    }
    const initial = await page.locator('.ks-traffic-count').innerText()
    await scroller(page).evaluate(node => { node.scrollTop = node.scrollHeight })
    await page.waitForFunction(initial => document.querySelector('.ks-traffic-count').textContent !== initial, initial)
    assert.match(await page.locator('.ks-traffic-count').innerText(), /64 loaded/)
    await noDocumentOverflow(page)
  })
})

test('hash search, left filters, clear and hide/show compose without losing selection', async () => {
  await withPackets({}, async page => {
    const search = page.getByRole('searchbox', { name: 'Search by hash', exact: true })
    await search.fill('a41000')
    await page.waitForFunction(() => document.querySelectorAll('[data-reception-id]').length === 2)
    assert.ok((await rows(page).locator('.packet-reception-code').allTextContents()).every(code => code === 'A41000'))
    assert.equal(await rows(page).first().locator('.packet-reception-code').innerText(), 'A41000')
    await page.getByRole('button', { name: 'Clear hash search', exact: true }).click()
    assert.equal(await search.inputValue(), '')
    assert.ok(await search.evaluate(node => node === document.activeElement))
    const type = filters(page).getByRole('checkbox', { name: 'Text', exact: true })
    await type.focus()
    await page.keyboard.press('Space')
    assert.equal(await type.getAttribute('aria-checked'), 'true')
    assert.ok((await rows(page).locator('[data-packet-type]').evaluateAll(nodes => nodes.map(node => node.dataset.packetType))).every(value => value === '0x02'))
    await rows(page).first().press('Enter')
    const details = page.getByRole('region', { name: 'Reception details' })
    await details.waitFor()
    await page.getByRole('button', { name: 'Hide filters', exact: true }).click()
    assert.equal(await filters(page).isVisible(), false)
    assert.ok(await details.isVisible())
    assert.ok(await page.getByText('1 active filter', { exact: true }).isVisible())
    await search.fill('not-a-hash')
    assert.ok(await page.getByText('No receptions match the current filters.', { exact: true }).isVisible())
    assert.ok(await details.getByText('Outside the current filter').isVisible())
    await page.getByRole('button', { name: 'Clear filters', exact: true }).click()
    assert.equal(await search.inputValue(), '')
    await page.getByRole('button', { name: 'Show filters', exact: true }).click()
    assert.equal(await type.getAttribute('aria-checked'), 'false')
    assert.ok(await details.isVisible())
    await noDocumentOverflow(page)
  })
})

test('paired filter controls stay below histogram and preserve filters on desktop and mobile', async () => {
  for (const width of [1440, 390, 320]) {
    await withPackets({ viewport: { width, height: 900 }, isMobile: width < 1024, hasTouch: width < 1024 }, async page => {
      const { search, toggle } = await verifyFilterControls(page)
      if (await toggle.getAttribute('aria-expanded') === 'false') await toggle.click()
      const type = filters(page).getByRole('checkbox', { name: 'Text', exact: true })
      await type.click()
      await search.fill('a46f28')
      const selectedRows = await rows(page).evaluateAll(nodes => nodes.map(node => node.dataset.receptionId))
      assert.ok(selectedRows.length > 0)
      await toggle.focus()
      await page.keyboard.press('Space')
      assert.equal(await filters(page).isVisible(), false)
      assert.equal(await toggle.getAttribute('aria-expanded'), 'false')
      assert.ok(await toggle.evaluate(node => node === document.activeElement))
      await verifyFilterControls(page)
      await page.keyboard.press('Enter')
      assert.equal(await filters(page).isVisible(), true)
      assert.equal(await type.getAttribute('aria-checked'), 'true')
      assert.equal(await search.inputValue(), 'a46f28')
      assert.deepEqual(await rows(page).evaluateAll(nodes => nodes.map(node => node.dataset.receptionId)), selectedRows)
      await verifyFilterControls(page)
      await noDocumentOverflow(page)
    })
  }
})

test('arrival editor validates and applies a range; histogram zoom shares the filter', async () => {
  await withPackets({}, async page => {
    await page.getByRole('button', { name: 'Edit arrival time' }).click()
    const editor = page.getByRole('dialog', { name: 'Arrival time', exact: true })
    await editor.getByRole('button', { name: 'Apply range', exact: true }).click()
    assert.equal(await editor.getByLabel('From', { exact: true }).getAttribute('aria-invalid'), 'true')
    assert.ok(await editor.getByLabel('From', { exact: true }).evaluate(node => node === document.activeElement))
    await editor.getByLabel('From', { exact: true }).fill('2026-11-19 16:14:43')
    await editor.getByLabel('To', { exact: true }).fill('2026-11-19 16:15')
    await editor.getByRole('button', { name: 'Apply range', exact: true }).click()
    assert.equal(await rows(page).count(), 1)
    assert.equal(await rows(page).first().locator('time').getAttribute('datetime'), '2026-11-19T14:14:43.928Z')
    await page.getByRole('button', { name: 'Clear arrival time' }).click()
    await page.locator('[data-histogram-bucket]').nth(1).click()
    await page.getByRole('button', { name: 'Zoom', exact: true }).click()
    assert.ok(await page.locator('.time-histogram-brush[data-applied]').isVisible())
    await page.getByRole('button', { name: 'Clear filters', exact: true }).click()
    assert.equal(await page.locator('.time-histogram-brush[data-applied]').count(), 0)
  })
})

test('arrival field uses compact endpoint rows when they fit and reserves a separate clear slot without moving the next filter', async () => {
  for (const [width, fontSize] of [[1440, 16], [390, 16], [320, 24], [1440, 24]]) {
    await withPackets({ viewport: { width, height: 900 }, isMobile: width < 1024, hasTouch: width < 1024 }, async page => {
      await page.evaluate(size => { document.documentElement.style.fontSize = `${size}px` }, fontSize)
      const toggle = page.locator('.ks-traffic-context').getByRole('button', { name: /^(Hide|Show) filters$/ })
      if (await toggle.getAttribute('aria-expanded') === 'false') await toggle.click()
      const field = filters(page).locator('.ks-arrival-field')
      const edit = field.getByRole('button', { name: 'Edit arrival time', exact: true })
      const clear = field.getByRole('button', { name: 'Clear arrival time', exact: true, includeHidden: true })
      const next = filters(page).getByRole('button', { name: 'Packet type', exact: true })
      await field.scrollIntoViewIfNeeded()
      const geometry = async () => {
        const fieldBox = await field.boundingBox()
        return {
          field: { width: fieldBox.width, height: fieldBox.height },
          trigger: await edit.evaluate(node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height })),
          nextOffset: (await next.boundingBox()).y - fieldBox.y,
        }
      }
      assert.equal(await clear.isEnabled(), false)
      const empty = await geometry()
      await edit.press('Enter')
      const editor = page.getByRole('dialog', { name: 'Arrival time', exact: true })
      await editor.getByLabel('From', { exact: true }).fill('2026-11-19 16:14:43')
      await editor.getByLabel('To', { exact: true }).fill('2026-11-20 18:15:59')
      await editor.getByRole('button', { name: 'Apply range', exact: true }).click()
      await editor.waitFor({ state: 'hidden' })
      assert.deepEqual(await geometry(), empty, `Selection must preserve geometry at ${width}px / ${fontSize}px text`)
      assert.ok(await edit.evaluate(node => node === document.activeElement))
      assert.deepEqual(await field.locator('time').evaluateAll(nodes => nodes.map(node => node.getAttribute('datetime'))), ['2026-11-19T14:14:43.000Z', '2026-11-20T16:15:59.000Z'])
      assert.match(await edit.innerText(), /16:14:43/)
      assert.match(await edit.innerText(), /18:15:59/)
      assert.match(await edit.innerText(), /Vilnius/)
      const endpointLayout = await field.locator('time').evaluateAll(nodes => nodes.map(node => {
        const [date, time] = [...node.children].map(child => child.getBoundingClientRect())
        const gap = parseFloat(getComputedStyle(node).columnGap)
        return { fits: date.width + gap + time.width <= node.clientWidth, inline: Math.abs(date.y - time.y) < 1 }
      }))
      for (const endpoint of endpointLayout) assert.equal(endpoint.inline, endpoint.fits, 'Date and time should share a row whenever they fit')
      if (fontSize === 16) assert.ok(endpointLayout.every(endpoint => endpoint.inline), 'Standard sidebar and mobile must show one row per endpoint')
      assert.equal(await field.locator('button button, button a, button input').count(), 0)
      assert.ok(await field.evaluate(node => node.scrollWidth <= node.clientWidth), 'The range must fit the field')
      assert.ok(await field.locator('time > span').evaluateAll(nodes => nodes.every(node => {
        const range = document.createRange()
        range.selectNodeContents(node)
        const boxes = [...range.getClientRects()]
        const field = node.closest('.ks-arrival-field').getBoundingClientRect()
        return boxes.length === 1 && boxes[0].left >= field.left && boxes[0].right <= field.right
      })), 'Date and time tokens must remain whole and visible')
      await page.keyboard.press('Tab')
      assert.ok(await clear.evaluate(node => node === document.activeElement))
      await page.keyboard.press('Space')
      assert.deepEqual(await geometry(), empty, `Clearing must preserve geometry at ${width}px / ${fontSize}px text`)
      assert.ok(await edit.evaluate(node => node === document.activeElement))
      assert.equal(await clear.isEnabled(), false)
      assert.match(await edit.innerText(), /All times/)
      await edit.press('Enter')
      await editor.getByLabel('From', { exact: true }).fill('2026-11-03 00:00')
      await editor.getByLabel('To', { exact: true }).fill('2026-11-29 23:59:59')
      await editor.getByRole('button', { name: 'Apply range', exact: true }).click()
      await editor.waitFor({ state: 'hidden' })
      assert.deepEqual(await geometry(), empty, 'Mixed minute/second precision must not change field height')
      await clear.click()
      assert.deepEqual(await geometry(), empty)
      await edit.press('Space')
      await editor.waitFor()
      await page.keyboard.press('Escape')
      await editor.waitFor({ state: 'hidden' })
      assert.ok(await edit.evaluate(node => node === document.activeElement))
      await noDocumentOverflow(page)
    })
  }
})

test('pointer and keyboard reception details browse and restore focus', async () => {
  await withPackets({}, async page => {
    const first = rows(page).first()
    await first.locator('td').first().click()
    const details = page.getByRole('region', { name: 'Reception details' })
    await details.waitFor()
    const initial = await first.getAttribute('data-reception-id')
    await first.press('ArrowDown')
    assert.notEqual(await page.locator('[data-selected]').first().getAttribute('data-reception-id'), initial)
    await details.focus()
    await page.keyboard.press('ArrowDown')
    const selectedId = await page.locator('tr[data-selected]').getAttribute('data-reception-id')
    await details.getByRole('button', { name: 'Close reception details' }).click()
    assert.equal(await details.count(), 0)
    assert.equal(await page.evaluate(() => document.activeElement?.getAttribute('data-reception-id')), selectedId)
    await page.keyboard.press('Space')
    await details.waitFor()
    await page.keyboard.press('Escape')
    assert.equal(await details.count(), 0)
  })
})

test('mobile light/dark and enlarged text keep search, filters, details and navigation usable', async () => {
  for (const width of [320, 390]) for (const fontSize of [16, 24]) for (const theme of ['light', 'dark']) {
    await withPackets({ viewport: { width, height: 900 }, isMobile: true, hasTouch: true, colorScheme: theme }, async page => {
      await page.evaluate(size => { document.documentElement.style.fontSize = `${size}px` }, fontSize)
      await noDocumentOverflow(page)
      await verifyFilterControls(page)
      assert.equal(await page.locator('html').getAttribute('data-theme'), theme)
      await page.getByRole('button', { name: 'Show filters', exact: true }).tap()
      await noDocumentOverflow(page)
      await verifyFilterControls(page)
      const region = filters(page)
      const edit = region.getByRole('button', { name: 'Edit arrival time' })
      await edit.tap()
      const editor = page.getByRole('dialog', { name: 'Arrival time', exact: true })
      await editor.waitFor()
      await editor.getByRole('button', { name: 'Cancel', exact: true }).tap()
      await editor.waitFor({ state: 'hidden' })
      assert.ok(await edit.evaluate(node => node === document.activeElement))
      await region.getByRole('button', { name: 'Hide filters', exact: true }).tap()
      await rows(page).first().press('Enter')
      const drawer = page.getByRole('dialog', { name: 'Reception details', exact: true })
      await drawer.waitFor()
      const previous = await drawer.locator('time').getAttribute('datetime')
      await drawer.getByRole('button', { name: 'Next', exact: true }).tap()
      assert.notEqual(await drawer.locator('time').getAttribute('datetime'), previous)
      await drawer.getByRole('button', { name: 'Previous', exact: true }).tap()
      assert.equal(await drawer.locator('time').getAttribute('datetime'), previous)
      await drawer.getByRole('button', { name: 'Close reception details', exact: true }).tap()
      await drawer.waitFor({ state: 'hidden' })
      assert.ok(await page.evaluate(() => !!document.activeElement?.hasAttribute('data-reception-id')))
      assert.ok(await page.getByRole('navigation', { name: 'Main navigation' }).isVisible())
      await noDocumentOverflow(page)
    })
  }
})

test('SDK agent discovers named packets controls and changes search, filters and arrival time', async () => {
  await withPackets({}, async page => {
    const snapshot = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      return agent.snapshot().filter(entry => !entry.offPage && entry.component !== 'more')
    })
    console.log(JSON.stringify(snapshot.map(({ component, label, actions }) => ({ component, label, actions })), null, 2))
    for (const entry of snapshot) assert.ok(entry.label, `${entry.component} ${entry.ref} has no name`)
    for (const label of ['Search by hash', 'Packet filters', 'Clear filters', 'Packet type', 'Scope', 'Observer', 'Region', 'Edit arrival time']) {
      assert.ok(snapshot.some(entry => entry.label === label), `${label} missing from SDK snapshot`)
    }
    const act = async steps => {
      const result = await page.evaluate(async steps => {
        const { agent } = await import('/tests/kitchen-sink-agent.mjs')
        return agent.act({ steps })
      }, steps)
      assert.equal(result.ok, true, result.error?.message)
    }
    await act([{ ref: 'Search by hash', action: 'setValue', args: ['a41000'] }])
    assert.equal(await rows(page).count(), 2)
    assert.ok((await rows(page).locator('.packet-reception-code').allTextContents()).every(code => code === 'A41000'))
    await act([{ ref: 'Search by hash', action: 'clear' }, { ref: 'Edit arrival time', action: 'press' },
      { ref: 'From', action: 'setValue', args: ['2026-11-19 16:14:43'] }, { ref: 'To', action: 'setValue', args: ['2026-11-19 16:15'] },
      { ref: 'Apply range', action: 'press' }])
    assert.equal(await rows(page).count(), 1)
    await act([{ ref: 'Clear arrival time', action: 'press' }])
    assert.match(await page.getByRole('button', { name: 'Edit arrival time' }).innerText(), /All times/)
    await act([{ ref: 'Packet filters', action: 'setOpen', args: [false] }])
    assert.equal(await filters(page).isVisible(), false)
    await page.getByRole('button', { name: 'Show filters', exact: true }).waitFor()
    await act([{ ref: 'Packet filters', action: 'setOpen', args: [true] }])
    assert.ok(await filters(page).isVisible())
    assert.ok(await rows(page).count() > 1)
    for (const label of ['Text', 'No scope', 'Kaunas 1', 'KUN']) {
      const option = await page.evaluate(async label => {
        const { agent } = await import('/tests/kitchen-sink-agent.mjs')
        return agent.snapshot().find(entry => !entry.offPage && entry.component === 'checkbox' && entry.label === label)
      }, label)
      assert.ok(option, `${label} option must be discoverable`)
      await act([{ ref: option.ref, action: 'setChecked', args: [true] }])
      assert.equal(await filters(page).getByRole('checkbox', { name: label, exact: true }).getAttribute('aria-checked'), 'true')
    }
    await act([{ ref: 'Clear filters', action: 'press' }])
    for (const label of ['Text', 'No scope', 'Kaunas 1', 'KUN']) {
      assert.equal(await filters(page).getByRole('checkbox', { name: label, exact: true }).getAttribute('aria-checked'), 'false')
    }
  }, true)
})
