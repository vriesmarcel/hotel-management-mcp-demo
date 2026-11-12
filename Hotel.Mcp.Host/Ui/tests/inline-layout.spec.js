import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

// Offline bridge test: no hotel process or database, and no booking tool allowed.
for (const resourcesAvailable of [true, false]) test(`room selection and reference facts in a constrained card (resource reads: ${resourcesAvailable})`, async ({ page }) => {
  const ui = await readFile(new URL('../dist/reservations.html', import.meta.url), 'utf8');
  const host = await readFile(new URL('../dist/preview.html', import.meta.url), 'utf8');
  const uri = 'ui://hotel/reservations/v1';
  const calls = [], reads = [];
  const roomInformation = { uri: 'hotel://room-types', roomTypes: [{category:'Family',maximumGuests:5},{category:'Single',maximumGuests:null}], beds:[{type:'Single',lengthCm:200},{type:'King',lengthCm:200},{type:'Queen',lengthCm:190}], note:'Individual room bed configurations are unspecified.' };
  const cancellationPolicy = { uri:'hotel://policies/cancellation/v1',description:'Free cancellation until 48 hours before 15:00 Europe/Berlin check-in, then one room night excluding packages.' };
  await page.route('http://hotel.test/**', async route => {
    if (route.request().method() === 'GET') return route.fulfill({ contentType: 'text/html', body: host.replace('min-height:680px', 'min-height:0') });
    const { id, method, params } = route.request().postDataJSON();
    let result = {};
    if (method === 'tools/list') result = { tools: [{ name: 'GetAvailableRooms', _meta: { ui: { resourceUri: uri } } }] };
    if (method === 'resources/read') {
      reads.push(params.uri);
      if (params.uri === uri) result = { contents: [{ uri, mimeType: 'text/html;profile=mcp-app', text: ui }] };
      else if (!resourcesAvailable) return route.fulfill({json:{jsonrpc:'2.0',id,error:{code:-32601,message:'Host does not forward reference reads'}}});
      else result = {contents:[{uri:params.uri,mimeType:'application/json',text:JSON.stringify(params.uri==='hotel://room-types'?roomInformation:cancellationPolicy)}]};
    }
    if (method === 'tools/call') {
      calls.push(params.name);
      let data;
      if (params.name === 'GetPackages') data = { food: [], services: [] };
      else if (params.name === 'GetAvailableRooms') data = { ...params.arguments, ...(resourcesAvailable ? {} : {roomInformation,cancellationPolicy}), nights: 3, rooms: Array.from({ length: 13 }, (_, i) => ({ roomId: i + 1, category: i === 2 ? 'Family' : 'Single', nightlyRate: i === 2 ? 5000 : 2000 })) };
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
  await app.locator('.room[data-room-id="3"]').click();
  await expect(app.locator('#availability')).toBeHidden();
  await expect(app.locator('#details')).toBeInViewport();
  await expect(app.locator('#selection')).toContainText('Room 3 · Family');
  await expect(app.locator('#roomFacts')).toContainText('Maximum 5 guests');
  await expect(app.locator('#bedFacts')).toHaveText(/Queen 190 cm/);
  await expect(app.locator('#policyFacts')).toHaveText(/48 hours/);
  if (resourcesAvailable) expect(reads).toEqual(expect.arrayContaining(['hotel://room-types','hotel://policies/cancellation/v1']));
  await expect(app.locator('#review')).toBeHidden();
  await app.getByRole('button', { name: 'Change room' }).click();
  await expect(app.locator('#details')).toBeHidden();
  await expect(app.locator('#availability')).toBeInViewport();
  await app.locator('.room[data-room-id="1"]').click();
  await expect(app.locator('#selection')).toContainText('Room 1 · Single');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(app.locator('#changeRoom')).toBeInViewport();
  const child = page.frames().find(f => f.parentFrame());
  expect(await child.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await app.locator('#changeRoom').screenshot({ path: 'test-results/change-room.png' });
  await page.screenshot({ path: 'test-results/inline-room-details-mobile.png', fullPage: true });
  expect(calls.every(name => ['GetAvailableRooms', 'GetPackages'].includes(name))).toBe(true);
});


