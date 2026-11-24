import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5179}`
let browser
before(async () => { browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' }) })
after(async () => { await browser?.close() })

async function withChannels(options, verify, search = '', agent = false) {
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
    assert.equal((await page.goto(`${origin}/channels${search}`)).status(), 200)
    await page.getByRole('region', { name: 'Channels workspace', exact: true }).waitFor()
    await verify(page)
    assert.deepEqual(errors, [])
    assert.deepEqual(blocked, [])
  } finally { await page.close() }
}

const timeline = page => page.getByLabel('Message timeline', { exact: true })
const evidence = page => page.getByRole('region', { name: 'Packet evidence', exact: true })
const rows = page => page.locator('[data-message-id]')
async function noOverflow(page) {
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.documentElement.scrollHeight <= innerHeight), 'Only workspace panes scroll')
  const footer = await page.getByRole('contentinfo').boundingBox()
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const fixed = await nav.evaluate(node => getComputedStyle(node).position === 'fixed')
  assert.ok(Math.abs(footer.y + footer.height - (fixed ? (await nav.boundingBox()).y : page.viewportSize().height)) < 1, 'Credit bar must remain above navigation at workspace bottom')
}

test('compact directory, URL channel selection and read-only messages', async () => {
  await withChannels({}, async page => {
    assert.ok(await page.getByText('Select a channel', { exact: true }).isVisible())
    assert.ok(await page.getByText('Fixture', { exact: true }).isVisible())
    assert.equal(await page.getByRole('textbox').count(), 0)
    await page.getByRole('button', { name: 'Browse Public', exact: true }).click()
    await page.getByRole('heading', { name: 'Public', exact: true }).waitFor()
    assert.equal(new URL(page.url()).searchParams.get('channel'), 'public')
    assert.equal(await page.getByRole('button', { name: 'Browse Public', exact: true }).getAttribute('aria-current'), 'true')
    assert.equal(await rows(page).count(), 20)
    assert.ok(await timeline(page).evaluate(node => node.scrollHeight - node.scrollTop - node.clientHeight < 2), 'New selection starts at latest')
    assert.equal(await page.getByRole('button', { name: /^(send|send message)$/i }).count(), 0)
    await noOverflow(page)
  })
})

test('automatic older pagination preserves reading anchor and focus; Latest returns to bottom', async () => {
  await withChannels({}, async page => {
    assert.equal(await rows(page).count(), 20)
    await timeline(page).focus()
    await timeline(page).evaluate(node => { node.scrollTop = 130 })
    await page.waitForFunction(() => document.querySelector('.channels-message-scroll').scrollTop === 130)
    const firstId = await rows(page).first().getAttribute('data-message-id')
    const before = await page.locator(`[data-message-id="${firstId}"]`).boundingBox()
    await page.keyboard.press('PageUp')
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 40)
    await timeline(page).evaluate(node => new Promise(resolve => {
      let previous = node.scrollTop
      let stable = 0
      function settled() {
        stable = node.scrollTop === previous ? stable + 1 : 0
        previous = node.scrollTop
        if (stable === 3) resolve()
        else requestAnimationFrame(settled)
      }
      requestAnimationFrame(settled)
    }))
    const after = await page.locator(`[data-message-id="${firstId}"]`).boundingBox()
    assert.ok(after.y >= before.y && after.y <= before.y + 131, 'Prepending preserves the reading anchor reached during native PageUp scrolling')
    assert.ok(await page.getByRole('button', { name: 'Latest messages', exact: true }).isVisible())
    assert.ok(await timeline(page).evaluate(node => node === document.activeElement), 'Automatic loading must not steal keyboard focus')
    await page.getByRole('button', { name: 'Latest messages', exact: true }).click()
    assert.ok(await timeline(page).evaluate(node => node.scrollHeight - node.scrollTop - node.clientHeight < 2 && node === document.activeElement))
    await page.keyboard.press('Home')
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 48)
    await page.keyboard.press('Home')
    assert.ok(await page.getByText('Beginning of history', { exact: true }).isVisible())
    assert.equal(await page.getByRole('button', { name: /load (older|more)/i }).count(), 0)
    await noOverflow(page)
  }, '?channel=public')
})

test('channel and evidence headings show focus only for keyboard navigation', async () => {
  await withChannels({}, async page => {
    const heading = page.getByRole('heading', { name: 'Field test', exact: true })
    const outline = locator => locator.evaluate(node => ({ focused: node === document.activeElement, visible: getComputedStyle(node).outlineStyle !== 'none' && parseFloat(getComputedStyle(node).outlineWidth) > 0 }))
    await page.getByRole('button', { name: 'Browse Field test', exact: true }).click()
    assert.deepEqual(await outline(heading), { focused: true, visible: false }, 'Pointer selection transfers focus without highlighting the heading')
    const message = rows(page).last().getByRole('button')
    await message.click()
    const evidenceHeading = evidence(page).getByRole('heading', { name: 'Packet evidence', exact: true })
    await page.waitForFunction(() => document.activeElement?.id === 'channels-evidence-title')
    assert.deepEqual(await outline(evidenceHeading), { focused: true, visible: false })
    await page.getByRole('button', { name: 'Close packet evidence', exact: true }).click()
    const browse = page.getByRole('button', { name: 'Browse #kaunas', exact: true })
    await browse.focus()
    await page.keyboard.press('Enter')
    assert.deepEqual(await outline(page.getByRole('heading', { name: '#kaunas', exact: true })), { focused: true, visible: true }, 'Keyboard selection keeps a visible focus indicator')
    await rows(page).last().getByRole('button').press('Enter')
    assert.deepEqual(await outline(evidenceHeading), { focused: true, visible: true })
    await page.keyboard.press('Escape')
    await noOverflow(page)
  })
})

test('wheel approaches load one page at a time without draining on evidence, resize or channel reset', async () => {
  await withChannels({}, async page => {
    await timeline(page).focus()
    await page.setViewportSize({ width: 1400, height: 900 })
    assert.equal(await rows(page).count(), 20)
    const trigger = rows(page).last().getByRole('button')
    await trigger.click()
    await evidence(page).waitFor()
    await page.getByRole('button', { name: 'Close packet evidence', exact: true }).click()
    assert.equal(await rows(page).count(), 20)
    await timeline(page).evaluate(node => { node.scrollTop = 130 })
    await page.waitForFunction(() => document.querySelector('.channels-message-scroll').scrollTop === 130)
    const first = rows(page).first()
    const id = await first.getAttribute('data-message-id')
    const before = await first.boundingBox()
    await timeline(page).hover()
    await page.mouse.wheel(0, -30)
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 40)
    const after = await page.locator(`[data-message-id="${id}"]`).boundingBox()
    assert.ok(Math.abs(after.y - before.y - 30) <= 1)
    await page.locator(`[data-message-id="${id}"] button`).click()
    await page.getByRole('button', { name: 'Close packet evidence', exact: true }).click()
    await page.setViewportSize({ width: 1440, height: 900 })
    assert.equal(await rows(page).count(), 40, 'Evidence and resize must not request another page')
    await timeline(page).hover()
    await page.mouse.wheel(0, -3000)
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 48)
    await page.mouse.wheel(0, -3000)
    await page.getByText('Beginning of history', { exact: true }).waitFor()
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    await page.getByRole('button', { name: 'Browse #baltics', exact: true }).click()
    await page.getByRole('heading', { name: '#baltics', exact: true }).waitFor()
    assert.equal(await rows(page).count(), 20)
    await page.getByRole('button', { name: 'Browse Public', exact: true }).click()
    await page.getByRole('heading', { name: 'Public', exact: true }).waitFor()
    assert.equal(await rows(page).count(), 20)
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    assert.ok(await timeline(page).evaluate(node => node.scrollHeight - node.scrollTop - node.clientHeight < 2), JSON.stringify(await timeline(page).evaluate(node => ({ top: node.scrollTop, height: node.scrollHeight, client: node.clientHeight }))))
    await noOverflow(page)
  }, '?channel=public')
})

test('native upward scrolling without wheel or key events loads history and preserves focus', async () => {
  await withChannels({}, async page => {
    assert.equal(await rows(page).count(), 20)
    await timeline(page).focus()
    const id = await rows(page).first().getAttribute('data-message-id')
    const offset = await timeline(page).evaluate(node => {
      node.scrollTop = 80
      const first = node.querySelector('[data-message-id]')
      const offset = first.getBoundingClientRect().top - node.getBoundingClientRect().top
      node.dispatchEvent(new Event('scroll'))
      return offset
    })
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 40)
    const after = await page.locator(`[data-message-id="${id}"]`).evaluate(node => node.getBoundingClientRect().top - node.closest('.channels-message-scroll').getBoundingClientRect().top)
    assert.ok(Math.abs(after - offset) < 1)
    assert.ok(await timeline(page).evaluate(node => node === document.activeElement))
    await page.setViewportSize({ width: 1400, height: 920 })
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    assert.equal(await rows(page).count(), 40)
    await timeline(page).evaluate(node => { node.scrollTop = 80; node.dispatchEvent(new Event('scroll')) })
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 48)
    assert.ok(await timeline(page).evaluate(node => node === document.activeElement))
    await noOverflow(page)
  }, '?channel=public')
})

test('native touch scrolling towards older messages loads one page without moving focus', async () => {
  await withChannels({ viewport: { width: 390, height: 900 }, isMobile: true, hasTouch: true }, async page => {
    await timeline(page).evaluate(node => { node.scrollTop = 160 })
    await page.waitForFunction(() => document.querySelector('.channels-message-scroll').scrollTop === 160)
    const focused = await page.evaluate(() => document.activeElement?.id)
    const session = await page.context().newCDPSession(page)
    await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: 190, y: 230 }] })
    for (const y of [250, 275, 300, 330, 370, 410]) await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: 190, y }] })
    await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    await page.waitForFunction(() => document.querySelectorAll('[data-message-id]').length === 40)
    assert.equal(await page.evaluate(() => document.activeElement?.id), focused)
    assert.equal(await rows(page).count(), 40)
    await session.detach()
    await noOverflow(page)
  }, '?channel=public')
})

test('evidence distinguishes variants, observers, clocks, and raw path prefixes', async () => {
  await withChannels({}, async page => {
    const trigger = page.locator('[data-message-id="public-message-36"] button')
    await trigger.press('Enter')
    await evidence(page).waitFor()
    assert.ok(await evidence(page).getByText('2 packet variants · 3 distinct observers', { exact: true }).isVisible())
    assert.ok(await evidence(page).getByText('Sent', { exact: true }).isVisible())
    assert.ok(await evidence(page).getByText('First arrived', { exact: true }).isVisible())
    assert.equal(await evidence(page).getByText('Group text', { exact: true }).count(), 1, 'Shared message type belongs in the message summary, not each wire variant')
    assert.ok(await evidence(page).locator('.channel-evidence-summary').getByText('Group text', { exact: true }).isVisible())
    assert.equal(await evidence(page).getByRole('article').count(), 2, 'Three observer hearings must not become three packet variants')
    assert.equal(await evidence(page).getByText('Flood', { exact: true }).count(), 1, 'Common transport appears once in the shared summary')
    assert.ok(await evidence(page).locator('.channel-evidence-summary').getByText('Flood', { exact: true }).isVisible())
    const first = evidence(page).getByRole('article', { name: 'Wire variant A47048', exact: true })
    const other = evidence(page).getByRole('article', { name: 'Wire variant A47049', exact: true })
    assert.ok(await first.getByText('4A → B7 → C2', { exact: true }).isVisible())
    assert.ok(await first.getByText('Kaunas 1, Kaunas 2', { exact: true }).isVisible())
    assert.ok(await other.getByText('B7 → C2', { exact: true }).isVisible())
    assert.ok(await other.getByText('Vilnius 1', { exact: true }).isVisible())
    assert.equal(await evidence(page).getByText('Flood history', { exact: true }).count(), 2)
    assert.equal(await evidence(page).getByRole('article').locator('.ks-route-badge').count(), 0)
    assert.ok(await evidence(page).getByText('4A → B7 → C2', { exact: true }).isVisible())
    assert.ok(await evidence(page).getByText('B7 → C2', { exact: true }).isVisible())
    assert.equal(await evidence(page).getByRole('link').count(), 0)
    assert.ok(await timeline(page).isVisible())
    await page.keyboard.press('Escape')
    await evidence(page).waitFor({ state: 'hidden' })
    assert.ok(await trigger.evaluate(node => node === document.activeElement))
    await page.locator('[data-message-id="public-message-44"] button').click()
    assert.equal(await evidence(page).getByText('Unavailable', { exact: true }).count(), 2)
    await page.getByRole('button', { name: 'Close packet evidence', exact: true }).click()
    await page.locator('[data-message-id="public-message-35"] button').click()
    assert.ok(await evidence(page).getByText('Empty path', { exact: true }).isVisible())
    assert.equal(await evidence(page).getByText('Direct', { exact: true }).count(), 1)
    assert.ok(await evidence(page).getByText('Remaining route', { exact: true }).isVisible())
    await page.getByRole('button', { name: 'Close packet evidence', exact: true }).click()
    await page.locator('[data-message-id="public-message-39"] button').click()
    assert.ok(await evidence(page).getByText('Unknown', { exact: true }).isVisible(), 'Missing sent time must remain unknown, not use arrival time')
    await noOverflow(page)
  }, '?channel=public')
})

test('empty channel and unknown URL channel recover through directory', async () => {
  await withChannels({}, async page => {
    assert.ok(await page.getByText('Channel unavailable', { exact: true }).isVisible())
    await page.getByRole('button', { name: 'Back to channels', exact: true }).click()
    await page.getByRole('button', { name: 'Browse #off-grid', exact: true }).click()
    assert.ok(await page.getByText('No messages in this channel', { exact: true }).isVisible())
    assert.equal(await rows(page).count(), 0)
    await page.getByRole('button', { name: 'Browse Field test', exact: true }).click()
    assert.equal(await rows(page).count(), 2)
    const text = 'Packet text stays text: <img src=x onerror=alert(1)>'
    assert.ok(await page.getByText(text, { exact: true }).isVisible())
    assert.equal(await page.locator('.channels-workspace img').count(), 0)
    await page.goBack()
    await page.getByText('No messages in this channel', { exact: true }).waitFor()
    await noOverflow(page)
  }, '?channel=missing')
})

test('mobile drill-down, medium evidence replacement, enlarged text and both themes', async () => {
  for (const width of [320, 390, 900, 1440]) {
    for (const colorScheme of ['light', 'dark']) {
      await withChannels({ viewport: { width, height: 900 }, colorScheme, isMobile: width < 1024, hasTouch: width < 1024 }, async page => {
        await page.evaluate(() => { document.documentElement.style.fontSize = '20px' })
        await page.getByRole('button', { name: 'Browse Public', exact: true }).click()
        const trigger = rows(page).last().getByRole('button')
        await trigger.click()
        await evidence(page).waitFor()
        assert.equal(await timeline(page).isVisible(), width >= 1280)
        await noOverflow(page)
        await page.getByRole('button', { name: 'Close packet evidence', exact: true }).click()
        assert.ok(await trigger.evaluate(node => node === document.activeElement))
        await noOverflow(page)
        if (width < 768) {
          await page.getByRole('button', { name: 'Back to channels', exact: true }).click()
          assert.ok(await page.getByRole('button', { name: 'Browse Public', exact: true }).evaluate(node => node === document.activeElement))
          assert.ok(await page.getByRole('navigation', { name: 'Channel directory' }).isVisible())
          await page.getByRole('button', { name: 'Browse Field test', exact: true }).click()
          await page.keyboard.press('Escape')
          assert.ok(await page.getByRole('button', { name: 'Browse Field test', exact: true }).evaluate(node => node === document.activeElement))
        }
      })
    }
  }
})

test('SDK agent discovers and operates channel browser controls', async () => {
  await withChannels({}, async page => {
    const snapshot = await page.evaluate(async () => (await import('/tests/kitchen-sink-agent.mjs')).agent.snapshot())
    const controls = snapshot.filter(entry => !entry.offPage && entry.component !== 'more')
    assert.ok(controls.every(entry => entry.label), JSON.stringify(controls.filter(entry => !entry.label)))
    for (const label of ['Browse Public', 'Browse #off-grid']) assert.ok(controls.some(entry => entry.label === label), `${label} must be discoverable`)
    const act = steps => page.evaluate(async steps => (await import('/tests/kitchen-sink-agent.mjs')).agent.act({ steps }), steps)
    assert.equal((await act([{ ref: 'Browse Public', action: 'press' }])).ok, true)
    await page.getByRole('heading', { name: 'Public', exact: true }).waitFor()
    const messages = await page.evaluate(async () => (await import('/tests/kitchen-sink-agent.mjs')).agent.snapshot())
    const message = messages.find(entry => entry.label?.startsWith('Inspect message from'))
    assert.ok(message)
    assert.equal((await act([{ ref: message.ref, action: 'press' }])).ok, true)
    await evidence(page).waitFor()
    assert.equal((await act([{ ref: 'Close packet evidence', action: 'press' }])).ok, true)
    await evidence(page).waitFor({ state: 'hidden' })
    assert.equal((await act([{ ref: 'Browse #off-grid', action: 'press' }])).ok, true)
    await page.getByText('No messages in this channel', { exact: true }).waitFor()
  }, '', true)
})

test('registered kitchen-sink specimens reuse directory and message selection controls', async () => {
  await withChannels({ viewport: { width: 390, height: 900 }, isMobile: true, hasTouch: true }, async page => {
    await page.goto(`${origin}/kitchen-sink#ks-chat-heading`)
    const specimen = page.getByRole('region', { name: 'Channel browser', exact: true })
    await specimen.waitFor()
    await specimen.scrollIntoViewIfNeeded()
    const selected = specimen.getByRole('button', { name: 'Browse Field test', exact: true })
    assert.equal(await selected.getAttribute('aria-current'), 'true')
    const message = specimen.getByRole('button', { name: /^Inspect message from Jonas/ })
    await message.press('Space')
    assert.equal(await message.getAttribute('aria-pressed'), 'true')
    assert.ok(await specimen.getByText('Message selected', { exact: true }).isVisible())
    await message.press('Enter')
    assert.equal(await message.getAttribute('aria-pressed'), 'false')
    await specimen.getByRole('button', { name: 'Browse #off-grid', exact: true }).click()
    assert.ok(await specimen.getByText('No messages in this channel', { exact: true }).isVisible())
    await page.evaluate(() => { document.documentElement.style.fontSize = '20px' })
    const long = specimen.getByRole('button', { name: /^Inspect message from Kaunas \/ portable field station/ })
    await long.scrollIntoViewIfNeeded()
    assert.ok(await long.evaluate(node => node.scrollWidth <= node.clientWidth && [...node.querySelectorAll('span')].every(part => part.scrollWidth <= part.clientWidth)), 'Long sender and unbroken token wrap inside the shared row')
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  })
})
