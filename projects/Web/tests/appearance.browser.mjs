import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
let browser

before(async () => {
  browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' })
})

after(async () => { await browser?.close() })

async function withAppearance(options, verify) {
  const context = await browser.newContext({ reducedMotion: 'reduce', serviceWorkers: 'block', ...options })
  const errors = []
  const unexpected = []
  context.on('page', page => page.on('pageerror', error => errors.push(error.message)))
  await context.routeWebSocket('**/*', socket => socket.close())
  await context.route('**/*', route => {
    const target = new URL(route.request().url())
    if (target.origin !== origin || /^\/(graphql|api)(\/|$)/.test(target.pathname)) {
      unexpected.push(target.href)
      return route.abort()
    }
    return route.continue()
  })
  try {
    await verify(context)
    assert.deepEqual(errors, [])
    assert.deepEqual(unexpected, [])
  } finally {
    await context.close()
  }
}

async function openWorkbench(context) {
  const page = await context.newPage()
  await page.goto(`${origin}/kitchen-sink`)
  await page.getByRole('heading', { name: 'Kitchen Sink', exact: true }).waitFor()
  return page
}

async function assertTheme(page, theme, appearance) {
  await page.waitForFunction(({ theme, appearance }) => {
    const root = document.documentElement
    return root.dataset.theme === theme && root.dataset.appearance === appearance
  }, { theme, appearance })
  assert.equal(await page.locator('html').evaluate(node => getComputedStyle(node).colorScheme), theme)
}

async function chooseAppearance(page, label) {
  await page.getByRole('combobox', { name: 'Appearance', exact: true }).click()
  await page.getByRole('option', { name: label, exact: true }).click()
}

function contrastRatio(first, second) {
  const luminance = color => {
    const channels = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => {
      const channel = value / 255
      return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
    })
    return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
  }
  const a = luminance(first)
  const b = luminance(second)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

test('system appearance follows OS changes while explicit choices persist and synchronize between tabs', async () => {
  await withAppearance({ colorScheme: 'dark', viewport: { width: 1440, height: 900 } }, async context => {
    const page = await openWorkbench(context)
    await assertTheme(page, 'dark', 'system')
    await page.emulateMedia({ colorScheme: 'light' })
    await assertTheme(page, 'light', 'system')
    const other = await openWorkbench(context)
    await chooseAppearance(page, 'Light')
    await assertTheme(other, 'light', 'light')
    await chooseAppearance(page, 'Dark')
    await assertTheme(page, 'dark', 'dark')
    await assertTheme(other, 'dark', 'dark')
    await page.reload()
    await page.getByRole('heading', { name: 'Kitchen Sink', exact: true }).waitFor()
    await assertTheme(page, 'dark', 'dark')
    await page.emulateMedia({ colorScheme: 'light' })
    await assertTheme(page, 'dark', 'dark')
    await chooseAppearance(page, 'System')
    await assertTheme(page, 'light', 'system')
    await assertTheme(other, 'dark', 'system')
    await chooseAppearance(page, 'Dark')
    await assertTheme(other, 'dark', 'dark')
    await other.evaluate(() => localStorage.clear())
    await assertTheme(page, 'light', 'system')
  })
})

test('appearance initializes before React and invalid preferences use the system theme', async () => {
  for (const [stored, theme, appearance] of [['light', 'light', 'light'], ['dark', 'dark', 'dark'], ['invalid', 'dark', 'system']]) {
    await withAppearance({ colorScheme: 'dark' }, async context => {
      await context.addInitScript(({ origin, stored }) => {
        if (location.origin === origin) localStorage.setItem('pulsas.appearance', stored)
      }, { origin, stored })
      const page = await context.newPage()
      await page.route('**/src/main.tsx*', route => route.abort())
      await page.goto(`${origin}/kitchen-sink`)
      assert.equal(await page.locator('#root').innerHTML(), '')
      assert.equal(await page.locator('html').getAttribute('data-theme'), theme)
      assert.equal(await page.locator('html').getAttribute('data-appearance'), appearance)
    })
  }
})

