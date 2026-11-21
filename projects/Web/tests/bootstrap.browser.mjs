import assert from 'node:assert/strict'
import test from 'node:test'
import { chromium } from 'playwright'

test('the local Web application starts without a backend dependency', async () => {
  const url = `http://127.0.0.1:${process.env.PULSAS_WEB_TEST_PORT ?? 5173}`
  const browser = await chromium.launch({
    executablePath: process.env.CHROMIUM_EXECUTABLE ?? '/usr/bin/chromium',
  })
  try {
    const page = await browser.newPage()
    const pageErrors = []
    const unexpectedRequests = []
    page.on('pageerror', (error) => pageErrors.push(error.message))
    await page.route('**/*', (route) => {
      const target = new URL(route.request().url())
      if (target.origin !== url || target.pathname.startsWith('/graphql')) {
        unexpectedRequests.push(target.pathname)
        return route.abort()
      }
      return route.continue()
    })

    const health = await page.request.get(`${url}/health`)
    assert.equal(health.status(), 200)
    assert.equal(await health.text(), 'OK')

    const bootstrap = page.waitForResponse((response) => new URL(response.url()).pathname === '/src/main.tsx')
    assert.equal((await page.goto(url)).status(), 200)
    assert.equal((await bootstrap).status(), 200)
    await page.waitForURL(`${url}/packets`)
    await page.getByRole('heading', { name: 'Packets', level: 1 }).waitFor()
    assert.equal(await page.title(), 'Pulsas V1')
    assert.deepEqual(pageErrors, [])
    assert.deepEqual(unexpectedRequests, [])
  } finally {
    await browser.close()
  }
})
