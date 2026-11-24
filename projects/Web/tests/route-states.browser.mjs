import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'

const origin = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
let browser
before(async () => { browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium' }) })
after(async () => { await browser?.close() })

async function withRoute(intercept, verify) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, reducedMotion: 'reduce' })
  const blocked = []
  await page.routeWebSocket('**/*', socket => socket.close())
  await page.route('**/*', async route => {
    const target = new URL(route.request().url())
    if (target.origin !== origin || /^\/(api|graphql)(\/|$)/.test(target.pathname)) {
      blocked.push(target.href)
      return route.abort()
    }
    if (target.pathname === '/src/routes/packets/page.tsx') return intercept(route)
    return route.continue()
  })
  try {
    await verify(page)
    assert.deepEqual(blocked, [])
  } finally { await page.close() }
}

async function shellGeometry(page) {
  return page.evaluate(() => {
    const bounds = selector => {
      const { x, y, width, height } = document.querySelector(selector).getBoundingClientRect()
      return { x, y, width, height }
    }
    return { header: bounds('.app-header'), content: bounds('.app-content'), footer: bounds('.app-footer') }
  })
}

test('a delayed route indicator reserves the final workspace without moving the shell', async () => {
  let release
  const pending = new Promise(resolve => { release = resolve })
  await withRoute(async route => { await pending; return route.continue() }, async page => {
    try {
      await page.goto(`${origin}/packets`, { waitUntil: 'commit' })
      await page.getByRole('navigation', { name: 'Main navigation' }).waitFor()
      const waiting = await shellGeometry(page)
      await page.getByRole('status', { name: 'Loading packets', exact: true }).waitFor()
      assert.deepEqual(await shellGeometry(page), waiting)
      release()
      await page.getByRole('table', { name: 'Packet receptions' }).waitFor()
      assert.equal(await page.getByRole('status', { name: 'Loading packets', exact: true }).count(), 0)
      assert.deepEqual(await shellGeometry(page), waiting)
      assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight))
    } finally { release() }
  })
})

test('route failure keeps navigation and footer in place and reload retries the actual module', async () => {
  let failed = true
  await withRoute(route => failed
    ? route.fulfill({ status: 200, contentType: 'text/javascript', body: 'export default function FailedPage() { throw new Error("Controlled route failure") }' })
    : route.continue(), async page => {
    await page.goto(`${origin}/packets`)
    await page.getByRole('heading', { name: 'Unable to load this page', exact: true }).waitFor()
    assert.ok(await page.getByRole('navigation', { name: 'Main navigation' }).isVisible())
    assert.ok(await page.getByRole('contentinfo').isVisible())
    assert.doesNotMatch(await page.getByRole('main').innerText(), /Controlled route failure/)
    const failure = await shellGeometry(page)
    failed = false
    const reload = page.waitForEvent('load')
    await page.getByRole('button', { name: 'Reload page', exact: true }).press('Enter')
    await reload
    await page.getByRole('table', { name: 'Packet receptions' }).waitFor()
    assert.deepEqual(await shellGeometry(page), failure)
    assert.equal(await page.getByRole('heading', { name: 'Unable to load this page', exact: true }).count(), 0)
  })
})