test('appearance remains usable when persistent storage is blocked', async () => {
  await withAppearance({ colorScheme: 'light', viewport: { width: 390, height: 844 } }, async context => {
    await context.addInitScript(() => {
      Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('Storage blocked', 'SecurityError') } })
    })
    const page = await openWorkbench(context)
    await assertTheme(page, 'light', 'system')
    await chooseAppearance(page, 'Dark')
    await assertTheme(page, 'dark', 'dark')
    await chooseAppearance(page, 'System')
    await assertTheme(page, 'light', 'system')
  })
})

test('keyboard appearance selection fits narrow screens and preserves explicit light and dark specimens', async () => {
  for (const hasTouch of [false, true]) {
    await withAppearance({ colorScheme: 'light', viewport: { width: 320, height: 844 }, isMobile: hasTouch, hasTouch }, async context => {
      const page = await openWorkbench(context)
      const appearance = page.getByRole('combobox', { name: 'Appearance', exact: true })
      await page.getByRole('checkbox', { name: 'Warning', exact: true }).click()
      assert.match(await page.getByRole('status', { name: 'Node identity preview' }).innerText(), /Warning/)
      const sample = page.getByRole('region', { name: 'Live status badge', exact: true })
      const sampleColors = () => sample.locator('[data-theme]').evaluateAll(nodes => nodes.map(node => ({
        theme: node.dataset.theme,
        background: getComputedStyle(node).backgroundColor,
        badges: [...node.querySelectorAll('[data-tone]')].map(badge => ({ fill: getComputedStyle(badge).backgroundColor, ink: getComputedStyle(badge).color })),
      })))
      const before = await sampleColors()
      await appearance.focus()
      await appearance.press('ArrowDown')
      await page.getByRole('listbox').waitFor()
      const bounds = await page.getByRole('listbox').boundingBox()
      assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 320)
      await page.keyboard.press('End')
      await page.keyboard.press('Enter')
      await assertTheme(page, 'dark', 'dark')
      assert.ok(await appearance.evaluate(node => node === document.activeElement))
      assert.equal(await page.getByRole('checkbox', { name: 'Warning', exact: true }).getAttribute('aria-checked'), 'true')
      assert.deepEqual(await sampleColors(), before)
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
      const control = await appearance.boundingBox()
      assert.equal(await page.evaluate(() => matchMedia('(pointer: coarse)').matches), hasTouch)
      const minimumSize = hasTouch ? 44 : 32
      assert.ok(control.width >= minimumSize && control.height >= minimumSize, `${hasTouch ? 'touch' : 'fine-pointer'} appearance target`)
    })
  }
})

test('workbench text, controls, and focus tokens meet contrast targets in both themes', async () => {
  await withAppearance({ viewport: { width: 1440, height: 900 } }, async context => {
    const page = await openWorkbench(context)
    for (const theme of ['Light', 'Dark']) {
      await chooseAppearance(page, theme)
      const tokens = await page.getByRole('main').evaluate(node => {
        const names = ['--ks-ground', '--ks-paper', '--ks-ink', '--ks-muted', '--ks-control-edge', '--ks-accent', '--ks-accent-wash', '--ks-focus']
        const probe = document.createElement('span')
        node.append(probe)
        const colors = Object.fromEntries(names.map(name => {
          probe.style.color = `var(${name}, var(--ks-accent))`
          return [name, getComputedStyle(probe).color]
        }))
        probe.remove()
        return colors
      })
      const ratio = (a, b) => contrastRatio(tokens[a], tokens[b])
      for (const background of ['--ks-ground', '--ks-paper']) {
        for (const foreground of ['--ks-ink', '--ks-muted', '--ks-accent']) {
          assert.ok(ratio(foreground, background) >= 4.5, `${theme}: ${foreground} on ${background} needs 4.5:1`)
        }
        for (const foreground of ['--ks-control-edge', '--ks-focus']) {
          assert.ok(ratio(foreground, background) >= 3, `${theme}: ${foreground} on ${background} needs 3:1`)
        }
      }
      assert.ok(ratio('--ks-accent', '--ks-accent-wash') >= 4.5, `${theme}: selected text needs 4.5:1`)
    }
  })
})

