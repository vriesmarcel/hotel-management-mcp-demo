import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

// Offline bridge test: no hotel process or database, and no booking tool allowed.
test('room selection advances in a height-constrained MCP Apps card', async ({ page }) => {
  const ui = await readFile(new URL('../dist/reservations.html', import.meta.url), 'utf8');
  const host = await readFile(new URL('../dist/preview.html', import.meta.url), 'utf8');
  const uri = 'ui://hotel/reservations/v1';
  const calls = [];
  await page.route('http://hotel.test/**', async route => {
    if (route.request().method() === 'GET') return route.fulfill({ contentType: 'text/html', body: host.replace('min-height:680px', 'min-height:0') });
    const { id, method, params } = route.request().postDataJSON();
    let result = {};
    if (method === 'tools/list') result = { tools: [{ name: 'GetAvailableRooms', _meta: { ui: { resourceUri: uri } } }] };
    if (method === 'resources/read') result = { contents: [{ uri, mimeType: 'text/html;profile=mcp-app', text: ui }] };
    if (method === 'tools/call') {
      calls.push(params.name);
      let data;
      if (params.name === 'GetPackages') data = { food: [], services: [] };
      else if (params.name === 'GetAvailableRooms') data = { ...params.arguments, nights: 3, rooms: Array.from({ length: 13 }, (_, i) => ({ roomId: i + 1, category: i === 2 ? 'Family' : 'Single', nightlyRate: i === 2 ? 5000 : 2000 })) };
      else throw new Error(`Unexpected tool: ${params.name}`);
      result = { content: [{ type: 'text', text: JSON.stringify(data) }] };
    }
    return route.fulfill({ json: { jsonrpc: '2.0', id, result } });
  });
  await page.goto('http://hotel.test/demo/reservations');
  const app = page.frameLocator('#app');
  await expect(app.locator('.room')).toHaveCount(13);
  await expect(page.locator('#app')).toHaveCSS('height', '640px');
  await app.getByRole('button', { name: 'Find rooms', exact: true }).click();
  await expect(app.locator('#count')).toContainText('13 rooms across all categories');
  await expect(app.locator('#availability')).toBeInViewport();
  await app.getByRole('button', { name: 'Room 3 Family 5,000/ night', exact: true }).click();
  await expect(app.locator('#availability')).toBeHidden();
  await expect(app.locator('#details')).toBeInViewport();
  await expect(app.locator('#selection')).toContainText('Room 3 · Family');
  await expect(app.locator('#review')).toBeHidden();
  await app.getByRole('button', { name: 'Change room' }).click();
  await expect(app.locator('#details')).toBeHidden();
  await expect(app.locator('#availability')).toBeInViewport();
  await app.getByRole('button', { name: 'Room 1 Single 2,000/ night', exact: true }).click();
  await expect(app.locator('#selection')).toContainText('Room 1 · Single');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(app.locator('#changeRoom')).toBeInViewport();
  const child = page.frames().find(f => f.parentFrame());
  expect(await child.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await app.locator('#changeRoom').screenshot({ path: 'test-results/change-room.png' });
  await page.screenshot({ path: 'test-results/inline-room-details-mobile.png', fullPage: true });
  expect(calls.every(name => ['GetAvailableRooms', 'GetPackages'].includes(name))).toBe(true);
});