test('SDK agents can discover and change the appearance preference', async () => {
  await withAppearance({ colorScheme: 'light', viewport: { width: 390, height: 844 } }, async context => {
    const page = await openWorkbench(context)
    const result = await page.evaluate(async () => {
      const { agent } = await import('/tests/kitchen-sink-agent.mjs')
      const appearance = agent.snapshot().find(entry => !entry.offPage && entry.label === 'Appearance')
      if (!appearance) return { ok: false, error: { message: 'Appearance is missing from the SDK snapshot' } }
      return agent.act({ steps: [{ ref: appearance.ref, action: 'setValue', args: ['dark'] }] })
    })
    assert.equal(result.ok, true, result.error?.message)
    await assertTheme(page, 'dark', 'dark')
  })
})

test('neutral labels stay neutral and readable without removing semantic category colors', async () => {
  await withAppearance({ viewport: { width: 1440, height: 900 } }, async context => {
    const page = await openWorkbench(context)
    for (const theme of ['Light', 'Dark']) {
      await chooseAppearance(page, theme)
      const colors = await page.locator('.ks-badge[data-tone="neutral"]').first().evaluate(node => {
        const style = getComputedStyle(node)
        return { fill: style.backgroundColor, ink: style.color }
      })
      for (const color of Object.values(colors)) {
        const [r, g, b] = color.match(/[\d.]+/g).slice(0, 3).map(Number)
        assert.ok(Math.max(r, g, b) - Math.min(r, g, b) <= 16)
        assert.ok(b >= g && g >= r, `${theme}: neutral labels must not have a green cast`)
      }
      assert.ok(contrastRatio(colors.ink, colors.fill) >= 4.5, `${theme}: neutral label text contrast`)
      const semantic = await page.locator('.ks-badge[data-tone="green"]').first().evaluate(node => {
        const style = getComputedStyle(node)
        return { fill: style.backgroundColor, ink: style.color }
      })
      assert.notDeepEqual(semantic, colors, 'green categories remain distinguishable from neutral labels')
      assert.ok(contrastRatio(semantic.ink, semantic.fill) >= 4.5, `${theme}: semantic label text contrast`)
    }
  })
})

test('dark appearance reaches reception details and filter popups without remounting the workbench', async () => {
  await withAppearance({ colorScheme: 'dark', viewport: { width: 390, height: 844 } }, async context => {
    const page = await openWorkbench(context)
    await assertTheme(page, 'dark', 'system')
    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    await traffic.locator('tr[data-reception-id] td').first().click()
    const details = page.getByRole('dialog', { name: 'Reception details', exact: true })
    await details.waitFor()
    await page.waitForFunction(() => {
      const drawer = document.querySelector('.ks-reception-drawer')
      return drawer && getComputedStyle(drawer).opacity === '1'
    })
    const darkColors = await details.evaluate(node => ({ background: getComputedStyle(node).backgroundColor, ink: getComputedStyle(node).color }))
    assert.notEqual(darkColors.background, 'rgb(255, 255, 255)')
    assert.notEqual(darkColors.background, darkColors.ink)
    await details.getByRole('button', { name: 'Close reception details', exact: true }).click()
    await details.waitFor({ state: 'hidden' })
    await traffic.getByRole('button', { name: 'Edit arrival time', exact: true }).click()
    const from = traffic.getByLabel('From', { exact: true })
    await from.waitFor()
    assert.equal(await from.evaluate(node => getComputedStyle(node).colorScheme), 'dark')
    await page.keyboard.press('Escape')
    assert.equal(await traffic.getByRole('table', { name: 'Packet receptions', exact: true }).count(), 1)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  })
})

test('texture stays on environmental surfaces and is disabled for forced colors and print', async () => {
  await withAppearance({ colorScheme: 'light', viewport: { width: 1440, height: 900 } }, async context => {
    const page = await openWorkbench(context)
    const backgrounds = () => page.evaluate(() => Object.fromEntries(
      ['.app-shell', '.ks-header', '.app-appearance-trigger', '.ks-traffic-layout'].map(selector => [selector, getComputedStyle(document.querySelector(selector)).backgroundImage]),
    ))
    for (const theme of ['Light', 'Dark']) {
      await chooseAppearance(page, theme)
      const images = await backgrounds()
      assert.match(images['.app-shell'], /data:image\/svg\+xml/)
      assert.match(images['.ks-header'], /data:image\/svg\+xml/)
      assert.equal(images['.app-appearance-trigger'], 'none')
      assert.equal(images['.ks-traffic-layout'], 'none')
    }
    await page.emulateMedia({ forcedColors: 'active' })
    let images = await backgrounds()
    assert.equal(images['.app-shell'], 'none')
    assert.equal(images['.ks-header'], 'none')
    await page.emulateMedia({ forcedColors: 'none', media: 'print' })
    images = await backgrounds()
    assert.equal(images['.app-shell'], 'none')
    assert.equal(images['.ks-header'], 'none')
  })
})

test('dark action fills, inputs, and focus use readable roles alongside crisp white light surfaces', async () => {
  await withAppearance({ colorScheme: 'dark', viewport: { width: 1440, height: 900 } }, async context => {
    const page = await openWorkbench(context)
    const colors = locator => locator.evaluate(node => {
      const style = getComputedStyle(node)
      return { fill: style.backgroundColor, text: style.color, edge: style.borderTopColor, focus: style.outlineColor }
    })
    const shell = await colors(page.locator('.app-shell'))
    const chrome = await colors(page.locator('.app-header'))
    const workspace = await colors(page.locator('.ks-workspace').first())
    for (const surface of [shell.fill, chrome.fill, workspace.fill]) {
      const channels = surface.match(/\d+/g).slice(0, 3)
      assert.equal(new Set(channels).size, 1, 'dark surfaces use neutral charcoal')
    }
    assert.notEqual(chrome.fill, workspace.fill, 'chrome and working surfaces have distinct roles')
    const warning = page.getByRole('checkbox', { name: 'Warning', exact: true })
    await warning.click()
    await page.keyboard.press('Tab')
    await page.keyboard.press('Shift+Tab')
    assert.ok(await warning.evaluate(node => node === document.activeElement))
    const checked = await colors(warning)
    assert.ok(contrastRatio(checked.text, checked.fill) >= 4.5, 'checked indicator contrast')
    assert.ok(contrastRatio(checked.focus, checked.fill) >= 3, 'focus against checked fill')

    const traffic = page.getByRole('region', { name: 'Packet traffic', exact: true })
    await traffic.getByRole('button', { name: 'Edit arrival time', exact: true }).click()
    const input = traffic.getByLabel('From', { exact: true })
    await input.focus()
    const field = await colors(input)
    assert.ok(contrastRatio(field.text, field.fill) >= 4.5, 'input text contrast')
    assert.ok(contrastRatio(field.edge, field.fill) >= 3, 'input boundary contrast')
    assert.ok(contrastRatio(field.focus, field.fill) >= 3, 'input focus contrast')
    const action = await colors(traffic.getByRole('button', { name: 'Apply range', exact: true }))
    assert.ok(contrastRatio(action.text, action.fill) >= 4.5, 'filled action text contrast')
    assert.notEqual(action.fill, checked.focus, 'action and focus have distinct roles')
    await page.keyboard.press('Escape')

    await chooseAppearance(page, 'Light')
    assert.equal((await colors(page.locator('.ks-workspace').first())).fill, 'rgb(255, 255, 255)')
    for (const locator of [page.locator('.app-shell'), page.locator('.app-header')]) {
      const channels = (await colors(locator)).fill.match(/\d+/g).slice(0, 3).map(Number)
      assert.ok(Math.min(...channels) >= 240, 'light framing stays near white')
      assert.ok(Math.max(...channels) - Math.min(...channels) <= 5, 'light framing avoids a beige cast')
      assert.ok(channels[2] >= channels[0], 'light framing stays neutral or cool')
    }
  })
})
